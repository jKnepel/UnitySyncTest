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
using jKnepel.UnitySync.Networking.Packets;

namespace jKnepel.UnitySync.Networking
{
	internal sealed class NetworkServer : ANetworkSocket
	{
		#region private fields

		private readonly Thread _heartbeatThread;

		private readonly ConcurrentDictionary<IPAddress, byte[]> _pendingConnections = new();

		private readonly ConcurrentDictionary<IPAddress, ClientInformationSocket> _connectedClients = new();
		private readonly ConcurrentDictionary<byte, IPAddress> _idIpTable = new();

		#endregion

		#region public properties

		public override bool IsServer => true;
		public override List<ClientInformation> ConnectedClients => _connectedClients.Values.Cast<ClientInformation>().ToList();

		#endregion

		#region lifecycle

		public NetworkServer(string servername)
		{
			try
			{
				if (!CheckLocalIP(ModuledNetManager.IP))
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

				_localIP = IPAddress.Parse(ModuledNetManager.IP);
				_port = _settings.Port;
				_udpClient = new(_port);
				ServerInformation = new(_localIP, servername, _settings.MaxNumberClients);
				ClientInformation = new(1, _settings.Username, _settings.Color);

				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
				_heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
				_heartbeatThread.Start();
				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();

				ModuledNetManager.OnUpdate += Update;

				ModuledNetManager.AddSyncMessage(new("Server has been opened!"));
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
					ModuledNetManager.OnUpdate -= Update;
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
			ModuledNetManager.AddSyncMessage(new("Disconnected from Server!"));
		}

		public override void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataReceived, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(true, data, onDataReceived, receiver))
				return;

			CreateDataSenderPackets(EPacketType.ReliableData, moduleHash, data, onDataReceived, receiver);
		}

