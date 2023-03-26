using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
	internal sealed partial class SyncClient : IDisposable
	{
		#region packet constants

		private const uint PROTOCOL_ID = 876237842;

		// headers
		private const int PROTOCOL_ID_LENGTH = 4;
		private const int CRC32_LENGTH = 4; // checksum and contains protocol and room id
		private const int SEQUENCE_ID_LENGTH = 2; // sequence id for preventing old updates to be consumed
		private const int PACKET_TYPE_LENGTH = 1; // id of the packet type
		private const int MODULE_HASH_LENGTH = 4; // flag containing the hash of the used module
		private const int NUMBER_OF_SLICES = 2; // flag showing the number of packets the chunk consists of
		private const int SLICE_NUMBER = NUMBER_OF_SLICES; // number of the current slice

		// data
		private const int ROOMNAME_FLAG_LENGTH = 1;
		private const int USERNAME_FLAG_LENGTH = 1;
		private const int COLOR_LENGTH = 3;

		#endregion

		#region private members

		private static readonly SyncSettings _settings = SyncSettings.GetOrCreateSettings();

		private readonly IPAddress _ip;
		private readonly int _port;
		private readonly UdpClient _udpClient;

		private readonly Thread _listenerThread;
		private Thread _senderThread;
		private Thread _heartbeatThread;

		private int _disposeCount;

		private readonly ConcurrentQueue<Action> _mainThreadActions = new();
		private readonly ConcurrentQueue<SyncSenderPacket> _packetsToSend = new();

		#endregion

		#region public members

		private bool _isClientActive = false;
		public bool IsClientActive 
		{ 
			get => _isClientActive;
			private set 
			{
				_isClientActive = value;
				if (value)
					SyncManager.OnClientConnected?.Invoke();
				else
				{
					Dispose();
					SyncManager.OnClientDisconnected?.Invoke();
				}
			} 
		}

		private bool _isConnectedToRoom = false;
		public bool IsConnectedToRoom
		{
			get => _isConnectedToRoom;
			private set
			{
				_isConnectedToRoom = value;
				if (value)
					SyncManager.OnConnectedToRoom?.Invoke();
				else
					SyncManager.OnDisconnectedFromRoom?.Invoke();
			}
		}

		public bool IsRoomOwner { get; private set; }
		public readonly ConcurrentDictionary<string, SyncOpenRoom> OpenRooms = new();
		public SyncOpenRoom CurrentRoom { get; private set; }

		#endregion

		#region lifecycle

		public SyncClient()
		{
			try
			{
				if (!CheckLocalIP(SyncManager.IP))
				{
					Debug.LogError("No network interface possesses the given local IP!");
					IsClientActive = false;
					return;
				}

				_ip = IPAddress.Parse(SyncManager.IP);
				_port = _settings.Port;

				_udpClient = new(_settings.Port);
				_udpClient.EnableBroadcast = true;

				SyncManager.OnUpdate += Update;

				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();

				IsClientActive = true;
			}
			catch (Exception ex)
			{
				IsClientActive = false;
				switch (ex)
				{
					case SocketException:
						Debug.LogError("An Error ocurred when accessing the socket. "
							+ "Make sure the port is not occupied by another process!");
						break;
					case ArgumentOutOfRangeException:
						Debug.LogError("The Given Port is outside the possible Range!");
						break;
					case ArgumentNullException:
						Debug.LogError("The local IP can't be null!");
						break;
					case FormatException:
						Debug.LogError("The local IP is not a valid IP Address!");
						break;
					case ThreadStartException:
						Debug.LogError("An Error ocurred when starting the Threads. Please try again later!");
						break;
					case OutOfMemoryException:
						Debug.LogError("Not enough memory available to start the Threads!");
						break;
					default: 
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
			}
		}

		~SyncClient()
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
				IsConnectedToRoom = false;
				CurrentRoom = null;
				IsRoomOwner = false;
				OpenRooms.Clear();

				if (_listenerThread != null)
				{
					_listenerThread.Abort();
					_listenerThread.Join();
				}
				if (_heartbeatThread != null)
				{
					_heartbeatThread.Abort();
					_heartbeatThread.Join();
				}
				if (_senderThread != null)
				{
					_senderThread.Abort();
					_senderThread.Join();
				}

				if (_udpClient != null)
				{
					_udpClient.Close();
					_udpClient.Dispose();
				}

				if (disposing)
				{
					SyncManager.OnUpdate -= Update;
				}
			}
		}

		public void Update()
		{
			while (_mainThreadActions.Count > 0)
			{
				if (_mainThreadActions.TryDequeue(out Action action))
					action?.Invoke();
			}
		}

		public bool ConnectToRoom(string roomname, bool createroom = false)
		{
			if (IsConnectedToRoom)
			{
				Debug.LogWarning($"The client is already connected!");
				return false;
			}

			if (roomname.Length > 100 || _settings.Username.Length > 100)
			{
				Debug.LogWarning($"The Roomname and Username must be shorter than 100 Characters!");
				return false;
			}

			if (!IsASCIIString(roomname) || !IsASCIIString(_settings.Username))
			{
				Debug.LogWarning($"The Roomname or Username contains a non-ASCII Character!");
				return false;
			}

			// create/save scene if room is joined/created
			if (createroom)
			{
				if (OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
				{
					Debug.LogWarning($"The Room {roomname} already exists!");
					return false;
				}
				CurrentRoom = new(roomname);
				OpenRooms.TryAdd(CurrentRoom.Roomname, CurrentRoom);
				IsRoomOwner = true;
			}
			else
			{
				if (!OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
				{
					Debug.LogWarning($"The Room {roomname} doesn't exist yet!");
					return false;
				}

				CurrentRoom = room;
				IsRoomOwner = false;
				// TODO : announce myself to room
			}
			
			_heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
			_heartbeatThread.Start();
			_senderThread = new(() => SenderThread()) { IsBackground = true };
			_senderThread.Start();

			SyncManager.OnUpdate += Update;
			IsConnectedToRoom = true;
			SyncManager.AddSyncMessage(new("Client connected to Room!"));
			return true;
		}

		public bool DisconnectFromRoom()
		{
			if (!IsConnectedToRoom)
				return false;

			if (_heartbeatThread != null)
			{
				_heartbeatThread.Abort();
				_heartbeatThread.Join();
			}
			if (_senderThread != null)
			{
				_senderThread.Abort();
				_senderThread.Join();
			}

			if (CurrentRoom.ConnectedClients.Count == 0)
				OpenRooms.TryRemove(CurrentRoom.Roomname, out _);
			CurrentRoom = null;
			IsRoomOwner = false;
			IsConnectedToRoom = false;
			SyncManager.AddSyncMessage(new("Client Disconnected from Room!"));
			return true;
		}

		#endregion

		#region public methods

		public void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.ReliableData, moduleHash, data, onDataSend, receiver));
		}

		public void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.ReliableUnorderedData, moduleHash, data, onDataSend, receiver));
		}

		public void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.UnreliableData, moduleHash, data, onDataSend, receiver));
		}

		public void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.UnreliableUnorderedData, moduleHash, data, onDataSend, receiver));
		}

		#endregion

		#region helper methods

		private bool CheckLocalIP(string localIp)
		{
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
							if (ip.Address.AddressFamily == AddressFamily.InterNetwork && localIp == ip.Address.ToString())
							{
								return true;
							}
						}
					}
				}
			}

			return false;
		}

		private bool CheckReceiver(SyncConnectedClient receiver)
		{
			return IsConnectedToRoom
				&& CurrentRoom.ConnectedClients.TryGetValue(receiver.IP, out _);
		}

		private static bool IsASCIIString(string str)
		{
			return (Encoding.UTF8.GetByteCount(str)) == str.Length;
		}

		private static bool IsUnreliableData(SyncPacketType type)
		{
			return type == SyncPacketType.UnreliableData || type == SyncPacketType.UnreliableUnorderedData;
		}
		
		private static bool IsReliableData(SyncPacketType type)
		{
			return type == SyncPacketType.ReliableData || type == SyncPacketType.ReliableUnorderedData;
		}

		private static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
		{
			if (size == 0)
				size = array.Length - offset;

			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

		private static void DebugByteMessage(byte[] bytes, string msg, bool inBinary = false)
		{
			if (!SyncManager.IsDebug)
				return;

			foreach (byte d in bytes)
				msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			Debug.Log(msg);
		}

		#endregion
	}
}
