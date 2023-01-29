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
using UnityGLTF;
using Toph.GLTFSerializer.Components;

namespace jKnepel.UnitySync
{
    public class SyncServer : IDisposable
    {
        private Socket _listener;
        private Thread _listenerThread;
        private int _maximumNumberConnections = 10;
        private List<SyncClientHandler> _connectedClients = new();

        private readonly Queue<byte[]> _bytesToSend = new();
        private readonly Queue<Action> _mainThreadActions = new();

        private Scene _scene;

        #region lifecycle

        public SyncServer(int port, Scene scene)
        {
            _scene = scene;

            IPAddress ipAddress = Dns.GetHostEntry("127.0.0.1").AddressList[0];
            IPEndPoint localEndPoint = new(ipAddress, port);

            _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(localEndPoint);
            _listener.Listen(_maximumNumberConnections);

            _listenerThread = new(() => ListenForIncomingConnections()) { IsBackground = true };
            _listenerThread.Start();

            SyncEvents.onIsOpenChanged?.Invoke(true);
            ObjectChangeEvents.changesPublished += ChangesPublished;
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

        public void Dispose(bool safeToDispose)
		{
            if (safeToDispose)
			{
                try
                {
                    lock (_connectedClients)
                    {
                        for (int i = _connectedClients.Count - 1; i >= 0; i--)
                        {
                            _connectedClients.ElementAt(i).Dispose();
                            _connectedClients.RemoveAt(i);
                        }
                    }
                    if (_listenerThread != null && _listenerThread.IsAlive)
					{
                        _listenerThread.Abort();
					}
                    if (_listener != null)
                    {
                        _listener.Close();
                    }

                    SyncEvents.onIsOpenChanged?.Invoke(false);
                    Debug.Log("Server was closed");
                }
                catch (SocketException ex) { Debug.Log(ex); }
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
                while (true)
                {
                    if (_connectedClients.Count >= _maximumNumberConnections) continue;

                    Socket handler = _listener.Accept();
                    SyncClientHandler connectedClient = new(handler);
                    lock (_connectedClients)
                    {
                        _connectedClients.Add(connectedClient);
                    }
                    _mainThreadActions.Enqueue(() => ExportSceneToClient(connectedClient));
                }
            }
            catch (Exception ex)
            {
                if (ex is ThreadStartException || ex is OutOfMemoryException)
                {
                    Debug.Log("Thread Exception " + ex.ToString());
                    return;
                }

                ExceptionDispatchInfo.Capture(ex).Throw();
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

                //if (_isDebug) Debug.Log($"GameObject '{rootComponent.name}' exported:\n{new GLTFParser().ParseToReadableText(bytes)}");
            }
        }

        #endregion
    }
}
