using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using GLTF;
using GLTF.Schema;
using UnityGLTF;
using Toph.GLTFSerializer.Components;

namespace jKnepel.UnitySync
{
    [InitializeOnLoad()]
    public static class SyncManager
    {
        #region private members

        private static bool _isDebug = true;
        private static SyncServer _server;
        private static SyncClient _client;

        private static Scene _exportScene;
        private static Scene _importScene;

        private static Dictionary<int, GameObject> _exportGameObjectIds = new();
        private static Dictionary<int, GameObject> _importGameObjectIds = new();

        private static readonly Queue<byte[]> _receivedBytes = new();

        #endregion

        #region unity cycle

        static SyncManager()
		{
            SyncEvents.onOpenTCPSocket += OpenTCPSocket;
            SyncEvents.onCloseTCPSocket += CloseTCPSocket;
            SyncEvents.onConnectClient += ConnectClient;
            SyncEvents.onCloseClient += CloseClient;

            // TODO : test if it works in build / find an alternative
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            lock (_receivedBytes)
            {
                while (_receivedBytes.Count > 0)
                {
                    byte[] bytes = _receivedBytes.Dequeue();
                    ImportHierarchy(bytes);
                }
            }
        }

        public static void AddReceivedBytes(byte[] bytes)
        {
            _receivedBytes.Enqueue(bytes);
        }

		#endregion

		#region sync manager

		#region server logic

		private static void OpenTCPSocket(string sceneName, int port)
        {
            if (_server != null)
			{
                Debug.LogWarning($"The server is already listening!");
                return;
			}

            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
			{
                Debug.LogWarning($"The Scene {sceneName} is not valid!");
                return;
			}

            ObjectChangeEvents.changesPublished += LogEvent;

            // TODO : also add nested gameobjects
            GameObject[] rootGameObjects = scene.GetRootGameObjects();
            foreach (GameObject go in rootGameObjects)
            {
                if (go.activeInHierarchy)
                {
                    _exportGameObjectIds.Add(go.GetInstanceID(), go);
                    foreach (Component co in go.GetComponents(typeof(Component)))
                    {
                        _exportGameObjectIds.Add(co.GetInstanceID(), go);
                    }
                }
            }

            _exportScene = scene;
            try
			{
                _server = new(port);
			}
            catch (Exception ex)
            {
                ObjectChangeEvents.changesPublished -= LogEvent;
                if (_server != null)
                {
                    _server.Dispose();
                    _server = null;
                }

                if (ex is SocketException)
                {
                    Debug.Log("Socket Exception " + ex.ToString());
                    return;
                }
                if (ex is ObjectDisposedException)
                {
                    Debug.Log("Object Disposed Exception " + ex.ToString());
                    return;
                }
                if (ex is ThreadStartException || ex is OutOfMemoryException)
				{
                    Debug.Log("Thread Exception " + ex.ToString());
                    return;
                }

                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            EditorApplication.update += _server.Update;

            // TODO : figure why tf this blocks the main thread
            //ExportScene();
        }

        private static void CloseTCPSocket()
        {
            EditorApplication.update -= _server.Update;
            _server.Dispose();

            ObjectChangeEvents.changesPublished -= LogEvent;
        }

        private static void LogEvent(ref ObjectChangeEventStream stream)
        {
            ChangeGameObjectOrComponentPropertiesEventArgs obj;
            stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
            SerializeDasJetzt(obj);
        }

        private async static void SerializeDasJetzt(ChangeGameObjectOrComponentPropertiesEventArgs obj)
        {
            if (obj.scene == _exportScene)
            {
                GameObject go = _exportGameObjectIds[obj.instanceId];
                GLTFRootGameObject gltf = go.GetOrAddComponent<GLTFRootGameObject>();
                byte[] bytes = await gltf.SerializeGameObject(new ExportOptions());
                _server.AddBytesToSend(bytes);
            }
        }

        #endregion

        #region client logic

        private static void ConnectClient(string sceneName, string ip, int port)
		{
            if (_client != null)
			{
                Debug.LogWarning($"The client is already connected!");
                return;
			}
            
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() || scene.isLoaded)
            {
                Debug.LogWarning($"The Scene {sceneName} already exists!");
                return;
            }

            _importScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            _importScene.name = sceneName;
            SceneManager.SetActiveScene(_importScene);

            try
            {
                _client = new(ip, port);
            }
            catch (Exception ex)
            {
                if (_client != null)
                {
                    _client.Dispose();
                    _client = null;
                }

                if (ex is SocketException)
                {
                    Debug.Log("Socket Exception " + ex.ToString());
                    return;
                }
                if (ex is ObjectDisposedException)
                {
                    Debug.Log("Object Disposed Exception " + ex.ToString());
                    return;
                }
                if (ex is ThreadStartException || ex is OutOfMemoryException)
                {
                    Debug.Log("Thread Exception " + ex.ToString());
                    return;
                }

                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            EditorApplication.update += _client.Update;
        }

        private static void CloseClient()
		{
            _client.Dispose();
        }

		#endregion

		#endregion

		#region serializer

		private async static void ExportScene()
        {
            List<GameObject> rootGameObjects = GLTFSceneHelper.GetSceneRootGameObjects(_exportScene).ToList();
            List<GLTFRootGameObject> rootComponents = GLTFSceneHelper.AddOrGetSceneRootComponents(rootGameObjects);
            rootComponents.ForEach(r => r.Init());

            List<byte[]> byteArrays = new();
            foreach (GLTFRootGameObject rootComponent in rootComponents)
            {
                byte[] bytes = await rootComponent.SerializeGameObject(new ExportOptions());
                _server.AddBytesToSend(bytes);

                if (_isDebug) Debug.Log($"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(bytes)}");
            }
        }

        private async static void ImportHierarchy(byte[] hierarchy)
        {
            Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
            {
                Debug.Log(createdObject.name);
                if (createdObject is null)
                {
                    return;
                }
                SceneManager.MoveGameObjectToScene(createdObject, _importScene);

                if (_isDebug) Debug.Log($"GameObject '{createdObject.name}' imported.");
            };

			using MemoryStream memoryStream = new();
			await memoryStream.WriteAsync(hierarchy);
			await DeserializeGameObject(_importScene, new ImportOptions(), memoryStream, onLoadComplete);

			if (_isDebug) Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

			return;
        }

        private async static Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
        {
            GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

            GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
            await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), _isDebug, -1, true, onLoadComplete);
        }

		#endregion
    }
}
