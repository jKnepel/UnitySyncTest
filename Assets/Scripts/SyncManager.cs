using System;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace jKnepel.UnitySync
{
    [InitializeOnLoad()]
    public static class SyncManager
    {
        #region private members

        private static bool _isDebug = true;
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

		#region sync manager

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

            try
			{
                _server = new(port, exportScene);
			}
            catch (Exception ex)
            {
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
        }

        private static void CloseTCPSocket()
        {
            EditorApplication.update -= _server.Update;
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

            Scene importScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            importScene.name = sceneName;
            SceneManager.SetActiveScene(importScene);

            try
            {
                _client = new(ip, port, importScene);
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
            EditorApplication.update -= _client.Update;
            _client.Dispose();
            _client = null;
        }

		#endregion

		#endregion
    }
}
