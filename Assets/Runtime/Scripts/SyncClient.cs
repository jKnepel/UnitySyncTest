using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
		private UdpClient _sender;
		private UdpClient _listener;
		private IPEndPoint _endpoint;

		private Thread _senderThread;
		private Thread _receiverThread;

		private Scene _exportScene;

		private readonly ConcurrentQueue<Action> _mainThreadActions = new();
		private readonly ConcurrentQueue<byte[]> _bytesToSend = new();
		private readonly ConcurrentQueue<byte[]> _receivedBytes = new();

		private Scene _scene;

		private readonly Action<ExceptionDispatchInfo> _connectionAbolished;
		private int _disposeCount;

		#region lifecycle

		public SyncClient(int port, Scene exportScene, string sceneName, Action<ExceptionDispatchInfo> connectionAbolished)
		{
			try
			{
				_connectionAbolished = connectionAbolished;

				_sender = new();
				_endpoint = new(IPAddress.Broadcast, port);

#if UNITY_EDITOR
				_scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				_scene.name = sceneName;
#else
                _scene = SceneManager.CreateScene(sceneName);
#endif
				SceneManager.SetActiveScene(_scene);
				_exportScene = exportScene;

				_senderThread = new(() => SocketSendThread()) { IsBackground = true };
				_senderThread.Start();
				_receiverThread = new(() => SocketReceiveThread()) { IsBackground = true };
				_receiverThread.Start();
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
					if (_receiverThread != null)
					{
						_receiverThread = null;
					}

#if UNITY_EDITOR
					ObjectChangeEvents.changesPublished -= ChangesPublished;
					EditorApplication.playModeStateChanged -= PreventPlayMode;
#endif
					SyncManager.OnUpdate -= Update;
					SyncEvents.onIsConnectedChanged?.Invoke(false);
					SyncManager.AddDebugMessage("Connection was closed");
				}

				try
				{
					if (_sender != null)
					{
						_sender.Client.Shutdown(SocketShutdown.Both);
						_sender.Close();
						_sender.Dispose();
					}
				}
				catch (ObjectDisposedException) { }

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
				bool success = _mainThreadActions.TryDequeue(out Action action);
				if (success) action?.Invoke();
			}

			while (_receivedBytes.Count > 0 && _scene.isLoaded)
			{
				bool success = _receivedBytes.TryDequeue(out byte[] bytes);
				if (success) ImportHierarchy(bytes);
			}
		}

		#endregion

		#region private methods

		private void SocketSendThread()
		{
			try
			{
				while(_disposeCount == 0)
				{
					if (_bytesToSend.Count == 0) continue;

					bool success = _bytesToSend.TryDequeue(out byte[] data);
					if (!success) continue;

					byte[] bytes = new byte[4 + 4 + data.Length];
					BitConverter.GetBytes(SyncManager.PROTOCOL_ID).CopyTo(bytes, 0);
					BitConverter.GetBytes((uint)data.Length).CopyTo(bytes, 4);
					data.CopyTo(bytes, 8);
					_sender.Send(bytes, bytes.Length, _endpoint);
					Debug.Log("Send");
				}
				if (_disposeCount == 0) _connectionAbolished?.Invoke(null);
			}
			catch (Exception ex)
			{
				if (_disposeCount == 0) _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
			}
		}

		private void SocketReceiveThread()
		{
			/*
			try
			{
				while (IsSocketConnected() && _disposeCount == 0)
				{
					byte[] protocolId = new byte[4];
					_client.Receive(protocolId, SocketFlags.None);
					if (BitConverter.ToUInt32(protocolId) != SyncManager.PROTOCOL_ID)
						continue;

					byte[] lengthBytes = new byte[4];
					_client.Receive(lengthBytes, SocketFlags.None);
					int dataLength = (int)BitConverter.ToUInt32(lengthBytes);
					if (dataLength <= 0)
						continue;

					byte[] receivedBytes = new byte[dataLength];
					for (int i = 0; i < dataLength; i++)
					{
						byte[] receivedByte = new byte[1];
						_client.Receive(receivedByte, SocketFlags.None);
						receivedBytes[i] = receivedByte[0];
					}
					SyncManager.AddDebugMessage("Received " + receivedBytes.Length + " Should have been " + dataLength);

					/*
					string debugOut = "Received Bytes: ";
					foreach (byte d in receivedBytes)
					{
						debugOut += Convert.ToString(d, 16).PadLeft(2, '0') + " ";
					}
					SyncManager.AddDebugMessage(debugOut);
					

					_receivedBytes.Enqueue(receivedBytes);
				}
				if (_disposeCount == 0) _connectionAbolished?.Invoke(null);
			}
			catch (Exception ex)
			{
				if (_disposeCount == 0) _connectionAbolished?.Invoke(ExceptionDispatchInfo.Capture(ex));
			}
			*/
		}

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
				SendHierarchy(go);
			}
		}
#endif

		private async void SendHierarchy(GameObject go)
		{
			GLTFRootGameObject gltf = go.GetRootParent().GetOrAddComponent<GLTFRootGameObject>();
			byte[] bytes = await gltf.SerializeGameObject(new ExportOptions());
			_bytesToSend.Enqueue(bytes);
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
			await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), SyncManager.IS_DEBUG, -1, true, onLoadComplete);
		}

		#endregion

		#region helper methods

#if UNITY_EDITOR
		private static void PreventPlayMode(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
			{
				EditorApplication.isPlaying = false;
			}
		}
#endif

		#endregion

	}
}
