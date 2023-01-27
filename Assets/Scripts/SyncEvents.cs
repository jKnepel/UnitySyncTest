using System;

namespace jKnepel.UnitySync
{
    public static class SyncEvents
    {
        public static Action<string, int> onOpenTCPSocket;
        public static Action onCloseTCPSocket;
        public static Action<bool> onIsOpenChanged;

        public static Action<string, string, int> onConnectClient;
        public static Action onCloseClient;
        public static Action<bool> onIsConnectedChanged;
    }
}
