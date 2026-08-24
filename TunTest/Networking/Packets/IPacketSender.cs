namespace TunTest.Networking.Packets;

public interface IPacketSender
{
    void SendPacket(ReadOnlySpan<byte> packet);
}