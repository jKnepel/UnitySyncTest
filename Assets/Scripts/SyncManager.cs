using System;
using System.IO;
using System.Linq;
using System.Net;
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
        #region public members

        public static string Ip { get; private set; }
        public static string Port { get; private set; }
        public static string ExportSceneName { get; private set; }
        public static string ImportSceneName { get; private set; }
        public static bool IsDebug { get; private set; }

        #endregion

        #region private members

        private static TcpListener tcpListener;
        private static TcpClient socketConnection;

        #endregion

        #region thread handlers

        private static Thread tcpListenerThread;
        private static readonly object _lock = new object();
        private static readonly Queue<Action> _mainThreadActions = new Queue<Action>();

		#endregion

		#region unity cycle

        static SyncManager()
		{
            IsDebug = true;
            SyncEvents.onIpChanged += (string value) => Ip = value;
            SyncEvents.onPortChanged += (string value) => Port = value;
            SyncEvents.onExportSceneNameChanged += (string value) => ExportSceneName = value;
            SyncEvents.onImportSceneNameChanged += (string value) => ImportSceneName = value;

            SyncEvents.onOpenTCPSocket += OpenTCPSocket;
            SyncEvents.onCloseTCPSocket += CloseTCPSocket;
            SyncEvents.onExportScene += ExportScene;

            EditorApplication.update += Update;
        }

        private static void Update()
        {
            lock (_lock)
            {
                while (_mainThreadActions.Count > 0)
                {
                    var action = _mainThreadActions.Dequeue();
                    action?.Invoke();
                }
            }
        }

        private static void AddMainThreadAction(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }

		#endregion

		#region private methods

		private static void OpenTCPSocket()
        {
			tcpListenerThread = new Thread(new ThreadStart(ListenForIncommingRequests))
			{
				IsBackground = true
			};
			tcpListenerThread.Start();
        }

        private static void CloseTCPSocket()
        {
            tcpListenerThread.Abort();
            tcpListener.Server.Close();
        }

        private static void ListenForIncommingRequests()
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Parse(Ip), int.Parse(Port));
                tcpListener.Start();
				Debug.Log("Server is listening");
                List<byte[]> allBytes = new List<byte[]>();
                while (true)
                {
                    using TcpClient connectedTcpClient = tcpListener.AcceptTcpClient();
                    using NetworkStream stream = connectedTcpClient.GetStream();

                    byte[] lengthBytes = new byte[4];
                    int length;
                    while ((length = stream.Read(lengthBytes, 0, lengthBytes.Length)) != 0)
                    {
                        byte[] data = new byte[BitConverter.ToUInt32(lengthBytes)];
                        int dataLength = stream.Read(data, 0, data.Length);
                        allBytes.Add(data);
                        AddMainThreadAction(() => ImportScene(allBytes));
						Debug.Log("Received " + data.Length);
                    }
                }
            }
            catch (SocketException socketException)
            {
				Debug.Log("Socket Exception " + socketException.ToString());
            }
            catch (ThreadAbortException ex)
			{
                
			}
        }

        private async static void ExportScene()
        {
            var exportScene = SceneManager.GetSceneByName(ExportSceneName);
            if (exportScene.IsValid() is false || exportScene.isLoaded is false)
            {
				Debug.LogError($"Export scene with name '{ExportSceneName}' can not be found or is not valid!");
                return;
            }

            var rootGameObjects = GLTFSceneHelper.GetSceneRootGameObjects(exportScene).ToList();
            var rootComponents = GLTFSceneHelper.AddOrGetSceneRootComponents(rootGameObjects);
            rootComponents.ForEach(r => r.Init());

            var byteArrays = new List<byte[]>();
            foreach (var rootComponent in rootComponents)
            {
                var byteArray = await rootComponent.SerializeGameObject(new ExportOptions());
                byteArrays.Add(byteArray);

                if (IsDebug) Debug.Log($"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(byteArray)}");
            }

            try
            {
                socketConnection = new TcpClient(Ip, int.Parse(Port));
                if (socketConnection == null)
                {
					Debug.Log("Socket Exception: couldn't connect to server");
                    return;
                }

                NetworkStream stream = socketConnection.GetStream();
                if (stream.CanWrite)
                {
                    foreach (byte[] bytes in byteArrays)
                    {
                        stream.Write(BitConverter.GetBytes((uint)bytes.Length), 0, 4);
                        stream.Write(bytes, 0, bytes.Length);
                        Debug.Log("Send " + bytes.Length);
                    }
                }
                socketConnection.Close();
            }
            catch (SocketException socketException)
            {
				Debug.Log("Socket Exception: " + socketException);
            }

            if (IsDebug) Debug.Log($"Scene '{exportScene.name}' exported.");
        }

        private async static void ImportScene(List<byte[]> scene)
        {
            var importScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            importScene.name = ImportSceneName;
            Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
            {
                if (createdObject is null)
                {
                    return;
                }
                SceneManager.MoveGameObjectToScene(createdObject, importScene);

                if (IsDebug) Debug.Log($"GameObject '{createdObject.name}' imported.");
            };

            foreach (var byteArray in scene)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await memoryStream.WriteAsync(byteArray);
                    await DeserializeGameObject(importScene, new ImportOptions(), memoryStream, onLoadComplete);
                }
            }
            if (IsDebug) Debug.Log($"Scene '{importScene.name}' imported.");
        }

        private async static Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream,
            Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
        {
            GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

            var importer = new GLTFSceneImporterExtended(gLTFRoot, stream, importOptions);
            await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), IsDebug, -1, true, onLoadComplete); // ToDo get progress using IProgress
        }
    }

	#endregion
}