		public override void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataReceived, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(true, data, onDataReceived, receiver))
				return;

			CreateDataSenderPackets(EPacketType.ReliableUnorderedData, moduleHash, data, onDataReceived, receiver);
		}

		public override void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(false, data, onDataSend, receiver))
				return;

			CreateDataSenderPackets(EPacketType.UnreliableData, moduleHash, data, onDataSend, receiver);
		}

		public override void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(false, data, onDataSend, receiver))
				return;

			CreateDataSenderPackets(EPacketType.UnreliableUnorderedData, moduleHash, data, onDataSend, receiver);
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
					byte typeBytes = receivedBytes[ModuledNetSettings.CRC32_LENGTH];
					byte mask = 1 << 7;
					typeBytes &= (byte)~mask;
					EPacketType packetType = (EPacketType)typeBytes;

					// TODO : send connectionclosed when forcefully disconnected client sends packet
					// TODO : otherwise refresh sequence numbers

					if (packetType.GetType().IsSubclassOf(typeof(AConnectionNetworkPacket)))
					{
						HandleConnectionPacket(sender, packetType, receivedBytes);
						continue;
					}

					if (packetType.GetType().IsSubclassOf(typeof(ASequencedNetworkPacket)))
					{   // handle sequenced packets for connected clients
						if (!_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
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

		private void HandleConnectionPacket(IPAddress sender, EPacketType packetType, byte[] packet)
		{
			switch (packetType)
			{
				case EPacketType.ACK:
					ACKPacket ack = new(packet);
					if (!ack.TryDeserialize() || !_connectedClients.TryGetValue(sender, out ClientInformationSocket ackClient))
						return;

					if (ack.IsChunked)
						ackClient.SendChunksBuffer.TryRemove((ack.Sequence, ack.SliceNumber), out _);
					else
						ackClient.SendPacketsBuffer.TryRemove(ack.Sequence, out _);
					break;
				case EPacketType.ConnectionRequest:
					ConnectionRequestPacket connectionRequest = new(packet);
					if (!connectionRequest.TryDeserialize())
						return;

					if (_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
					{   // resend client id in case of client not receiving theirs
						_packetsToSend.Enqueue((client.IP, new ConnectionAcceptedPacket(client.ID)));
						return;
					}

					if (_connectedClients.Count >= ServerInformation.MaxNumberConnectedClients)
					{   // send connection denied packet if no space available
						_packetsToSend.Enqueue((sender, new ConnectionDeniedPacket()));
						return;
					}

					// create challenge packet and send it if enough space available
					ConnectionChallengePacket connectionChallenge = new();
					using (SHA256 h = SHA256.Create())
					{   // save sha256 hash of challenge nonce for comparing answer from client
						byte[] hashedChallenge = h.ComputeHash(BitConverter.GetBytes(connectionChallenge.Challenge));
						_pendingConnections.AddOrUpdate(sender, hashedChallenge, (key, oldValue) => oldValue = hashedChallenge);
					}
					_packetsToSend.Enqueue((sender, connectionChallenge));

					break;
				case EPacketType.ChallengeAnswer:
					ChallengeAnswerPacket challengeAnswer = new(packet);
					if (!challengeAnswer.TryDeserialize())
						return;

					if (!_pendingConnections.TryGetValue(sender, out byte[] value))
						return;

					if (!CompareByteArrays(value, challengeAnswer.ChallengeAnswer) || _connectedClients.Count >= ServerInformation.MaxNumberConnectedClients)
					{   // send connection denied packet if no space available or challenge answer is incorrect
						_packetsToSend.Enqueue((sender, new ConnectionDeniedPacket()));
						return;
					}

					byte newID = AddClient(sender, challengeAnswer.Username, challengeAnswer.Color);
					_packetsToSend.Enqueue((sender, new ConnectionAcceptedPacket(newID)));
					break;
			}
		}

		private void HandleSequencedPacket(ClientInformationSocket sender, EPacketType packetType, byte[] packet)
		{	// handle data packets
			// TODO : handle Client info
			DataPacket dataPacket = new(packetType, packet);
			if (!dataPacket.TryDeserialize())
				return;

			// resend packets that are not addressed to server

			// TODO : handle 0 (all clients) or > 1 (specific client)

			if (dataPacket.ClientID != 1)
				return;

			// unreliable packet sequence
			if (IsUnreliableData(packetType))
			{   // ignore old packets unless they are unordered
				if (!IsNewPacket(dataPacket.Sequence, sender.UnreliableRemoteSequence)
					&& packetType != EPacketType.UnreliableUnorderedData)
					return;

				sender.UnreliableRemoteSequence = dataPacket.Sequence;
				ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, dataPacket.Data);
				return;
			}

			// send ACK
			_packetsToSend.Enqueue((sender.IP, new ACKPacket(dataPacket.Sequence, dataPacket.SliceNumber)));

			// reliable (and chunked) packet sequence
			if (dataPacket.IsChunked)
			{   // ignore old packets unless they are unordered
				if (!IsNewPacket(dataPacket.Sequence, sender.ReliableRemoteSequence)
					&& packetType != EPacketType.ReliableUnorderedData)
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
					&& packetType != EPacketType.ReliableUnorderedData)
				{
					sender.ReceivedPacketsBuffer.TryAdd(dataPacket.Sequence, (dataPacket.ModuleHash, data));
					return;
				}

				sender.ReliableRemoteSequence = dataPacket.Sequence;
				ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, data);
			}
			else
			{   // ignore old packets unless they are unordered
				if (!IsNewPacket(dataPacket.Sequence, sender.ReliableRemoteSequence)
					&& packetType != EPacketType.ReliableUnorderedData)
					return;

				if (!IsNextPacket(dataPacket.Sequence, sender.ReliableRemoteSequence)
					&& packetType != EPacketType.ReliableUnorderedData)
				{   // if a packet is missing in the sequence keep it in the buffer
					sender.ReceivedPacketsBuffer.TryAdd(dataPacket.Sequence, (dataPacket.ModuleHash, dataPacket.Data));
					return;
				}

				sender.ReliableRemoteSequence = dataPacket.Sequence;
				ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, dataPacket.Data);
			}

			// apply all packets from that senders buffer that are now next in the sequence
			ushort sequence = dataPacket.Sequence;
			while (sender.ReceivedPacketsBuffer.Count > 0)
			{
				sequence++;
				if (!sender.ReceivedPacketsBuffer.TryRemove(sequence, out (uint, byte[]) bufferedPacket))
					break;

				sender.ReliableRemoteSequence = sequence;
				ModuledNetManager.DataReceived?.Invoke(bufferedPacket.Item1, sender.ID, bufferedPacket.Item2);
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
					HeartbeatPacket heartbeat = new(ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients, (byte)_connectedClients.Count);
					_udpClient.Send(heartbeat.Bytes, heartbeat.Bytes.Length, new(IPAddress.Broadcast, _port));
					DebugByteMessage(heartbeat.Bytes, "Send Heartbeat Bytes: ");
					Thread.Sleep(_settings.ServerHeartbeatDelay);
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
							_mainThreadActions.Enqueue(() => IsConnected = false);
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
					if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out (IPAddress, ANetworkPacket) packet))
						continue;

					if (packet.Item2 is AConnectionNetworkPacket c)
					{
						c.Serialize();
						_udpClient.Send(c.Bytes, c.Bytes.Length, new(packet.Item1, _port));
						continue;
					}
					
					if (packet.Item2 is ASequencedNetworkPacket s)
					{
						if (!_connectedClients.TryGetValue(packet.Item1, out ClientInformationSocket client))
							continue;

						if (IsUnreliableData(s.Type))
						{	// serialize with unrelaible sequence and increment
							s.Serialize(client.UnreliableLocalSequence);
							_udpClient.Send(s.Bytes, s.Bytes.Length, new(packet.Item1, _port));
							client.UnreliableLocalSequence++;

							if (s is DataPacket ud)
								ud.Callback?.Invoke(true);

							continue;
						}

						// serialize with reliable sequence and increment
						byte[] data = s.Serialize(client.ReliableLocalSequence);
						_udpClient.Send(data, data.Length, new(packet.Item1, _port));
						client.ReliableLocalSequence++;

						if (s is DataPacket rd)
						{
							if (rd.IsChunked)
							{   // save slices in buffer in case of resends
								client.SendChunksBuffer.TryAdd((client.ReliableLocalSequence, rd.SliceNumber), data);
								_ = ResendSliceData(packet.Item1, (client.ReliableLocalSequence, rd.SliceNumber));

								if (rd.SliceNumber < rd.NumberOfSlices - 1)
								{   // if slices are still missing advance slice number and add to queue
									rd.SetSliceData();
									_packetsToSend.Enqueue((packet.Item1, rd));
								}
								else
								{	// invoke callback once all slices are send
									rd.Callback?.Invoke(true);
								}
								continue;
							}
							rd.Callback?.Invoke(true);
						}

						// save packets in buffer in case of resends
						client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
						_ = ResendPacketData(packet.Item1, client.ReliableLocalSequence);
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
							_mainThreadActions.Enqueue(() => IsConnected = false);
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
		private async Task ResendSliceData(IPAddress clientIP, (ushort, ushort) sequence, int retries = 0)
		{
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (_connectedClients.TryGetValue(clientIP, out ClientInformationSocket client) 
				&& client.SendChunksBuffer.TryGetValue(sequence, out byte[] data))
			{
				_udpClient.Send(data, data.Length, new(client.IP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendSliceData(clientIP, sequence, retries + 1);
				else
					RemoveClient(client.ID, true);
			}
		}


		/// <summary>
		/// Retry sending a Packet after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendPacketData(IPAddress clientIP, ushort sequence, int retries = 0)
		{
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (_connectedClients.TryGetValue(clientIP, out ClientInformationSocket client)
				&& client.SendPacketsBuffer.TryGetValue(sequence, out byte[] data))
			{
				_udpClient.Send(data, data.Length, new(client.IP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendPacketData(clientIP, sequence, retries + 1);
				else
					RemoveClient(client.ID, true);
			}
		}

		#endregion

		#region helper methods

		private bool GetClientById(byte clientID, out ClientInformationSocket client)
		{
			if (!_idIpTable.TryGetValue(clientID, out IPAddress clientIP)
				|| !_connectedClients.TryGetValue(clientIP, out ClientInformationSocket foundClient))
			{
				client = null;
				return false;
			}

			client = foundClient;
			return true;
		}

		private byte AddClient(IPAddress ip, string username, Color32 color)
		{
			// find next available client id
			byte newID = 0;
			for (byte i = 2; i < ServerInformation.MaxNumberConnectedClients; i++)
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
			ClientInformationSocket newClient = new(newID, ip, username, color);
			if (!_connectedClients.TryAdd(ip, newClient) || !_idIpTable.TryAdd(newID, ip) || !_pendingConnections.TryRemove(ip, out _))
				throw new Exception("Something went wrong creating the Client!");

			ServerInformation.NumberConnectedClients++;
			_mainThreadActions.Enqueue(() => ModuledNetManager.OnClientConnected?.Invoke(newID));

			return newID;
		}

		// TODO : add reason
		private bool RemoveClient(byte clientID, bool saveClient)
		{
			if (!GetClientById(clientID, out ClientInformationSocket client))
				return false;

			_packetsToSend.Enqueue((client.IP, new ConnectionClosedPacket()));
			_connectedClients.TryRemove(client.IP, out _);
			ServerInformation.NumberConnectedClients--;
			_mainThreadActions.Enqueue(() => ModuledNetManager.OnClientConnected?.Invoke(clientID));

			foreach (ClientInformationSocket remainingClient in _connectedClients.Values)
				_packetsToSend.Enqueue((remainingClient.IP, new ClientDisconnectedPacket(clientID)));
			
			if (saveClient)
			{
				// TODO : save disconnected clients in buffer unless forcefully disconnected
			}
			return true;
		}

		private bool CheckIfEligibleForSending(bool isReliable, byte[] data, Action<bool> dataCallback, byte? receiver = null)
		{
			if (receiver != null && !GetClientById((byte)receiver, out ClientInformationSocket _))
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

		private void CreateDataSenderPackets(EPacketType type, uint moduleHash, byte[] data, Action<bool> dataCallback, byte? receiver = null)
		{
			if (receiver != null)
			{
				if (GetClientById((byte)receiver, out ClientInformationSocket client))
					_packetsToSend.Enqueue((client.IP, new DataPacket(type, dataCallback, moduleHash, data, ClientInformation.ID)));
				else
					dataCallback?.Invoke(false);
				return;
			}

			foreach (ClientInformationSocket client in _connectedClients.Values)
				_packetsToSend.Enqueue((client.IP, new DataPacket(type, dataCallback, moduleHash, data, ClientInformation.ID)));
		}

		private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			return a.SequenceEqual(b);
		}

		#endregion
	}
}
