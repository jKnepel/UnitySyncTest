using System;

namespace jKnepel.UnitySync
{
    internal abstract class SyncPacket
    {
        protected const int MAX_SLICE_LENGTH = 1200;

        public SyncPacketType Type { get; protected set; }
        public SyncConnectedClient Client { get; protected set; }
        public byte[] Data { get; protected set; }
    }
}
