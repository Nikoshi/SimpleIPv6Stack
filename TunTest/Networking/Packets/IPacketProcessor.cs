namespace TunTest.Networking.Packets;

public interface IPacketProcessor
{
    void ProcessPacket(ReadOnlySpan<byte> packetData);
}