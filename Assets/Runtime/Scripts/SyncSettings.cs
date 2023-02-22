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
                    instance = (SyncSettings)ScriptableObject.CreateInstance(typeof(SyncSettings));
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
    public int GameObjectAndComponentUpdateDelay = 500;
    public int ClientCameraUpdateDelay = 50;
    public int HeartbeatDelay = 5000;

    // user settings
    public int Port = 8080;
    public string Username = "Username";
    public Color32 Color = new(255, 255, 255, 255);
    public string ExportSceneName = "ExportScene";
    public string ImportSceneName = "ImportScene";

    // prefabs
    public GameObject ClientTransformPrefab;
}
