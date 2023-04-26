using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
    internal enum SyncPacketType : byte
    {
        ACK,
        Heartbeat,
        ConnectionRequest,
        ConnectionChallenge,
        ConnectionDenied,
        ChallengeAnswer,
        ConnectionClosed,
        ReliableData,
        ReliableUnorderedData,
        UnreliableData,
        UnreliableUnorderedData,
        ClientInfo
    }

    internal abstract class SyncPacket
	{
        // TODO : replace fields with properties restricting write
        public IPAddress        ClientIP;
        public uint             ClientID;
        public SyncPacketType   Type;
        public Action<bool>     Callback;
        public uint             CRC32;
        public byte[]           Bytes;
	}

    internal abstract class SyncSequencedPacket : SyncPacket
	{
        public ushort  Sequence;
	}

    internal class SyncDataPacket : SyncSequencedPacket
    {
        private readonly int MTU = SyncSettings.GetOrCreateSettings().MTU;

        public readonly ConcurrentDictionary<ushort, byte[]> ReceivedSlices;

        public readonly uint    Target;
        public readonly bool    IsChunked;
        public readonly ushort  NumberOfSlices;
        public readonly ushort  SliceNumber;
        public readonly uint    ModuleHash;
        public readonly byte[]  Data;


        public SyncDataPacket(SyncPacketType type, Action<bool> callback, uint moduleHash, byte[] data, uint receiver = 0)
		{
            Type = type;
            ClientID = receiver;
            Callback = callback;
            ModuleHash = moduleHash;
            Data = data;

            // define mtu reliant values
            IsChunked = data.Length > MTU;
            NumberOfSlices = (ushort)(data.Length % MTU == 0
                ? data.Length / MTU
                : data.Length / MTU + 1);
            SliceNumber = 0;

            // define length of packet bytes depending on if the packet has to be chunked or not
            int chunkedHeaderLength = IsChunked ? SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER : 0;
            int dataLength = IsChunked ? MTU : data.Length;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH +
                chunkedHeaderLength + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + dataLength];

            // fill in values that don't need to be adjusted per receiver
            Array.Copy(BitConverter.GetBytes(receiver), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength, SyncSettings.CLIENT_ID_LENGTH);
            Array.Copy(BitConverter.GetBytes(moduleHash), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + SyncSettings.CLIENT_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH);
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            if (IsChunked)
			{   // set bit for marking chunked packets and add number of slices
                Bytes[SyncSettings.CRC32_LENGTH] |= 1 << 7;
                Array.Copy(BitConverter.GetBytes(NumberOfSlices), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.NUMBER_OF_SLICES);
			} 
            else
			{   // add complete data
                Array.Copy(data, 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH, data.Length);
			}
        }

        public SyncDataPacket(IPAddress sender, SyncPacketType type, byte[] packet)
		{
            Type = type;
            ClientIP = sender;
            Bytes = packet;

            CRC32 = SyncPacketHelper.GetCRC32(packet);
            Sequence = SyncPacketHelper.GetSequenceBytes(packet);

            IsChunked = SyncPacketHelper.GetIsChunked(packet);
            if (IsChunked)
			{
                NumberOfSlices = SyncPacketHelper.GetNumberOfSlices(packet);
                SliceNumber = SyncPacketHelper.GetSliceNumber(packet);
			}

            ClientID = SyncPacketHelper.GetClientID(packet, IsChunked);
            ModuleHash = SyncPacketHelper.GetModuleHash(packet, IsChunked);
            Data = SyncPacketHelper.GetDataPacketData(packet, IsChunked);
        }

        public byte[] PrepareBytesForReceiver(uint receiver, ushort sequence, byte[] servernameBytes)
		{
            if (IsChunked)
			{
                Debug.LogError("This Method is exclusively meant for non-chunked packets!");
                return null;
			}

            Array.Copy(BitConverter.GetBytes(sequence), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
            Array.Copy(BitConverter.GetBytes(receiver), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.CLIENT_ID_LENGTH);
            SyncPacketHelper.CalculateAndAddChecksum(Bytes, servernameBytes);
            return Bytes;
		}

        public byte[] PrepareSliceBytesForReceiver(uint receiver, ushort sequence, int sliceNumber, byte[] servernameBytes)
		{
            if (!IsChunked)
            {
                Debug.LogError("This Method is exclusively meant for chunked packets!");
                return null;
            }

            Array.Copy(BitConverter.GetBytes(sequence), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
            Array.Copy(BitConverter.GetBytes(receiver), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER, SyncSettings.CLIENT_ID_LENGTH);

            int sliceSize = sliceNumber < NumberOfSlices - 1 ? MTU : Data.Length % MTU;
            Array.Copy(Data, sliceNumber * MTU, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH, sliceSize);

            SyncPacketHelper.CalculateAndAddChecksum(Bytes, servernameBytes);
            return Bytes;
        }
    }

    internal class SyncClientInfoPacket : SyncSequencedPacket
	{
        public string   Username;
        public Color32  Color;

        public SyncClientInfoPacket(uint clientId, string username, Color32 color)
		{
            Type = SyncPacketType.ClientInfo;
            ClientID = clientId;
            Username = username;
            Color = color;

            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH];
		}
	}
}
