namespace TunTest;

public interface IPacketProcessor
{
    void ProcessPacket(ReadOnlySpan<byte> packetData);
}