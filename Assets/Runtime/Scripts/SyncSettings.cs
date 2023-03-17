using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.NetworkInformation;
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
		public bool KeepSyncScene = true;
        public string ImportSceneName = "SyncImportScene";
        public string Username = "Username";
        public Color32 Color = new(255, 255, 255, 255);
        
        // packet frequency settings
        public int HeartbeatDelay = 1000;
        public int ClientTimeoutDelay = 3000;
        public int EditorUpdatesDelay = 500;
        public int ClientVisualizerUpdateDelay = 50;

		public int ResendReliablePacketsDelay = 250;
		public int MaxNumberResendReliablePackets = 5;

		// debug settings
        public bool IsDebug = false;
		public string IP = GetLocalIPAddress();
		public int Port = 26822;
		private readonly string _syncLocalScenename = "UnitySync Local Objects";
		public string SyncLocalScenename { get => _syncLocalScenename; }

        // prefabs
        public SyncClientVisualizer ClientVisualizer;
		public Material HighlightMaterial;

		internal Scene SyncScene;
		internal Transform SyncParentContainer;

		private static string GetLocalIPAddress()
		{
			// taken from https://stackoverflow.com/a/24814027
			string ipAddress = "";
			foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (item.OperationalStatus == OperationalStatus.Up)
				{   // Fetch the properties of this adapter
					IPInterfaceProperties adapterProperties = item.GetIPProperties();
					// Check if the gateway adress exist, if not its most likley a virtual network or smth
					if (adapterProperties.GatewayAddresses.FirstOrDefault() != null)
					{   // Iterate over each available unicast adresses
						foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
						{   // If the IP is a local IPv4 adress
							if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
							{
								ipAddress = ip.Address.ToString();
								break;
							}
						}
					}
				}
				if (ipAddress != "") { break; }
			}
			return ipAddress;
		}
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
