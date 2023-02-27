using System;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public class SyncConnectedClient
	{
		public string IP { get; private set; }
		public string Username { get; set; }
		public SyncClientVisualizer Visualizer { get; private set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }

		public SyncConnectedClient(string ip, string username, SyncClientVisualizer visualizer, Color32 color)
		{
			IP = ip;
			Username = username;
			Visualizer = visualizer;
			Color = color;
			LastHeartbeat = DateTime.Now;
		}

		public void Dispose()
		{
			if (Visualizer == null)
				return;

#if UNITY_EDITOR
			GameObject.DestroyImmediate(Visualizer);
#else
			GameObject.Destroy(Visualizer);
#endif
			Visualizer = null;
		}
	}
}
