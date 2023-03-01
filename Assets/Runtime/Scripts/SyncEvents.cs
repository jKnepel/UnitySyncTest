using System;

namespace jKnepel.UnitySync
{
    public struct SyncEvents
    {
        public static Action<SyncMessage> OnSyncMessageAdded;
        public static Action<bool> OnShowSyncObjectInHierarchyChanged;
    }
}
