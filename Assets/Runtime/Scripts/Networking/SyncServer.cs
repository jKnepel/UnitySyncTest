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
		private readonly ConcurrentDictionary<byte, IPAddress> _idIpTable = new();

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


		#endregion

		#region public methods
		
		public override void DisconnectFromServer()
		{
			if (!IsConnected)
				return;

			Dispose();
			SyncManager.AddSyncMessage(new("Disconnected from Server!"));
		}

		public override void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataReceived, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(true, data, onDataReceived, receiver))
				return;

			CreateDataSenderPackets(SyncPacketType.ReliableData, moduleHash, data, onDataReceived, receiver);
		}

		public override void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataReceived, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(true, data, onDataReceived, receiver))
				return;

			CreateDataSenderPackets(SyncPacketType.ReliableUnorderedData, moduleHash, data, onDataReceived, receiver);
		}

		public override void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(false, data, onDataSend, receiver))
				return;

			CreateDataSenderPackets(SyncPacketType.UnreliableData, moduleHash, data, onDataSend, receiver);
		}

		public override void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(false, data, onDataSend, receiver))
				return;

			CreateDataSenderPackets(SyncPacketType.UnreliableUnorderedData, moduleHash, data, onDataSend, receiver);
		}

		#endregion

		#region listener logic

		private void ListenerThread()
		{
			while (_disposeCount == 0 && IsConnected)
			{
				try
				{	// get packet ip headers
					IPEndPoint receiveEndpoint = new(IPAddress.Any, _settings.Port);
					byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
					IPAddress sender = receiveEndpoint.Address;
					if (sender.Equals(_localIP))
						continue;

					// get packet type without chunked packet bit
					byte typeBytes = receivedBytes[SyncSettings.CRC32_LENGTH];
					byte mask = 1 << 7;
					typeBytes &= (byte)~mask;
					SyncPacketType packetType = (SyncPacketType)typeBytes;

					// TODO : send connectionclosed when disconnected client sends packet

					if (packetType.GetType().IsSubclassOf(typeof(SyncConnectionPacket)))
					{
						HandleConnectionPacket(sender, packetType, receivedBytes);
						continue;
					}

					if (packetType.GetType().IsSubclassOf(typeof(SyncSequencedPacket)))
					{   // handle sequenced packets for connected clients
						if (!_connectedClients.TryGetValue(sender, out SyncConnectedClientDetail client))
							continue;

						client.LastHeartbeat = DateTime.Now;
						HandleSequencedPacket(client, packetType, receivedBytes);
						continue;
					}
				}
				catch (Exception ex)
				{
					switch (ex)
					{
						case IndexOutOfRangeException:
						case ArgumentException:
							continue;
						case ThreadAbortException:
							break;
						case SocketException:
						case ObjectDisposedException:
							Debug.LogError($"An Error occurred in the Server. Try again later! \n {ex}");
							_mainThreadActions.Enqueue(() => IsConnected = false);
							break;
						default:
							_mainThreadActions.Enqueue(() => IsConnected = false);
							ExceptionDispatchInfo.Capture(ex).Throw();
							throw;
					}
				}
			}
		}

		private void HandleConnectionPacket(IPAddress sender, SyncPacketType packetType, byte[] packet)
		{
			switch (packetType)
			{
				case SyncPacketType.ConnectionRequest:
					SyncConnectionRequestPacket connectionRequest = new(sender, packet);
					if (!connectionRequest.CheckCRC32Checksum())
						return;

					if (_connectedClients.TryGetValue(sender, out SyncConnectedClientDetail client))
					{   // resend client id in case of client not receiving theirs
						_packetsToSend.Enqueue(new SyncConnectionAcceptedPacket(sender, client.ID));
						return;
					}

					if (_connectedClients.Count >= byte.MaxValue - 2)
					{   // send connection denied packet if no space available
						_packetsToSend.Enqueue(new SyncConnectionDeniedPacket(sender));
						return;
					}

					// create challenge packet and send it if enough space available
					SyncConnectionChallengePacket connectionChallenge = new(sender);
					using (SHA256 h = SHA256.Create())
					{   // save sha256 hash of challenge nonce for comparing answer from client
						byte[] hashedChallenge = h.ComputeHash(BitConverter.GetBytes(connectionChallenge.Challenge));
						_pendingConnections.AddOrUpdate(sender, hashedChallenge, (key, oldValue) => oldValue = hashedChallenge);
					}
					_packetsToSend.Enqueue(connectionChallenge);

					break;
				case SyncPacketType.ChallengeAnswer:
					SyncChallengeAnswerPacket challengeAnswer = new(sender, packet);
					if (!challengeAnswer.CheckCRC32Checksum())
						return;

					if (!_pendingConnections.TryGetValue(sender, out byte[] value))
						return;

					if (!CompareByteArrays(value, challengeAnswer.HashedChallenge) || _connectedClients.Count >= byte.MaxValue - 2)
					{   // send connection denied packet if no space available or challenge answer is incorrect
						_packetsToSend.Enqueue(new SyncConnectionDeniedPacket(sender));
						return;
					}

					// find next available client id
					byte newID = 0;
					for (byte i = 2; i < byte.MaxValue - 2; i++)
					{
						if (!_idIpTable.TryGetValue(i, out _))
						{
							newID = i;
							break;
						}
					}

					if (newID == 0)
						throw new Exception("Something went wrong assigning the Client ID!");

					// create client
					SyncConnectedClientDetail newClient = new(newID, sender, challengeAnswer.Username, challengeAnswer.Color);
					if (!_connectedClients.TryAdd(sender, newClient) || !_idIpTable.TryAdd(newID, sender) || !_pendingConnections.TryRemove(sender, out _))
						throw new Exception("Something went wrong creating the Client!");


					_packetsToSend.Enqueue(new SyncConnectionAcceptedPacket(sender, newID));

					break;
			}
		}

		private void HandleSequencedPacket(SyncConnectedClientDetail sender, SyncPacketType packetType, byte[] packet)
		{
			if (IsDataPacket(packetType))
			{
				HandleDataPacket(sender, packetType, packet);
				return;
			}

			switch (packetType)
			{
				case SyncPacketType.ACK:
					SyncACKPacket ack = new(sender.ID, packet);
					if (!ack.CheckCRC32Checksum(ServerInformation.ServernameBytes))
						return;

					if (ack.IsChunked)
						sender.SendChunksBuffer.TryRemove((ack.Sequence, ack.SliceNumber), out _);
					else
						sender.SendPacketsBuffer.TryRemove(ack.Sequence, out _);
					break;
			}
		}

		private void HandleDataPacket(SyncConnectedClientDetail sender, SyncPacketType packetType, byte[] packet)
		{	// handle data packets
			SyncDataPacket dataPacket = new(packetType, sender.ID, packet);
			if (!dataPacket.CheckCRC32Checksum(ServerInformation.ServernameBytes))
				return;

			// resend packets that are not addressed to server
			// TODO : handle 0 (all clients) or > 1 (specific client)

			if (dataPacket.ClientID != 1)
				return;

			// unreliable packet sequence
			if (IsUnreliableData(packetType))
			{	// ignore old packets unless they are unordered
				if (!IsNewPacket(dataPacket.Sequence, sender.UnreliableRemoteSequence)
					&& packetType != SyncPacketType.UnreliableUnorderedData)
					return;

				sender.UnreliableRemoteSequence = dataPacket.Sequence;
				SyncManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, dataPacket.Data);
				return;
			}

			// send ACK
			_packetsToSend.Enqueue(new SyncACKPacket(sender.ID, dataPacket.Sequence, ServerInformation.ServernameBytes, dataPacket.SliceNumber));

			// reliable (and chunked) packet sequence
			if (dataPacket.IsChunked)
			{   // ignore old packets unless they are unordered
				if (!IsNewPacket(dataPacket.Sequence, sender.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData)
					return;

				if (!sender.ReceivedChunksBuffer.TryGetValue(dataPacket.Sequence, out ConcurrentDictionary<ushort, byte[]> bufferedChunk))
				{   // create chunked packet if it doesn't exist yet
					ConcurrentDictionary<ushort, byte[]> dict = new();
					sender.ReceivedChunksBuffer.TryAdd(dataPacket.Sequence, dict);
					bufferedChunk = dict;
				}

				// add slice to chunk and return if chunk is not complete
				bufferedChunk.TryAdd(dataPacket.SliceNumber, dataPacket.Data);
				if (bufferedChunk.Count != dataPacket.NumberOfSlices)
					return;

				// concatenate slices to complete packet and remove it from chunk buffer
				List<byte> dataBytes = new();
				for (ushort i = 1; i <= dataPacket.NumberOfSlices; i++)
				{
					if (!bufferedChunk.TryGetValue(i, out byte[] sliceData)) 
						return;
					dataBytes.AddRange(sliceData);
				}
				byte[] data = dataBytes.ToArray();
				sender.ReceivedChunksBuffer.TryRemove(dataPacket.Sequence, out _);

				// if a packet is missing in the sequence keep it in the packet buffer
				if (!IsNextPacket(dataPacket.Sequence, sender.ReliableRemoteSequence) 
					&& packetType != SyncPacketType.ReliableUnorderedData)
				{
					sender.ReceivedPacketsBuffer.TryAdd(dataPacket.Sequence, (dataPacket.ModuleHash, data));
					return;
				}

				sender.ReliableRemoteSequence = dataPacket.Sequence;
				SyncManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, data);
			}
			else
			{   // ignore old packets unless they are unordered
				if (!IsNewPacket(dataPacket.Sequence, sender.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData)
					return;

				if (!IsNextPacket(dataPacket.Sequence, sender.ReliableRemoteSequence)
					&& packetType != SyncPacketType.ReliableUnorderedData)
				{   // if a packet is missing in the sequence keep it in the buffer
					sender.ReceivedPacketsBuffer.TryAdd(dataPacket.Sequence, (dataPacket.ModuleHash, dataPacket.Data));
					return;
				}

				sender.ReliableRemoteSequence = dataPacket.Sequence;
				SyncManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, dataPacket.Data);
			}

			// apply all packets from that senders buffer that are now next in the sequence
			ushort sequence = dataPacket.Sequence;
			while (sender.ReceivedPacketsBuffer.Count > 0)
			{
				sequence++;
				if (!sender.ReceivedPacketsBuffer.TryRemove(sequence, out (uint, byte[]) bufferedPacket))
					break;

				sender.ReliableRemoteSequence = sequence;
				SyncManager.DataReceived?.Invoke(bufferedPacket.Item1, sender.ID, bufferedPacket.Item2);
			}
		}

		#endregion

		#region sender logic

		private void HeartbeatThread()
		{
			// send heartbeat used for discovery until server is closed
			while (_disposeCount == 0 && IsConnected)
			{
				try
				{
					// TODO : only recalculate if connected clients changed
					SyncHeartbeatPacket heartbeat = new(ServerInformation.ServernameBytes, (byte)_connectedClients.Count);
					_udpClient.Send(heartbeat.Bytes, heartbeat.Bytes.Length, new(IPAddress.Broadcast, _port));
					DebugByteMessage(heartbeat.Bytes, "Send Heartbeat Bytes: ");
					Thread.Sleep(_settings.HeartbeatDelay);
				}
				catch (Exception ex)
				{
					switch (ex)
					{
						case IndexOutOfRangeException:
						case ArgumentException:
							continue;
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
		}

		private void SenderThread()
		{
			while (_disposeCount == 0 && IsConnected)
			{
				try
				{
					if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out SyncPacket packet))
						continue;

					if (packet.GetType().IsSubclassOf(typeof(SyncConnectionPacket)))
					{
						IPAddress receiver = ((SyncConnectionPacket)packet).Client;
						_udpClient.Send(packet.Bytes, packet.Bytes.Length, new(receiver, _port));
						continue;
					}
					
					if (packet.GetType().IsSubclassOf(typeof(SyncSequencedPacket)))
					{
						byte clientID = ((SyncSequencedPacket)packet).Client;
						if (!GetClientById(clientID, out SyncConnectedClientDetail client))
							continue;

						_udpClient.Send(packet.Bytes, packet.Bytes.Length, new(client.IP, _port));

						if (packet.GetType() == typeof(SyncACKPacket))
							continue;

						// TODO : implement client info

						if (IsReliableData(packet.Type))
						{   // save packet/chunk for resending if required
							SyncDataPacket dataPacket = (SyncDataPacket)packet;
							if (dataPacket.IsChunked)
							{   // save slices in buffer in case of resends
								client.SendChunksBuffer.TryAdd((client.ReliableLocalSequence, dataPacket.SliceNumber), dataPacket);
								_ = ResendSliceData(clientID, (client.ReliableLocalSequence, dataPacket.SliceNumber));
							}
							else
							{   // save reliable packets in buffer in case of resends
								client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, dataPacket);
								_ = ResendPacketData(clientID, client.ReliableLocalSequence);
							}
							client.ReliableLocalSequence++;
						}
						else
						{	// invoke callback upon successful sending for unreliable packets
							((SyncDataPacket)packet).Callback?.Invoke(true);
							client.UnreliableLocalSequence++;
						}
					}
				}
				catch (Exception ex)
				{
					switch (ex)
					{
						case IndexOutOfRangeException:
						case ArgumentException:
							continue;
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
		}

		/// <summary>
		/// Retry sending a Packet Slice after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendSliceData(byte clientID, (ushort, ushort) sequence, int retries = 0)
		{
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (GetClientById(clientID, out SyncConnectedClientDetail client) 
				&& client.SendChunksBuffer.TryGetValue(sequence, out SyncDataPacket packetData))
			{
				_udpClient.Send(packetData.Bytes, packetData.Bytes.Length, new(client.IP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendSliceData(clientID, sequence, retries + 1);
				else
					DisconnectClient(client.ID);
			}
		}


		/// <summary>
		/// Retry sending a Packet after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendPacketData(byte clientID, ushort sequence, int retries = 0)
		{
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (GetClientById(clientID, out SyncConnectedClientDetail client)
				&& client.SendPacketsBuffer.TryGetValue(sequence, out SyncDataPacket packetData))
			{
				_udpClient.Send(packetData.Bytes, packetData.Bytes.Length, new(client.IP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendPacketData(clientID, sequence, retries + 1);
				else
					DisconnectClient(client.ID);
			}
		}

		#endregion

		#region helper methods

		private bool GetClientById(byte clientID, out SyncConnectedClientDetail client)
		{
			if (!_idIpTable.TryGetValue(clientID, out IPAddress clientIP)
				|| !_connectedClients.TryGetValue(clientIP, out SyncConnectedClientDetail foundClient))
			{
				client = null;
				return false;
			}

			client = foundClient;
			return true;
		}

		// TODO : add reason
		private bool DisconnectClient(byte clientID)
		{
			// TODO : save disconnected clients in buffer in case of reconnecting sequence numbers
			if (!GetClientById(clientID, out SyncConnectedClientDetail client))
				return false;

			_packetsToSend.Enqueue(new SyncConnectionClosedPacket(client.IP));
			_connectedClients.TryRemove(client.IP, out _);
			// TODO : send message to all clients that a client disconnected
			return true;
		}

		private bool CheckIfEligibleForSending(bool isReliable, byte[] data, Action<bool> dataCallback, byte? receiver = null)
		{
			if (receiver != null && !GetClientById((byte)receiver, out SyncConnectedClientDetail _))
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

		private void CreateDataSenderPackets(SyncPacketType type, uint moduleHash, byte[] data, Action<bool> dataCallback, byte? receiver = null)
		{
			if (receiver != null)
			{
				_packetsToSend.Enqueue(new SyncDataPacket(type, dataCallback, moduleHash, data, ServerInformation.ServernameBytes, receiver, ServerInformation.ServerID));
				return;
			}

			foreach (SyncConnectedClientDetail client in _connectedClients.Values)
				_packetsToSend.Enqueue(new SyncDataPacket(type, dataCallback, moduleHash, data, ServerInformation.ServernameBytes, client.ID, ServerInformation.ServerID));
		}

		private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			return a.SequenceEqual(b);
		}

		#endregion
	}
}
