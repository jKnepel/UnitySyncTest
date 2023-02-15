using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace jKnepel.UnitySync
{
#if UNITY_EDITOR
	[InitializeOnLoad()]
#endif
	public static class SyncManager
	{
		#region public members

		public const bool IS_DEBUG = true;
		public const int MAX_NUMBER_CONNECTIONS = 10;
		public const uint PROTOCOL_ID = 876237842;

		public static Action OnUpdate;
		public static Action OnAwake;
		public static Action OnStart;

		#endregion

		#region private members

		private static SyncServer _server;
		private static SyncClient _client;

		private static readonly ConcurrentQueue<Action> _mainThreadActions = new();

		#endregion

		#region unity cycle

		static SyncManager()
		{
			SyncEvents.onOpenTCPSocket += OpenTCPSocket;
			SyncEvents.onCloseTCPSocket += CloseTCPSocket;
			SyncEvents.onConnectClient += ConnectClient;
			SyncEvents.onCloseClient += CloseClient;

#if UNITY_EDITOR
			Awake();
			Start();
			EditorApplication.update += Update;
#else
			SyncRuntimeManager.Instance.OnAwake += Awake;
			SyncRuntimeManager.Instance.OnStart += Start;
			SyncRuntimeManager.Instance.OnUpdate += Update;
#endif
		}

		public static void Init() { }

		private static void Update()
		{
			OnUpdate?.Invoke();

			bool success = _mainThreadActions.TryDequeue(out Action action);
			if (success) action?.Invoke();
		}

		private static void Awake()
		{
			OnAwake?.Invoke();
		}

		private static void Start()
		{
			OnStart?.Invoke();
		}

		#endregion

		#region public methods

		public static void AddDebugMessage(string text)
		{
			if (!IS_DEBUG) return;

			Debug.Log(text);
			_mainThreadActions.Enqueue(() => SyncEvents.onDebugTextAdded?.Invoke(text));
		}

		#endregion

		#region server logic

		private static void OpenTCPSocket(string sceneName, string ip, int port)
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

			_server = new(ip, port, exportScene, (ExceptionDispatchInfo ex) =>
			{
				if (ex != null)
				{
					switch (ex.SourceException)
					{
						case SocketException:
							AddDebugMessage("Socket Exception " + ex.SourceException.ToString());
							break;
						case ObjectDisposedException:
							AddDebugMessage("Object Disposed Exception " + ex.SourceException.ToString());
							break;
						case ThreadStartException:
						case OutOfMemoryException:
							AddDebugMessage("Thread Exception " + ex.SourceException.ToString());
							break;
						default:
							ex.Throw();
							break;
					}
				}

				CloseTCPSocket();
			});
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

			_client = new(ip, port, sceneName, (ExceptionDispatchInfo ex) =>
			{
				if (ex != null)
				{
					switch (ex.SourceException)
					{
						case SocketException:
							AddDebugMessage("Socket Exception " + ex.SourceException.ToString());
							break;
						case ObjectDisposedException:
							AddDebugMessage("Object Disposed Exception " + ex.SourceException.ToString());
							break;
						case ThreadStartException:
						case OutOfMemoryException:
							AddDebugMessage("Thread Exception " + ex.SourceException.ToString());
							break;
						default:
							ex.Throw();
							break;
					}
				}

				CloseClient();
			});
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
