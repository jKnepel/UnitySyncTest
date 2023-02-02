using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;

namespace jKnepel.UnitySync
{
    public class SyncEditorWindow : EditorWindow
    {
        #region public members

        private static string _ip = "127.0.0.1";
        private static string _port = "8080";
        private static string _exportSceneName = "ExportScene";
        private static string _importSceneName = "ImportScene";

        private static bool _isOpen = false;
        private static bool _isConnected = false;

        #endregion

        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(SyncEditorWindow));
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
            GUIStyle myStyle = new(GUI.skin.textField);
            myStyle.alignment = TextAnchor.MiddleLeft;

            GUI.Label(new Rect(5, 30, 100, 20), "IP:");
            _ip = GUI.TextField(new Rect(30, 30, 100, 20), _ip, myStyle);
            GUI.Label(new Rect(140, 30, 100, 20), "Port:");
            _port = GUI.TextField(new Rect(180, 30, 60, 20), _port, myStyle);
            _port = Regex.Replace(_port, @"[^0-9 ]", "");

            if (GUI.Button(new Rect(5, 60, 100, 30), new GUIContent("Open Socket", "Open Socket")))
                SyncEvents.onOpenTCPSocket?.Invoke(_exportSceneName, _ip, int.Parse(_port));
            if (GUI.Button(new Rect(115, 60, 100, 30), new GUIContent("Close Socket", "Close Socket")))
                SyncEvents.onCloseTCPSocket?.Invoke();
            GUI.Label(new Rect(225, 60, 100, 30), _isOpen ? "Socket Open" : "Socket Closed");
            _exportSceneName = GUI.TextField(new Rect(5, 100, 200, 30), _exportSceneName, myStyle);


            if (GUI.Button(new Rect(5, 150, 100, 30), new GUIContent("Connect Client", "Connect Client")))
                SyncEvents.onConnectClient?.Invoke(_importSceneName, _ip, int.Parse(_port));
            if (GUI.Button(new Rect(115, 150, 100, 30), new GUIContent("Close Client", "Close Client")))
                SyncEvents.onCloseClient?.Invoke();
            GUI.Label(new Rect(225, 150, 100, 30), _isConnected ? "Connected" : "Disconnected");
            _importSceneName = GUI.TextField(new Rect(5, 190, 200, 30), _importSceneName, myStyle);
        }

		#region setters

        private void SetIsOpen(bool value) { _isOpen = value; }
        private void SetIsConnected(bool value) { _isConnected = value; }

        #endregion
    }
}
