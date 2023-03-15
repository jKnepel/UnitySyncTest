using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

		public static bool IsClientActive { get => _client != null && _client.IsClientActive; }
		public static bool IsConnectedToRoom { get => _client != null && _client.IsConnectedToRoom; }
		public static List<SyncOpenRoom> OpenRooms { get => IsClientActive ? _client.OpenRooms.Values.ToList() : null; }
		public static SyncOpenRoom CurrentRoom { get => IsClientActive ? _client.CurrentRoom : null; }
		public static List<SyncConnectedClient> ConnectedClients 
		{
			get => IsClientActive 
				? _client.CurrentRoom.ConnectedClients.Values.ToList()
				: null;
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

		/// <summary>
		/// Retrieves the Settings used for the Sync Client.
		/// </summary>
		/// <returns></returns>
		public static SyncSettings GetSyncSettings()
		{
			return SyncSettings.GetOrCreateSettings();
		}

		/// <summary>
		/// Resets the Client. Use this, when Exceptions ocurred or the Socket was closed, to reconnect to the network.
		/// </summary>
		/// <returns><see langword="true"/> if the Client is Active after the Reset</returns>
		public static bool ResetClient()
		{
			if (_client != null)
			{
				_client.Dispose();
			}
			_client = new();
			return IsClientActive;
		}

		/// <summary>
		/// Connect to a Room or create a new one. Membership in a Room is required before Data can be send.
		/// </summary>
		/// <param name="roomname">Name of the Room.</param>
		/// <param name="scenename">
		/// Name of the Scene that should be synced upon creation of the Room.
		/// If the client joins a Room, this will become the Name of the temporary Scene.
		/// </param>
		/// <param name="createroom">Wether an existing Room should be joined or a new Room should be created.</param>
		/// <returns><see langword="true"/> if the Client successfully connected to the Room</returns>
		public static bool ConnectToRoom(string roomname, string scenename, bool createroom = false)
		{
			if (_client == null)
			{
				_client = new();
			}
			return _client.ConnectToRoom(roomname, scenename, createroom);
		}

		/// <summary>
		/// Disconnects the Client from the Room
		/// </summary>
		/// <returns>
		/// <see langword="true"/> if the Client successfully disconnected from the Room <br/>
		/// <see langword="false"/> if the Disconnect failed or the Client was already disconnected
		/// </returns>
		public static bool DisconnectFromRoom()
		{
			if (_client == null)
			{
				_client = new();
				return false;
			}
			return _client.DisconnectFromRoom();
		}

		// TODO : add manual sync Hierarchy

		// TODO : add manual delete Hierarchy

		// TODO : add manual sync GameObject

		// TODO : add manual delete GameObject

		// TODO : add manual sync Component

		// TODO : add manual delete Component

		/// <summary>
		/// Sends Data over Reliable UDP. This guarantees the arrival of all Data Packets in the correct Order.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null,
		/// all Clients receive the Packet.
		/// </param>
		/// <returns>
		/// <see langword="true"/> If the data was successfully send <br/>
		/// <see langword="false"/> If the data failed to send
		/// </returns>
		public static bool SendDataReliable(byte[] data, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
				return false;

			return _client.SendDataReliable(data, receiver);
		}

		/// <summary>
		/// Sends Data over Unreliable UDP. 
		/// This ensure arrival of Packets in the correct Order but does not guarantee that all Packets actually arrive.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null,
		/// all Clients receive the Packet.
		/// </param>
		/// <returns>
		/// <see langword="true"/> If the data was successfully send <br/>
		/// <see langword="false"/> If the data failed to send
		/// </returns>
		public static bool SendDataUnreliable(byte[] data, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
				return false;

			return _client.SendDataUnreliable(data, receiver);
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
