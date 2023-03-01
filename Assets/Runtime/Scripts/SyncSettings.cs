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
                SyncEvents.OnShowSyncObjectInHierarchyChanged?.Invoke(value);
            }
	    }

        // packet frequency settings
        public int HeartbeatDelay = 1000;
        public int ClientTimeoutDelay = 3000;
        public int EditorUpdatesDelay = 500;
        public int ClientVisualizerUpdateDelay = 50;

        // user settings
        public int Port = 8080;
        public string ImportSceneName = "SyncImportScene";
        public string Username = "Username";
        public Color32 Color = new(255, 255, 255, 255);

        // prefabs
        public GameObject ClientTransformPrefab;
    }
}
