using System;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using GLTF;
using GLTF.Schema;
using UnityGLTF;
using Toph.GLTFSerializer.Components;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace jKnepel.UnitySync
{
	public class SyncClient : IDisposable
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
		private Scene _exportScene;
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
				FindLocalIPAddress();

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
						SyncManager.AddDebugMessage("SocketException " + ex.ToString());
						break;
					case ArgumentOutOfRangeException:
						SyncManager.AddDebugMessage("ArgumentOutOfRangeException " + ex.ToString());
						break;
					case ThreadStartException:
						SyncManager.AddDebugMessage("ThreadStartException " + ex.ToString());
						break;
					case OutOfMemoryException:
						SyncManager.AddDebugMessage("OutOfMemoryException " + ex.ToString());
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
				_ = TimeoutRoom(CurrentRoom);
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
				CurrentRoom = new(roomname);
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
			SyncManager.AddDebugMessage("Client connected to Room!");
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
			Debug.Log("Client Disconnected from Room!");
		}

		#endregion

		#region network logic

		#region send logic

		private void HeartbeatThread()
		{
			try
			{
				while (_disposeCount == 0)
				{
					_pauseEvent.WaitOne(Timeout.Infinite);

					byte[] username = Encoding.ASCII.GetBytes(SyncSettings.Instance.Username);
					byte[] color = { SyncSettings.Instance.Color.r, SyncSettings.Instance.Color.g, SyncSettings.Instance.Color.b };
					int currentPosition = CRC32_LENGTH;

					// prepare data with room name, username and -color
					byte[] data = new byte[CRC32_LENGTH + DATA_TYPE_LENGTH +
						ROOMNAME_FLAG_LENGTH + CurrentRoom.RoomnameBytes.Length +
						USERNAME_FLAG_LENGTH + username.Length +
						COLOR_LENGTH];
					data[currentPosition] = (byte)SyncPacketType.Heartbeat;
					currentPosition += DATA_TYPE_LENGTH;
					data[currentPosition] = (byte)CurrentRoom.RoomnameBytes.Length;
					currentPosition += ROOMNAME_FLAG_LENGTH;
					Array.Copy(CurrentRoom.RoomnameBytes, 0, data, currentPosition, CurrentRoom.RoomnameBytes.Length);
					currentPosition += CurrentRoom.RoomnameBytes.Length;
					data[currentPosition] = (byte)username.Length;
					currentPosition += USERNAME_FLAG_LENGTH;
					Array.Copy(username, 0, data, currentPosition, username.Length);
					currentPosition += username.Length;
					Array.Copy(color, 0, data, currentPosition, COLOR_LENGTH);

					CalculateAndAddChecksum(data);

					_sender.Send(data, data.Length, _broadCastEndpoint);
					DebugBytesLog(data, "Send Heartbeat Bytes: ");
					Thread.Sleep(SyncSettings.Instance.HeartbeatDelay);
				}
			}
			catch (Exception ex)
			{
				CurrentRoom = null;
				IsSearchingForRooms = false;
				switch (ex)
				{
					case SocketException:
						SyncManager.AddDebugMessage("Socket Exception " + ex.ToString());
						break;
					case ObjectDisposedException:
						SyncManager.AddDebugMessage("Object Disposed Exception " + ex.ToString());
						break;
					default:
						throw;
				}
			}
		}

		private void SenderThread()
		{
			while (_disposeCount == 0)
			{
				_pauseEvent.WaitOne(Timeout.Infinite);

				if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out SyncPacket packet))
					continue;

				switch (packet.Type)
				{
					case SyncPacketType.ClientCamera:
						SendClientCameraBytes(packet.Data);
						break;
					default: break;
				}
			}
		}

		private void SendClientCameraBytes(byte[] bytes)
		{
			byte[] data = new byte[CRC32_LENGTH + DATA_TYPE_LENGTH + bytes.Length];
			// TODO : add sequence ids
			data[CRC32_LENGTH] = (byte)SyncPacketType.ClientCamera;
			Array.Copy(bytes, 0, data, CRC32_LENGTH + DATA_TYPE_LENGTH, bytes.Length);

			CalculateAndAddChecksum(data);

			_sender.Send(data, data.Length, _broadCastEndpoint);
			DebugBytesLog(data, "Send Camera Bytes: ");
		}

		#endregion

		#region receive logic

		private void ListenerThread()
		{
			while (_disposeCount == 0)
			{
				// check if packet comes from other client
				byte[] receivedBytes = _listener.Receive(ref _receiveEndpoint);
				string sender = _receiveEndpoint.Address.ToString();
				if (sender.Equals(_localIpAddress) && !SyncSettings.Instance.AllowReceivingOwnPackets)
					continue;

				// check crc32 checksum with roomname
				byte[] checksum = GetBytesFromArray(receivedBytes, 0, CRC32_LENGTH);
				byte[] remainder = GetBytesFromArray(receivedBytes, CRC32_LENGTH,
					receivedBytes.Length - CRC32_LENGTH);
				byte[] roomnameBytes;
				if (!IsConnected)
				{
					if ((SyncPacketType)remainder[0] != SyncPacketType.Heartbeat)
						continue;

					byte roomnameLength = receivedBytes[CRC32_LENGTH + DATA_TYPE_LENGTH];
					roomnameBytes = GetBytesFromArray(receivedBytes,
						CRC32_LENGTH + DATA_TYPE_LENGTH + ROOMNAME_FLAG_LENGTH, roomnameLength);
				}
				else
				{
					roomnameBytes = CurrentRoom.RoomnameBytes;
				}
				if (BitConverter.ToUInt32(checksum) != CalculatePacketChecksum(remainder, roomnameBytes))
					continue;

				// handle data depending on packet type
				DebugBytesLog(receivedBytes, "Received Bytes: ");
				byte[] data = GetBytesFromArray(remainder, 1, remainder.Length - 1);
				switch ((SyncPacketType)remainder[0])
				{
					case SyncPacketType.Heartbeat:
						_receivedPackets.Enqueue(new(SyncPacketType.Heartbeat, sender, data));
						break;
					case SyncPacketType.ClientCamera:
						_receivedPackets.Enqueue(new(SyncPacketType.ClientCamera, sender, data));
						break;
					default: break;
				}
			}
		}

		#endregion

		#endregion

		#region notify updates

