using System;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace jKnepel.UnitySync
{
    [InitializeOnLoad()]
    public static class SyncManager
    {
        #region public members

        public static bool IsDebug = true;
        public static int MaximumNumberConnections = 10;

        #endregion

        #region private members

        private static SyncServer _server;
        private static SyncClient _client;

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

        }

		#endregion

		#region server logic

		private static void OpenTCPSocket(string sceneName, int port)
        {
            if (_server != null)
			{
                Debug.LogWarning($"The server is already listening!");
                return;
			}

            Scene exportScene = SceneManager.GetSceneByName(sceneName);
            if (!exportScene.IsValid() || !exportScene.isLoaded)
			{
                Debug.LogWarning($"The Scene {sceneName} is not valid!");
                return;
			}

            static void connectionAbolished(ExceptionDispatchInfo ex)
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

                CloseTCPSocket();
            }

            _server = new(port, exportScene, connectionAbolished);
        }

        private static void CloseTCPSocket()
        {
            if (_server == null) return;

            _server.Dispose();
            _server = null;
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

			static void connectionAbolished(ExceptionDispatchInfo ex)
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

				CloseClient();
			}

			_client = new(ip, port, sceneName, connectionAbolished);
        }

        private static void CloseClient()
		{
            if (_client == null) return;

            _client.Dispose();
            _client = null;
        }

        #endregion
    }
}
