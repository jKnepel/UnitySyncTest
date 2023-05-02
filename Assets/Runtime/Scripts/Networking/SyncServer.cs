using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
	internal sealed class SyncServer : SyncSocket
	{
		#region private fields

		private readonly Thread _heartbeatThread;

		private readonly ConcurrentDictionary<IPAddress, byte[]> _pendingConnections = new();
		private readonly ConcurrentDictionary<IPAddress, SyncConnectedClientDetail> _connectedClients = new();

		private readonly ConcurrentDictionary<uint, IPAddress> _ipHashTable = new();

		#endregion

		#region public properties

		public override bool IsServer => true;
		public override List<SyncConnectedClient> ConnectedClients => _connectedClients.Values.Cast<SyncConnectedClient>().ToList();

		#endregion

		#region lifecycle

		public SyncServer(string servername)
		{
			try
			{
				if (!CheckLocalIP(SyncManager.IP))
				{
					Debug.LogError("No network interface possesses the given local IP!");
					return;
				}

				if (servername.Length > 100 || _settings.Username.Length > 100)
				{
					Debug.LogError($"The Servername and Username must be shorter than 100 Characters!");
					return;
				}

				if (!IsASCIIString(servername) || !IsASCIIString(_settings.Username))
				{
					Debug.LogError($"The Servername or Username contains a non-ASCII Character!");
					return;
				}

				_localIP = IPAddress.Parse(SyncManager.IP);
				_port = _settings.Port;
				_udpClient = new(_port);
				ServerInformation = new(_localIP, servername);

				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
				_heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
				_heartbeatThread.Start();
				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();

				SyncManager.OnUpdate += Update;

				SyncManager.AddSyncMessage(new("Server has been opened!"));
				IsConnected = true;
			}
			catch (Exception ex)
			{
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
						Dispose();
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
				Dispose();
			}
		}

		public override void Dispose(bool disposing)
		{
			if (Interlocked.Increment(ref _disposeCount) == 1)
			{
				if (disposing)
				{
					SyncManager.OnUpdate -= Update;
				}

				if (_listenerThread != null)
				{
					_listenerThread.Abort();
					_listenerThread.Join();
				}
				if (_heartbeatThread != null)
				{
					_heartbeatThread.Abort();
					_heartbeatThread.Join();
				}
				if (_senderThread != null)
				{
					_senderThread.Abort();
					_senderThread.Join();
				}

				if (_udpClient != null)
				{
					_udpClient.Close();
					_udpClient.Dispose();
				}

				IsConnected = false;
				ServerInformation = null;
			}
		}

		public void Update()
		{
			while (_mainThreadActions.Count > 0)
			{
				if (_mainThreadActions.TryDequeue(out Action action))
					action?.Invoke();
			}
		}

		public override void DisconnectFromServer()
		{
			if (!IsConnected)
				return;

			Dispose();
			SyncManager.AddSyncMessage(new("Disconnected from Server!"));
		}

		protected override bool CheckIfEligibleForSending(bool isReliable, byte[] data, Action<bool> dataCallback, uint receiver = 0)
		{
			if (!_ipHashTable.TryGetValue(receiver, out IPAddress ip) || !_connectedClients.TryGetValue(ip, out _))
			{
				Debug.LogError("The given Receiver does not exist in the Server!");
				dataCallback?.Invoke(false);
				return false;
			}

			if (!isReliable && data.Length > _settings.MTU)
			{
				Debug.LogError($"Only reliable Packets can be larger than {_settings.MTU} Bytes!");
				dataCallback?.Invoke(false);
				return false;
			}

			return true;
		}

		#endregion

		#region listener logic

		private void ListenerThread()
		{
			try
			{
				while (_disposeCount == 0 && IsConnected)
				{
					try
					{
						// get packet ip headers
						IPEndPoint receiveEndpoint = new(IPAddress.Any, _settings.Port);
						byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
						IPAddress sender = receiveEndpoint.Address;
						if (sender.Equals(_localIP))
							continue;

						SyncPacketType packetType = SyncPacketHelper.GetPacketType(receivedBytes);

						// if packet type doesn't require an active connection
						if (!SyncPacketHelper.IsConnectionRequired(packetType))
						{
							HandleInternalPacket(sender, packetType, receivedBytes);
							continue;
						}

						// if packet type does require an active connection
						if (!_connectedClients.TryGetValue(sender, out SyncConnectedClientDetail client))
							continue;

						// check if the crc32 is correct with the servername as magic header
						if (!SyncPacketHelper.CheckPacketCRC32(
							SyncPacketHelper.GetCRC32(receivedBytes),
							SyncPacketHelper.GetRemainder(receivedBytes),
							ServerInformation.ServernameBytes))
							continue;

						client.LastHeartbeat = DateTime.Now;

					 	if (!SyncPacketHelper.IsDataPacket(packetType))
						{	// if packet type is internal
							HandleConnectedInternalPacket(client, packetType, receivedBytes);
						}
						else
						{	// if packet type is a data packet holding module data
							HandleDataPacket(client, packetType, receivedBytes);
						}
					}
					catch (OverflowException) { continue; }
				}
			}
			catch (Exception ex)
			{
				switch (ex)
				{
					case ThreadAbortException:
						break;
					case SocketException:
					case ObjectDisposedException:
						Debug.LogError(ex.ToString());
						_mainThreadActions.Enqueue(() => IsConnected = false);
						break;
					default:
						_mainThreadActions.Enqueue(() => IsConnected = false);
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
			}
		}

		private void HandleInternalPacket(IPAddress sender, SyncPacketType packetType, byte[] packet)
		{
			// check if the crc32 is correct
			if (!SyncPacketHelper.CheckPacketCRC32(
				SyncPacketHelper.GetCRC32(packet),
				SyncPacketHelper.GetRemainder(packet)))
				return;

			switch (packetType)
			{
				case SyncPacketType.ConnectionRequest:
					// accept connection request if there's space left
					if (ServerInformation.NumberConnectedClients < byte.MaxValue)
					{
						ulong challenge = SyncPacketHelper.GenerateChallengeNonce();
						SendConnectionChallenge(sender, challenge);
						using (SHA256 h = SHA256.Create())
						{
							byte[] hashedChallenge = h.ComputeHash(BitConverter.GetBytes(challenge));
							_pendingConnections.AddOrUpdate(sender, hashedChallenge, (key, oldValue) => oldValue = hashedChallenge);
						}

						// TODO : also send connection accepted if already connected
					}
					// TODO : send connection denied
					break;
				case SyncPacketType.ChallengeAnswer:
					// accept connection if challenge answer equals previously send challenge
					if (_pendingConnections.TryGetValue(sender, out byte[] value)
						&& CompareByteArrays(value, SyncPacketHelper.GetChallengeAnswer(packet))
						&& ServerInformation.NumberConnectedClients < byte.MaxValue)
					{
						uint clientID = SyncCRC32.CRC32Bytes(Encoding.ASCII.GetBytes(sender.ToString()));
						_ipHashTable.TryAdd(clientID, sender);
						string username = SyncPacketHelper.GetChallengeUsername(packet);
						Color32 color = SyncPacketHelper.GetChallengeColor(packet);
						_connectedClients.TryAdd(sender, new(clientID, sender, username, color));
						_pendingConnections.TryRemove(sender, out _);
						_mainThreadActions.Enqueue(() => SyncManager.OnClientConnected?.Invoke(clientID));
						SendConnectionAccepted(sender, clientID);
					}
					// TODO : send connection denied
					break;
			}
		}

		private void HandleConnectedInternalPacket(SyncConnectedClientDetail client, SyncPacketType packetType, byte[] packet)
		{ 
			bool isChunked = SyncPacketHelper.GetIsChunked(packet);
			ushort sequence = SyncPacketHelper.GetSequenceBytes(packet);

			if (packetType == SyncPacketType.ACK)
			{   // remove ACK'd packets from buffer
				if (isChunked)
					client.SendSlicesBuffer.TryRemove((sequence, SyncPacketHelper.GetSliceNumber(packet)), out _);
				else
					client.SendPacketsBuffer.TryRemove(sequence, out _);
				return;
			}
		}

		// TODO : handle packets that were meant for other clients or hand out data to all other clients
		private void HandleDataPacket(SyncConnectedClientDetail client, SyncPacketType packetType, byte[] packet)
		{
			bool isChunked = SyncPacketHelper.GetIsChunked(packet);
			ushort sequence = SyncPacketHelper.GetSequenceBytes(packet);
			uint moduleHash = SyncPacketHelper.GetModuleHash(packet, isChunked);
			byte[] data = SyncPacketHelper.GetDataPacketData(packet, isChunked);

			// unreliable packet sequences
			if (SyncPacketHelper.IsUnreliableData(packetType))
			{   
				// ignore old packets unless they are unordered
				if (!SyncPacketHelper.IsNewPacket(sequence, client.UnreliableRemoteSequence)
					&& packetType != SyncPacketType.UnreliableUnorderedData)
					return;

				client.UnreliableRemoteSequence = sequence;
				SyncManager.DataReceived?.Invoke(moduleHash, client, data);
				return;
			}

			// reliable (and chunked) packet sequences
			if (isChunked)
			{   // handle chunked packet by checking sequence and only consuming once it's complete and relevant
				ushort sliceNumber = SyncPacketHelper.GetSliceNumber(packet);
				SendACK(client, sequence, sliceNumber);

				// ignore old packets unless they are unordered
				if (!SyncPacketHelper.IsNewPacket(sequence, client.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData)
					return;

				if (!client.ReceivedPacketsBuffer.TryGetValue(sequence, out SyncReceiverPacket receiverPacket))
				{   // create chunked packet if it doesn't exist yet
					receiverPacket = new(packetType, moduleHash, client, SyncPacketHelper.GetNumberOfSlices(packet));
					client.ReceivedPacketsBuffer.TryAdd(sequence, receiverPacket);
				}
				receiverPacket.Slices.TryAdd(sliceNumber, data);

				// if packet is not complete or a packet is missing in the sequence keep it in the buffer
				if (!receiverPacket.IsPacketComplete() || (!SyncPacketHelper.IsNextPacket(sequence, client.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData))
					return;

				client.ReceivedPacketsBuffer.TryRemove(sequence, out _);
				client.ReliableRemoteSequence = sequence;
				receiverPacket.ConcatenateChunkedPacket();
				SyncManager.DataReceived?.Invoke(receiverPacket.ModuleHash, receiverPacket.Client, receiverPacket.Data);
			}
			else
			{   // handle packet by checking sequence and only consuming once it's relevant
				SendACK(client, sequence);

				// ignore old packets unless they are unordered
				if (!SyncPacketHelper.IsNewPacket(sequence, client.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData)
					return;

				if (!SyncPacketHelper.IsNextPacket(sequence, client.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData)
				{   // if a packet is missing in the sequence keep it in the buffer
					client.ReceivedPacketsBuffer.TryAdd(sequence, new(packetType, moduleHash, data, client));
					// TODO : if not received within RTTx1.25 timeframe timeout client
					return;
				}

				client.ReliableRemoteSequence = sequence;
				SyncManager.DataReceived?.Invoke(moduleHash, client, data);
			}

			// apply all packets in buffer that are now next in line and complete
			while (client.ReceivedPacketsBuffer.Count > 0)
			{
				sequence++;
				if (!client.ReceivedPacketsBuffer.TryGetValue(sequence, out SyncReceiverPacket receiverPacket))
					break;

				// only consume chunked packet if it is complete
				if (receiverPacket.IsChunked && !receiverPacket.ConcatenateChunkedPacket())
					break;

				client.ReceivedPacketsBuffer.TryRemove(sequence, out _);
				client.ReliableRemoteSequence = sequence;
				SyncManager.DataReceived?.Invoke(receiverPacket.ModuleHash, receiverPacket.Client, receiverPacket.Data);
			}
		}

		#endregion

		#region sender logic

		private void HeartbeatThread()
		{
			// prepare data with type, servername
			byte[] data = new byte[SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH + SyncSettings.DATA_FLAG_LENGTH + ServerInformation.ServernameBytes.Length];
			data[0] = (byte)SyncPacketType.Heartbeat;
			data[SyncSettings.PACKET_TYPE_LENGTH] = (byte)ServerInformation.NumberConnectedClients;
			data[SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH] = (byte)ServerInformation.ServernameBytes.Length;
			Array.Copy(ServerInformation.ServernameBytes, 0, data, SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH + SyncSettings.DATA_FLAG_LENGTH, ServerInformation.ServernameBytes.Length);

			// and checksum
			byte[] packet = new byte[SyncSettings.CRC32_LENGTH + data.Length];
			Array.Copy(data, 0, packet, SyncSettings.CRC32_LENGTH, data.Length);
			SyncPacketHelper.CalculateAndAddChecksum(packet, ServerInformation.ServernameBytes);

			try
			{	// send heartbeat used for discovery until server is closed
				while (_disposeCount == 0 && IsConnected)
				{
					_udpClient.Send(packet, packet.Length, new(IPAddress.Broadcast, _port));
					DebugByteMessage(packet, "Send Heartbeat Bytes: ");
					Thread.Sleep(_settings.HeartbeatDelay);
				}
			}
			catch (Exception ex)
			{
				switch (ex)
				{
					case ThreadAbortException:
						break;
					case SocketException:
					case ObjectDisposedException:
						Debug.LogError(ex.ToString());
						_mainThreadActions.Enqueue(() => IsConnected = false);
						break;
					default:
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
			}
		}

		private void SenderThread()
		{
			try
			{
				while (_disposeCount == 0 && IsConnected)
				{
					if (_packetsToSend.Count == 0 || !_packetsToSend.TryPeek(out SyncSenderPacket packet))
						continue;

					if (packet.IsChunked && SyncPacketHelper.IsUnreliableData(packet.Type))
					{
						Debug.LogError("Only reliable Packets can be larger than 1200 Bytes!");
						packet.OnDataSend?.Invoke(false);
						_packetsToSend.TryDequeue(out _);
						continue;
					}

					if (!packet.IsChunked)
					{   // remove packet from sender queue since it only needs to be send once
						_packetsToSend.TryDequeue(out _);
					}

					byte[] packetData = packet.GetNextSlice();
					if (packetData == null || packetData.Length == 0)
					{   // remove invalid or already completed packets from sender queue
						_packetsToSend.TryDequeue(out _);
						continue;
					}

					byte[] data;
					if (packet.IsChunked)
					{   // add packet type, slice numbers, current slice number and data to the packet
						data = SyncPacketHelper.CreateChunkedDataPacket(packet.ModuleHash, packet.Type, 
							packet.NumberOfSlices, packet.CurrentSliceNumber, packet.Data);
					}
					else
					{   // add packet type and data to the packet
						data = SyncPacketHelper.CreateDataPacket(packet.ModuleHash, packet.Type, packet.Data);
					}

					// define receivers of the packet
					List<SyncConnectedClientDetail> clients = new();
					if (packet.Client == null)
						clients = _connectedClients.Values.ToList();
					else
						clients.Add(packet.Client);

					// add sequence number and checksum for each client and send packet to them
					foreach (SyncConnectedClientDetail client in clients)
					{
						if (SyncPacketHelper.IsUnreliableData(packet.Type))
						{   // handle unreliable packets
							if (!packet.IsChunked || packet.CurrentSliceNumber == 1)
								client.UnreliableLocalSequence++;
							Array.Copy(BitConverter.GetBytes(client.UnreliableLocalSequence), 0, data, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
						}
						else if (SyncPacketHelper.IsReliableData(packet.Type))
						{   // handle reliable packets
							if (!packet.IsChunked || packet.CurrentSliceNumber == 1)
								client.ReliableLocalSequence++;
							Array.Copy(BitConverter.GetBytes(client.ReliableLocalSequence), 0, data, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
						}

						SyncPacketHelper.CalculateAndAddChecksum(data, ServerInformation.ServernameBytes);

						_udpClient.Send(data, data.Length, new(client.IP, _port));
						if (packet.OnDataSend != null)
							packet.OnDataSend?.Invoke(true);

						if (packet.IsChunked)
						{   // save slices in buffer in case of resends
							client.SendSlicesBuffer.TryAdd((client.ReliableLocalSequence, packet.CurrentSliceNumber), data);
							_ = ResendSliceData(client, (client.ReliableLocalSequence, packet.CurrentSliceNumber));
						}
						else if (SyncPacketHelper.IsReliableData(packet.Type))
						{   // save reliable packets in buffer in case of resends
							client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
							_ = ResendPacketData(client, client.ReliableLocalSequence);
						}
					}
					DebugByteMessage(data, $"Send {packet.Type} Bytes: ", true);
				}
			}
			catch (Exception ex)
			{
				switch (ex)
				{
					case ThreadAbortException:
						break;
					case SocketException:
					case ObjectDisposedException:
						Debug.LogError(ex.ToString());
						_mainThreadActions.Enqueue(() => IsConnected = false);
						break;
					default:
						_mainThreadActions.Enqueue(() => IsConnected = false);
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
			}
		}

		/// <summary>
		/// Retry sending a Packet Slice after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendSliceData(SyncConnectedClientDetail client, (ushort, ushort) sequence, int retries = 0)
		{
			// TODO : use RTTx1.25 instead of constant delay
			await Task.Delay(_settings.ResendReliablePacketsDelay);
			if (client != null && client.SendSlicesBuffer.TryGetValue(sequence, out byte[] packetData))
			{
				_udpClient.Send(packetData, packetData.Length, new(client.IP, _port));
				// TODO : handle timeout when packets dont arrive within max number of resends
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendSliceData(client, sequence, retries + 1);
			}
			return;
		}


		/// <summary>
		/// Retry sending a Packet after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendPacketData(SyncConnectedClientDetail client, ushort sequence, int retries = 0)
		{
			// TODO : use RTTx1.25 instead of constant delay
			await Task.Delay(_settings.ResendReliablePacketsDelay);
			if (client != null && client.SendPacketsBuffer.TryGetValue(sequence, out byte[] packetData))
			{
				_udpClient.Send(packetData, packetData.Length, new(client.IP, _port));
				// TODO : handle timeout when packets dont arrive within max number of resends
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendPacketData(client, sequence, retries + 1);
			}
			return;
		}

		/// <summary>
		/// Send ACK to Client, acknowledging the receival of a Packet.
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		private void SendACK(SyncConnectedClientDetail client, ushort sequence)
		{
			byte[] data = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH];
			data[SyncSettings.CRC32_LENGTH] = (byte)SyncPacketType.ACK;
			Array.Copy(BitConverter.GetBytes(sequence), 0, data, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
			SyncPacketHelper.CalculateAndAddChecksum(data, ServerInformation.ServernameBytes);
			_udpClient.Send(data, data.Length, new(client.IP, _port));
		}

		/// <summary>
		/// Send ACK to Client, acknowledging the receival of a Packet Slice.
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="sliceNumber"></param>
		private void SendACK(SyncConnectedClientDetail client, ushort sequence, ushort sliceNumber)
		{
			byte[] data = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.SLICE_NUMBER];
			data[SyncSettings.CRC32_LENGTH] = (byte)SyncPacketType.ACK;
			data[SyncSettings.CRC32_LENGTH] |= 1 << 7;
			Array.Copy(BitConverter.GetBytes(sequence), 0, data, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
			Array.Copy(BitConverter.GetBytes(sliceNumber), 0, data, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.SLICE_NUMBER);
			SyncPacketHelper.CalculateAndAddChecksum(data, ServerInformation.ServernameBytes);
			_udpClient.Send(data, data.Length, new(client.IP, _port));
		}

		private void SendConnectionChallenge(IPAddress address, ulong challenge)
		{
			byte[] data = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.CHALLENGE_LENGTH];
			Array.Copy(BitConverter.GetBytes(challenge), 0, data, SyncSettings.CRC32_LENGTH, SyncSettings.CHALLENGE_LENGTH);
			SyncPacketHelper.CalculateAndAddChecksum(data, ServerInformation.ServernameBytes);
			_udpClient.Send(data, data.Length, new(address, _port));
		}

		private void SendConnectionAccepted(IPAddress address, uint hash)
		{
			byte[] data = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.CLIENT_ID_LENGTH];
			Array.Copy(BitConverter.GetBytes(hash), 0, data, SyncSettings.CRC32_LENGTH, SyncSettings.CLIENT_ID_LENGTH);
			SyncPacketHelper.CalculateAndAddChecksum(data, ServerInformation.ServernameBytes);
			_udpClient.Send(data, data.Length, new(address, _port));
		}

		#endregion

		#region helper methods

		private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			return a.SequenceEqual(b);
		}

		#endregion
	}
}
