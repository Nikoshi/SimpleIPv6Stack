namespace TunTest.Networking.Devices;

public interface ITunDevice : IDisposable
{
    string Name { get; }
    int Read(Span<byte> buffer);
    void Write(ReadOnlySpan<byte> packet);
}