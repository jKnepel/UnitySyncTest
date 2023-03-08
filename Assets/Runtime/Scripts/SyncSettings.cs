using System.Linq;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using UnityEngine;

namespace jKnepel.UnitySync
{
    [System.Serializable]
    [CreateAssetMenu(fileName = "UnitySyncSettings", menuName = "ScriptableObjects/UnitySyncSettings", order = 1)]
    public class SyncSettings : ScriptableObject
    {
		public void OnEnable()
		{
            if (instance != null)
                instance = this;
		}

		private static SyncSettings instance;
        public static SyncSettings Instance
	    {
            get
		    {
                if (instance == null)
			    {
                    instance = Resources.Load<SyncSettings>("UnitySyncSettings");
                    if (instance == null)
				    {
                        instance = (SyncSettings)CreateInstance(typeof(SyncSettings));
                    }
			    }
                return instance;
		    }
	    }
    
        // debug settings
        public bool IsDebug = false;
        private bool _showSyncObjectinHierarchy = false;
        public bool ShowSyncObjectInHierarchy
	    {
            get { return _showSyncObjectinHierarchy; }
            set
		    {
                _showSyncObjectinHierarchy = value;
                SyncEvents.OnShowSyncObjectInHierarchyChanged?.Invoke();
            }
	    }

        // packet frequency settings
        public int HeartbeatDelay = 1000;
        public int ClientTimeoutDelay = 3000;
        public int EditorUpdatesDelay = 500;
        public int ClientVisualizerUpdateDelay = 50;

		// user settings
		public string IP = GetLocalIPAddress();
		public int Port = 8080;
        public string ImportSceneName = "SyncImportScene";
        public string Username = "Username";
        public Color32 Color = new(255, 255, 255, 255);

        // prefabs
        public SyncClientVisualizer ClientVisualizer;
		public Material HighlightMaterial;

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
}
