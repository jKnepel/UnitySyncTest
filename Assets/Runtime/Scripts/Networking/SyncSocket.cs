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

		protected IPAddress _localIP;
		protected int _port;
		protected UdpClient _udpClient;

		protected Thread _listenerThread;
		protected Thread _senderThread;

		protected int _disposeCount;

		protected readonly ConcurrentQueue<Action> _mainThreadActions = new();
		protected readonly ConcurrentQueue<SyncPacket> _packetsToSend = new();

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
					SyncManager.OnConnected?.Invoke();
				else
				{
					Dispose();
					SyncManager.OnDisconnected?.Invoke();
				}
			}
		}

		public abstract bool IsServer { get; }

		/// <summary>
		/// Information about the Local Server/the Server that you are connected to.
		/// </summary>
		public SyncServerInformation ServerInformation { get; protected set; }

		public abstract List<SyncConnectedClient> ConnectedClients { get; }

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

		public abstract void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataReceived, byte? receiver = null);

		public abstract void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataReceived, byte? receiver = null);

		public abstract void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null);

		public abstract void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null);

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

		private const ushort HALF_USHORT = ushort.MaxValue / 2;

		/// <summary>
		/// Checks if a packet is new by comparing the packets sequence number and the corresponding remote sequence number.
		/// Also handles ushort wrap-arounds by allowing packets that are smaller by half of the maximum value.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		protected static bool IsNewPacket(ushort packetSequence, ushort remoteSequence)
		{
			return ((packetSequence > remoteSequence) && (packetSequence - remoteSequence <= HALF_USHORT))
				|| ((packetSequence < remoteSequence) && (remoteSequence - packetSequence > HALF_USHORT));
		}

		/// <summary>
		/// Checks if a packet is next after remote sequence number.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		protected static bool IsNextPacket(ushort packetSequence, ushort remoteSequence)
		{
			return (packetSequence == (ushort)(remoteSequence + 1))
				|| (packetSequence == 0 && remoteSequence == ushort.MaxValue);
		}

		/// <summary>
		/// Returns wether or not the packet is reliable or reliable unordered.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		protected static bool IsReliableData(SyncPacketType type)
		{
			return type == SyncPacketType.ReliableData || type == SyncPacketType.ReliableUnorderedData;
		}

		/// <summary>
		/// Returns wether or not the packet is unreliable or unreliable unordered.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		protected static bool IsUnreliableData(SyncPacketType type)
		{
			return type == SyncPacketType.UnreliableData || type == SyncPacketType.UnreliableUnorderedData;
		}

		protected static bool IsDataPacket(SyncPacketType type)
		{
			return IsReliableData(type) || IsUnreliableData(type);
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
