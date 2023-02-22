using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
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
		private readonly UdpClient _sender;
		private readonly UdpClient _listener;
		private readonly IPEndPoint _broadCastEndpoint;
		private IPEndPoint _receiveEndpoint;
		private string _localIPAddress;

		private Thread _senderThread;
		private Thread _listenerThread;

		private Scene _exportScene;

		private readonly ConcurrentQueue<Action> _mainThreadActions = new();
		private readonly ConcurrentQueue<SyncPacket> _packetsToSend = new();
		private readonly ConcurrentQueue<SyncPacket> _receivedPackets = new();

		// TODO : replace transform with client struct for names, position etc.
		private readonly ConcurrentDictionary<string, Transform> _connectedClients = new();

		private Scene _scene;

		private Vector3 _lastCameraPosition;
		private Vector3 _lastCameraRotation;
		private int _cameraUpdateDelay = 1000;

		private readonly Action<ExceptionDispatchInfo> _connectionAbolished;
		private int _disposeCount;

		#region constants

		public const uint PROTOCOL_ID = 876237842;
		public const int PROTOCOL_ID_LENGTH = 4;
		// TODO : public const int ROOM_NAME_LENGTH = ?
		public const int CRC32_LENGTH = 4; // checksum and contains protocol and room id
		// TODO : public const int SEQUENCE_LENGTH = 4;
		public const int DATA_TYPE_LENGTH = 1; // id of the packet type
		public const int DATA_FLAG_LENGTH = 4; // flag containing data length

		#endregion

		#region lifecycle

		public SyncClient(int port, Scene exportScene, string sceneName, Action<ExceptionDispatchInfo> connectionAbolished)
		{
			try
			{
				_connectionAbolished = connectionAbolished;

				FindLocalIPAddress();

				_sender = new();
				_sender.EnableBroadcast = true;
				_listener = new(port);
				_listener.EnableBroadcast = true;

				_broadCastEndpoint = new(IPAddress.Broadcast, port);
				_receiveEndpoint = new(IPAddress.Any, port);

#if UNITY_EDITOR
				_scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_scene.name = sceneName;
#else
                _scene = SceneManager.CreateScene(sceneName);
#endif
				SceneManager.SetActiveScene(_scene);
				_exportScene = exportScene;

				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();
				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
			}
			catch (Exception ex)
			{
				_connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
				return;
			}

#if UNITY_EDITOR
			ObjectChangeEvents.changesPublished += ChangesPublished;
			EditorApplication.playModeStateChanged += PreventPlayMode;
#endif
			SyncManager.OnUpdate += Update;
			SyncEvents.onIsConnectedChanged?.Invoke(true);
			SyncManager.AddDebugMessage("Client connected to Server");
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
					if (_senderThread != null)
					{
						_senderThread = null;
					}
					if (_listenerThread != null)
					{
						_listenerThread = null;
					}

#if UNITY_EDITOR
					ObjectChangeEvents.changesPublished -= ChangesPublished;
					EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif
					SyncManager.OnUpdate -= Update;
					SyncEvents.onIsConnectedChanged?.Invoke(false);
					SyncManager.AddDebugMessage("Connection was closed");
				}

				foreach (Transform client in _connectedClients.Values)
				{
#if UNITY_EDITOR
					GameObject.DestroyImmediate(client.gameObject);
#else
					GameObject.Destroy(client.gameObject);
#endif
				}

				try
				{
					if (_sender != null)
					{
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
				if (_scene.IsValid())
				{
#if UNITY_EDITOR
					EditorSceneManager.UnloadSceneAsync(_scene);
#else
                    SceneManager.UnloadSceneAsync(_scene);
#endif
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
				// TODO : implement different functionalities depending on packet type
				if (_receivedPackets.TryDequeue(out SyncPacket packet))
				{
					switch (packet.Type)
					{
						case SyncPacketType.ClientCamera:
							UpdateEditorCamera(packet.Sender, packet.Data);
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

		#endregion

		#region network logic

		#region send logic

		private void SenderThread()
		{
			try
			{
				while (_disposeCount == 0)
				{
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
				if (_disposeCount == 0) _connectionAbolished?.Invoke(null);
			}
			catch (Exception ex)
			{
				if (_disposeCount == 0) _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
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

		private void CalculateAndAddChecksum(byte[] packet)
		{
			// calculate crc32 checksum from packet and add it to the start
			byte[] checksum = GetBytesFromArray(packet, CRC32_LENGTH, packet.Length - CRC32_LENGTH);
			Array.Copy(GetCRCChecksumBytes(checksum), 0, packet, 0, CRC32_LENGTH);
		}

		#endregion

		#region receive logic

		private void ListenerThread()
		{
			try
			{
				while (_disposeCount == 0)
				{
					// check if packet comes from other client
					byte[] receivedBytes = _listener.Receive(ref _receiveEndpoint);
					string sender = _receiveEndpoint.Address.ToString();
					if (sender.Equals(_localIPAddress) && !SyncSettings.Instance.AllowReceivingOwnPackets)
						continue;

					// check crc32 checksum
					byte[] checksum = GetBytesFromArray(receivedBytes, 0, CRC32_LENGTH);
					DebugBytesLog(checksum, "Received Checksum: ");
					byte[] remainder = GetBytesFromArray(receivedBytes, CRC32_LENGTH, receivedBytes.Length - checksum.Length);
					DebugBytesLog(remainder, "Received Data: ");
					if (!checksum.SequenceEqual(GetCRCChecksumBytes(remainder)))
						continue;

					// handle data depending on packet type
					DebugBytesLog(receivedBytes, "Received Bytes: ");
					switch ((SyncPacketType)remainder[0])
					{
						case SyncPacketType.ClientCamera:
							ReceiveClientCameraBytes(sender, GetBytesFromArray(remainder, 1, 24));
							break;
						default: break;
					}
				}
				if (_disposeCount == 0) _connectionAbolished?.Invoke(null);
			}
			catch (Exception ex)
			{
				if (_disposeCount == 0) _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
			}
		}

		private void ReceiveClientCameraBytes(string sender, byte[] bytes)
		{
			_receivedPackets.Enqueue(new(SyncPacketType.ClientCamera, sender, bytes));
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
				Task.Delay(SyncSettings.Instance.GameObjectAndComponentUpdateDelay)
					.ContinueWith(t => ObjectChangeEvents.changesPublished += ChangesPublished);
			}

			// TODO : add functionality for sending hierarchy only on hierarchy changes
		}
#endif

		private void CurrentCameraMoved(Transform camera)
		{
			// limit camera syncs
			if (_cameraUpdateDelay < SyncSettings.Instance.ClientCameraUpdateDelay)
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

		// TODO : optimize this
		private void UpdateEditorCamera(string sender, byte[] data)
		{
			int size = sizeof(float);
			byte[] positionX = GetBytesFromArray(data, 0 * size, size);
			byte[] positionY = GetBytesFromArray(data, 1 * size, size);
			byte[] positionZ = GetBytesFromArray(data, 2 * size, size);
			byte[] rotationX = GetBytesFromArray(data, 3 * size, size);
			byte[] rotationY = GetBytesFromArray(data, 4 * size, size);
			byte[] rotationZ = GetBytesFromArray(data, 5 * size, size);

			if (_connectedClients.TryGetValue(sender, out Transform camera))
			{
				if (camera == null)
				{
					_connectedClients.TryRemove(sender, out Transform _);
					return;
				}

				camera.position = new Vector3(BitConverter.ToSingle(positionX),
					BitConverter.ToSingle(positionY),
					BitConverter.ToSingle(positionZ));
				camera.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
					BitConverter.ToSingle(rotationY),
					BitConverter.ToSingle(rotationZ));
				return;
			}

			GameObject obj = GameObject.Instantiate(SyncSettings.Instance.ClientTransformPrefab);
			if (!SyncSettings.Instance.ShowSyncObjectInHiearchy) obj.hideFlags = HideFlags.HideInHierarchy;
			obj.name = sender;
			obj.transform.GetChild(0).GetChild(0).GetComponent<TMPro.TMP_Text>().text = sender;
			obj.transform.position = new Vector3(BitConverter.ToSingle(positionX),
				BitConverter.ToSingle(positionY),
				BitConverter.ToSingle(positionZ));
			obj.transform.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
				BitConverter.ToSingle(rotationY),
				BitConverter.ToSingle(rotationZ));
			_connectedClients.TryAdd(sender, obj.transform);
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
				_localIPAddress = endPoint.Address.ToString();
			}
		}

		private byte[] GetCRCChecksumBytes(byte[] data)
		{
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + data.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(data, 0, bytes, PROTOCOL_ID_LENGTH, data.Length);
			return BitConverter.GetBytes(SyncCRC32.CRC32Bytes(bytes));
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
			string debugOut = msg;
			foreach (byte d in bytes)
			{
				debugOut += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			}
			SyncManager.AddDebugMessage(debugOut);
		}

		#endregion

	}
}
