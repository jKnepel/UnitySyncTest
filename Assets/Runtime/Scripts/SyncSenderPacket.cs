using System;

namespace jKnepel.UnitySync
{
    internal class SyncSenderPacket : SyncPacket
    {
        public Action<bool> OnDataSend { get; private set; }
        public bool IsChunked { get; private set; }
        public ushort NumberOfSlices { get; private set; }
        public ushort CurrentSliceNumber { get; private set; }

        /// <summary>
        /// Creates packet that has yet to be sent to clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="onDataSend"></param>
        /// <param name="client"></param>
        public SyncSenderPacket(SyncPacketType type, byte[] data, Action<bool> onDataSend, SyncConnectedClient client = null)
        {
            Type = type;
            Client = client;
            Data = data;
            OnDataSend = onDataSend;
            IsChunked = data.Length > 1200;
            NumberOfSlices = (ushort)(data.Length % MAX_SLICE_LENGTH == 0
                ? data.Length / MAX_SLICE_LENGTH
                : data.Length / MAX_SLICE_LENGTH + 1);
            CurrentSliceNumber = 0;
        }

        /// <summary>
        /// Creates packet that has yet to be sent to clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="client"></param>
        public SyncSenderPacket(SyncPacketType type, byte[] data, SyncConnectedClient client = null)
            : this(type, data, null, client) { }

        public byte[] GetNextSlice()
        {
            if (!IsChunked)
                return Data;

            if (CurrentSliceNumber >= NumberOfSlices)
                return null;

            int sliceSize = CurrentSliceNumber < NumberOfSlices - 1
                ? MAX_SLICE_LENGTH
                : Data.Length % MAX_SLICE_LENGTH;
            byte[] data = new byte[sliceSize];
            Array.Copy(Data, CurrentSliceNumber * MAX_SLICE_LENGTH, data, 0, sliceSize);

            CurrentSliceNumber++;
            return data;
        }
    }
}
