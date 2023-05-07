using System;
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
        public byte             Client;
        public uint             CRC32;
        public byte[]           Bytes;

        protected static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
        {
            if (size == 0)
                size = array.Length - offset;

            byte[] bytes = new byte[size];
            Array.Copy(array, offset, bytes, 0, size);
            return bytes;
        }

        protected virtual uint CalculateChecksumBytes(byte[] servername = null)
		{
            if (Bytes == null)
                throw new Exception("The Bytes have to be initialized before a Checksum can be calculated!");

            byte[] packetData = GetBytesFromArray(Bytes, SyncSettings.CRC32_LENGTH);
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

    internal class SyncHeartbeatPacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncHeartbeatPacket(byte[] servername, byte numberOfClients)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.Heartbeat;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH + servername.Length + SyncSettings.DATA_FLAG_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            Bytes[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH] = (byte)servername.Length;
            Array.Copy(servername, 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH, servername.Length);
            Bytes[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH + servername.Length] = numberOfClients;

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public readonly byte[]  Servername;
        public readonly byte    NumberOfClients;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncHeartbeatPacket(byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ACK;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            int servernameLength = packet[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Array.Copy(Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH, Servername, 0, servernameLength);
            NumberOfClients = Bytes[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.DATA_FLAG_LENGTH + servernameLength];
        }
	}

    internal class SyncConnectionRequestPacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionRequestPacket(byte receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionRequest;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionRequestPacket(byte sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionRequest;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
		}
	}

    internal class SyncConnectionChallengePacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionChallengePacket(byte receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionChallenge;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            System.Random rnd = new();
            ulong challenge = (ulong)(rnd.NextDouble() * ulong.MaxValue);
            Array.Copy(BitConverter.GetBytes(challenge), 0, Bytes, SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_LENGTH);

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public ulong Challenge;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionChallengePacket(byte sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionRequest;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            Challenge = BitConverter.ToUInt64(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_LENGTH));
        }
	}

    internal class SyncChallengeAnswerPacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncChallengeAnswerPacket(byte receiver, ulong challenge, string username, Color32 color)
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

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public ulong HashedChallenge;
        public string Username;
        public Color32 Color;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncChallengeAnswerPacket(byte sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ChallengeAnswer;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            HashedChallenge = BitConverter.ToUInt64(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_ANSWER_LENGTH));
            byte usernameLength = packet[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH];
            Username = Encoding.ASCII.GetString(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH, usernameLength));
            int position = SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_ANSWER_LENGTH + SyncSettings.DATA_FLAG_LENGTH + usernameLength;
            Color = new(packet[position], packet[position + 1], packet[position + 2], 255);
        }
    }

    internal class SyncConnectionAccepted : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionAccepted(byte receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionAccepted;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CLIENT_ID_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            Array.Copy(BitConverter.GetBytes(receiver), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CLIENT_ID_LENGTH);

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public uint ClientID;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionAccepted(byte sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionAccepted;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            ClientID = BitConverter.ToUInt32(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CLIENT_ID_LENGTH));
        }
    }

    // TODO : add reason?
    internal class SyncConnectionDenied : SyncPacket
	{ 
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionDenied(byte receiver)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionDenied;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionDenied(byte sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionDenied;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    // TODO : add reason?
    internal class SyncConnectionClosed : SyncPacket
    {
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionClosed(byte receiver)
        {
            IsSenderPacket = true;
            Type = SyncPacketType.ConnectionClosed;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionClosed(byte sender, byte[] packet)
        {
            IsSenderPacket = false;
            Type = SyncPacketType.ConnectionClosed;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    internal class SyncIsStillActive : SyncPacket
    {
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncIsStillActive(byte receiver)
        {
            IsSenderPacket = true;
            Type = SyncPacketType.IsStillActive;
            Client = receiver;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = CalculateChecksumBytes();
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncIsStillActive(byte sender, byte[] packet)
        {
            IsSenderPacket = false;
            Type = SyncPacketType.IsStillActive;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    //------------------ Data Packets ------------------

    internal abstract class SyncSequencedPacket : SyncPacket
	{
        public ushort Sequence;
	}

    internal class SyncACKPacket : SyncSequencedPacket
	{
        public readonly bool    IsChunked;
        public readonly ushort  SliceNumber;

        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncACKPacket(byte receiver, ushort sequence, byte[] servername, ushort? sliceNumber = null)
		{
            IsSenderPacket = true;
            Type = SyncPacketType.ACK;
            Client = receiver;
            Sequence = sequence;
            IsChunked = sliceNumber != null;

            int sliceNumberLength = IsChunked ? SyncSettings.SLICE_NUMBER : 0;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + sliceNumberLength];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            Array.Copy(BitConverter.GetBytes(Sequence), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);

            if (IsChunked)
			{
                Bytes[SyncSettings.CRC32_LENGTH] |= 1 << 7;
                Array.Copy(BitConverter.GetBytes((ushort)sliceNumber), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.SLICE_NUMBER);
            }

            CRC32 = CalculateChecksumBytes(servername);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncACKPacket(byte sender, byte[] packet)
		{
            IsSenderPacket = false;
            Type = SyncPacketType.ACK;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            Sequence = BitConverter.ToUInt16(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH));
            IsChunked = (packet[SyncSettings.CRC32_LENGTH] & (1 << 7)) != 0;

            if (IsChunked)
			{
                SliceNumber = BitConverter.ToUInt16(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.SLICE_NUMBER));
            }
        }
    }

    internal class SyncDataPacket : SyncSequencedPacket
    {
        // TODO: potential bug if mtu changes while packet is in flight or during slice generation
        private readonly int MTU = SyncSettings.GetOrCreateSettings().MTU;

        public readonly byte?           ClientID;
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
            byte[] servername, byte? receiver, byte? source = null)
		{
            if (type != SyncPacketType.ReliableData && type != SyncPacketType.ReliableUnorderedData
                && type != SyncPacketType.UnreliableData && type != SyncPacketType.UnreliableUnorderedData)
                throw new Exception("This Packet only supports Reliable and Unreliable Data Types!");

            if (type != SyncPacketType.ReliableData && type != SyncPacketType.ReliableUnorderedData && data.Length > MTU)
            {
                Debug.LogError($"Only reliable Packets can be larger than {MTU} Bytes!");
                callback?.Invoke(false);
                return;
            }

            IsSenderPacket = true;
            Type = type;
            Client = (byte)receiver;
            ClientID = source;
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
            byte[] client = source == null ? BitConverter.GetBytes((byte)receiver) : BitConverter.GetBytes((byte)source);
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

            CRC32 = CalculateChecksumBytes(servername);
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

            CRC32 = CalculateChecksumBytes(Servername);
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncDataPacket(SyncPacketType type, byte sender, byte[] packet)
		{
            if (type != SyncPacketType.ReliableData && type != SyncPacketType.ReliableUnorderedData
                && type != SyncPacketType.UnreliableData && type != SyncPacketType.UnreliableUnorderedData)
                throw new Exception("This Packet only supports Reliable and Unreliable Data Types!");

            IsSenderPacket = false;
            Type = type;
            Client = sender;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
            Sequence = BitConverter.ToUInt16(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH));

            IsChunked = (packet[SyncSettings.CRC32_LENGTH] & (1 << 7)) != 0;
            int chunkedHeaderLength = IsChunked ? SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER : 0;
            ClientID = packet[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength];
            ModuleHash = BitConverter.ToUInt32(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + SyncSettings.CLIENT_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH));

            if (IsChunked)
			{
                NumberOfSlices = BitConverter.ToUInt16(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.NUMBER_OF_SLICES));
                SliceNumber = BitConverter.ToUInt16(GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES, SyncSettings.SLICE_NUMBER));
                Data = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH);
			}
            else
			{
                Data = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH);
            }
        }
    }
}
