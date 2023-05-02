using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
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
        public SyncPacketType   Type;
        public uint             Client;
        public uint             CRC32;
        public byte[]           Bytes;

        public virtual bool CheckChecksumBytes()
		{
            uint packetChecksum = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, SyncSettings.CRC32_LENGTH));
            return packetChecksum == CRC32;
        }
	}

    //------------------ Connection Packets ------------------

    internal class SyncConnectionRequestPacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionRequestPacket(uint client)
		{
            Type = SyncPacketType.ConnectionRequest;
            Client = client;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            
            CRC32 = SyncCRC32.CRC32Bytes(new byte[] { (byte)Type });
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionRequestPacket(uint client, byte[] packet)
		{
            Type = SyncPacketType.ConnectionRequest;
            Client = client;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
		}
	}

    internal class SyncConnectionChallengePacket : SyncPacket
	{
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncConnectionChallengePacket(uint client)
		{
            Type = SyncPacketType.ConnectionChallenge;
            Client = client;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CHALLENGE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            ulong challenge = SyncPacketHelper.GenerateChallengeNonce();
            Array.Copy(BitConverter.GetBytes(challenge), 0, Bytes, SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CHALLENGE_LENGTH);

            CRC32 = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, 0, SyncSettings.CRC32_LENGTH));
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public ulong Challenge;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionChallengePacket(uint client, byte[] packet)
		{
            Type = SyncPacketType.ConnectionRequest;
            Client = client;
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
        public SyncChallengeAnswerPacket(uint client, ulong challenge, string username, Color32 color)
		{
            Type = SyncPacketType.ChallengeAnswer;
            Client = client;
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

            CRC32 = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, 0, SyncSettings.CRC32_LENGTH));
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public ulong HashedChallenge;
        public string Username;
        public Color32 Color;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncChallengeAnswerPacket(uint client, byte[] packet)
		{
            Type = SyncPacketType.ChallengeAnswer;
            Client = client;
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
        public SyncConnectionAccepted(uint client)
		{
            Type = SyncPacketType.ConnectionAccepted;
            Client = client;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.CLIENT_ID_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;
            Array.Copy(BitConverter.GetBytes(client), 0, Bytes, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.CLIENT_ID_LENGTH);

            CRC32 = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, 0, SyncSettings.CRC32_LENGTH));
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        public uint ClientID;

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionAccepted(uint client, byte[] packet)
		{
            Type = SyncPacketType.ConnectionAccepted;
            Client = client;
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
        public SyncConnectionDenied(uint client)
		{
            Type = SyncPacketType.ConnectionDenied;
            Client = client;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, 0, SyncSettings.CRC32_LENGTH));
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionDenied(uint client, byte[] packet)
		{
            Type = SyncPacketType.ConnectionDenied;
            Client = client;
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
        public SyncConnectionClosed(uint client)
        {
            Type = SyncPacketType.ConnectionClosed;
            Client = client;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, 0, SyncSettings.CRC32_LENGTH));
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncConnectionClosed(uint client, byte[] packet)
        {
            Type = SyncPacketType.ConnectionClosed;
            Client = client;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    internal class SyncIsStillActive : SyncPacket
    {
        /// <summary>
        /// Sender constructor.
        /// </summary>
        public SyncIsStillActive(uint client)
        {
            Type = SyncPacketType.IsStillActive;
            Client = client;
            Bytes = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH];
            Bytes[SyncSettings.CRC32_LENGTH] = (byte)Type;

            CRC32 = SyncCRC32.CRC32Bytes(SyncPacketHelper.GetBytesFromArray(Bytes, 0, SyncSettings.CRC32_LENGTH));
            Array.Copy(BitConverter.GetBytes(CRC32), 0, Bytes, 0, SyncSettings.CRC32_LENGTH);
        }

        /// <summary>
        /// Receiver constructor.
        /// </summary>
        public SyncIsStillActive(uint client, byte[] packet)
        {
            Type = SyncPacketType.IsStillActive;
            Client = client;
            Bytes = packet;
            CRC32 = BitConverter.ToUInt32(SyncPacketHelper.GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
        }
    }

    //------------------ Data Packets ------------------

    internal abstract class SyncSequencedPacket : SyncPacket
	{
        public ushort  Sequence;
	}

    internal class SyncDataPacket : SyncSequencedPacket
    {
        private readonly int MTU = SyncSettings.GetOrCreateSettings().MTU;

        public readonly ConcurrentDictionary<ushort, byte[]> ReceivedSlices;

        public readonly uint    Source;
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
