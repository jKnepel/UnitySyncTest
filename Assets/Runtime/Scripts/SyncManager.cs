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

		public static Action OnUpdate;
		public static Action OnAwake;
		public static Action OnStart;

		#endregion

		#region private members

		private static SyncClient _client;

		private static readonly ConcurrentQueue<Action> _mainThreadActions = new();

		#endregion

		#region unity cycle

		static SyncManager()
		{
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

			if (_mainThreadActions.TryDequeue(out Action action))
				action?.Invoke();
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
			if (!SyncSettings.Instance.IsDebug) return;

			Debug.Log(text);
			_mainThreadActions.Enqueue(() => SyncEvents.onDebugTextAdded?.Invoke(text));
		}

		#endregion

		#region client logic

		private static void ConnectClient(string exportSceneName, string importSceneName, int port)
		{
			if (_client != null)
			{
				Debug.LogWarning($"The client is already connected!");
				return;
			}

#if UNITY_EDITOR
			if (EditorApplication.isPlaying)
			{
				Debug.LogWarning($"This is not possible in the Editor Playmode");
				return;
			}
#endif

			Scene exportScene = SceneManager.GetSceneByName(exportSceneName);
			if (!exportScene.IsValid() || !exportScene.isLoaded)
			{
				Debug.LogWarning($"The Scene {exportSceneName} is not valid!");
				return;
			}

			Scene scene = SceneManager.GetSceneByName(importSceneName);
			if (scene.IsValid() || scene.isLoaded)
			{
				Debug.LogWarning($"The Scene {importSceneName} already exists!");
				return;
			}

			_client = new(port, exportScene, importSceneName, (ExceptionDispatchInfo ex) =>
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
