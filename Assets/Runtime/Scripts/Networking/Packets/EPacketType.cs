namespace jKnepel.UnitySync.Networking.Packets
{
    internal enum EPacketType : byte
    {
        ServerInformation,
        ACK,
        ConnectionRequest,
        ConnectionChallenge,
        ChallengeAnswer,
        ConnectionAccepted,
        ConnectionDenied,
        ConnectionClosed,
        ClientDisconnected,
        IsStillActive,
        ReliableData,
        ReliableUnorderedData,
        UnreliableData,
        UnreliableUnorderedData,
        ClientInfo
    }
}