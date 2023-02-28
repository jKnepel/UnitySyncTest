using System;

namespace jKnepel.UnitySync
{
    public struct SyncEvents
    {
        public static Action<string, SyncMessageSeverity> OnSyncMessageAdded;
        public static Action<bool> OnShowSyncObjectInHierarchyChanged;
    }
}
