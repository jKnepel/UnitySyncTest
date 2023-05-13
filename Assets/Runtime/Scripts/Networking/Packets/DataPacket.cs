using System;
using System.Text;
using UnityEngine;

namespace jKnepel.UnitySync.Networking.Packets
{
	internal class DataPacket : ASequencedNetworkPacket
	{
		public byte? ClientID { get; private set; }
		public Action<bool> Callback { get; private set; }
		public uint ModuleHash { get; private set; }
		public byte[] Data { get; private set; }

		public bool IsChunked { get; private set; }
		public ushort NumberOfSlices { get; private set; }
		public ushort SliceNumber { get; private set; }

		private readonly int MTU;
		private readonly byte[] ChunkData;

		public DataPacket(EPacketType type, Action<bool> callback, uint moduleHash, byte[] data, byte? clientID)
		{
			Type = type;
			ClientID = clientID;
			Callback = callback;
			ModuleHash = moduleHash;

			MTU = ModuledNetSettings.GetOrCreateSettings().MTU;
			IsChunked = data.Length > MTU;

			if (IsChunked)
			{
				NumberOfSlices = (ushort)(data.Length % MTU == 0
					? data.Length / MTU
					: data.Length / MTU + 1);
				SliceNumber = 0;
				ChunkData = data;
				Data = GetBytesFromArray(data, 0, MTU);
			}
			else
			{
				NumberOfSlices = 0;
				SliceNumber = 0;
				Data = data;
				ChunkData = data;
			}
		}

		public DataPacket(EPacketType type, byte[] packet)
		{
			Type = type;
			Bytes = packet;
		}

		public void SetSliceData(ushort? sliceNumber = null)
		{
			if (!IsChunked)
				throw new Exception("Can't set Slice data on a non-chunked Packet!");

			if (sliceNumber == null)
				SliceNumber++;
			else
				SliceNumber = Math.Min((ushort)sliceNumber, NumberOfSlices);

			int sliceLength = SliceNumber < NumberOfSlices - 1 ? MTU : ChunkData.Length % MTU;
			Data = GetBytesFromArray(ChunkData, SliceNumber * MTU, sliceLength);
		}

		public override byte[] Serialize(ushort sequence)
		{
			int chunkedHeaderLength = IsChunked ? ModuledNetSettings.NUMBER_OF_SLICES + ModuledNetSettings.SLICE_NUMBER : 0;
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_HASH_LENGTH + Data.Length];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(BitConverter.GetBytes(sequence), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength] = (byte)ClientID;
			Array.Copy(BitConverter.GetBytes(ModuleHash), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH, ModuledNetSettings.MODULE_HASH_LENGTH);
			Array.Copy(Data, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_HASH_LENGTH, Data.Length);

			if (IsChunked)
			{
				bytes[ModuledNetSettings.CRC32_LENGTH] |= 1 << 7;
				Array.Copy(BitConverter.GetBytes(NumberOfSlices), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH, ModuledNetSettings.NUMBER_OF_SLICES);
				Array.Copy(BitConverter.GetBytes(SliceNumber), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.NUMBER_OF_SLICES, ModuledNetSettings.SLICE_NUMBER);
			}

			CRC32 = CalculateChecksumBytes(bytes);
			Array.Copy(BitConverter.GetBytes(CRC32), 0, bytes, 0, ModuledNetSettings.CRC32_LENGTH);

			return bytes;
		}

		public override bool TryDeserialize()
		{
			if (!CheckCRC32Checksum(Bytes))
				return false;

			IsChunked = (Bytes[ModuledNetSettings.CRC32_LENGTH] & (1 << 7)) != 0;
			int chunkedHeaderLength = IsChunked ? ModuledNetSettings.NUMBER_OF_SLICES + ModuledNetSettings.SLICE_NUMBER : 0;

			CRC32 = BitConverter.ToUInt32(GetBytesFromArray(Bytes, 0, ModuledNetSettings.CRC32_LENGTH));
			Sequence = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH));
			ClientID = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength];
			ModuleHash = BitConverter.ToUInt32(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH, ModuledNetSettings.MODULE_HASH_LENGTH));
			Data = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_HASH_LENGTH);

			if (IsChunked)
			{
				NumberOfSlices = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH, ModuledNetSettings.NUMBER_OF_SLICES));
				SliceNumber = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.NUMBER_OF_SLICES, ModuledNetSettings.SLICE_NUMBER));
			}

			return true;
		}
	}
}
