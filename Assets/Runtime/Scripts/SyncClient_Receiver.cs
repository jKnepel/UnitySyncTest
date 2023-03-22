using System;
using System.IO;
using System.Net;
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
	internal sealed partial class SyncClient
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
					IPAddress sender = _receiveEndpoint.Address;
					if (sender.Equals(_ip))
						continue;

					// get bytes used for crc32 check
					byte[] checksumBytes = GetBytesFromArray(receivedBytes, 0, CRC32_LENGTH);
					byte[] remainder = GetBytesFromArray(receivedBytes, CRC32_LENGTH);

					// get relevant header bytes
					byte[] sequenceBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
					ushort sequence = BitConverter.ToUInt16(sequenceBytes);
					bool isChunked = (receivedBytes[CRC32_LENGTH + SEQUENCE_ID_LENGTH] & (1 << 7)) != 0;
					SyncPacketType packetType = GetOriginalType(receivedBytes[CRC32_LENGTH + SEQUENCE_ID_LENGTH]);

					if (packetType == SyncPacketType.Heartbeat)
					{
						UpdateHeartbeat(sender, checksumBytes, remainder);
						continue;
					}

					if (!IsConnectedToRoom)
						continue;

					if (!CheckPacketCRC32(checksumBytes, remainder, CurrentRoom.RoomnameBytes))
						continue;

					if (!CurrentRoom.ConnectedClients.TryGetValue(sender, out SyncConnectedClient client))
						continue;

					if (packetType == SyncPacketType.ACK)
					{   // remove ACK'd packets from buffer
						if (isChunked)
						{
							byte[] sliceNumberBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH);
							ushort sliceNumber = BitConverter.ToUInt16(sliceNumberBytes);
							client.SendSlicesBuffer.TryRemove((sequence, sliceNumber), out _);
						}
						else
							client.SendPacketsBuffer.TryRemove(sequence, out _);
						continue;
					}

					byte[] data = isChunked
						? GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH + NUMBER_OF_SLICES + SLICE_NUMBER)
						: GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH);

					if (isChunked)
					{	// handle chunked packet by checking sequence and only consuming once its complete and relevant
						byte[] sliceNumberBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH + NUMBER_OF_SLICES, SLICE_NUMBER);
						ushort sliceNumber = BitConverter.ToUInt16(sliceNumberBytes);

						SendACK(client, sequence, sliceNumber);
						
						if (!IsNewPacket(sequence, client.ReliableRemoteSequence)
							&& packetType != SyncPacketType.ReliableUnorderedData)
							continue;

						if (!client.ReceivedPacketsBuffer.TryGetValue(sequence, out SyncReceiverPacket packet))
						{	// create chunked packet if it doesn't exist yet
							byte[] numberOfSlicesBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH, NUMBER_OF_SLICES);
							ushort numberOfSlices = BitConverter.ToUInt16(numberOfSlicesBytes);
							packet = new(packetType, client, numberOfSlices);
							client.ReceivedPacketsBuffer.TryAdd(sequence, packet);
						}
						packet.Slices.TryAdd(sliceNumber, data);

						// if packet is not complete or a packet is missing in the sequence keep it in buffer
						if (!packet.IsPacketComplete() || (!IsNextPacket(sequence, client.ReliableRemoteSequence)
							&& packetType != SyncPacketType.ReliableUnorderedData))
							continue;

						client.ReceivedPacketsBuffer.TryRemove(sequence, out _);
						client.ReliableRemoteSequence = sequence;
						packet.ConcatenateChunkedPacket();
						_receivedPackets.Enqueue(packet);
					}
					else
					{   // handle packet by checking sequence and consume packet if it is relevant
						if (SyncPacketChannels.UnreliableSyncPackets.Contains(packetType))
						{   // unreliable packet sequences
							if (!IsNewPacket(sequence, client.UnreliableRemoteSequence)
								&& packetType != SyncPacketType.UnreliableUnorderedData)
								continue;

							client.UnreliableRemoteSequence = sequence;
							_receivedPackets.Enqueue(new(packetType, data, client));
						}
						else
						{   // reliable packet sequences
							SendACK(client, sequence);

							if (!IsNewPacket(sequence, client.ReliableRemoteSequence) 
								&& packetType != SyncPacketType.ReliableUnorderedData)
								continue;

							if (!IsNextPacket(sequence, client.ReliableRemoteSequence)
								&& packetType != SyncPacketType.ReliableUnorderedData)
							{   // if the packet is new but a packet is missing in-between
								client.ReceivedPacketsBuffer.TryAdd(sequence, new(packetType, data, client));
								// TODO : if not received within RTTx1.25 timeframe timeout client
								// TODO : lock gameobject if packets reference unity objects
								continue;
							}

							client.ReliableRemoteSequence = sequence;
							_receivedPackets.Enqueue(new(packetType, data, client));
						}
					}

					// apply all packets in buffer that are now next in line and complete
					if (SyncPacketChannels.ReliableSyncPackets.Contains(packetType))
					{
						while (client.ReceivedPacketsBuffer.Count > 0)
						{
							sequence++;
							if (!client.ReceivedPacketsBuffer.TryGetValue(sequence, out SyncReceiverPacket packet))
								break;

							// TODO : unlock gameobjects if packets reference unity objects
							// only consume chunked packet if it is complete
							if (packet.IsChunked && !packet.ConcatenateChunkedPacket())
								break;

							client.ReceivedPacketsBuffer.TryRemove(sequence, out _);
							client.ReliableRemoteSequence = sequence;
							_receivedPackets.Enqueue(packet);
						}
					}
				}
				catch (OverflowException) { continue; }
				// TODO : handle other exceptions and set IsClientActive = false
			}
		}

		#endregion

		#region packet receiving

		private void UpdateHeartbeat(IPAddress sender, byte[] checksum, byte[] data)
		{
			// read roomname and check for bitswitches
			int currentPosition = SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH;
			int roomnameLength = data[currentPosition];
			currentPosition += ROOMNAME_FLAG_LENGTH;
			byte[] roomnameBytes = GetBytesFromArray(data, currentPosition, roomnameLength);
			if (!CheckPacketCRC32(checksum, data, roomnameBytes))
				return;

			DebugByteMessage(data, "Received Heartbeat Bytes: ");

			// get username and -color from data
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
			client.LastHeartbeat = DateTime.Now;
			_mainThreadActions.Enqueue(() =>
			{
				if (client.Visualizer)
				{
					client.Visualizer.UpdateVisualizer(client.IPString, username, color);
				}
				else if (client.Visualizer == null && room.Equals(CurrentRoom))
				{
					GameObject obj = GameObject.Instantiate(_settings.ClientVisualizer.gameObject, _settings.SyncParentContainer);
					client.Visualizer = obj.GetComponent<SyncClientVisualizer>();
					client.Visualizer.UpdateVisualizer(client.IPString, username, color);
				}
			});
		}

		private async Task TimeoutClient(SyncConnectedClient client, SyncOpenRoom room)
		{
			while (_disposeCount == 0)
			{
				if ((DateTime.Now - client.LastHeartbeat).TotalMilliseconds > _settings.ClientTimeoutDelay)
				{
					_mainThreadActions.Enqueue(client.Dispose);
					room.ConnectedClients.TryRemove(client.IP, out _);
					if (room.ConnectedClients.Count == 0 && !room.Equals(CurrentRoom))
						OpenRooms.TryRemove(room.Roomname, out _);
					return;
				}
				await Task.Delay(_settings.ClientTimeoutDelay);
			}
		}

		// TODO : optimize this
		private void UpdateClientVisualizer(SyncReceiverPacket packet)
		{
			if (_settings.IsDebug)
				Debug.Log($"Received Camera Update from {packet.Client.IP}");

			int size = sizeof(float);
			byte[] positionX = GetBytesFromArray(packet.Data, 0 * size, size);
			byte[] positionY = GetBytesFromArray(packet.Data, 1 * size, size);
			byte[] positionZ = GetBytesFromArray(packet.Data, 2 * size, size);
			byte[] rotationX = GetBytesFromArray(packet.Data, 3 * size, size);
			byte[] rotationY = GetBytesFromArray(packet.Data, 4 * size, size);
			byte[] rotationZ = GetBytesFromArray(packet.Data, 5 * size, size);

			packet.Client.LastHeartbeat = DateTime.Now;
			if (packet.Client.Visualizer == null)
				return;

			packet.Client.Visualizer.transform.position = new Vector3(BitConverter.ToSingle(positionX),
				BitConverter.ToSingle(positionY),
				BitConverter.ToSingle(positionZ));
			packet.Client.Visualizer.transform.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
				BitConverter.ToSingle(rotationY),
				BitConverter.ToSingle(rotationZ));
			if (!packet.Client.Visualizer.gameObject.activeSelf)
				packet.Client.Visualizer.gameObject.SetActive(true);
		}

		private void ReceiveReliableData(SyncReceiverPacket packet)
		{
			SyncManager.OnReliableDataReceived?.Invoke(packet.Data);
		}

		private void ReceiveReliableUnorderedData(SyncReceiverPacket packet)
		{
			SyncManager.OnReliableUnorderedDataReceived?.Invoke(packet.Data);
		}

		private void ReceiveUnreliableData(SyncReceiverPacket packet)
		{
			SyncManager.OnUnreliableDataReceived?.Invoke(packet.Data);
		}

		private void ReceiveUnreliableUnorderedData(SyncReceiverPacket packet)
		{
			SyncManager.OnUnreliableUnorderedDataReceived?.Invoke(packet.Data);
		}

		private async void ImportHierarchy(byte[] hierarchy)
		{
			Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
			{
				if (createdObject is null)
				{
					return;
				}
				SceneManager.MoveGameObjectToScene(createdObject, _settings.SyncScene);

				SyncManager.AddSyncMessage(new($"GameObject '{createdObject.name}' imported."));
			};

			using MemoryStream memoryStream = new();
			await memoryStream.WriteAsync(hierarchy);
			await DeserializeGameObject(_settings.SyncScene, new ImportOptions(), memoryStream, onLoadComplete);

			Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

			return;
		}

		private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
		{
			GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

			GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
			await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), _settings.IsDebug, -1, true, onLoadComplete);
		}

		#endregion

		#region helper

		private static SyncPacketType GetOriginalType(byte typeBytes)
		{
			byte mask = 1 << 7;
			typeBytes &= (byte)~mask;
			return (SyncPacketType)typeBytes;
		}

		private static bool CheckPacketCRC32(byte[] checksum, byte[] packet, byte[] roomname)
		{
			// calculate crc32 checksum from protocol id, roomname and packet
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + roomname.Length + packet.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(roomname, 0, bytes, PROTOCOL_ID_LENGTH, roomname.Length);
			Array.Copy(packet, 0, bytes, PROTOCOL_ID_LENGTH + roomname.Length, packet.Length);
			return BitConverter.ToUInt32(checksum) == SyncCRC32.CRC32Bytes(bytes);
		}

		/// <summary>
		/// Checks if a packet is new by comparing the packets sequence number and the corresponding remote sequence number.
		/// Also handles ushort wrap-arounds by allowing packets that are smaller by half of the maximum value.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		private static bool IsNewPacket(ushort packetSequence, ushort remoteSequence)
		{
			return ((packetSequence > remoteSequence) && (packetSequence - remoteSequence <= 32768))
				|| ((packetSequence < remoteSequence) && (remoteSequence - packetSequence > 32768));
		}

		/// <summary>
		/// Checks if a packet is next after remote sequence number.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		private static bool IsNextPacket(ushort packetSequence, ushort remoteSequence)
		{
			return (packetSequence == (ushort)(remoteSequence + 1))
				|| (packetSequence == 0 && remoteSequence == 65535);
		}

		#endregion
	}
}