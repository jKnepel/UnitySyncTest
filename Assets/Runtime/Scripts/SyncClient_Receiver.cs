using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using GLTF;
using GLTF.Schema;
using UnityGLTF;
using Toph.GLTFSerializer.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace jKnepel.UnitySync
{
	public partial class SyncClient
	{
		#region threads

		private void ListenerThread()
		{
			while (_disposeCount == 0)
			{
				try
				{
					// check if packet comes from other client
					byte[] receivedBytes = _listener.Receive(ref _receiveEndpoint);
					string sender = _receiveEndpoint.Address.ToString();
					if (sender.Equals(SyncSettings.Instance.IP))
						continue;

					// check crc32 checksum with roomname
					byte[] checksum = GetBytesFromArray(receivedBytes, 0, CRC32_LENGTH);
					byte[] remainder = GetBytesFromArray(receivedBytes, CRC32_LENGTH,
						receivedBytes.Length - CRC32_LENGTH);
					byte[] roomnameBytes;
					if (!IsConnected)
					{
						if (remainder[0] != (byte)SyncPacketType.Heartbeat)
							continue;

						byte roomnameLength = receivedBytes[CRC32_LENGTH + DATA_TYPE_LENGTH];
						roomnameBytes = GetBytesFromArray(receivedBytes,
							CRC32_LENGTH + DATA_TYPE_LENGTH + ROOMNAME_FLAG_LENGTH, roomnameLength);
					}
					else
					{
						roomnameBytes = CurrentRoom.RoomnameBytes;
					}
					if (CheckPacketCRC32(checksum, remainder, roomnameBytes))
						continue;

					// handle data depending on packet type
					AddDebugByteMessage(receivedBytes, "Received Bytes: ");
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
				catch (IndexOutOfRangeException) { continue; }
			}
		}

		#endregion

		#region packet receiving

		private void UpdateHeartbeat(string sender, byte[] data)
		{
			if (SyncSettings.Instance.IsDebug)
				Debug.Log($"Received Heartbeat from {sender}");

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
			}
			if (!room.ConnectedClients.TryGetValue(sender, out SyncConnectedClient client))
			{
				client = new(sender, username, color);
				room.ConnectedClients.TryAdd(sender, client);
				_ = TimeoutClient(client, room);
			}

			// update client values
			client.Color = color;
			if (client.Visualizer == null && room.Equals(CurrentRoom))
			{
				GameObject obj = GameObject.Instantiate(SyncSettings.Instance.ClientVisualizer.gameObject, _clientVisualizerParent);
				client.Visualizer = obj.GetComponent<SyncClientVisualizer>();
			}
			if (client.Visualizer)
				client.Visualizer.UpdateVisualizer(sender, username, color);
			client.LastHeartbeat = DateTime.Now;
		}

		private async Task TimeoutClient(SyncConnectedClient client, SyncOpenRoom room)
		{
			while (_disposeCount == 0)
			{
				if ((DateTime.Now - client.LastHeartbeat).TotalMilliseconds > SyncSettings.Instance.ClientTimeoutDelay)
				{
					_mainThreadActions.Enqueue(client.Dispose);
					room.ConnectedClients.TryRemove(client.IP, out SyncConnectedClient _);
					if (room.ConnectedClients.Count == 0 && !room.Equals(CurrentRoom))
						OpenRooms.TryRemove(room.Roomname, out SyncOpenRoom _);
					return;
				}
				await Task.Delay(SyncSettings.Instance.ClientTimeoutDelay);
			}
		}

		// TODO : optimize this
		private void UpdateClientVisualizer(string sender, byte[] data)
		{
			if (SyncSettings.Instance.IsDebug)
				Debug.Log($"Received Camera Update from {sender}");

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
				if (client.Visualizer == null)
					return;

				client.Visualizer.transform.position = new Vector3(BitConverter.ToSingle(positionX),
					BitConverter.ToSingle(positionY),
					BitConverter.ToSingle(positionZ));
				client.Visualizer.transform.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
					BitConverter.ToSingle(rotationY),
					BitConverter.ToSingle(rotationZ));
				if (!client.Visualizer.gameObject.activeSelf)
					client.Visualizer.gameObject.SetActive(true);
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
				SceneManager.MoveGameObjectToScene(createdObject, _syncScene);

				SyncManager.AddSyncMessage(new($"GameObject '{createdObject.name}' imported."));
			};

			using MemoryStream memoryStream = new();
			await memoryStream.WriteAsync(hierarchy);
			await DeserializeGameObject(_syncScene, new ImportOptions(), memoryStream, onLoadComplete);

			SyncManager.AddSyncMessage(new($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}"));

			return;
		}

		private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
		{
			GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

			GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
			await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), SyncSettings.Instance.IsDebug, -1, true, onLoadComplete);
		}

		#endregion

		#region helper

		private bool CheckPacketCRC32(byte[] checksum, byte[] packet, byte[] roomname = null)
		{
			if (roomname == null)
				roomname = CurrentRoom.RoomnameBytes;

			// calculate crc32 checksum from protocol id, roomname and data
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + roomname.Length + packet.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(roomname, 0, bytes, PROTOCOL_ID_LENGTH, roomname.Length);
			Array.Copy(packet, 0, bytes, PROTOCOL_ID_LENGTH + roomname.Length, packet.Length);
			return BitConverter.ToUInt32(checksum) == SyncCRC32.CRC32Bytes(bytes);
		}

		#endregion
	}
}