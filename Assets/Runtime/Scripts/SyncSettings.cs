using UnityEngine;
using UnityEditor;

// TODO : rework into normal scriptable object to allow for build
public class SyncSettings : ScriptableSingleton<SyncSettings>
{
    public bool IsDebug = true;
    public bool AllowReceivingOwnPackets = true;

    public int GameObjectAndComponentUpdateDelay = 500;
    public int EditorCameraUpdateDelay = 500;

    public GameObject ClientTransformPrefab;
}
