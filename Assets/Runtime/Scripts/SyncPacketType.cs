namespace jKnepel.UnitySync
{
    public enum SyncPacketType : byte
    {
        ACK,
        Heartbeat,
        ClientCamera,
        Transform,
        GameObject,
        Component,
        Hierarchy
    }
}
