using System.Collections.Generic;

namespace jKnepel.UnitySync
{
    internal enum SyncPacketType : byte
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

    internal static class SyncPacketChannels
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
            SyncPacketType.ClientCamera,
            SyncPacketType.UnreliableData
        };
    }
}
