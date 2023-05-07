using jKnepel.UnitySync.Networking;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace jKnepel.UnitySync
{
	public static class SyncManager
	{
		#region public properties

		public static bool IsDebug = false;
		public static string IP = GetLocalIPAddress();

		public static Action OnAwake;
		public static Action OnStart;
		public static Action OnUpdate;
		
		public static Action OnSyncMessageAdded;

		/// <summary>
		/// Action for when the Server Discovery was activated.
		/// </summary>
		public static Action OnServerDiscoveryActivated;

		/// <summary>
		/// Action for when the Server Discovery was deactivated.
		/// </summary>
		public static Action OnServerDiscoveryDeactivated;

		/// <summary>
		/// Action for when successfully connecting to or creating a Server.
		/// </summary>
		public static Action OnConnected;

		/// <summary>
		/// Action for when disconnecting from or closing the Server.
		/// </summary>
		public static Action OnDisconnected;

		/// <summary>
		/// Action for when a Client connected to the current Server and can now receive Messages.
		/// </summary>
		public static Action<uint> OnClientConnected;

		/// <summary>
		/// Action for when a Client disconnected from the current Server and can no longer receive any Messages.
		/// </summary>
		public static Action<uint> OnClientDisconnected;

		/// <summary>
		/// If the Client is connected to the Network and able to discover Servers.
		/// </summary>
		public static bool IsServerDiscoveryActive
		{
			get => _isServerDiscoveryActive; private set => _isServerDiscoveryActive = value;
		}
		
		/// <summary>
		/// If the Client is connected to a Server or is a Server.
		/// </summary>
		public static bool IsConnected
		{ 
			get => _socket != null && _socket.IsConnected; 
		}
		
		/// <summary>
		/// A List of all available Servers.
		/// </summary>
		public static List<SyncServerInformation> OpenServers
		{ 
			get => IsServerDiscoveryActive
				? _openServers.Values.ToList() 
				: null; 
		}
		
		/// <summary>
		/// The Server to which the Client is currently connected to.
		/// </summary>
		public static SyncServerInformation CurrentServer 
		{ 
			get => IsConnected
				? _socket.ServerInformation
				: null; 
		}

		/// <summary>
		/// A List of remote Clients connected to the same Server as the local Client.
		/// </summary>
		public static List<SyncConnectedClient> ConnectedClients 
		{
			get => IsConnected
				? _socket.ConnectedClients
				: null;
		}

		/// <summary>
		/// If the local Client acts as a Server that remote Clients can connect to.
		/// </summary>
		public static bool IsServer
		{
			get => IsConnected && _socket.IsServer;
		}

		public static readonly List<SyncMessage> SyncMessages = new();

		#endregion

		#region internal fields

		internal static Action<uint, byte, byte[]> DataReceived;

		#endregion

		#region private fields

		private readonly static SyncSettings _settings = SyncSettings.GetOrCreateSettings();
		
		private readonly static Dictionary<string, SyncServerInformation> _openServers = new();

		private readonly static Dictionary<uint, SyncModule> _registeredModules = new();

		private readonly static ConcurrentQueue<Action> _mainThreadActions = new();

		private static IPAddress _ip;
		private static int		 _port;
		private static UdpClient _udpClient;

		private static Thread _discoveryThread;

		private static SyncSocket _socket;
		private static bool _isServerDiscoveryActive;

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

			DataReceived += OnDataReceived;

			ResetClient();
		}

		public static void Init() { }

		private static void Awake()
		{
			OnAwake?.Invoke();
		}

		private static void Start()
		{
			OnStart?.Invoke();
		}

		private static void Update()
		{
			OnUpdate?.Invoke();

			while (_mainThreadActions.Count > 0)
			{
				if (_mainThreadActions.TryDequeue(out Action action))
					action?.Invoke();
			}
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

		private static void DiscoveryThread()
		{
			try
			{
				while (true)
				{
					try
					{
						// get packet ip headers
						IPEndPoint receiveEndpoint = new(IPAddress.Any, 0);
						byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
						IPAddress sender = receiveEndpoint.Address;
						if (sender.Equals(_ip))
							continue;

						byte typeBytes = receivedBytes[SyncSettings.CRC32_LENGTH];
						byte mask = 1 << 7;
						typeBytes &= (byte)~mask;
						SyncPacketType packetType = (SyncPacketType)typeBytes;

						if (packetType != SyncPacketType.Heartbeat)
							continue;

						// check for bitswitches using crc32 checksum
						SyncHeartbeatPacket heartbeat = new(receivedBytes);
						if (!heartbeat.CheckCRC32Checksum())
							continue;

						string servername = Encoding.ASCII.GetString(heartbeat.Servername);
						if (!_openServers.TryGetValue(servername, out SyncServerInformation server))
							server = new SyncServerInformation(sender, servername);

						// TODO : set number clients
						// TODO : update server values
						// TODO : timeout server
					}
					catch (OverflowException) { continue; }
				}
			}
			catch (ThreadAbortException) { }
		}

		private static void OnDataReceived(uint moduleHash, byte client, byte[] data)
		{
			if (_registeredModules.TryGetValue(moduleHash, out SyncModule module))
			{
				_mainThreadActions.Enqueue(() => module.OnReceiveData(client, data));
			}
		}

		#endregion

		#region internal methods

		internal static uint RegisterModule(SyncModule module)
		{
			string moduleValues = module.GetType().Name;
			BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			foreach (var f in module.GetType().GetMembers(flags))
			{
				if (f.MemberType == MemberTypes.Field || (f.MemberType & MemberTypes.Property) != 0)
					moduleValues += f.Name;

				if (f is FieldInfo fi)
					moduleValues += fi.FieldType.Name;

				if (f.MemberType == MemberTypes.Method)
					moduleValues += f.Name;
			}
			uint hash = SyncCRC32.CRC32Bytes(Encoding.ASCII.GetBytes(moduleValues));

			if (_registeredModules.TryGetValue(hash, out _))
			{
				Debug.LogError("Only one Module of each type can be registered!");
				return 0;
			}

			_registeredModules.Add(hash, module);
			return hash;
		}

		internal static void UnregisterModule(uint moduleHash)
		{
			if (!_registeredModules.Remove(moduleHash))
				Debug.LogWarning($"The given Module could not be found!");
		}

		internal static bool IsModuleRegistered(uint moduleHash)
		{
			return _registeredModules.TryGetValue(moduleHash, out _);
		}

		internal static void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataReliable(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataReliableUnordered(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataUnreliable(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataUnreliableUnordered(moduleHash, data, onDataSend, receiver);
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
			if (_udpClient != null)
			{
				_udpClient.Close();
				_udpClient.Dispose();
			}
			if (_discoveryThread != null)
			{
				_discoveryThread.Abort();
				_discoveryThread.Join();
			}

			try
			{
				_ip = IPAddress.Parse(IP);
				_port = _settings.DiscoveryPort;

				IPEndPoint localport = new(IPAddress.Any, _port);
				_udpClient = new();
				_udpClient.ExclusiveAddressUse = false;
				_udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				_udpClient.EnableBroadcast = true;
				_udpClient.Client.Bind(localport);

				_discoveryThread = new(() => DiscoveryThread()) { IsBackground = true };
				_discoveryThread.Start();
			}
			catch (Exception ex)
			{
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
				IsServerDiscoveryActive = false;
				return false;
			}

			IsServerDiscoveryActive = true;
			return IsServerDiscoveryActive;
		}

		/// <summary>
		/// Connect to a Server or create a new one. Membership in a Server is required before Data can be send.
		/// </summary>
		/// <param name="servername">Name of the Server</param>
		/// <param name="createserver">Wether an existing Server should be joined or a new Server should be created.</param>
		/// <returns><see langword="true"/> if the Client successfully connected to the Server</returns>
		public static bool ConnectToServer(string servername, bool createserver = false)
		{
			if (IsConnected)
			{
				Debug.LogWarning("The local Client is already connected to a Server!");
				return false;
			}

			if (createserver)
			{
				if (_openServers.TryGetValue(servername, out _))
				{
					Debug.LogError("A Server with this Name already exists!");
					return false;
				}

				_socket = new SyncServer(servername);
			}
			else
			{
				if (!_openServers.TryGetValue(servername, out _))
				{
					Debug.LogError("No Server with this Name exists!");
					return false;
				}

				//_socket = new SyncClient(servername);
			}

			return IsConnected;
		}

		/// <summary>
		/// Disconnects the Client from the Server
		/// </summary>
		public static void DisconnectFromServer()
		{
			if (_socket == null)
			{
				Debug.LogWarning("The local Client is not currently connected to a Server!");
				return;
			}

			_socket.DisconnectFromServer();
		}

		public static void AddSyncMessage(SyncMessage message)
		{
			_mainThreadActions.Enqueue(() => {
				SyncMessages.Add(message);
				OnSyncMessageAdded?.Invoke();
			});
		}

		#endregion

		#region helper methods

		private static bool IsEligibleForSending(Action<bool> onDataSend)
		{
			if (!IsConnected)
			{
				Debug.LogError("The local Client is currently not connected to a Server!");
				onDataSend?.Invoke(false);
				return false;
			}
			return true;
		}

		#endregion
	}
}
