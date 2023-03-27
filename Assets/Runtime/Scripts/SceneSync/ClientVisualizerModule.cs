using jKnepel.UnitySync.Networking;
using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace jKnepel.UnitySync.SceneSync
{
    public class ClientVisualizerModule : SyncUnreliableModule
    {
		private static readonly SyncSettings _settings = SyncManager.GetSyncSettings();

		private readonly Dictionary<SyncConnectedClient, SyncClientVisualizer> _visualizers = new();

		private Vector3 _lastCameraPosition;
		private Vector3 _lastCameraRotation;
		private int _cameraUpdateDelay = _settings.ClientVisualizerUpdateDelay;

		private Transform _visualizerParent;

		#region lifecycle

		public ClientVisualizerModule(Transform parent) : base()
		{
			SyncManager.OnUpdate += Update;
			SyncManager.OnClientDisconnected += RemoveConnectedClient;

			_visualizerParent = parent;
		}

		~ClientVisualizerModule()
		{
			Dispose();
		}

		public void Dispose()
		{
			SyncManager.OnUpdate -= Update;
			SyncManager.OnClientDisconnected -= RemoveConnectedClient;

			if (IsModuleRegistered)
				UnregisterModule();
		}

		public void Update()
		{
			_cameraUpdateDelay++;
#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView.camera.transform.hasChanged)
				CurrentCameraMoved(SceneView.lastActiveSceneView.camera.transform);
#else
			if (Camera.current.transform && Camera.current.transform.hasChanged)
				CurrentCameraMoved(Camera.current.transform);
#endif
		}

		#endregion

		#region public methods

		public void RemoveConnectedClient(IPAddress ip)
		{
			foreach (KeyValuePair<SyncConnectedClient, SyncClientVisualizer> client in _visualizers)
			{
				if (client.Key.IP.Equals(ip))
				{
					_visualizers.Remove(client.Key);
#if UNITY_EDITOR
					GameObject.DestroyImmediate(client.Value.gameObject);
#else
					GameObject.Destroy(client.Value.gameObject);
#endif
					break;
				}
			}
		}

		public override void OnReceiveData(SyncConnectedClient client, byte[] data)
		{
			if (SyncManager.IsDebug)
				Debug.Log($"Received Camera Update from {client.IP}");

			int size = sizeof(float);
			byte[] positionX = GetBytesFromArray(data, 0 * size, size);
			byte[] positionY = GetBytesFromArray(data, 1 * size, size);
			byte[] positionZ = GetBytesFromArray(data, 2 * size, size);
			byte[] rotationX = GetBytesFromArray(data, 3 * size, size);
			byte[] rotationY = GetBytesFromArray(data, 4 * size, size);
			byte[] rotationZ = GetBytesFromArray(data, 5 * size, size);

			client.LastHeartbeat = DateTime.Now;
			if (!_visualizers.TryGetValue(client, out SyncClientVisualizer visualizer))
			{
				GameObject obj = GameObject.Instantiate(_settings.ClientVisualizer.gameObject, _visualizerParent);
				visualizer = obj.GetComponent<SyncClientVisualizer>();
				visualizer.UpdateVisualizer(client.IPString, client.Username, client.Color);
				_visualizers.Add(client, visualizer);
			}

			visualizer.transform.position = new Vector3(BitConverter.ToSingle(positionX),
				BitConverter.ToSingle(positionY),
				BitConverter.ToSingle(positionZ));
			visualizer.transform.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
				BitConverter.ToSingle(rotationY),
				BitConverter.ToSingle(rotationZ));
			if (!visualizer.gameObject.activeSelf)
				visualizer.gameObject.SetActive(true);
		}

		#endregion

		#region private methods

		private void CurrentCameraMoved(Transform camera)
		{
			// limit camera syncs
			if (_cameraUpdateDelay < _settings.ClientVisualizerUpdateDelay)
			{
				camera.hasChanged = false;
				return;
			}

			if (_lastCameraPosition != null
				&& camera.position.Equals(_lastCameraPosition)
				&& camera.eulerAngles.Equals(_lastCameraRotation))
			{
				camera.hasChanged = false;
				return;
			}

			// TODO : combine de-/serializer
			// TODO : optimize this
			byte[] data = new byte[sizeof(float) * 6];
			Array.Copy(BitConverter.GetBytes(camera.position.x), 0, data, 0 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.position.y), 0, data, 1 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.position.z), 0, data, 2 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.x), 0, data, 3 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.y), 0, data, 4 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.z), 0, data, 5 * sizeof(float), sizeof(float));
			SendData(data, null);

			_lastCameraPosition = camera.position;
			_lastCameraRotation = camera.eulerAngles;
			camera.hasChanged = false;
			_cameraUpdateDelay = 0;
		}

		private static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
		{
			if (size == 0)
				size = array.Length - offset;

			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

		#endregion
	}
}
