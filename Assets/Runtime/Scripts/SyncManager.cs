using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
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

		public static bool IsSearchingRooms { get { return _client != null && _client.IsSearchingForRooms; } }
		public static bool IsConnected { get { return _client != null && _client.IsConnected; } }

		public static List<SyncOpenRoom> OpenRooms
		{
			get 
			{
				if (_client == null || _client.OpenRooms == null)
					return null;
				return _client.OpenRooms.Values.ToList(); 
			}
		}

		public static SyncOpenRoom CurrentRoom
		{
			get 
			{
				if (_client == null)
					return null;
				return _client.CurrentRoom;
			}
		}

		public static readonly List<SyncMessage> SyncMessages = new();

		#endregion

		#region private members

		private static SyncClient _client;

		private static readonly ConcurrentQueue<Action> _mainThreadActions = new();

		#endregion

		#region unity lifecycle

		static SyncManager()
		{
#if UNITY_EDITOR
			Awake();
			Start();
			EditorApplication.update += Update;
#else
			SyncRuntimeManager.Instance.OnAwake += Awake;
			SyncRuntimeManager.Instance.OnStart += Start;
			SyncRuntimeManager.Instance.OnUpdate += Update;
#endif

			_ = SyncSettings.Instance;

			_client = new();
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

		public static void RetryRoomDiscovery()
		{
			if (_client != null)
			{
				_client.Dispose();
			}
			_client = new();
		}

		public static void ConnectToRoom(string roomname, string scenename, bool createroom = false)
		{
			if (_client == null)
			{
				_client = new();
			}
			_client.ConnectToRoom(roomname, scenename, createroom);
		}

		public static void DisconnectFromRoom()
		{
			if (_client == null)
			{
				_client = new();
				return;
			}
			_client.DisconnectFromRoom();
		}

		public static void AddSyncMessage(SyncMessage message)
		{
			_mainThreadActions.Enqueue(() => {
				SyncMessages.Add(message);
				SyncEvents.OnSyncMessageAdded?.Invoke();
			});
		}

		#endregion
	}
}
