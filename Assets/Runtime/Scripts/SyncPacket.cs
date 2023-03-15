namespace jKnepel.UnitySync
{
    public struct SyncPacket
    {
        public SyncPacketType Type { get; private set; }
        public SyncConnectedClient Client { get; private set; }
        public byte[] Data { get; private set; }

        /// <summary>
        /// Creates received/yet to be sent packets from/to clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="client"></param>
        public SyncPacket(SyncPacketType type, byte[] data, SyncConnectedClient client = null)
		{
            Type = type;
            Client = client;
            Data = data;
		}
    }
}
