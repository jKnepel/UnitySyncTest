using System;

namespace jKnepel.UnitySync
{
    public static class SyncEvents
    {
        public static Action onConnectClient;
        public static Action onCloseClient;

        public static Action<string> onDebugTextAdded;
    }
}
