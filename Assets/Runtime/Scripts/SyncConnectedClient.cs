using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public class SyncConnectedClient
	{
		public string IP { get; private set; }
		public string Username { get; set; }
		public SyncClientVisualizer Visualizer { get; set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }

		public readonly ConcurrentDictionary<ushort, SyncPacket> ReceivedPacketsBuffer = new();
		public readonly ConcurrentDictionary<ushort, byte[]> SendPacketsBuffer = new();

		public ushort UnreliableLocalSequence { get; set; }
		public ushort UnreliableRemoteSequence { get; set; }
		public ushort ReliableLocalSequence { get; set; }
		public ushort ReliableRemoteSequence { get; set; }

		public SyncConnectedClient(string ip, string username, Color32 color)
		{
			IP = ip;
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
