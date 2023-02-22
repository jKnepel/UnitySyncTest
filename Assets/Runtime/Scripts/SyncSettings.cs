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
    
    public bool IsDebug = true;
    public bool AllowReceivingOwnPackets = false;
    public bool ShowSyncObjectInHiearchy = false;

    public int GameObjectAndComponentUpdateDelay = 500;
    public int ClientCameraUpdateDelay = 50;

    public GameObject ClientTransformPrefab;
}
