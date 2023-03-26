using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.NetworkInformation;
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

		public static bool IsDebug = false;
		public static string IP = GetLocalIPAddress();

		public static Action OnUpdate;
		public static Action OnAwake;
		public static Action OnStart;
		
		public static Action OnSyncMessageAdded;

		/// <summary>
		/// Action for when the Client successfully connected to the Network and is able to discover and join Rooms.
		/// </summary>
		public static Action OnClientConnected;

		/// <summary>
		/// Action for when the Client was disconnected from the Network and is now unable to discover and join Rooms.
		/// </summary>
		public static Action OnClientDisconnected;

		/// <summary>
		/// Action for when the Client connected to a Room and can now send Messages.
		/// </summary>
		public static Action OnConnectedToRoom;

		/// <summary>
		/// Action for when the Client disconnected from a Room and can no longer send any Messages.
		/// </summary>
		public static Action OnDisconnectedFromRoom;

		/// <summary>
		/// Action for when the Client received reliable Data from a Client.
		/// </summary>
		public static Action<byte[]> OnReliableDataReceived;

		/// <summary>
		/// Action for when the Client received reliable unordered Data from a Client.
		/// </summary>
		public static Action<byte[]> OnReliableUnorderedDataReceived;

		/// <summary>
		/// Action for when the Client received unreliable Data from a Client.
		/// </summary>
		public static Action<byte[]> OnUnreliableDataReceived;

		/// <summary>
		/// Action for when the Client received unreliable unordered Data from a Client.
		/// </summary>
		public static Action<byte[]> OnUnreliableUnorderedDataReceived;

		/// <summary>
		/// If the Client is connected to the Network and able to discover Rooms.
		/// </summary>
		public static bool IsClientActive 
		{ 
			get => _client != null && _client.IsClientActive; 
		}
		
		/// <summary>
		/// If the Client is connected to a Room and able to send Messages.
		/// </summary>
		public static bool IsConnectedToRoom 
		{ 
			get => _client != null && _client.IsConnectedToRoom; 
		}
		
		/// <summary>
		/// A List of all available Rooms.
		/// </summary>
		public static List<SyncOpenRoom> OpenRooms 
		{ 
			get => IsClientActive 
				? _client.OpenRooms.Values.ToList() 
				: null; 
		}
		
		/// <summary>
		/// The Room to which the Client is currently connected to.
		/// </summary>
		public static SyncOpenRoom CurrentRoom 
		{ 
			get => IsClientActive 
				? _client.CurrentRoom 
				: null; 
		}

		/// <summary>
		/// A List of remote Clients connected to the same Room as the local Client.
		/// </summary>
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

			while (_mainThreadActions.Count > 0)
			{
				if (_mainThreadActions.TryDequeue(out Action action))
					action?.Invoke();
			}
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

		#region private methods

		private static string GetLocalIPAddress()
		{
			// taken from https://stackoverflow.com/a/24814027
			string ipAddress = "";
			foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (item.OperationalStatus == OperationalStatus.Up)
				{   // Fetch the properties of this adapter
					IPInterfaceProperties adapterProperties = item.GetIPProperties();
					// Check if the gateway adress exist, if not its most likley a virtual network or smth
					if (adapterProperties.GatewayAddresses.FirstOrDefault() != null)
					{   // Iterate over each available unicast adresses
						foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
						{   // If the IP is a local IPv4 adress
							if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
							{
								ipAddress = ip.Address.ToString();
								break;
							}
						}
					}
				}
				if (ipAddress != "") { break; }
			}
			return ipAddress;
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
		/// <param name="roomname">Name of the Room</param>
		/// <param name="createroom">Wether an existing Room should be joined or a new Room should be created.</param>
		/// <returns><see langword="true"/> if the Client successfully connected to the Room</returns>
		public static bool ConnectToRoom(string roomname, bool createroom = false)
		{
			if (_client == null)
			{
				_client = new();
			}
			return _client.ConnectToRoom(roomname, createroom);
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
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null,
		/// all Clients receive the Packet.
		/// </param>
		public static void SendDataReliable(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataReliable(data, onDataSend, receiver);
		}

		/// <summary>
		/// Sends Data over Reliable UDP. This guarantees the arrival of all Data Packets without taking the Order into account.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null,
		/// all Clients receive the Packet.
		/// </param>
		public static void SendDataReliableUnordered(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataReliableUnordered(data, onDataSend, receiver);
		}

		/// <summary>
		/// Sends Data over Unreliable UDP. 
		/// This ensure arrival of Packets in the correct Order but does not guarantee that all Packets actually arrive.
		/// It is recommended to keep the size of the Data array below 1200, since anything over this threshold will create a 
		/// chunked Packet, slowing down the transmission.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null,
		/// all Clients receive the Packet.
		/// </param>
		public static void SendDataUnreliable(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				onDataSend?.Invoke(false);
				return;
			}

			if (data.Length > 1200)
			{
				Debug.LogWarning("Only reliable Packets can be larger than 1200 Bytes!");
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataUnreliable(data, onDataSend, receiver);
		}

		public static void SendDataUnreliableUnordered(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				onDataSend?.Invoke(false);
				return;
			}

			if (data.Length > 1200)
			{
				Debug.LogWarning("Only reliable Packets can be larger than 1200 Bytes!");
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataUnreliableUnordered(data, onDataSend, receiver);
		}

		public static void AddSyncMessage(SyncMessage message)
		{
			_mainThreadActions.Enqueue(() => {
				SyncMessages.Add(message);
				OnSyncMessageAdded?.Invoke();
			});
		}

		#endregion
	}
}
