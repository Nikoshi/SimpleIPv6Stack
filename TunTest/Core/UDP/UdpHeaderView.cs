using System.Buffers.Binary;

namespace TunTest.Core.UDP;

public readonly ref struct UdpHeaderView
{
    private readonly ReadOnlySpan<byte> _data;

    public UdpHeaderView(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("UDP Header zu klein!");
        _data = data;
    }
    
    public ushort SourcePort => BinaryPrimitives.ReadUInt16BigEndian(_data[..2]);
    
    public ushort DestinationPort => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(2, 2));
    
    public ushort Length => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(4, 2));
    
    public ushort Checksum => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(6, 2));
    
    public ReadOnlySpan<byte> Payload => _data[8..];
    
    public ReadOnlySpan<byte> RawData => _data;
    
}