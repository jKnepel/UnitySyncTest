using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    public class SyncServer : IDisposable
    {
        private Socket _listener;
        private Thread _listenerThread;
        private int _maximumNumberConnections = 10;
        private List<SyncClientHandler> _connectedClients = new();

        private static readonly Queue<byte[]> _bytesToSend = new();

        #region lifecycle

        public SyncServer(int port)
        {
            IPAddress ipAddress = Dns.GetHostEntry("127.0.0.1").AddressList[0];
            IPEndPoint localEndPoint = new(ipAddress, port);

            _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(localEndPoint);
            _listener.Listen(_maximumNumberConnections);
            _listener.Blocking = true;
            SyncEvents.onIsOpenChanged?.Invoke(true);

            Debug.Log("Server is listening");

            _listenerThread = new(() => ListenForIncomingConnections()) { IsBackground = true };
            _listenerThread.Start();
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

                    if (_listener != null)
                    {
                        _listenerThread.Abort();
                        _listener.Close();
                        SyncEvents.onIsOpenChanged?.Invoke(false);
                    }
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
                    foreach (SyncClientHandler client in _connectedClients)
					{
                        byte[] bytes = _bytesToSend.Dequeue();
                        client.AddBytesToSend(bytes);
					}
				}
			}
		}

		#endregion

		#region public methods

        public void AddBytesToSend(byte[] bytes)
		{
            lock(_bytesToSend) { _bytesToSend.Enqueue(bytes); }
		}

		#endregion

		#region private methods

		private void ListenForIncomingConnections()
        {
            try
            {
                while (true)
                {
                    Socket handler = _listener.Accept();

                    SyncClientHandler connectedClient = new(handler);
                    lock (_connectedClients) 
                    { 
                        _connectedClients.Add(
                            connectedClient
                        );
                    }
                }
            }
            catch (SocketException ex)
            {
                Debug.Log("Socket Exception " + ex);
            }
        }

		#endregion
	}
}
