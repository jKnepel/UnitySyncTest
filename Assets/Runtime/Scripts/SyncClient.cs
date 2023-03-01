using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace jKnepel.UnitySync
{
	public partial class SyncClient : IDisposable
	{
		#region packet constants

		private const uint PROTOCOL_ID = 876237842;

		// headers
		private const int PROTOCOL_ID_LENGTH = 4;
		private const int CRC32_LENGTH = 4; // checksum and contains protocol and room id
		// TODO : public const int SEQUENCE_LENGTH = 4;
		private const int DATA_TYPE_LENGTH = 1; // id of the packet type
		private const int DATA_FLAG_LENGTH = 2; // flag containing data length

		// data
		private const int ROOMNAME_FLAG_LENGTH = 1;
		private const int USERNAME_FLAG_LENGTH = 1;
		private const int COLOR_LENGTH = 3;
		private const int CLIENT_CAMERA_DATA_LENGTH = 24;
		private const int Transform_DATA_LENGTH = 36;

		#endregion

		#region private members

		private readonly UdpClient _listener;
		private readonly UdpClient _sender;
		private readonly IPEndPoint _broadCastEndpoint;
		private IPEndPoint _receiveEndpoint;

		private readonly Thread _senderThread;
		private readonly Thread _listenerThread;
		private readonly Thread _heartbeatThread;

		private readonly ManualResetEvent _pauseEvent = new(true);
		private int _disposeCount;

		private readonly ConcurrentQueue<Action> _mainThreadActions = new();
		private readonly ConcurrentQueue<SyncPacket> _packetsToSend = new();
		private readonly ConcurrentQueue<SyncPacket> _receivedPackets = new();

		private Vector3 _lastCameraPosition;
		private Vector3 _lastCameraRotation;
		private int _cameraUpdateDelay = SyncSettings.Instance.ClientVisualizerUpdateDelay;

		private Scene _scene;
		private Transform _objectContainer;
		private string _localIpAddress;

		#endregion

		#region public members

		public bool IsSearchingForRooms { get; private set; }
		public bool IsConnected { get { return CurrentRoom != null; } }
		public bool IsRoomOwner { get; private set; }
		public readonly ConcurrentDictionary<string, SyncOpenRoom> OpenRooms = new();
		public SyncOpenRoom CurrentRoom { get; private set; }

		#endregion

		#region lifecycle

		public SyncClient()
		{
			try
			{
				_localIpAddress = GetLocalIPAddress();
				if (_localIpAddress.Length == 0)
					throw(new SocketException());

				_listener = new(SyncSettings.Instance.Port);
				_listener.EnableBroadcast = true;
				_sender = new();
				_sender.EnableBroadcast = true;

				_broadCastEndpoint = new(IPAddress.Broadcast, SyncSettings.Instance.Port);
				_receiveEndpoint = new(IPAddress.Any, SyncSettings.Instance.Port);

				SyncManager.OnUpdate += Update;

				_pauseEvent.Reset();

				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();
				_heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
				_heartbeatThread.Start();

				IsSearchingForRooms = true;
			}
			catch (Exception ex)
			{
				IsSearchingForRooms = false;
				switch (ex)
				{
					case SocketException:
						SyncManager.AddSyncMessage(new("SocketException " + ex.ToString(), SyncMessageSeverity.LogError));
						break;
					case ArgumentOutOfRangeException:
						SyncManager.AddSyncMessage(new("ArgumentOutOfRangeException " + ex.ToString(), SyncMessageSeverity.LogError));
						break;
					case ThreadStartException:
						SyncManager.AddSyncMessage(new("ThreadStartException " + ex.ToString(), SyncMessageSeverity.LogError));
						break;
					case OutOfMemoryException:
						SyncManager.AddSyncMessage(new("OutOfMemoryException " + ex.ToString(), SyncMessageSeverity.LogError));
						break;
					default: throw;
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
				if (disposing)
				{
					if (_listenerThread != null)
					{
						_pauseEvent.Set();
						_listenerThread.Join();
						_senderThread.Join();
						_heartbeatThread.Join();
					}

#if UNITY_EDITOR
					ObjectChangeEvents.changesPublished -= ChangesPublished;
					EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif
					SyncManager.OnUpdate -= Update;
				}

				// TODO : doesnt work on recompile, fix
#if UNITY_EDITOR
				GameObject.DestroyImmediate(_objectContainer);
#else
				GameObject.Destroy(_objectContainer);
#endif
				OpenRooms.Clear();
				CurrentRoom = null;
				IsSearchingForRooms = false;

				try
				{
					if (_sender != null)
					{
						_sender.Client.Shutdown(SocketShutdown.Both);
						_sender.Close();
						_sender.Dispose();
					}
					if (_listener != null)
					{
						_listener.Client.Shutdown(SocketShutdown.Both);
						_listener.Close();
						_listener.Dispose();
					}
				}
				catch (ObjectDisposedException) { }

				// TODO : doesnt work on recompile, fix
				if (!IsRoomOwner && _scene.IsValid())
				{
#if UNITY_EDITOR
					EditorSceneManager.UnloadSceneAsync(_scene);
#else
                    SceneManager.UnloadSceneAsync(_scene);
#endif
				}
				IsRoomOwner = false;
			}
		}

		public void Update()
		{
			while (_mainThreadActions.Count > 0)
			{
				if (_mainThreadActions.TryDequeue(out Action action))
					action?.Invoke();
			}

			while (_receivedPackets.Count > 0)
			{
				if (_receivedPackets.TryDequeue(out SyncPacket packet))
				{
					switch (packet.Type)
					{
						case SyncPacketType.Heartbeat:
							UpdateHeartbeat(packet.Sender, packet.Data);
							break;
						case SyncPacketType.ClientCamera:
							UpdateClientCamera(packet.Sender, packet.Data);
							break;
						default: break;
					}
				}
			}

			_cameraUpdateDelay++;
#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView.camera.transform.hasChanged)
				CurrentCameraMoved(SceneView.lastActiveSceneView.camera.transform);
#else
			if (Camera.main.transform && Camera.main.transform.hasChanged)
				CurrentCameraMoved(Camera.current.transform);
#endif
		}

		public void ConnectToRoom(string roomname, string scenename, bool createroom = false)
		{
			if (IsConnected)
			{
				Debug.LogWarning($"The client is already connected!");
				return;
			}

#if UNITY_EDITOR
			if (EditorApplication.isPlaying)
			{
				Debug.LogWarning($"Can't connect to room while Editor is in Playmode");
				return;
			}
#endif

			// create/save scene if room is joined/created
			Scene scene = SceneManager.GetSceneByName(scenename);
			if (createroom)
			{
				if (OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
				{
					Debug.LogWarning($"The Room {roomname} already exists!");
					return;
				}
				if (!scene.IsValid() && !scene.isLoaded)
				{
					Debug.LogWarning($"The Scene {scenename} is not valid!");
					return;
				}
				_scene = scene;
				CurrentRoom = new(roomname);
				OpenRooms.TryAdd(CurrentRoom.Roomname, CurrentRoom);
				IsRoomOwner = true;
			}
			else
			{
				if (!OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
				{
					Debug.LogWarning($"The Room {roomname} doesn't exist yet!");
					return;
				}
				if (scene.IsValid() || scene.isLoaded)
				{
					Debug.LogWarning($"The Scene {scenename} already exists!");
					return;
				}
#if UNITY_EDITOR
				_scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_scene.name = scenename;
#else
				_scene = SceneManager.CreateScene(scenename);
#endif
				CurrentRoom = room;
				IsRoomOwner = false;
				// TODO : announce myself to room
			}
			SceneManager.SetActiveScene(_scene);

			// show/hide parent gameobject for sync objects
			_objectContainer = new GameObject("UnitySync Object Container").transform;
			if (!SyncSettings.Instance.ShowSyncObjectInHierarchy)
				_objectContainer.hideFlags = HideFlags.HideInHierarchy;
			SyncEvents.OnShowSyncObjectInHierarchyChanged += ShowOrHideSyncObject;

			_pauseEvent.Set();

#if UNITY_EDITOR
			ObjectChangeEvents.changesPublished += ChangesPublished;
			EditorApplication.playModeStateChanged += PreventPlayMode;
#endif
			SyncManager.OnUpdate += Update;
			SyncManager.AddSyncMessage(new("Client connected to Room!"));
		}

		public void DisconnectFromRoom()
		{
			if (!IsConnected)
				return;

			_pauseEvent.Reset();


#if UNITY_EDITOR
			ObjectChangeEvents.changesPublished -= ChangesPublished;
			EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif
			SyncEvents.OnShowSyncObjectInHierarchyChanged -= ShowOrHideSyncObject;

			foreach (SyncConnectedClient client in CurrentRoom.ConnectedClients.Values)
				client.Dispose();
#if UNITY_EDITOR
			GameObject.DestroyImmediate(_objectContainer.gameObject);
#else
			GameObject.Destroy(_objectContainer.gameObject);
#endif

			if (CurrentRoom.ConnectedClients.Count == 0)
				OpenRooms.TryRemove(CurrentRoom.Roomname, out SyncOpenRoom _);
			CurrentRoom = null;

			if (!IsRoomOwner && _scene.IsValid())
			{
#if UNITY_EDITOR
				EditorSceneManager.UnloadSceneAsync(_scene);
#else
                SceneManager.UnloadSceneAsync(_scene);
#endif
			}
			IsRoomOwner = false;
			SyncManager.AddSyncMessage(new("Client Disconnected from Room!"));
		}

		#endregion

		#region helper methods

		private string GetLocalIPAddress()
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

		private void ShowOrHideSyncObject(bool val)
		{
			_objectContainer.hideFlags = val ? HideFlags.None : HideFlags.HideInHierarchy;
		}

		private static byte[] GetBytesFromArray(byte[] array, int offset, int size)
		{
			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

#if UNITY_EDITOR
		private static void PreventPlayMode(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
				EditorApplication.isPlaying = false;
		}
#endif

		private static void AddDebugByteMessage(byte[] bytes, string msg, bool inBinary = false)
		{
			foreach (byte d in bytes)
				msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			SyncManager.AddSyncMessage(new(msg, true));
		}

		#endregion
	}
}
