using System;

namespace jKnepel.UnitySync
{
    internal struct SyncPacket
    {
        public SyncPacketType Type { get; private set; }
        public SyncConnectedClient Client { get; private set; }
        public byte[] Data { get; private set; }
        public Action<bool> OnDataSend { get; private set; }

        /// <summary>
        /// Creates received packet from clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="client"></param>
        public SyncPacket(SyncPacketType type, byte[] data, SyncConnectedClient client = null)
		{
            Type = type;
            Client = client;
            Data = data;
            OnDataSend = null;
		}

        /// <summary>
        /// Creates packet that has yet to be sent to clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="onDataSend"></param>
        /// <param name="client"></param>
        public SyncPacket(SyncPacketType type, byte[] data, Action<bool> onDataSend, SyncConnectedClient client = null)
		{
            Type = type;
            Client = client;
            Data = data;
            OnDataSend = onDataSend;
        }
    }
}
