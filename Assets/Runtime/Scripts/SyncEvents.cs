using System;

namespace jKnepel.UnitySync
{
    public struct SyncEvents
    {
        public static Action<string> OnDebugTextAdded;
        public static Action<bool> OnShowSyncObjectInHierarchyChanged;
    }
}
