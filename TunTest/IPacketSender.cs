namespace TunTest;

public interface IPacketSender
{
    void SendPacket(ReadOnlySpan<byte> packet);
}