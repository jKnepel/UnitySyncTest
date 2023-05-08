using System;
using System.Collections.Concurrent;
using System.Net;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
	public class SyncConnectedClient
	{
		public byte ID { get; set; }
		public string Username { get; set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }

		public SyncConnectedClient(byte id, string username, Color32 color)
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
			return ID;
		}
	}

	internal class SyncConnectedClientDetail : SyncConnectedClient
	{
		public IPAddress IP { get; private set; }
		public string IPString { get; private set; }

		internal readonly ConcurrentDictionary<ushort, (uint, byte[])> ReceivedPacketsBuffer = new();
		internal readonly ConcurrentDictionary<ushort, ConcurrentDictionary<ushort, byte[]>> ReceivedChunksBuffer = new();

		internal readonly ConcurrentDictionary<ushort, SyncDataPacket> SendPacketsBuffer = new();
		internal readonly ConcurrentDictionary<(ushort, ushort), SyncDataPacket> SendChunksBuffer = new();

		internal ushort UnreliableLocalSequence { get; set; }
		internal ushort UnreliableRemoteSequence { get; set; }
		internal ushort ReliableLocalSequence { get; set; }
		internal ushort ReliableRemoteSequence { get; set; }

		public SyncConnectedClientDetail(byte id, IPAddress ip, string username, Color32 color) : base(id, username, color)
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
