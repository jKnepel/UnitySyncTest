using UnityEngine;
using UnityEditor;

namespace jKnepel.UnitySync
{
    public class SyncEditorWindow : EditorWindow
    {
        [MenuItem("Window/Unity Sync")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(SyncEditorWindow));
        }

		public void CreateGUI()
		{
        }

        private void OnDestroy()
		{
        }

		void OnGUI()
        {
            EditorGUILayout.BeginVertical();
			{
                GUILayout.Label("UnitySync", EditorStyles.largeLabel);

                SyncSettings.Instance.Port = EditorGUILayout.IntField("Port: ", SyncSettings.Instance.Port);
                SyncSettings.Instance.ExportSceneName = EditorGUILayout.TextField("Export Scene Name: ", SyncSettings.Instance.ExportSceneName);
                SyncSettings.Instance.ImportSceneName = EditorGUILayout.TextField("Import Scene Name: ", SyncSettings.Instance.ImportSceneName);
                SyncSettings.Instance.Color = EditorGUILayout.ColorField("Color: ", SyncSettings.Instance.Color);

                EditorGUILayout.BeginHorizontal();
				{
                    if (GUILayout.Button(new GUIContent("Connect Client"), GUILayout.ExpandWidth(false)))
                        SyncEvents.onConnectClient?.Invoke();

                    if (GUILayout.Button(new GUIContent("Close Client"), GUILayout.ExpandWidth(false)))
                        SyncEvents.onCloseClient?.Invoke();

                    GUILayout.Label(SyncManager.IsConnected ? "Connected" : "Disconnected");
				}
                EditorGUILayout.EndHorizontal();
			}
            EditorGUILayout.EndVertical();
        }
    }
}
