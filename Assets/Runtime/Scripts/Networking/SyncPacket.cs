using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
    internal enum SyncPacketType : byte
    {
        ACK,
        Heartbeat,
        ConnectionRequest,
        ConnectionChallenge,
        ChallengeAnswer,
        ConnectionAccepted,
        ConnectionDenied,
        ConnectionClosed,
        IsStillActive,
        ReliableData,
        ReliableUnorderedData,
        UnreliableData,
        UnreliableUnorderedData,
        ClientInfo
    }

    internal abstract class SyncPacket
	{
        // TODO : replace fields with properties restricting write
        public bool             IsSenderPacket;
        public SyncPacketType   Type;
        public uint             Client;
        public uint             CRC32;
        public byte[]           Bytes;

        protected virtual uint CalculateChecksumBytes(byte[] servername = null)
		{
            if (Bytes == null)
                throw new Exception("The Bytes have to be initialized before a Checksum can be calculated!");

            byte[] packetData = SyncPacketHelper.GetBytesFromArray(Bytes, SyncSettings.CRC32_LENGTH);
            int servernameLength = servername == null ? 0 : servername.Length;

            // calculate CRC32 checksum from protocol id, (optional) servername and packet data
            byte[] bytes = new byte[SyncSettings.PROTOCOL_ID_LENGTH + servernameLength + packetData.Length];
            Array.Copy(BitConverter.GetBytes(SyncSettings.PROTOCOL_ID), 0, bytes, 0, SyncSettings.PROTOCOL_ID_LENGTH);
            if (servername != null)
                Array.Copy(servername, 0, bytes, SyncSettings.PROTOCOL_ID_LENGTH, servername.Length);
            Array.Copy(packetData, 0, bytes, SyncSettings.PROTOCOL_ID_LENGTH + servernameLength, packetData.Length);

            return SyncCRC32.CRC32Bytes(bytes);
        }

        public virtual bool CheckCRC32Checksum(byte[] servername = null)
		{
            return CRC32 == CalculateChecksumBytes(servername);
        }
	}

    //------------------ Connection Packets ------------------

    internal class SyncConnectionRequestPacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionRequestPacket(uint receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionRequest;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionRequestPacket(uint sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionRequest;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
		}
	}

    internal class SyncConnectionChallengePacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionChallengePacket(uint receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionChallenge;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            ulong challenge = SyncPacketHelper.GenerateChallengeNonce();
            Array.Copy(BitConverter.GetBytes(challenge), 0, Bytes, SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_LENGTH);

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public ulong Challenge;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionChallengePacket(uint sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionRequest;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            Challenge = BitConverter.ToUInt64(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_LENGTH));
        }
	}

    internal class SyncChallengeAnswerPacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncChallengeAnswerPacket(uint receiver, ulong challenge, string username, Color32 color)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ChallengeAnswer;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH + username.Length + 3];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            using (SHA256 h = SHA256.Create())
            {
                byte[] hashedChallenge = h.ComputeHash(BitConverter.GetBytes(challenge));
                Array.Copy(hashedChallenge, 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_ANSWER_LENGTH);
            }
            Bytes[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH] = (byte)username.Length;
            Array.Copy(Encoding.ASCII.GetBytes(username), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH, username.Length);
            int position = SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH + username.Length;
            Bytes[position + 0] = color.r;
            Bytes[position + 1] = color.g;
            Bytes[position + 2] = color.b;

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public ulong HashedChallenge;
        public string Username;
        public Color32 Color;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncChallengeAnswerPacket(uint sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ChallengeAnswer;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            HashedChallenge = BitConverter.ToUInt64(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_ANSWER_LENGTH));
            byte usernameLength = packet[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH];
            Username = Encoding.ASCII.GetString(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH, usernameLength));
            int position = SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH + usernameLength;
            Color = new(packet[position], packet[position + 1], packet[position + 2], 255);
        }
    }

    internal class SyncConnectionAccepted : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionAccepted(uint receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionAccepted;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CLIENT_ID_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            Array.Copy(BitConverter.GetBytes(receiver), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CLIENT_ID_LENGTH);

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public uint ClientID;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionAccepted(uint sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionAccepted;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            ClientID = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CLIENT_ID_LENGTH));
        }
    }

    // TODO : add reason?
    internal class SyncConnectionDenied : SyncPacket
	{ 
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionDenied(uint receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionDenied;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionDenied(uint sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionDenied;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    // TODO : add reason?
    internal class SyncConnectionClosed : SyncPacket
    {
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionClosed(uint receiver)
        {
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionClosed;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionClosed(uint sender, byte[] packet)
        {
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionClosed;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    internal class SyncIsStillActive : SyncPacket
    {
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncIsStillActive(uint receiver)
        {
            IsSenderPacket = true;
            Type = SyncPacketType.IsStillActive;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncIsStillActive(uint sender, byte[] packet)
        {
            IsSenderPacket = false;
            Type = SyncPacketType.IsStillActive;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    //------------------ Data Packets ------------------

    internal abstract class SyncSequencedPacket : SyncPacket
	{
        public ushort Sequence;
	}

    internal class SyncDataPacket : SyncSequencedPacket
    {
        // TODO: potential bug if mtu changes while packet is in flight or during slice generation
        private readonly int MTU = SyncSettings.GetOrCreateSettings().MTU;

        public readonly ConcurrentDictionary<ushort, byte[]> ReceivedSlices;

        public readonly uint            Source;
        public readonly Action<bool>    Callback;
        public readonly uint            ModuleHash;
        public readonly byte[]          Servername;

        public readonly bool    IsChunked;
        public readonly ushort  NumberOfSlices;

        public byte[]   Data;
        public ushort   SliceNumber;

        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncDataPacket(SyncPacketType type, Action<bool> callback, uint moduleHash, byte[] data, 
            byte[] servername, uint receiver, bool isServer, uint source = 0)
		{
            if (type != SyncPacketType.ReliableData && type != SyncPacketType.ReliableUnorderedData
                && type != SyncPacketType.UnreliableData && type != SyncPacketType.UnreliableUnorderedData)
                throw new Exception("This Packet only supports Reliable and Unreliable Data Types!");

            IsSenderPacket = true;
            Type = type;
            Client = receiver;
            Source = source;
            Callback = callback;
            ModuleHash = moduleHash;
            Data = data;
            Servername = servername;

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

            // fill in constant values
            byte[] client = isServer ? BitConverter.GetBytes(source) : BitConverter.GetBytes(receiver);
            Array.Copy(client, 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength, SyncSettings.CLIENT_ID_LENGTH);
            Array.Copy(BitConverter.GetBytes(moduleHash), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + SyncSettings.CLIENT_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH);
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            if (IsChunked)
			{   // set bit for marking chunked packets and first slice values
                Bytes[SyncSettings.CRC32_LENGTH] |= 1 << 7;
                Array.Copy(BitConverter.GetBytes(NumberOfSlices), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.NUMBER_OF_SLICES);
                Array.Copy(BitConverter.GetBytes(SliceNumber), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES, SyncSettings.SLICE_NUMBER);
                Array.Copy(Data, MTU, Bytes, 0, MTU);
            } 
            else
			{   // add complete data
                Array.Copy(data, 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH, data.Length);
            }

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public void AdvanceSliceNumber()
		{
            if (!IsChunked || !IsSenderPacket)
                throw new Exception("Slice Numbers can only be advanced if the Packet is chunked!");

            SliceNumber++;
            int sliceLength = SliceNumber < NumberOfSlices - 1 ? MTU : Data.Length % MTU;

            // copy old bytes to new array overwriting data length with new slice length
            byte[] newBytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH +
                SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + sliceLength];
            Array.Copy(Bytes, 0, newBytes, 0, newBytes.Length - sliceLength);
            Bytes = newBytes;

            // insert slice number and data
            Array.Copy(BitConverter.GetBytes(SliceNumber), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES, SyncSettings.SLICE_NUMBER);
            Array.Copy(Data, SliceNumber * MTU, Bytes, 0, sliceLength);

            CRC32 = CalculateChecksumBytes(Bytes);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncDataPacket(SyncPacketType type, bool isChunked, uint sender, byte[] packet, byte[] servername)
		{
            if (type != SyncPacketType.ReliableData && type != SyncPacketType.ReliableUnorderedData
                && type != SyncPacketType.UnreliableData && type != SyncPacketType.UnreliableUnorderedData)
                throw new Exception("This Packet only supports Reliable and Unreliable Data Types!");

            IsSenderPacket = false;
            Type = type;
            Client = sender;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            Sequence = BitConverter.ToUInt16(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH));
            ModuleHash = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH));

            IsChunked = isChunked;
            if (isChunked)
			{
                NumberOfSlices = BitConverter.ToUInt16(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.NUMBER_OF_SLICES));
                SliceNumber = BitConverter.ToUInt16(SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES, SyncSettings.SLICE_NUMBER));
                ReceivedSlices = new();
                byte[] data = SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH);
                ReceivedSlices.AddOrUpdate(SliceNumber, data, (key, oldValue) => oldValue = data);
			}
            else
			{
                Bytes = packet;
                Data = SyncPacketHelper.GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH);
            }
        }

        public bool ConcatenateSlices()
		{
            if (ReceivedSlices == null || ReceivedSlices.Count < NumberOfSlices)
                return false;

            List<byte> dataBytes = new();
            for (ushort i = 1; i <= NumberOfSlices; i++)
            {
                if (!ReceivedSlices.TryGetValue(i, out byte[] sliceData))
                    return false;
                dataBytes.AddRange(sliceData);
            }
            Data = dataBytes.ToArray();
            
            return true;
        }
    }
}
