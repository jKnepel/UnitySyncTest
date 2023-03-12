namespace jKnepel.UnitySync
{
    public struct SyncPacket
    {
        public SyncPacketType Type { get; private set; }
        public SyncConnectedClient Client { get; private set; }
        public byte[] Data { get; private set; }

        /// <summary>
        /// Creates packet for specific Client. Used for creating received/yet to be sent packets from/to clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="client"></param>
        /// <param name="data"></param>
        public SyncPacket(SyncPacketType type, SyncConnectedClient client, byte[] data)
		{
            Type = type;
            Client = client;
            Data = data;
		}

        /// <summary>
        /// Creates packet with no Client specified. Used for creating yet to be sent packets addressed 
        /// to all clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        public SyncPacket(SyncPacketType type, byte[] data) : this(type, null, data) { }
    }
}
