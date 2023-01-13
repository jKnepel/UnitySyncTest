using System;

namespace jKnepel.UnitySync
{
    public static class SyncEvents
    {
        public static Action<string> onIpChanged;
        public static Action<string> onPortChanged;
        public static Action<string> onExportSceneNameChanged;
        public static Action<string> onImportSceneNameChanged;

        public static Action onOpenTCPSocket;
        public static Action onCloseTCPSocket;
        public static Action onExportScene;
    }
}
