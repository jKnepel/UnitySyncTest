using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityGLTF;
using Toph.GLTFSerializer.Components;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace jKnepel.UnitySync
{
	internal sealed partial class SyncClient
	{
		#region threads

		private void HeartbeatThread()
		{
			byte[] username = Encoding.ASCII.GetBytes(_settings.Username);
			byte[] color = { _settings.Color.r, _settings.Color.g, _settings.Color.b };

			// prepare data with room name, username and -color
			byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH +
				ROOMNAME_FLAG_LENGTH + CurrentRoom.RoomnameBytes.Length +
				USERNAME_FLAG_LENGTH + username.Length +
				COLOR_LENGTH];
			int currentPosition = CRC32_LENGTH;
			Array.Copy(BitConverter.GetBytes((ushort)0), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
			currentPosition += SEQUENCE_ID_LENGTH;
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

			try
			{
				while (_disposeCount == 0)
				{
					_sender.Send(data, data.Length, _broadCastEndpoint);
					DebugByteMessage(data, "Send Heartbeat Bytes: ");
					Thread.Sleep(_settings.HeartbeatDelay);
				}
			}
			catch (Exception ex)
			{
				switch (ex)
				{
					case SocketException:
						Debug.LogError(ex.ToString());
						break;
					case ObjectDisposedException:
						Debug.LogError(ex.ToString());
						break;
					default:
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
				IsClientActive = false;
			}
		}

		private void SenderThread()
		{
			while (_disposeCount == 0)
			{
				if (_packetsToSend.Count == 0 || !_packetsToSend.TryPeek(out SyncSenderPacket packet))
					continue;

				if (packet.IsChunked && !SyncPacketChannels.ReliableSyncPackets.Contains(packet.Type))
				{
					Debug.LogWarning("Only reliable Packets can be larger than 1200 Bytes!");
					packet.OnDataSend?.Invoke(false);
				}

				byte[] packetData = packet.GetNextSlice();
				if (!packet.IsChunked)
				{	// remove packet from sender queue since it only needs to be send once
					_packetsToSend.TryDequeue(out _);
				}
				else if (packetData == null)
				{	// remove packet from sender queue once all slices were send
					_packetsToSend.TryDequeue(out _);
					continue;
				}

				byte[] data;
				if (packet.IsChunked)
				{   // add packet type, slice numbers, current slice number and data to the packet
					data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH
						+ NUMBER_OF_SLICES + SLICE_NUMBER + packetData.Length];
					data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)packet.Type;
					data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] |= 1 << 7;
					Array.Copy(BitConverter.GetBytes(packet.NumberOfSlices), 0, data,
						CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH, NUMBER_OF_SLICES);
					Array.Copy(BitConverter.GetBytes(packet.CurrentSliceNumber), 0, data,
						CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH + NUMBER_OF_SLICES, SLICE_NUMBER);
					Array.Copy(packetData, 0, data,
						CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH + NUMBER_OF_SLICES + SLICE_NUMBER, packetData.Length);
				}
				else
				{	// add packet type and data to the packet
					data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH + packetData.Length];
					data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)packet.Type;
					Array.Copy(packetData, 0, data, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH, packetData.Length);
				}


				// define receivers of the packet
				List<SyncConnectedClient> clients = new();
				if (packet.Client == null)
					clients = CurrentRoom.ConnectedClients.Values.ToList();
				else
					clients.Add(packet.Client);

				// add sequence number and checksum for each client and send packet to them
				foreach (SyncConnectedClient client in clients)
				{
					if (SyncPacketChannels.UnreliableSyncPackets.Contains(packet.Type))
					{   // handle unreliable packets
						if (!packet.IsChunked || packet.CurrentSliceNumber == 1)
							client.UnreliableLocalSequence++;
						Array.Copy(BitConverter.GetBytes(client.UnreliableLocalSequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
					}
					else
					{   // handle reliable packets
						if (!packet.IsChunked || packet.CurrentSliceNumber == 1)
							client.ReliableLocalSequence++;
						Array.Copy(BitConverter.GetBytes(client.ReliableLocalSequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
					}

					CalculateAndAddChecksum(data);

					_sender.Send(data, data.Length, new(client.IP, _settings.Port));
					if (packet.OnDataSend != null)
						packet.OnDataSend?.Invoke(true);

					if (packet.IsChunked)
					{	// save slices in buffer in case of resends
						client.SendSlicesBuffer.TryAdd((client.ReliableLocalSequence, packet.CurrentSliceNumber), data);
						_ = ResendSliceData(client, (client.ReliableLocalSequence, packet.CurrentSliceNumber), 0);
					}
					else if (SyncPacketChannels.ReliableSyncPackets.Contains(packet.Type))
					{   // save reliable packets in buffer in case of resends
						client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
						_ = ResendPacketData(client, client.ReliableLocalSequence, 0);
					}
				}
				DebugByteMessage(data, $"Send {packet.Type} Bytes: ", true);
			}
			// TODO : handle other exceptions and set IsClientActive = false
		}

		private async Task ResendSliceData(SyncConnectedClient client, (ushort, ushort) sequence, int retries)
		{
			// TODO : use RTTx1.25 instead of constant delay
			await Task.Delay(_settings.ResendReliablePacketsDelay);
			if (client != null && client.SendSlicesBuffer.TryGetValue(sequence, out byte[] packetData))
			{
				_sender.Send(packetData, packetData.Length, new(client.IP, _settings.Port));
				// TODO : handle timeout when packets dont arrive within max number of resends
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendSliceData(client, sequence, retries + 1);
			}
			return;
		}

		private async Task ResendPacketData(SyncConnectedClient client, ushort sequence, int retries)
		{
			// TODO : use RTTx1.25 instead of constant delay
			await Task.Delay(_settings.ResendReliablePacketsDelay);
			if (client != null && client.SendPacketsBuffer.TryGetValue(sequence, out byte[] packetData))
			{
				_sender.Send(packetData, packetData.Length, new(client.IP, _settings.Port));
				// TODO : handle timeout when packets dont arrive within max number of resends
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendPacketData(client, sequence, retries + 1);
			}
			return;
		}

		private void SendACK(SyncConnectedClient client, ushort sequence)
		{
			byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH];
			Array.Copy(BitConverter.GetBytes(sequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
			data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)SyncPacketType.ACK;
			CalculateAndAddChecksum(data);
			_sender.Send(data, data.Length, new(client.IP, _settings.Port));
			// TODO : handle other exceptions and set IsClientActive = false
		}

		private void SendACK(SyncConnectedClient client, ushort sequence, ushort sliceNumber)
		{
			byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH + SLICE_NUMBER];
			Array.Copy(BitConverter.GetBytes(sequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
			data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)SyncPacketType.ACK;
			data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] |= 1 << 7;
			Array.Copy(BitConverter.GetBytes(sliceNumber), 0, data, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH, SLICE_NUMBER);
			CalculateAndAddChecksum(data);
			_sender.Send(data, data.Length, new(client.IP, _settings.Port));
		}

		#endregion

		#region find updates

#if UNITY_EDITOR
		private void ChangesPublished(ref ObjectChangeEventStream stream)
		{
			ChangeGameObjectOrComponentPropertiesEventArgs obj;
			stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
			if (obj.scene == _settings.SyncScene)
			{
				UnityEngine.Object o = EditorUtility.InstanceIDToObject(obj.instanceId);
				GameObject go;
				try { go = ((Component)o).gameObject; }
				catch (InvalidCastException) { go = (GameObject)o; }
				SendHierarchy(go); // TODO : replace with functionality for only sending gameobject/component

				// Limit hierarchy syncs
				ObjectChangeEvents.changesPublished -= ChangesPublished;
				Task.Delay(_settings.EditorUpdatesDelay)
					.ContinueWith(t => ObjectChangeEvents.changesPublished += ChangesPublished);
			}

			// TODO : add functionality for sending hierarchy only on hierarchy changes
			// TODO : support all kinds of changesPublished
			// TODO : add flag for turning off automatic update sync in editor
		}

		private async void SendHierarchy(GameObject go)
		{
			GLTFRootGameObject gltf = go.GetRootParent().GetOrAddComponent<GLTFRootGameObject>();
			byte[] bytes = await gltf.SerializeGameObject(new ExportOptions());
			_packetsToSend.Enqueue(new(SyncPacketType.Hierarchy, bytes));
		}
#endif

		private void CurrentCameraMoved(Transform camera)
		{
			// limit camera syncs
			if (_cameraUpdateDelay < _settings.ClientVisualizerUpdateDelay)
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

			// TODO : combine de-/serializer
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

		#endregion

		#region helper methods

		private void CalculateAndAddChecksum(byte[] packet)
		{
			// get packet without CRC32 bytes
			byte[] packetData = GetBytesFromArray(packet, CRC32_LENGTH);

			// calculate CRC32 checksum from protocol id, roomname and packet
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length + packetData.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(CurrentRoom.RoomnameBytes, 0, bytes, PROTOCOL_ID_LENGTH, CurrentRoom.RoomnameBytes.Length);
			Array.Copy(packetData, 0, bytes, PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length, packetData.Length);
			
			// and add it to packet
			byte[] checksumBytes = BitConverter.GetBytes(SyncCRC32.CRC32Bytes(bytes));
			Array.Copy(checksumBytes, 0, packet, 0, CRC32_LENGTH);
		}

		#endregion
	}
}
