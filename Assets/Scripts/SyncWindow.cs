using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;   

namespace jKnepel.UnitySync
{
    public class SyncWindow : EditorWindow
    {
        #region public members

        private static string _ip = "127.0.0.1";
        public static string Ip
        {
            get { return _ip; }
            set
			{
                _ip = value;
                SyncEvents.onIpChanged?.Invoke(value);
			}
        }

        private static string _port = "8080";
        public static string Port
		{
            get { return _port; }
            set
			{
                _port = value;
                SyncEvents.onPortChanged?.Invoke(int.Parse(value));
			}
		}

        private static string _exportSceneName = "ExportScene";
        public static string ExportSceneName
		{
            get { return _exportSceneName; }
            set
			{
                _exportSceneName = value;
                SyncEvents.onExportSceneNameChanged?.Invoke(value);
			}
		}

        private static string _importSceneName = "ImportScene";
        public static string ImportSceneName
		{
            get { return _importSceneName; }
            set
			{
                _importSceneName = value;
                SyncEvents.onImportSceneNameChanged?.Invoke(value);
			}
		}

        private static bool _isOpen = false;
        private static bool _isConnected = false;

        #endregion

        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(SyncWindow));
        }

		public void CreateGUI()
		{
            SyncEvents.onIsOpenChanged += SetIsOpen;
            SyncEvents.onIsConnectedChanged += SetIsConnected;
        }

		private void OnDestroy()
		{
            SyncEvents.onIsOpenChanged -= SetIsOpen;
            SyncEvents.onIsConnectedChanged -= SetIsConnected;
        }

		void OnGUI()
        {
            GUILayout.Label("Unity Sync", EditorStyles.boldLabel);
            GUIStyle myStyle = new(GUI.skin.textField);
            myStyle.alignment = TextAnchor.MiddleLeft;

            GUI.Label(new Rect(5, 30, 100, 20), "IP:");
            Ip = GUI.TextField(new Rect(30, 30, 100, 20), Ip, myStyle);
            GUI.Label(new Rect(140, 30, 100, 20), "Port:");
            Port = GUI.TextField(new Rect(180, 30, 60, 20), Port, myStyle);
            Port = Regex.Replace(Port, @"[^a-zA-Z0-9 ]", "");

            if (GUI.Button(new Rect(5, 60, 100, 30), new GUIContent("Open Socket", "Open Socket")))
            {
                SyncEvents.onOpenTCPSocket?.Invoke();
            }

            if (GUI.Button(new Rect(115, 60, 100, 30), new GUIContent("Close Socket", "Close Socket")))
            {
                SyncEvents.onCloseTCPSocket?.Invoke();
            }
            GUI.Label(new Rect(225, 60, 100, 30), _isOpen ? "Socket Open" : "Socket Closed");

            if (GUI.Button(new Rect(5, 100, 100, 30), new GUIContent("Connect Client", "Connect Client")))
            {
                SyncEvents.onConnectClient?.Invoke();
            }

            if (GUI.Button(new Rect(115, 100, 100, 30), new GUIContent("Close Client", "Close Client")))
            {
                SyncEvents.onCloseClient?.Invoke();
            }
            GUI.Label(new Rect(225, 100, 100, 30), _isConnected ? "Connected" : "Disconnected");

            ExportSceneName = GUI.TextField(new Rect(5, 140, 200, 30), ExportSceneName, myStyle);
            ImportSceneName = GUI.TextField(new Rect(5, 180, 200, 30), ImportSceneName, myStyle);

            if (GUI.Button(new Rect(5, 220, 100, 30), new GUIContent("Export Scene", "Export Scene")))
            {
                SyncEvents.onExportScene?.Invoke();
            }
        }

		#region setters

        private void SetIsOpen(bool value) { _isOpen = value; }
        private void SetIsConnected(bool value) { _isConnected = value; }

		#endregion
	}
}
