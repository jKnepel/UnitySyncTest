using System;
using System.Timers;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public struct SyncConnectedClient
	{
		public string IP { get; private set; }
		public string Username { get; set; }
		public Transform Visualizer { get; private set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }
		public Timer TimeoutTimer { get; private set; }

		public SyncConnectedClient(string ip, string username, Transform visualizer, Color32 color, Timer timeoutTimer)
		{
			IP = ip;
			Username = username;
			Visualizer = visualizer;
			Color = color;
			LastHeartbeat = DateTime.Now;
			TimeoutTimer = timeoutTimer;
		}
	}
}
