using System;
using System.Net;
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
	public partial class SyncClient
	{
		#region threads

		private void HeartbeatThread()
		{
			byte[] nullBytes16 = BitConverter.GetBytes((ushort)0);

			try
			{
				while (_disposeCount == 0)
				{
					_pauseEvent.WaitOne(Timeout.Infinite);

					byte[] username = Encoding.ASCII.GetBytes(_settings.Username);
					byte[] color = { _settings.Color.r, _settings.Color.g, _settings.Color.b };

					// prepare data with room name, username and -color
					byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH +
						ROOMNAME_FLAG_LENGTH + CurrentRoom.RoomnameBytes.Length +
						USERNAME_FLAG_LENGTH + username.Length +
						COLOR_LENGTH];
					int currentPosition = CRC32_LENGTH;
					Array.Copy(nullBytes16, 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
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

					// add crc32 to the packet
					byte[] checksumBytes = CalculateChecksum(GetBytesFromArray(data, CRC32_LENGTH));
					Array.Copy(checksumBytes, 0, data, 0, CRC32_LENGTH);

					_sender.Send(data, data.Length, _broadCastEndpoint);
					DebugByteMessage(data, "Send Heartbeat Bytes: ");
					Thread.Sleep(_settings.HeartbeatDelay);
				}
			}
			catch (Exception ex)
			{
				CurrentRoom = null;
				IsSearchingForRooms = false;
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
			}
		}

		private void SenderThread()
		{
			while (_disposeCount == 0)
			{
				_pauseEvent.WaitOne(Timeout.Infinite);

				if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out SyncPacket packet))
					continue;

				// add packet type and data to packet
				byte[] data = new byte[CRC32_LENGTH + DATA_TYPE_LENGTH + SEQUENCE_ID_LENGTH + packet.Data.Length];
				data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)packet.Type;
				Array.Copy(packet.Data, 0, data, CRC32_LENGTH + SEQUENCE_ID_LENGTH + DATA_TYPE_LENGTH, packet.Data.Length);

				// add local sequence number and checksum for each client and send packet to them
				foreach (SyncConnectedClient client in CurrentRoom.ConnectedClients.Values)
				{
					client.UnreliableLocalSequence++;
					Array.Copy(BitConverter.GetBytes(client.UnreliableLocalSequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
					byte[] checksumBytes = CalculateChecksum(GetBytesFromArray(data, CRC32_LENGTH));
					Array.Copy(checksumBytes, 0, data, 0, CRC32_LENGTH);
					_sender.Send(data, data.Length, new(IPAddress.Parse(client.IP), _settings.Port));
				}
				DebugByteMessage(data, $"Send {packet.Type} Bytes: ");
			}
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
				//SendHierarchy(go); // TODO : replace with functionality for only sending gameobject/component

				// Limit hierarchy syncs
				ObjectChangeEvents.changesPublished -= ChangesPublished;
				Task.Delay(_settings.EditorUpdatesDelay)
					.ContinueWith(t => ObjectChangeEvents.changesPublished += ChangesPublished);
			}

			// TODO : add functionality for sending hierarchy only on hierarchy changes
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

		private byte[] CalculateChecksum(byte[] packet)
		{
			// calculate crc32 checksum from protocol id, roomname and packet
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length + packet.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(CurrentRoom.RoomnameBytes, 0, bytes, PROTOCOL_ID_LENGTH, CurrentRoom.RoomnameBytes.Length);
			Array.Copy(packet, 0, bytes, PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length, packet.Length);
			return BitConverter.GetBytes(SyncCRC32.CRC32Bytes(bytes));
		}

		#endregion
	}
}
