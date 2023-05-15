using System;

namespace jKnepel.UnitySync.Networking.Packets
{
	internal class ConnectionClosedPacket : AConnectionNetworkPacket
	{
		public ConnectionClosedPacket()
		{
			Type = EPacketType.ConnectionClosed;
		}

		public ConnectionClosedPacket(byte[] packet)
		{
			Type = EPacketType.ConnectionClosed;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;

			CRC32 = CalculateChecksumBytes(bytes);
			Array.Copy(BitConverter.GetBytes(CRC32), 0, bytes, 0, ModuledNetSettings.CRC32_LENGTH);

			return bytes;
		}

		public override bool TryDeserialize()
		{
			try
			{
				if (!CheckCRC32Checksum(Bytes))
					return false;

				CRC32 = BitConverter.ToUInt32(GetBytesFromArray(Bytes, 0, ModuledNetSettings.CRC32_LENGTH));
			
				return true;
			}
			catch (Exception) { return false; }
		}
	}
}