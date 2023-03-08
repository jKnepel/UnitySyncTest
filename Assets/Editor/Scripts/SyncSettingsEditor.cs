using UnityEngine;
using UnityEditor;

namespace jKnepel.UnitySync
{
	[CustomEditor(typeof(SyncSettings))]
	public class SyncSettingsEditor : Editor
	{
		private bool _packetSettingsIsVisible = false;
		private bool _additionalSettingsIsVisible = false;
		private bool _debugSettingsIsVisible = false;

		// TODO : add descriptions to labels, was too lazy
		public override void OnInspectorGUI()
		{
			// user settings
			SyncSettings.Instance.IP = EditorGUILayout.TextField("IP:", SyncSettings.Instance.IP);
			SyncSettings.Instance.Port = EditorGUILayout.IntField("Port:", SyncSettings.Instance.Port);
			SyncSettings.Instance.ImportSceneName = EditorGUILayout.TextField("ImportScene Name:",
				SyncSettings.Instance.ImportSceneName);
			SyncSettings.Instance.Username = EditorGUILayout.TextField("Username:", SyncSettings.Instance.Username);
			SyncSettings.Instance.Color = EditorGUILayout.ColorField("Color:", SyncSettings.Instance.Color);

			// packet frequency settings
			_packetSettingsIsVisible = EditorGUILayout.Foldout(_packetSettingsIsVisible, "Packet Settings", EditorStyles.foldoutHeader);
			if (_packetSettingsIsVisible)
			{
				EditorGUI.indentLevel++;
				SyncSettings.Instance.HeartbeatDelay = EditorGUILayout.IntField("Heartbeat Delay:", SyncSettings.Instance.HeartbeatDelay);
				SyncSettings.Instance.ClientTimeoutDelay = EditorGUILayout.IntField("Client Timeout Delay:", SyncSettings.Instance.ClientTimeoutDelay);
				SyncSettings.Instance.EditorUpdatesDelay = EditorGUILayout.IntField("Editor Update Delay:", SyncSettings.Instance.EditorUpdatesDelay);
				SyncSettings.Instance.ClientVisualizerUpdateDelay = EditorGUILayout.IntField("Client Visualizer Update Delay:", SyncSettings.Instance.ClientVisualizerUpdateDelay);
				EditorGUI.indentLevel--;
			}

			_additionalSettingsIsVisible = EditorGUILayout.Foldout(_additionalSettingsIsVisible, "Additional Settings", EditorStyles.foldoutHeader);
			if (_additionalSettingsIsVisible)
			{
				SyncSettings.Instance.ClientVisualizer = (SyncClientVisualizer)EditorGUILayout.ObjectField("Client Visualizer Prefab:", SyncSettings.Instance.ClientVisualizer, typeof(SyncClientVisualizer), false);
				SyncSettings.Instance.HighlightMaterial = (Material)EditorGUILayout.ObjectField("Highlight Material:", SyncSettings.Instance.HighlightMaterial, typeof(Material), false);
			}

				// debug settings
				_debugSettingsIsVisible = EditorGUILayout.Foldout(_debugSettingsIsVisible, "Debug Settings", EditorStyles.foldoutHeader);
			if (_debugSettingsIsVisible)
			{
				EditorGUI.indentLevel++;
				SyncSettings.Instance.IsDebug = EditorGUILayout.Toggle(new GUIContent("Is Debug:", "Allows the display of debug messages."),
					SyncSettings.Instance.IsDebug);
				EditorGUI.indentLevel--;
			}

		}
	}
}
