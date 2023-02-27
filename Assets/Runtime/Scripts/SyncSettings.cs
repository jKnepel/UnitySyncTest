using UnityEngine;

[CreateAssetMenu(fileName = "UnitySyncSettings", menuName = "ScriptableObjects/UnitySyncSettings", order = 1)]
public class SyncSettings : ScriptableObject
{
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
                    // TODO : this should be saved to allow for manually created settings asset, created after this instance,
                    //      to become the new settings instance
                    instance = (SyncSettings)CreateInstance(typeof(SyncSettings));
                }
			}
            return instance;
		}
	}
    
    // debug settings
    public bool IsDebug = true;
    public bool AllowReceivingOwnPackets = false;
    public bool ShowSyncObjectInHiearchy = false;

    // packet frequency settings
    public int HeartbeatDelay = 1000;
    public int ClientTimeoutDelay = 3000;
    public int RoomTimeoutDelay = 3000;
    public int GameObjectAndComponentUpdateDelay = 500;
    public int ClientCameraUpdateDelay = 50;

    // user settings
    public int Port = 8080;
    public string ImportSceneName = "SyncImportScene";
    public string Username = "Username";
    public Color32 Color = new(255, 255, 255, 255);

    // prefabs
    public GameObject ClientTransformPrefab;
}
