using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jKnepel.UnitySync
{
	public class SyncRuntimeWindow : MonoBehaviour
	{
		[SerializeField] private TMPro.TMP_InputField _ip;
		[SerializeField] private TMPro.TMP_InputField _port;
		[SerializeField] private TMPro.TMP_InputField _exportSceneName;
		[SerializeField] private TMPro.TMP_InputField _importSceneName;
		[SerializeField] private Transform _viewPort;

		[SerializeField] private GameObject _debugGameObject;

		private void Awake()
		{
			SyncEvents.onDebugTextAdded += AddDebug;
		}

		private void OnDestroy()
		{
			SyncEvents.onDebugTextAdded -= AddDebug;
		}

		public void OpenSocket()
		{
			SyncEvents.onOpenTCPSocket?.Invoke(_exportSceneName.text, _ip.text, int.Parse(_port.text));
		}

		public void CloseSocket()
		{
			SyncEvents.onCloseTCPSocket?.Invoke();
		}

		public void ConnectClient()
		{
			SyncEvents.onConnectClient?.Invoke(_importSceneName.text, _ip.text, int.Parse(_port.text));
		}

		public void CloseClient()
		{
			SyncEvents.onCloseClient?.Invoke();
		}

		private void AddDebug(string text)
		{
			GameObject go = Instantiate(_debugGameObject, _viewPort);
			TMPro.TMP_Text textGo = go.GetComponent<TMPro.TMP_Text>();
			textGo.text = text;
		}
	}
}
