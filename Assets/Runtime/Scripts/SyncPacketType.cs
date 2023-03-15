using System.Collections.Generic;

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
        Hierarchy,
        ReliableData,
        UnreliableData
    }

    public static class SyncPacketChannels
	{
        public static readonly List<SyncPacketType> ReliableSyncPackets = new()
        {
            SyncPacketType.Transform,
            SyncPacketType.GameObject,
            SyncPacketType.Component,
            SyncPacketType.Hierarchy,
            SyncPacketType.ReliableData
        };

        public static readonly List<SyncPacketType> UnreliableSyncPackets = new()
        {
            SyncPacketType.ACK,
            SyncPacketType.Heartbeat,
            SyncPacketType.ClientCamera,
            SyncPacketType.UnreliableData
        };
    }
}
