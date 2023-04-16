using jKnepel.UnitySync.SceneSync;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
#endif

namespace jKnepel.UnitySync
{
    [System.Serializable]
    public class SyncSettings : ScriptableObject
    {
		private const string _fileName = "UnitySyncSettings.asset";
		private const string _settingsPath = "Assets/Resources/" + _fileName;

		internal static SyncSettings cachedSettings;
		public static SyncSettings GetOrCreateSettings()
		{
			if (cachedSettings)
				return cachedSettings;

			SyncSettings settings = Resources.Load<SyncSettings>(Path.GetFileNameWithoutExtension(_fileName));

#if UNITY_EDITOR
			if (!settings)
			{
				settings = AssetDatabase.LoadAssetAtPath<SyncSettings>(_settingsPath);
			}
			if (!settings)
			{
				string[] allSettings = AssetDatabase.FindAssets("t:SyncSettings");
				if (allSettings.Length > 0)
				{
					settings = AssetDatabase.LoadAssetAtPath<SyncSettings>(AssetDatabase.GUIDToAssetPath(allSettings[0]));
				}
			}
			if (!settings)
			{
				settings = CreateInstance<SyncSettings>();
				string dir = Path.GetDirectoryName(_settingsPath);
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);
				AssetDatabase.CreateAsset(settings, _settingsPath);
				AssetDatabase.SaveAssets();
			}
#else
			if (!settings)
			{
				settings = ScriptableObject.CreateInstance<SyncSettings>();
			}
#endif
			cachedSettings = settings;
			return cachedSettings;
		}

		// user settings
        public string Username = "Username";
        public Color32 Color = new(255, 255, 255, 255);
        
        // packet frequency settings
        public int HeartbeatDelay = 1000;
        public int ClientTimeoutDelay = 3000;
		public int ResendReliablePacketsDelay = 250;
		public int MaxNumberResendReliablePackets = 5;

		// debug settings
		public int Port = 26822;
		public int DiscoveryPort = 26823;


		// packet constants
		internal const uint PROTOCOL_ID = 876237842;
		internal const int PROTOCOL_ID_LENGTH = 4;          // length of the protocol id which is calculated from the package version
		internal const int CRC32_LENGTH = 4;                // checksum and contains protocol and server id
		internal const int PACKET_TYPE_LENGTH = 1;          // id of the packet type
		internal const int SEQUENCE_ID_LENGTH = 2;          // sequence id for preventing old updates to be consumed
		internal const int MODULE_HASH_LENGTH = 4;          // flag containing the hash of the used module
		internal const int NUMBER_OF_SLICES = 2;            // flag showing the number of packets the chunk consists of
		internal const int SLICE_NUMBER = NUMBER_OF_SLICES; // number of the current slice
		internal const int DATA_FLAG_LENGTH = 1;            // byte flag containing the length of the following byte data
		internal const int CHALLENGE_LENGTH = 4;			// length of the challenge values send to the connecting client

		// scene sync variables
		// TODO : move these to seperate settings asset?
		public int ClientVisualiserDelay = 50;
        public int GLTFHierarchyDelay = 500;
        public SyncClientVisualiser ClientVisualiser;
		public Material HighlightMaterial;
	}

#if UNITY_EDITOR && UNITY_IMGUI
	internal class SyncSettingsProvider : SettingsProvider
	{
		private SyncSettings _settings;
		private Editor _settingsEditor;

		public override void OnGUI(string searchContext)
		{
			if (!_settings)
			{
				_settings = SyncSettings.GetOrCreateSettings();
				_settingsEditor = Editor.CreateEditor(_settings);
			}
			_settingsEditor.OnInspectorGUI();
		}

		[SettingsProvider]
		public static SettingsProvider CreateSyncSettingsProvider()
		{
			SyncSettings.GetOrCreateSettings();
			return new SyncSettingsProvider("Project/UnitySync", SettingsScope.Project);
		}

		public SyncSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) 
			: base(path, scopes, keywords) { }
	}
#endif
}
