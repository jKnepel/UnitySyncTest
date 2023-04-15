using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
    internal abstract class SyncSocket : IDisposable
    {
		#region protected fields

		protected static readonly SyncSettings _settings = SyncSettings.GetOrCreateSettings();

		protected IPAddress _ip;
		protected int _port;
		protected UdpClient _udpClient;

		protected Thread _listenerThread;
		protected Thread _senderThread;

		protected int _disposeCount;

		protected readonly ConcurrentQueue<Action> _mainThreadActions = new();
		protected readonly ConcurrentQueue<SyncSenderPacket> _packetsToSend = new();

		#endregion

		#region public properties

		private bool _isConnected = false;
		public bool IsConnected
		{
			get => _isConnected;
			protected set
			{
				if (value == _isConnected)
					return;

				_isConnected = value;
				if (value)
					SyncManager.OnLocalClientConnected?.Invoke();
				else
				{
					Dispose();
					SyncManager.OnLocalClientDisconnected?.Invoke();
				}
			}
		}

		public abstract bool IsServer { get; }

		/// <summary>
		/// Information about the Local Server/the Server the local Client is connected to.
		/// </summary>
		public SyncServerInformation ServerInformation { get; protected set; }

		#endregion

		#region lifecycle

		~SyncSocket()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
		}

		public abstract void Dispose(bool disposing);

		#endregion

		#region public methods
		public abstract void DisconnectFromServer();

		public void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!CheckIfEligibleForSending(data, onDataSend, receiver))
				return;

			_packetsToSend.Enqueue(new(SyncPacketType.ReliableData, moduleHash, data, onDataSend, receiver));
		}

		public void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!CheckIfEligibleForSending(data, onDataSend, receiver))
				return;

			_packetsToSend.Enqueue(new(SyncPacketType.ReliableUnorderedData, moduleHash, data, onDataSend, receiver));
		}

		public void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!CheckIfEligibleForSending(data, onDataSend, receiver))
				return;

			_packetsToSend.Enqueue(new(SyncPacketType.UnreliableData, moduleHash, data, onDataSend, receiver));
		}

		public void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!CheckIfEligibleForSending(data, onDataSend, receiver))
				return;

			_packetsToSend.Enqueue(new(SyncPacketType.UnreliableUnorderedData, moduleHash, data, onDataSend, receiver));
		}

		#endregion

		#region helper methods

		protected bool CheckLocalIP(string localIp)
		{
			foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (item.OperationalStatus == OperationalStatus.Up)
				{   // Fetch the properties of this adapter
					IPInterfaceProperties adapterProperties = item.GetIPProperties();
					// Check if the gateway adress exist, if not its most likley a virtual network or smth
					if (adapterProperties.GatewayAddresses.FirstOrDefault() != null)
					{   // Iterate over each available unicast adresses
						foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
						{   // If the IP is a local IPv4 adress
							if (ip.Address.AddressFamily == AddressFamily.InterNetwork && localIp == ip.Address.ToString())
							{
								return true;
							}
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Checks if local client is connected to server and receiver is valid.
		/// </summary>
		/// <param name="onDataSend"></param>
		/// <param name="receiver"></param>
		/// <returns></returns>
		private bool CheckIfEligibleForSending(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (receiver != null && !ServerInformation.ConnectedClients.TryGetValue(receiver.IP, out _))
			{
				Debug.LogError("The given Receiver does not exist in the Server!");
				onDataSend?.Invoke(false);
				return false;
			}

			if (data.Length > 1200)
			{
				Debug.LogError("Only reliable Packets can be larger than 1200 Bytes!");
				onDataSend?.Invoke(false);
				return false;
			}

			return true;
		}

		/// <summary>
		/// Checks if string is ascii conform
		/// </summary>
		/// <param name="str"></param>
		/// <returns></returns>
		protected static bool IsASCIIString(string str)
		{
			return (Encoding.UTF8.GetByteCount(str)) == str.Length;
		}

		protected static void DebugByteMessage(byte[] bytes, string msg, bool inBinary = false)
		{
			if (!SyncManager.IsDebug)
				return;

			foreach (byte d in bytes)
				msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
			Debug.Log(msg);
		}

		#endregion
	}
}
