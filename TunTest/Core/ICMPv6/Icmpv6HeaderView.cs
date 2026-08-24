using System.Buffers.Binary;

namespace TunTest;

public readonly ref struct Icmpv6HeaderView
{
    private readonly ReadOnlySpan<byte> _data;

    public Icmpv6HeaderView(ReadOnlySpan<byte> data)
    {
        //if (_data.Length < 8)
        //    throw new ArgumentException("ICMPv6 Packet zu klein.");
        
        _data = data;
    }
    
    public byte Type => _data[0];
    public byte Code => _data[1];
    public ushort Checksum => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(2, 2));
    public ushort Identifier => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(4, 2));
    public ushort SequenceNumber => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(6, 2));

    public ReadOnlySpan<byte> Payload => _data[8..];
    
    public ReadOnlySpan<byte> RawData => _data;
}