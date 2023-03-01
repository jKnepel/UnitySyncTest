using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
					SyncManager.AddSyncMessage(new("Send Heartbeat"));
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
						SyncManager.AddSyncMessage(new("Socket Exception " + ex.ToString(), SyncMessageSeverity.LogError));
						break;
					case ObjectDisposedException:
						SyncManager.AddSyncMessage(new("Object Disposed Exception " + ex.ToString(), SyncMessageSeverity.LogError));
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

		#endregion

		#region packet sending

		private void SendClientCameraBytes(byte[] bytes)
		{
			byte[] data = new byte[CRC32_LENGTH + DATA_TYPE_LENGTH + bytes.Length];
			// TODO : add sequence ids
			data[CRC32_LENGTH] = (byte)SyncPacketType.ClientCamera;
			Array.Copy(bytes, 0, data, CRC32_LENGTH + DATA_TYPE_LENGTH, bytes.Length);

			CalculateAndAddChecksum(data);

			_sender.Send(data, data.Length, _broadCastEndpoint);
			AddDebugByteMessage(data, "Send Camera Bytes: ");
		}

		private async void SendHierarchy(GameObject go)
		{
			GLTFRootGameObject gltf = go.GetRootParent().GetOrAddComponent<GLTFRootGameObject>();
			byte[] bytes = await gltf.SerializeGameObject(new ExportOptions());
			_packetsToSend.Enqueue(new(SyncPacketType.Hierarchy, bytes));
		}

		#endregion

		#region find updates

#if UNITY_EDITOR
		private void ChangesPublished(ref ObjectChangeEventStream stream)
		{
			ChangeGameObjectOrComponentPropertiesEventArgs obj;
			stream.GetChangeGameObjectOrComponentPropertiesEvent(0, out obj);
			if (obj.scene == _scene)
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

		#endregion

		#region helper methods

		private void CalculateAndAddChecksum(byte[] packet)
		{
			// calculate crc32 checksum from protocol id, roomname and packet
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length + packet.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(CurrentRoom.RoomnameBytes, 0, bytes, PROTOCOL_ID_LENGTH, CurrentRoom.RoomnameBytes.Length);
			Array.Copy(packet, 0, bytes, PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length, packet.Length);

			// and add it to the packet
			Array.Copy(BitConverter.GetBytes(SyncCRC32.CRC32Bytes(bytes)), 0, packet, 0, CRC32_LENGTH);
		}

		#endregion
	}
}
