using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;

namespace jKnepel.UnitySync
{
    public class SyncWindow : EditorWindow
    {
        #region public members

        public static string ip = "127.0.0.1";
        public static string port = "8080";
        public static string exportSceneName = "ExportScene";
        public static string importSceneName = "ImportScene";

        #endregion

        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(SyncWindow));
        }

        void OnGUI()
        {
            GUILayout.Label("Unity Sync", EditorStyles.boldLabel);
            GUIStyle myStyle = new GUIStyle(GUI.skin.textField);
            myStyle.alignment = TextAnchor.MiddleLeft;

            GUI.Label(new Rect(5, 30, 100, 20), "IP:");
            ip = GUI.TextField(new Rect(30, 30, 100, 20), ip, myStyle);
            GUI.Label(new Rect(140, 30, 100, 20), "Port:");
            port = GUI.TextField(new Rect(180, 30, 60, 20), port, myStyle);
            port = Regex.Replace(port, @"[^a-zA-Z0-9 ]", "");

            if (GUI.Button(new Rect(5, 60, 100, 30), new GUIContent("Open Socket", "Open Socket")))
            {
                SyncEvents.onOpenTCPSocket?.Invoke();
            }

            if (GUI.Button(new Rect(115, 60, 100, 30), new GUIContent("Close Socket", "Close Socket")))
            {
                SyncEvents.onCloseTCPSocket?.Invoke();
            }

            exportSceneName = GUI.TextField(new Rect(5, 100, 200, 30), exportSceneName, myStyle);
            if (GUI.Button(new Rect(215, 100, 100, 30), new GUIContent("Export Scene", "Export Scene")))
            {
                SyncEvents.onExportScene?.Invoke();
            }

            importSceneName = GUI.TextField(new Rect(5, 140, 200, 30), importSceneName, myStyle);
        }


    }
}
