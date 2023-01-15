using System;

namespace jKnepel.UnitySync
{
    public static class SyncEvents
    {
        public static Action<string> onIpChanged;
        public static Action<int> onPortChanged;
        public static Action<string> onExportSceneNameChanged;
        public static Action<string> onImportSceneNameChanged;

        public static Action onOpenTCPSocket;
        public static Action onCloseTCPSocket;
        public static Action<bool> onIsOpenChanged;
        public static Action onConnectClient;
        public static Action onCloseClient;
        public static Action<bool> onIsConnectedChanged;
        
        public static Action onExportScene;
    }
}
