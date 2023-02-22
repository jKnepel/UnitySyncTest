using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;

namespace jKnepel.UnitySync
{
    public class SyncEditorWindow : EditorWindow
    {
        #region public members

        private static string _port = "8080";
        private static string _exportSceneName = "ExportScene";
        private static string _importSceneName = "ImportScene";

        private static bool _isConnected = false;

        #endregion

        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(SyncEditorWindow));
        }

		public void CreateGUI()
		{
            SyncEvents.onIsConnectedChanged += SetIsConnected;
        }

		private void OnDestroy()
		{
            SyncEvents.onIsConnectedChanged -= SetIsConnected;
        }

		void OnGUI()
        {
            GUIStyle myStyle = new(GUI.skin.textField);
            myStyle.alignment = TextAnchor.MiddleLeft;

            GUI.Label(new Rect(5, 30, 100, 20), "Port:");
            _port = GUI.TextField(new Rect(45, 30, 60, 20), _port, myStyle);
            _port = Regex.Replace(_port, @"[^0-9 ]", "");

            _exportSceneName = GUI.TextField(new Rect(5, 60, 200, 30), _exportSceneName, myStyle);
            _importSceneName = GUI.TextField(new Rect(5, 100, 200, 30), _importSceneName, myStyle);


            if (GUI.Button(new Rect(5, 140, 100, 30), new GUIContent("Connect Client", "Connect Client")))
                SyncEvents.onConnectClient?.Invoke(_exportSceneName, _importSceneName, int.Parse(_port));
            if (GUI.Button(new Rect(115, 140, 100, 30), new GUIContent("Close Client", "Close Client")))
                SyncEvents.onCloseClient?.Invoke();
            GUI.Label(new Rect(225, 147.5f, 100, 30), _isConnected ? "Connected" : "Disconnected");
        }

		#region setters

        private void SetIsConnected(bool value) { _isConnected = value; }

        #endregion
    }
}
