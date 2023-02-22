namespace jKnepel.UnitySync
{
    public struct SyncPacket
    {
        public SyncPacketType Type { get; private set; }
        public string Sender { get; private set; }
        public byte[] Data { get; private set; }

        public SyncPacket(SyncPacketType type, string sender, byte[] data)
		{
            Type = type;
            Sender = sender;
            Data = data;
		}

        public SyncPacket(SyncPacketType type, byte[] data)
        {
            Type = type;
            Sender = "";
            Data = data;
        }
    }
}
