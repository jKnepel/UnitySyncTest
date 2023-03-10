using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
	public partial class SyncClient : IDisposable
	{
		#region packet constants

		private const uint PROTOCOL_ID = 876237842;

		// headers
		private const int PROTOCOL_ID_LENGTH = 4;
		private const int CRC32_LENGTH = 4; // checksum and contains protocol and room id
		private const int DATA_TYPE_LENGTH = 1; // id of the packet type
		private const int SEQUENCE_ID_LENGTH = 2; // sequence id for preventing old updates to be consumed

		// data
		// private const int DATA_FLAG_LENGTH = 2; // flag containing data length
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

		private Scene _syncScene;
		private Scene _localScene;
		private Transform _clientVisualizerParent;
		private GameObject _selectedGameObject;

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
				if (SyncSettings.Instance.IP.Length == 0)
					throw(new SocketException());

				Scene oldLocalScene = SceneManager.GetSceneByName(SyncSettings.Instance.SyncLocalScenename);
				if (oldLocalScene != null && oldLocalScene.IsValid())
				{
#if UNITY_EDITOR
					EditorSceneManager.UnloadSceneAsync(oldLocalScene);
#else
					SceneManager.UnloadSceneAsync(oldLocalScene);
#endif
				}

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
						Debug.LogError(ex.ToString());
						break;
					case ArgumentOutOfRangeException:
						Debug.LogError(ex.ToString());
						break;
					case ThreadStartException:
						Debug.LogError(ex.ToString());
						break;
					case OutOfMemoryException:
						Debug.LogError(ex.ToString());
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

					if (_localScene.IsValid())
					{
#if UNITY_EDITOR
						EditorSceneManager.UnloadSceneAsync(_localScene);
#else
						SceneManager.UnloadSceneAsync(_localScene);
#endif
					}

					if (!IsRoomOwner && _syncScene.IsValid())
					{
#if UNITY_EDITOR
						EditorSceneManager.UnloadSceneAsync(_syncScene);
#else
						SceneManager.UnloadSceneAsync(_syncScene);
#endif
					}

					OpenRooms.Clear();
					CurrentRoom = null;
					IsSearchingForRooms = false;
					IsRoomOwner = false;
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
				if (_receivedPackets.TryDequeue(out SyncPacket packet))
				{
					switch (packet.Type)
					{
						case SyncPacketType.ClientCamera:
							UpdateClientVisualizer(packet.Sender, packet.Data);
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
				_syncScene = scene;
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
				_syncScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_syncScene.name = scenename;
#else
				_syncScene = SceneManager.CreateScene(scenename);
#endif
				CurrentRoom = room;
				IsRoomOwner = false;
				// TODO : announce myself to room
			}
			SceneManager.SetActiveScene(_syncScene);

#if UNITY_EDITOR
			_localScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
			_localScene.name = SyncSettings.Instance.SyncLocalScenename;
#else
			_localScene = SceneManager.CreateScene(SyncSettings.Instance.SyncLocalScenename);
#endif
			_clientVisualizerParent = new GameObject("Cient Visualizer Parent").transform;
			SceneManager.MoveGameObjectToScene(_clientVisualizerParent.gameObject, _localScene);

			_pauseEvent.Set();

#if UNITY_EDITOR
			Selection.selectionChanged += SelectionChanged;
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
			Selection.selectionChanged -= SelectionChanged;
			ObjectChangeEvents.changesPublished -= ChangesPublished;
			EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif

			// TODO : throws scene is not loaded
			if (_localScene.IsValid())
			{
#if UNITY_EDITOR
				EditorSceneManager.UnloadSceneAsync(_localScene);
#else
				SceneManager.UnloadSceneAsync(_localScene);
#endif
			}

			if (!IsRoomOwner && _syncScene.IsValid())
			{
#if UNITY_EDITOR
				EditorSceneManager.UnloadSceneAsync(_syncScene);
#else
                SceneManager.UnloadSceneAsync(_syncScene);
#endif
			}

			if (CurrentRoom.ConnectedClients.Count == 0)
				OpenRooms.TryRemove(CurrentRoom.Roomname, out SyncOpenRoom _);
			CurrentRoom = null;
			IsRoomOwner = false;
			SyncManager.AddSyncMessage(new("Client Disconnected from Room!"));
		}

		#endregion

		#region helper methods

		private static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
		{
			if (size == 0)
				size = array.Length - offset;

			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

#if UNITY_EDITOR
		private void SelectionChanged()
		{
			// remove highlighting from old selection
			// TODO : doesnt work when leaving room
			if (_selectedGameObject != null)
			{
				Renderer renderer = _selectedGameObject.GetComponent<MeshRenderer>();
				for (int i = renderer.sharedMaterials.Length - 1; i >= 0; i--)
				{
					if (renderer.sharedMaterials[i].name.Equals(SyncSettings.Instance.HighlightMaterial.name))
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
			GameObject selection = Selection.activeGameObject;
			if (selection != null && selection.scene == _syncScene)
			{
				_selectedGameObject = selection;
				MeshRenderer renderer = _selectedGameObject.GetComponent<MeshRenderer>();
				Material[] materials = new Material[renderer.sharedMaterials.Length + 1];
				renderer.sharedMaterials.CopyTo(materials, 0);
				Material newMaterial = new(SyncSettings.Instance.HighlightMaterial);
				newMaterial.SetColor("_Color", SyncSettings.Instance.Color);
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
			if (!SyncSettings.Instance.IsDebug)
				return;

			foreach (byte d in bytes)
				msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			Debug.Log(msg);
		}

		#endregion
	}
}
