using System;
using System.Collections.Concurrent;
using System.Net;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public class SyncConnectedClient
	{
		public IPAddress IP { get; private set; }
		public string IPString { get; private set; }
		public string Username { get; set; }
		public SyncClientVisualizer Visualizer { get; set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }

		internal readonly ConcurrentDictionary<ushort, SyncPacket> ReceivedPacketsBuffer = new();
		internal readonly ConcurrentDictionary<ushort, byte[]> SendPacketsBuffer = new();

		internal ushort UnreliableLocalSequence { get; set; }
		internal ushort UnreliableRemoteSequence { get; set; }
		internal ushort ReliableLocalSequence { get; set; }
		internal ushort ReliableRemoteSequence { get; set; }

		public SyncConnectedClient(IPAddress ip, string username, Color32 color)
		{
			IP = ip;
			IPString = ip.ToString();
			Username = username;
			Visualizer = null;
			Color = color;
			LastHeartbeat = DateTime.Now;

			UnreliableLocalSequence = 0;
			UnreliableRemoteSequence = 0;
			ReliableLocalSequence = 0;
			ReliableRemoteSequence = 0;
		}

		public void Dispose()
		{


			if (Visualizer == null)
				return;

#if UNITY_EDITOR
			GameObject.DestroyImmediate(Visualizer.gameObject);
#else
			GameObject.Destroy(Visualizer.gameObject);
#endif
			Visualizer = null;
		}
	}
}
