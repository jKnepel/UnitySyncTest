using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using GLTF;
using UnityGLTF;
using Toph.GLTFSerializer.Components;

namespace jKnepel.UnitySync
{
    public class SyncServer : IDisposable
    {
        private Socket _listener;
        private Thread _listenerThread;
        private List<SyncClientHandler> _connectedClients = new();

        private readonly Queue<byte[]> _bytesToSend = new();
        private readonly Queue<Action> _mainThreadActions = new();

        private Scene _scene;

        private Action<ExceptionDispatchInfo> _connectionAbolished;
        private int _disposeCount;

        #region lifecycle

        public SyncServer(int port, Scene scene, Action<ExceptionDispatchInfo> connectionAbolished)
        {
            try
			{
                _scene = scene;
                _connectionAbolished = connectionAbolished;

                IPAddress ipAddress = Dns.GetHostEntry("127.0.0.1").AddressList[0];
                IPEndPoint localEndPoint = new(ipAddress, port);

                _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(localEndPoint);
                _listener.Listen(SyncManager.MaximumNumberConnections);

                _listenerThread = new(() => ListenForIncomingConnections()) { IsBackground = true };
                _listenerThread.Start();
			}
            catch(Exception ex)
			{
                _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
                return;
            }

            ObjectChangeEvents.changesPublished += ChangesPublished;
            EditorApplication.update += Update;
            SyncEvents.onIsOpenChanged?.Invoke(true);
            Debug.Log("Server is listening");
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

                    ObjectChangeEvents.changesPublished -= ChangesPublished;
                    EditorApplication.update -= Update;
                    SyncEvents.onIsOpenChanged?.Invoke(false);
                    Debug.Log("Server was closed");
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
            lock(_bytesToSend)
			{
                while(_bytesToSend.Count > 0)
				{
                    byte[] bytes = _bytesToSend.Dequeue();
                    foreach (SyncClientHandler client in _connectedClients)
					{
                        client.AddBytesToSend(bytes);
					}
				}
			}

            lock (_mainThreadActions)
            {
                while (_mainThreadActions.Count > 0)
                {
                    Action action = _mainThreadActions.Dequeue();
                    action?.Invoke();
                }
            }
        }

        #endregion

        #region public methods

        public void AddBytesToSend(byte[] bytes)
		{
            lock(_bytesToSend)
            { 
                _bytesToSend.Enqueue(bytes);
            }
		}

        #endregion

        #region private methods

        private void ListenForIncomingConnections()
        {
            try
            {
                while (_disposeCount == 0)
                {
                    if (_connectedClients.Count >= SyncManager.MaximumNumberConnections) continue;

                    Socket handler = _listener.Accept();
                    SyncClientHandler connectedClient = new(handler, (handler, ex) => RemoveClientHandler(handler, ex));
                    lock (_connectedClients)
                    {
                        _connectedClients.Add(connectedClient);
                    }
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
                        Debug.Log("Socket Exception " + ex.ToString());
                        break;
                    case ObjectDisposedException:
                        Debug.Log("Object Disposed Exception " + ex.ToString());
                        break;
                    case ThreadStartException:
                    case OutOfMemoryException:
                        Debug.Log("Thread Exception " + ex.ToString());
                        break;
                    default:
                        ex.Throw();
                        break;
                }
            }

            lock (_connectedClients)
			{
                handler.Dispose();
                _connectedClients.Remove(handler);
			}
		}

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

            List<byte[]> byteArrays = new();
            foreach (GLTFRootGameObject rootComponent in rootComponents)
            {
                byte[] bytes = await rootComponent.SerializeGameObject(new ExportOptions());
                client.AddBytesToSend(bytes);

                if (SyncManager.IsDebug) Debug.Log($"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(bytes)}");
            }
        }

        #endregion
    }
}
