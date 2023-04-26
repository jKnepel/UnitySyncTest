using System;
using System.Collections.Concurrent;
using System.Net;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
	public class SyncConnectedClient
	{
		public uint ID { get; set; }
		public string Username { get; set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }

		public SyncConnectedClient(uint id, string username, Color32 color)
		{
			ID = id;
			Username = username;
			Color = color;
			LastHeartbeat = DateTime.Now;
		}

		public override bool Equals(object obj)
		{
			if ((obj == null) || !GetType().Equals(obj.GetType()))
			{
				return false;
			}
			else
			{
				return ID.Equals(((SyncConnectedClient)obj).ID);
			}
		}

		public override int GetHashCode()
		{
			return (int)ID;
		}
	}

	internal class SyncConnectedClientDetail : SyncConnectedClient
	{
		public IPAddress IP { get; private set; }
		public string IPString { get; private set; }

		internal readonly ConcurrentDictionary<ushort, SyncReceiverPacket> ReceivedPacketsBuffer = new();
		internal readonly ConcurrentDictionary<ushort, byte[]> SendPacketsBuffer = new();
		internal readonly ConcurrentDictionary<(ushort, ushort), byte[]> SendSlicesBuffer = new();

		internal ushort UnreliableLocalSequence { get; set; }
		internal ushort UnreliableRemoteSequence { get; set; }
		internal ushort ReliableLocalSequence { get; set; }
		internal ushort ReliableRemoteSequence { get; set; }

		public SyncConnectedClientDetail(uint id, IPAddress ip, string username, Color32 color) : base(id, username, color)
		{
			IP = ip;
			IPString = ip.ToString();

			UnreliableLocalSequence = 0;
			UnreliableRemoteSequence = 0;
			ReliableLocalSequence = 0;
			ReliableRemoteSequence = 0;
		}
	}
}
