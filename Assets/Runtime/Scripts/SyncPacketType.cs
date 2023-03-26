using System.Collections.Generic;

namespace jKnepel.UnitySync
{
    internal enum SyncPacketType : byte
    {
        ACK,
        Heartbeat,
        ReliableData,
        ReliableUnorderedData,
        UnreliableData,
        UnreliableUnorderedData
    }
}
