using System;
using System.Timers;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public class SyncConnectedClient
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

		public void Dispose()
		{
			TimeoutTimer.Dispose();
#if UNITY_EDITOR
			GameObject.DestroyImmediate(Visualizer.gameObject);
#else
			GameObject.Destroy(Visualizer.gameObject);
#endif
		}
	}
}
