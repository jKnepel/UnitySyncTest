using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.SceneManagement;
using GLTF;
using UnityGLTF;
using Toph.GLTFSerializer.Components;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace jKnepel.UnitySync
{
    public class SyncServer : IDisposable
    {
        private Socket _listener;
        private Thread _listenerThread;
        private List<SyncClientHandler> _connectedClients = new();

        private readonly ConcurrentQueue<byte[]> _bytesToSend = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        private Scene _scene;

        private readonly Action<ExceptionDispatchInfo> _connectionAbolished;
        private int _disposeCount;

        #region lifecycle

        public SyncServer(string ip, int port, Scene scene, Action<ExceptionDispatchInfo> connectionAbolished)
        {
            try
			{
                _scene = scene;
                _connectionAbolished = connectionAbolished;

                IPAddress ipAddress = Dns.GetHostEntry(ip).AddressList[0];
                IPEndPoint localEndPoint = new(ipAddress, port);

                _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(localEndPoint);
                _listener.Listen(SyncManager.MAX_NUMBER_CONNECTIONS);

                _listenerThread = new(() => ListenForIncomingConnections()) { IsBackground = true };
                _listenerThread.Start();
			}
            catch(Exception ex)
			{
                _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
                return;
            }
#if UNITY_EDITOR
            ObjectChangeEvents.changesPublished += ChangesPublished;
            EditorApplication.playModeStateChanged += PreventPlayMode;
#endif
            SyncManager.OnUpdate += Update;
            SyncEvents.onIsOpenChanged?.Invoke(true);
            SyncManager.AddDebugMessage("Server is listening");
        }

        ~SyncServer()
		{
            Dispose(false);
		}

        public void Dispose()
        {
            Dispose(true);
        }

        public void Dispose(bool disposing)
		{
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                if (disposing)
                {
                    lock (_connectedClients)
					{
                        for (int i = _connectedClients.Count - 1; i >= 0; i--)
                        {
                            RemoveClientHandler(_connectedClients.ElementAt(i), null);
                        }
					}
                    if (_listenerThread != null)
                    {
                        _listenerThread = null;
                    }

#if UNITY_EDITOR
                    ObjectChangeEvents.changesPublished -= ChangesPublished;
                    EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif
                    SyncManager.OnUpdate -= Update;
                    SyncEvents.onIsOpenChanged?.Invoke(false);
                    SyncManager.AddDebugMessage("Server was closed");
                }

                try
                {
                    if (_listener != null)
                    {
                        _listener.Close();
                    }
                }
                catch (ObjectDisposedException) { }
            }
		}

        public void Update()
        {
            while (_bytesToSend.Count > 0)
            {
                bool success = _bytesToSend.TryDequeue(out byte[] bytes);
                if (!success) continue;
                lock (_connectedClients)
				{
                    foreach (SyncClientHandler client in _connectedClients)
                    {
                        client.AddBytesToSend(bytes);
                    }
				}
            }

            while (_mainThreadActions.Count > 0)
            {
                bool success = _mainThreadActions.TryDequeue(out Action action);
                if (success) action?.Invoke();
            }
        }

        #endregion

        #region public methods

        public void AddBytesToSend(byte[] bytes)
		{
            _bytesToSend.Enqueue(bytes);
        }

        #endregion

        #region private methods

        private void ListenForIncomingConnections()
        {
            try
            {
                while (_disposeCount == 0)
                {
                    if (_connectedClients.Count >= SyncManager.MAX_NUMBER_CONNECTIONS) continue;

                    Socket handler = _listener.Accept();
                    SyncClientHandler connectedClient = new(handler, (handler, ex) => RemoveClientHandler(handler, ex));
                    lock (_connectedClients) _connectedClients.Add(connectedClient);
                    _mainThreadActions.Enqueue(() => ExportSceneToClient(connectedClient));
                }
            }
            catch (Exception ex)
            {
                if (_disposeCount == 0) _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
            }
        }

        private void RemoveClientHandler(SyncClientHandler handler, ExceptionDispatchInfo ex)
		{
            if (ex != null)
            {
                switch (ex.SourceException)
                {
                    case SocketException:
                        SyncManager.AddDebugMessage("Socket Exception " + ex.ToString());
                        break;
                    case ObjectDisposedException:
                        SyncManager.AddDebugMessage("Object Disposed Exception " + ex.ToString());
                        break;
                    case ThreadStartException:
                    case OutOfMemoryException:
                        SyncManager.AddDebugMessage("Thread Exception " + ex.ToString());
                        break;
                    default:
                        ex.Throw();
                        break;
                }
            }

            handler.Dispose();
			lock (_connectedClients) _connectedClients.Remove(handler);
        }

#if UNITY_EDITOR
        private void ChangesPublished(ref ObjectChangeEventStream stream)
        {
            ChangeGameObjectOrComponentPropertiesEventArgs obj;
            stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
            if (obj.scene == _scene)
            {
                UnityEngine.Object o = EditorUtility.InstanceIDToObject(obj.instanceId);
                GameObject go;
                try { go = ((Component)o).gameObject; }
                catch (InvalidCastException) { go = (GameObject)o; }
                SendHierarchy(go);
            }
        }
#endif

        private async void SendHierarchy(GameObject go)
        {
            GLTFRootGameObject gltf = go.GetRootParent().GetOrAddComponent<GLTFRootGameObject>();
            byte[] bytes = await gltf.SerializeGameObject(new ExportOptions());
            AddBytesToSend(bytes);
        }

        private async void ExportSceneToClient(SyncClientHandler client)
        {
            List<GameObject> rootGameObjects = GLTFSceneHelper.GetSceneRootGameObjects(_scene).ToList();
            List<GLTFRootGameObject> rootComponents = GLTFSceneHelper.AddOrGetSceneRootComponents(rootGameObjects);
            rootComponents.ForEach(r => r.Init());

            foreach (GLTFRootGameObject rootComponent in rootComponents)
            {
                byte[] bytes = await rootComponent.SerializeGameObject(new ExportOptions());
                client.AddBytesToSend(bytes);

                SyncManager.AddDebugMessage(
                    $"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(bytes)}"
                );
            }
        }

        #endregion

        #region helper methods

#if UNITY_EDITOR
        private static void PreventPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                EditorApplication.isPlaying = false;
            }
        }
#endif
    
        #endregion
    }
}
