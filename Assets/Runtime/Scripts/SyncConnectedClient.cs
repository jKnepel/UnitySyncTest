using System;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public struct SyncConnectedClient
	{
		public string IP { get; private set; }
		public string Username { get; set; }
		public Transform Visualizer { get; private set; }
		public Color32 Color { get; set; }
		public bool IsVisible { get; set; }
		public DateTime lastHeartbeat { get; set; }

		public SyncConnectedClient(string ip, string username, Transform visualizer, Color32 color)
		{
			IP = ip;
			Username = username;
			Visualizer = visualizer;
			Color = color;
			IsVisible = false;
			lastHeartbeat = DateTime.Now;
		}
	}
}
