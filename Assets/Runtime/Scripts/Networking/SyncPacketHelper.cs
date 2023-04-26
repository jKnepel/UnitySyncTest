using System;
using System.Text;
using UnityEngine;

namespace jKnepel.UnitySync.Networking
{
    internal static class SyncPacketHelper
    {
		#region public methods

		#region retrieve packet values

		public static uint GetCRC32(byte[] packet)
		{
			return BitConverter.ToUInt32(GetBytesFromArray(packet, 0, SyncSettings.CRC32_LENGTH));
		}

		public static byte[] GetRemainder(byte[] packet)
		{
			return GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH);
		}
		
		public static bool GetIsChunked(byte[] packet)
		{
			return (packet[SyncSettings.CRC32_LENGTH] & (1 << 7)) != 0;
		}

		public static SyncPacketType GetPacketType(byte[] packet)
		{
			byte typeBytes = packet[SyncSettings.CRC32_LENGTH];
			byte mask = 1 << 7;
			typeBytes &= (byte)~mask;
			return (SyncPacketType)typeBytes;
		}

		public static ushort GetSequenceBytes(byte[] packet)
		{
			byte[] sequenceBytes = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH, SyncSettings.SEQUENCE_ID_LENGTH);
			return BitConverter.ToUInt16(sequenceBytes);
		}

		public static ushort GetNumberOfSlices(byte[] packet)
		{
			byte[] numberOfSlicesBytes = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH, SyncSettings.NUMBER_OF_SLICES);
			return BitConverter.ToUInt16(numberOfSlicesBytes);
		}

		public static ushort GetSliceNumber(byte[] packet)
		{
			byte[] sliceNumberBytes = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + SyncSettings.NUMBER_OF_SLICES, SyncSettings.SLICE_NUMBER);
			return BitConverter.ToUInt16(sliceNumberBytes);
		}

		public static ushort GetACKSliceNumber(byte[] ackPacket)
		{
			byte[] sliceNumberBytes = GetBytesFromArray(ackPacket, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH);
			return BitConverter.ToUInt16(sliceNumberBytes);
		}

		public static uint GetClientID(byte[] packet, bool isChunked)
		{
			byte[] clientIdBytes = isChunked
				? GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER, SyncSettings.CLIENT_ID_LENGTH)
				: GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.CLIENT_ID_LENGTH);
			return BitConverter.ToUInt32(clientIdBytes);
		}

		public static uint GetModuleHash(byte[] packet, bool isChunked)
		{
			byte[] moduleHashBytes = isChunked
				? GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH)
				: GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.CLIENT_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH);
			return BitConverter.ToUInt32(moduleHashBytes);
		}

		public static byte[] GetDataPacketData(byte[] packet, bool isChunked)
		{
			return isChunked
					? GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH)
					: GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.CLIENT_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH);
		}

		public static ulong GetChallenge(byte[] packet)
		{
			byte[] challengeBytes = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH, SyncSettings.CHALLENGE_LENGTH);
			return BitConverter.ToUInt64(challengeBytes);
		}

		public static byte[] GetChallengeAnswer(byte[] packet)
		{
			return GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH, SyncSettings.CHALLENGE_ANSWER_LENGTH);
		}

		public static string GetChallengeUsername(byte[] packet)
		{
			byte usernameLength = packet[SyncSettings.CRC32_LENGTH];
			byte[] usernameBytes = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH + SyncSettings.DATA_FLAG_LENGTH, usernameLength);
			return Encoding.ASCII.GetString(usernameBytes);
		}

		public static Color32 GetChallengeColor(byte[] packet)
		{
			byte usernameLength = packet[SyncSettings.CRC32_LENGTH];
			int colorPosition = SyncSettings.CRC32_LENGTH + SyncSettings.DATA_FLAG_LENGTH + usernameLength;
			return new(packet[colorPosition], packet[colorPosition + 1], packet[colorPosition + 2], 255);
		}


		#endregion

		#region create packets

		/// <summary>
		/// Creates a Data Packet while leaving the CRC32 and Sequence values empty.
		/// </summary>
		/// <param name="moduleHash"></param>
		/// <param name="type"></param>
		/// <param name="data"></param>
		/// <returns></returns>
		public static byte[] CreateDataPacket(uint moduleHash, SyncPacketType type, byte[] data)
		{
			byte[] packet = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + data.Length];
			data[SyncSettings.CRC32_LENGTH] = (byte)type;
			Array.Copy(BitConverter.GetBytes(moduleHash), 0, packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH);
			Array.Copy(data, 0, packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH, data.Length);
			return packet;
		}

		/// <summary>
		/// Creates a Chunked Data Packet while leaving the CRC32 and Sequence values empty.
		/// </summary>
		/// <param name="moduleHash"></param>
		/// <param name="type"></param>
		/// <param name="numberOfSlices"></param>
		/// <param name="sliceNumber"></param>
		/// <param name="data"></param>
		/// <returns></returns>
		public static byte[] CreateChunkedDataPacket(uint moduleHash, SyncPacketType type, ushort numberOfSlices, ushort sliceNumber, byte[] data)
		{
			byte[] packet = new byte[SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER + data.Length];
			packet[SyncSettings.CRC32_LENGTH] = (byte)type;
			packet[SyncSettings.CRC32_LENGTH] |= 1 << 7;
			Array.Copy(BitConverter.GetBytes(moduleHash),	  0, packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH, SyncSettings.MODULE_HASH_LENGTH);
			Array.Copy(BitConverter.GetBytes(numberOfSlices), 0, packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH, SyncSettings.NUMBER_OF_SLICES);
			Array.Copy(BitConverter.GetBytes(sliceNumber),    0, packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + SyncSettings.NUMBER_OF_SLICES, SyncSettings.SLICE_NUMBER);
			Array.Copy(data, 0, packet, SyncSettings.CRC32_LENGTH + SyncSettings.PACKET_TYPE_LENGTH + SyncSettings.SEQUENCE_ID_LENGTH + SyncSettings.MODULE_HASH_LENGTH + SyncSettings.NUMBER_OF_SLICES + SyncSettings.SLICE_NUMBER, data.Length);
			return packet;
		}

		#endregion

		#region packet helpers

		/// <summary>
		/// Returns bytes from from source array starting at an offset.
		/// </summary>
		/// <param name="array"></param>
		/// <param name="offset"></param>
		/// <param name="size">if size is set to 0 the remainder of the array will be returned</param>
		/// <returns></returns>
		public static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
		{
			if (size == 0)
				size = array.Length - offset;

			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

		/// <summary>
		/// Returns wether or not the packet is reliable or reliable unordered.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public static bool IsReliableData(SyncPacketType type)
		{
			return type == SyncPacketType.ReliableData || type == SyncPacketType.ReliableUnorderedData;
		}

		/// <summary>
		/// Returns wether or not the packet is unreliable or unreliable unordered.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public static bool IsUnreliableData(SyncPacketType type)
		{
			return type == SyncPacketType.UnreliableData || type == SyncPacketType.UnreliableUnorderedData;
		}

		public static bool IsDataPacket(SyncPacketType type)
		{
			return IsReliableData(type) || IsUnreliableData(type);
		}

		public static bool IsConnectionRequired(SyncPacketType type)
		{
			return IsDataPacket(type) || type == SyncPacketType.ACK
				|| type == SyncPacketType.ConnectionClosed;
		}

		/// <summary>
		/// Check a Packets CRC32 Checksum against a newly calculated one using the Packet Bytes.
		/// Also includes the Servername as a magic Header if the Sender is connected to the same Server.
		/// </summary>
		/// <param name="checksum"></param>
		/// <param name="packet"></param>
		/// <param name="servername"></param>
		/// <returns></returns>
		public static bool CheckPacketCRC32(uint checksum, byte[] packet, byte[] servername = null)
		{
			int servernameLength = servername == null ? 0 : servername.Length;

			byte[] bytes = new byte[SyncSettings.PROTOCOL_ID_LENGTH + servernameLength + packet.Length];
			Array.Copy(BitConverter.GetBytes(SyncSettings.PROTOCOL_ID), 0, bytes, 0, SyncSettings.PROTOCOL_ID_LENGTH);
			if (servername != null)
				Array.Copy(servername, 0, bytes, SyncSettings.PROTOCOL_ID_LENGTH, servername.Length);
			Array.Copy(packet, 0, bytes, SyncSettings.PROTOCOL_ID_LENGTH + servernameLength, packet.Length);
			return checksum == SyncCRC32.CRC32Bytes(bytes);
		}

		/// <summary>
		/// Add CRC32 Checksum to packet header, calculated using Protocol ID and, if connected, current Servername.
		/// </summary>
		/// <param name="packet"></param>
		public static void CalculateAndAddChecksum(byte[] packet, byte[] servername = null)
		{
			// get packet without CRC32 bytes
			byte[] packetData = GetBytesFromArray(packet, SyncSettings.CRC32_LENGTH);

			// only use servername when it's present
			int servernameLength = servername == null ? 0 : servername.Length;

			// calculate CRC32 checksum from protocol id, servername and packet
			byte[] bytes = new byte[SyncSettings.PROTOCOL_ID_LENGTH + servernameLength + packetData.Length];
			Array.Copy(BitConverter.GetBytes(SyncSettings.PROTOCOL_ID), 0, bytes, 0, SyncSettings.PROTOCOL_ID_LENGTH);
			if (servername != null)
				Array.Copy(servername, 0, bytes, SyncSettings.PROTOCOL_ID_LENGTH, servername.Length);
			Array.Copy(packetData, 0, bytes, SyncSettings.PROTOCOL_ID_LENGTH + servernameLength, packetData.Length);

			// and add it to packet
			byte[] checksumBytes = BitConverter.GetBytes(SyncCRC32.CRC32Bytes(bytes));
			Array.Copy(checksumBytes, 0, packet, 0, SyncSettings.CRC32_LENGTH);
		}

		/// <summary>
		/// Checks if a packet is new by comparing the packets sequence number and the corresponding remote sequence number.
		/// Also handles ushort wrap-arounds by allowing packets that are smaller by half of the maximum value.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		public static bool IsNewPacket(ushort packetSequence, ushort remoteSequence)
		{
			return ((packetSequence > remoteSequence) && (packetSequence - remoteSequence <= (ushort.MaxValue / 2)))
				|| ((packetSequence < remoteSequence) && (remoteSequence - packetSequence > (ushort.MaxValue / 2)));
		}

		/// <summary>
		/// Checks if a packet is next after remote sequence number.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		public static bool IsNextPacket(ushort packetSequence, ushort remoteSequence)
		{
			return (packetSequence == (ushort)(remoteSequence + 1))
				|| (packetSequence == 0 && remoteSequence == ushort.MaxValue);
		}

		public static ulong GenerateChallengeNonce()
		{
			System.Random rnd = new();
			return (ulong)(rnd.NextDouble() * ulong.MaxValue);
		}

		#endregion

		#endregion
	}
}
