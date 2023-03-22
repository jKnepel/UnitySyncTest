using System.Collections.Generic;
using System.Collections.Concurrent;

namespace jKnepel.UnitySync
{
    internal class SyncReceiverPacket : SyncPacket
    {
        public bool IsChunked { get; private set; }
        public ushort NumberOfSlices { get; private set; }
        public ConcurrentDictionary<ushort, byte[]> Slices { get; private set; }

        /// <summary>
        /// Creates a Packet that was sent by another Client.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="client"></param>
        public SyncReceiverPacket(SyncPacketType type, byte[] data, SyncConnectedClient client)
        {
            Type = type;
            Client = client;
            Data = data;
            IsChunked = false;
            Slices = null;
        }

        /// <summary>
        /// Creates a chunked Packet that was sent by another Client.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="client"></param>
        /// <param name="numberOfSlices"></param>
        public SyncReceiverPacket(SyncPacketType type, SyncConnectedClient client, ushort numberOfSlices)
		{
            Type = type;
            Client = client;
            Data = null;
            IsChunked = true;
            NumberOfSlices = numberOfSlices;
            Slices = new();
		}

        public bool IsPacketComplete()
		{
            return !IsChunked || Slices.Count == NumberOfSlices;
		}

        public bool ConcatenateChunkedPacket()
		{
            if (!IsPacketComplete())
                return false;

            List<byte> dataBytes = new();
            for (ushort i = 1; i <= NumberOfSlices; i++)
            {
                Slices.TryGetValue(i, out byte[] sliceData);
                dataBytes.AddRange(sliceData);
            }
            Data = dataBytes.ToArray();
            IsChunked = false;
            NumberOfSlices = 0;
            return true;
        }
    }
}