#if UNITY_EDITOR
		private void ChangesPublished(ref ObjectChangeEventStream stream)
		{
			ChangeGameObjectOrComponentPropertiesEventArgs obj;
			stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
			if (obj.scene == _exportScene)
			{
				UnityEngine.Object o = EditorUtility.InstanceIDToObject(obj.instanceId);
				GameObject go;
				try { go = ((Component)o).gameObject; }
				catch (InvalidCastException) { go = (GameObject)o; }
				//SendHierarchy(go); // TODO : replace with functionality for only sending gameobject/component

				// Limit hierarchy syncs
				ObjectChangeEvents.changesPublished -= ChangesPublished;
				Task.Delay(SyncSettings.Instance.EditorUpdatesDelay)
					.ContinueWith(t => ObjectChangeEvents.changesPublished += ChangesPublished);
			}

			// TODO : add functionality for sending hierarchy only on hierarchy changes
		}
#endif

		private void CurrentCameraMoved(Transform camera)
		{
			// limit camera syncs
			if (_cameraUpdateDelay < SyncSettings.Instance.ClientVisualizerUpdateDelay)
			{
				camera.hasChanged = false;
				return;
			}

			if (_lastCameraPosition != null
				&& camera.position.Equals(_lastCameraPosition)
				&& camera.eulerAngles.Equals(_lastCameraRotation))
			{
				camera.hasChanged = false;
				return;
			}

			// TODO : optimize this
			byte[] bytes = new byte[sizeof(float) * 6];
			Array.Copy(BitConverter.GetBytes(camera.position.x), 0, bytes, 0 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.position.y), 0, bytes, 1 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.position.z), 0, bytes, 2 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.x), 0, bytes, 3 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.y), 0, bytes, 4 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.z), 0, bytes, 5 * sizeof(float), sizeof(float));
			_packetsToSend.Enqueue(new(SyncPacketType.ClientCamera, bytes));

			_lastCameraPosition = camera.position;
			_lastCameraRotation = camera.eulerAngles;
			camera.hasChanged = false;
			_cameraUpdateDelay = 0;
		}

		private async void SendHierarchy(GameObject go)
		{
			GLTFRootGameObject gltf = go.GetRootParent().GetOrAddComponent<GLTFRootGameObject>();
			byte[] bytes = await gltf.SerializeGameObject(new ExportOptions());
			_packetsToSend.Enqueue(new(SyncPacketType.Hierarchy, bytes));
		}

		#endregion

		#region consume updates

		private void UpdateHeartbeat(string sender, byte[] data)
		{
			// read roomname, username and -color from data
			int currentPosition = 0;
			int roomnameLength = data[currentPosition];
			currentPosition += ROOMNAME_FLAG_LENGTH;
			byte[] roomnameBytes = GetBytesFromArray(data, currentPosition, roomnameLength);
			string roomname = Encoding.ASCII.GetString(roomnameBytes);
			currentPosition += roomnameLength;
			int usernameLength = data[currentPosition];
			currentPosition += USERNAME_FLAG_LENGTH;
			byte[] usernameBytes = GetBytesFromArray(data, currentPosition, usernameLength);
			string username = Encoding.ASCII.GetString(usernameBytes);
			currentPosition += usernameLength;
			Color32 color = new(data[currentPosition], data[currentPosition + 1], data[currentPosition + 2], 255);

			// create room/client and add it to dictionary if it doesn't exist yet
			if (!OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
			{
				room = new(roomname);
				OpenRooms.TryAdd(roomname, room);
				_ = TimeoutRoom(room);
			}
			if (!room.ConnectedClients.TryGetValue(sender, out SyncConnectedClient client))
			{
				SyncClientVisualizer vis = null;
				if (IsConnected && CurrentRoom.Equals(room))
				{
					GameObject obj = GameObject.Instantiate(SyncSettings.Instance.ClientTransformPrefab, _objectContainer);
					vis = obj.GetComponent<SyncClientVisualizer>();
				}
				client = new(sender, username, vis, color);
				room.ConnectedClients.TryAdd(sender, client);
				_ = TimeoutClient(client, room);
			}

			// update client values
			client.Color = color;
			if (client.Visualizer) client.Visualizer.UpdateVisualizer(sender, username, color);
			room.LastHeartbeat = DateTime.Now;
			client.LastHeartbeat = DateTime.Now;
		}

		private async Task TimeoutClient(SyncConnectedClient client, SyncOpenRoom room)
		{
			while (_disposeCount == 0 && room.ConnectedClients.ContainsKey(client.IP))
			{
				if ((DateTime.Now - client.LastHeartbeat).TotalMilliseconds > SyncSettings.Instance.ClientTimeoutDelay)
				{
					client.Dispose();
					if (room.ConnectedClients.Count == 1)
						OpenRooms.TryRemove(room.Roomname, out SyncOpenRoom _);
					else
						room.ConnectedClients.TryRemove(client.IP, out SyncConnectedClient _);
					return;
				}
				await Task.Delay(SyncSettings.Instance.ClientTimeoutDelay);
			}
		}

		private async Task TimeoutRoom(SyncOpenRoom room)
		{
			while (_disposeCount == 0 && OpenRooms.ContainsKey(room.Roomname))
			{
				if (!room.Equals(CurrentRoom)
					&& (DateTime.Now - room.LastHeartbeat).TotalMilliseconds > SyncSettings.Instance.RoomTimeoutDelay)
				{
					OpenRooms.TryRemove(room.Roomname, out SyncOpenRoom _);
					return;
				}
				await Task.Delay(SyncSettings.Instance.RoomTimeoutDelay);
			}
		}

		// TODO : optimize this
		private void UpdateClientCamera(string sender, byte[] data)
		{
			int size = sizeof(float);
			byte[] positionX = GetBytesFromArray(data, 0 * size, size);
			byte[] positionY = GetBytesFromArray(data, 1 * size, size);
			byte[] positionZ = GetBytesFromArray(data, 2 * size, size);
			byte[] rotationX = GetBytesFromArray(data, 3 * size, size);
			byte[] rotationY = GetBytesFromArray(data, 4 * size, size);
			byte[] rotationZ = GetBytesFromArray(data, 5 * size, size);

			if (CurrentRoom.ConnectedClients.TryGetValue(sender, out SyncConnectedClient client))
			{
				client.LastHeartbeat = DateTime.Now;
				client.Visualizer.transform.position = new Vector3(BitConverter.ToSingle(positionX),
					BitConverter.ToSingle(positionY),
					BitConverter.ToSingle(positionZ));
				client.Visualizer.transform.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
					BitConverter.ToSingle(rotationY),
					BitConverter.ToSingle(rotationZ));
				if (!client.Visualizer.gameObject.activeSelf) 
					client.Visualizer.gameObject.SetActive(true);
				return;
			}
		}

		private async void ImportHierarchy(byte[] hierarchy)
		{
			Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
			{
				if (createdObject is null)
				{
					return;
				}
				SceneManager.MoveGameObjectToScene(createdObject, _scene);

				SyncManager.AddDebugMessage($"GameObject '{createdObject.name}' imported.");
			};

			using MemoryStream memoryStream = new();
			await memoryStream.WriteAsync(hierarchy);
			await DeserializeGameObject(_scene, new ImportOptions(), memoryStream, onLoadComplete);

			SyncManager.AddDebugMessage($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

			return;
		}

		private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
		{
			GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

			GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
			await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), SyncSettings.Instance.IsDebug, -1, true, onLoadComplete);
		}

		#endregion

		#region helper methods

		// TODO : doesnt work without running uplink, fix
		private void FindLocalIPAddress()
		{
			using (Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0))
			{
				socket.Connect("8.8.8.8", 65530);
				IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
				_localIpAddress = endPoint.Address.ToString();
			}
		}

		private void ShowOrHideSyncObject(bool val)
		{
			_objectContainer.hideFlags = val ? HideFlags.None : HideFlags.HideInHierarchy;
		}

		private uint CalculatePacketChecksum(byte[] packet, byte[] roomname = null)
		{
			if (roomname == null) roomname = CurrentRoom.RoomnameBytes;

			// calculate crc32 checksum from protocol id, roomname and data
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + roomname.Length + packet.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(roomname, 0, bytes, PROTOCOL_ID_LENGTH, roomname.Length);
			Array.Copy(packet, 0, bytes, PROTOCOL_ID_LENGTH + roomname.Length, packet.Length);
			return SyncCRC32.CRC32Bytes(bytes);
		}

		private void CalculateAndAddChecksum(byte[] packet)
		{
			// calculate crc32 checksum from packet and add it to the start
			uint checksum = CalculatePacketChecksum(GetBytesFromArray(packet, CRC32_LENGTH, packet.Length - CRC32_LENGTH));
			Array.Copy(BitConverter.GetBytes(checksum), 0, packet, 0, CRC32_LENGTH);
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
			{
				EditorApplication.isPlaying = false;
			}
		}
#endif

		private static void DebugBytesLog(byte[] bytes, string msg, bool inBinary = false)
		{
			foreach (byte d in bytes)
				msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			SyncManager.AddDebugMessage(msg);
		}

		#endregion

	}
}
