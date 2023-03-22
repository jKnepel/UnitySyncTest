using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace jKnepel.UnitySync
{
	internal sealed partial class SyncClient : IDisposable
	{
		#region packet constants

		private const uint PROTOCOL_ID = 876237842;

		// headers
		private const int PROTOCOL_ID_LENGTH = 4;
		private const int CRC32_LENGTH = 4; // checksum and contains protocol and room id
		private const int DATA_TYPE_LENGTH = 1; // id of the packet type
		private const int SEQUENCE_ID_LENGTH = 2; // sequence id for preventing old updates to be consumed
		private const int NUMBER_OF_SLICES = 2; // flag showing the number of packets the chunk consists of
		private const int SLICE_NUMBER = 2; // number of the current slice

		// data
		// private const int DATA_FLAG_LENGTH = 2; // flag containing data length
		private const int ROOMNAME_FLAG_LENGTH = 1;
		private const int USERNAME_FLAG_LENGTH = 1;
		private const int COLOR_LENGTH = 3;

		#endregion

		#region private members

		private static readonly SyncSettings _settings = SyncSettings.GetOrCreateSettings();

		private readonly IPAddress _ip;
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
		private readonly ConcurrentQueue<SyncSenderPacket> _packetsToSend = new();
		private readonly ConcurrentQueue<SyncReceiverPacket> _receivedPackets = new();

		private Vector3 _lastCameraPosition;
		private Vector3 _lastCameraRotation;
		private int _cameraUpdateDelay = _settings.ClientVisualizerUpdateDelay;

		private GameObject _selectedGameObject;

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
				_ip = IPAddress.Parse(_settings.IP);

				if (_settings.SyncParentContainer != null)
				{
#if UNITY_EDITOR
					GameObject.DestroyImmediate(_settings.SyncParentContainer.gameObject);
#else
					GameObject.Destroy(_settings.SyncParentContainer.gameObject);
#endif
					_settings.SyncParentContainer = null;
				}

				_listener = new(_settings.Port);
				_listener.EnableBroadcast = true;
				_sender = new();
				_sender.EnableBroadcast = true;

				_broadCastEndpoint = new(IPAddress.Broadcast, _settings.Port);
				_receiveEndpoint = new(IPAddress.Any, _settings.Port);

				SyncManager.OnUpdate += Update;

				_pauseEvent.Reset();

				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();
				_heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
				_heartbeatThread.Start();

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
				OpenRooms.Clear();
				CurrentRoom = null;
				IsRoomOwner = false;
				IsConnectedToRoom = false;

				_listenerThread.Abort();
				_listenerThread.Join();
				_senderThread.Abort();
				_senderThread.Join();
				_heartbeatThread.Abort();
				_heartbeatThread.Join();

				_pauseEvent.Dispose();

				if (_sender != null)
				{
					_sender.Close();
					_sender.Dispose();
				}
				if (_listener != null)
				{
					_listener.Close();
					_listener.Dispose();
				}

				if (disposing)
				{
#if UNITY_EDITOR
					Selection.selectionChanged -= SelectionChanged;
					ObjectChangeEvents.changesPublished -= ChangesPublished;
					EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif
					SyncManager.OnUpdate -= Update;

					if (_settings.SyncParentContainer != null)
					{
#if UNITY_EDITOR
						GameObject.DestroyImmediate(_settings.SyncParentContainer.gameObject);
#else
						GameObject.Destroy(_settings.SyncParentContainer.gameObject);
#endif
						_settings.SyncParentContainer = null;
					}

					if (!IsRoomOwner && !_settings.KeepSyncScene && _settings.SyncScene.IsValid())
					{
#if UNITY_EDITOR
						EditorSceneManager.UnloadSceneAsync(_settings.SyncScene);
#else
						SceneManager.UnloadSceneAsync(_settings.SyncScene);
#endif
					}
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

			while (_receivedPackets.Count > 0)
			{
				if (_receivedPackets.TryDequeue(out SyncReceiverPacket packet))
				{
					switch (packet.Type)
					{
						case SyncPacketType.ClientCamera:
							UpdateClientVisualizer(packet);
							break;
						case SyncPacketType.ReliableData:
							ReceiveReliableData(packet);
							break;
						case SyncPacketType.ReliableUnorderedData:
							ReceiveReliableUnorderedData(packet);
							break;
						case SyncPacketType.UnreliableData:
							ReceiveUnreliableData(packet);
							break;
						case SyncPacketType.UnreliableUnorderedData:
							ReceiveUnreliableUnorderedData(packet);
							break;
						case SyncPacketType.Hierarchy:
							ImportHierarchy(packet.Data);
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
			if (Camera.current.transform && Camera.current.transform.hasChanged)
				CurrentCameraMoved(Camera.current.transform);
#endif
		}

		public bool ConnectToRoom(string roomname, string scenename, bool createroom = false)
		{
			if (IsConnectedToRoom)
			{
				Debug.LogWarning($"The client is already connected!");
				return false;
			}

#if UNITY_EDITOR
			if (EditorApplication.isPlaying)
			{
				Debug.LogWarning($"Can't connect to room while Editor is in Playmode!");
				return false;
			}
#endif

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
			Scene scene = SceneManager.GetSceneByName(scenename);
			if (createroom)
			{
				if (OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
				{
					Debug.LogWarning($"The Room {roomname} already exists!");
					return false;
				}
				if (!scene.IsValid() && !scene.isLoaded)
				{
					Debug.LogWarning($"The Scene {scenename} is not valid!");
					return false;
				}
				_settings.SyncScene = scene;
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
				if (scene.IsValid() || scene.isLoaded)
				{
					Debug.LogWarning($"The Scene {scenename} already exists!");
					return false;
				}

#if UNITY_EDITOR
				_settings.SyncScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_settings.SyncScene.name = scenename;
#else
				_settings.SyncScene = SceneManager.CreateScene(scenename);
#endif
				CurrentRoom = room;
				IsRoomOwner = false;
				// TODO : announce myself to room
			}
			SceneManager.SetActiveScene(_settings.SyncScene);
			_settings.SyncParentContainer = new GameObject("Cient Visualizer Parent").transform;

			_pauseEvent.Set();

#if UNITY_EDITOR
			//Selection.selectionChanged += SelectionChanged;
			ObjectChangeEvents.changesPublished += ChangesPublished;
			EditorApplication.playModeStateChanged += PreventPlayMode;
#endif
			SyncManager.OnUpdate += Update;
			IsConnectedToRoom = true;
			SyncManager.AddSyncMessage(new("Client connected to Room!"));
			return true;
		}

		public bool DisconnectFromRoom()
		{
			if (!IsConnectedToRoom)
				return false;

			_pauseEvent.Reset();

#if UNITY_EDITOR
			Selection.selectionChanged -= SelectionChanged;
			ObjectChangeEvents.changesPublished -= ChangesPublished;
			EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif

			if (_settings.SyncParentContainer != null)
			{
#if UNITY_EDITOR
				GameObject.DestroyImmediate(_settings.SyncParentContainer.gameObject);
#else
				GameObject.Destroy(_settings.SyncParentContainer.gameObject);
#endif
				_settings.SyncParentContainer = null;
			}

			if (!IsRoomOwner && !_settings.KeepSyncScene && _settings.SyncScene.IsValid())
			{
#if UNITY_EDITOR
				EditorSceneManager.UnloadSceneAsync(_settings.SyncScene);
#else
                SceneManager.UnloadSceneAsync(_settings.SyncScene);
#endif
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

		// TODO : add manual sync Hierarchy

		// TODO : add manual delete Hierarchy

		// TODO : add manual sync GameObject

		// TODO : add manual delete GameObject

		// TODO : add manual sync Component

		// TODO : add manual delete Component

		public void SendDataReliable(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.ReliableData, data, onDataSend, receiver));
		}

		public void SendDataReliableUnordered(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.ReliableUnorderedData, data, onDataSend, receiver));
		}

		public void SendDataUnreliable(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.UnreliableData, data, onDataSend, receiver));
		}

		public void SendDataUnreliableUnordered(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (IsConnectedToRoom || receiver != null && !CheckReceiver(receiver))
			{
				onDataSend?.Invoke(false);
				return;
			}

			_packetsToSend.Enqueue(new(SyncPacketType.UnreliableUnorderedData, data, onDataSend, receiver));
		}

		#endregion

		#region helper methods

		private bool CheckReceiver(SyncConnectedClient receiver)
		{
			return IsConnectedToRoom
				&& CurrentRoom.ConnectedClients.TryGetValue(receiver.IP, out _);
		}

		private bool IsASCIIString(string str)
		{
			return (Encoding.UTF8.GetByteCount(str)) == str.Length;
		}

		private static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
		{
			if (size == 0)
				size = array.Length - offset;

			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

#if UNITY_EDITOR
		// NOTE : test method for when object highlighting is implemented over network
		private void SelectionChanged()
		{
			// remove highlighting from old selection
			// TODO : doesnt work when leaving room
			if (_selectedGameObject != null)
			{
				Renderer renderer = _selectedGameObject.GetComponent<MeshRenderer>();
				for (int i = renderer.sharedMaterials.Length - 1; i >= 0; i--)
				{
					if (renderer.sharedMaterials[i].name.Equals(_settings.HighlightMaterial.name))
					{
						Material[] materials = new Material[renderer.sharedMaterials.Length - 1];
						if (i > 0)
							Array.Copy(renderer.sharedMaterials, 0, materials, 0, i);
						if (i < renderer.sharedMaterials.Length - 1)
							Array.Copy(renderer.sharedMaterials, i + 1, materials, i, renderer.sharedMaterials.Length - i - 1);
						renderer.sharedMaterials = materials;
						break;
					}
				}
			}

			// highlight selected object
			// TODO : doesnt work on gameobjects without renderer
			GameObject selection = Selection.activeGameObject;
			if (selection != null && selection.scene == _settings.SyncScene)
			{
				_selectedGameObject = selection;
				MeshRenderer renderer = _selectedGameObject.GetComponent<MeshRenderer>();
				Material[] materials = new Material[renderer.sharedMaterials.Length + 1];
				renderer.sharedMaterials.CopyTo(materials, 0);
				Material newMaterial = new(_settings.HighlightMaterial);
				newMaterial.SetColor("_Color", _settings.Color);
#if UNITY_EDITOR
				newMaterial.SetFloat("_Width", 1f);
#else
				newMaterial.SetFloat("_Width", 0.1f);
#endif
				materials[^1] = newMaterial;
				renderer.sharedMaterials = materials;
			}
			else
			{
				_selectedGameObject = null;
			}
		}

		private static void PreventPlayMode(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
				EditorApplication.isPlaying = false;
		}
#endif

		private static void DebugByteMessage(byte[] bytes, string msg, bool inBinary = false)
		{
			if (!_settings.IsDebug)
				return;

			foreach (byte d in bytes)
				msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			Debug.Log(msg);
		}

		#endregion
	}
}
