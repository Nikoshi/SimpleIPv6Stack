using System.Buffers.Binary;

namespace TunTest.Core.IPv6;

public readonly ref struct Ipv6HeaderView
{
    private readonly ReadOnlySpan<byte> _data;

    public Ipv6HeaderView(ReadOnlySpan<byte> data)
    {
        //if (_data.Length < 40)
        //    throw new ArgumentException("IPv6 Packet zu klein.");
        
        _data = data;
    }
    
    private uint VersionTrafficClassFlow => BinaryPrimitives.ReadUInt32BigEndian(_data[0..4]);
    
    // Version: Die obersten 4 Bits.
    public int Version => (int)(VersionTrafficClassFlow >> 28);

    public int TrafficClass => (int)(VersionTrafficClassFlow >> 20) & 0xFF;
    
    public int FlowLabel => (int)(VersionTrafficClassFlow & 0x0FFFFF);
    
    public ushort PayloadLength => BinaryPrimitives.ReadUInt16BigEndian(_data[4..6]);
    
    public byte NextHeader => _data[6];
    
    public byte HopLimit => _data[7];
    
    public ReadOnlySpan<byte> SourceAddressBytes => _data.Slice(8, 16);
    public ReadOnlySpan<byte> DestinationAddressBytes => _data.Slice(24, 16);
    
    // public IPAddress SourceAddress => new IPAddress(SourceAddressBytes);
    // public IPAddress DestinationAddress => new IPAddress(DestinationAddressBytes);
    
    public ReadOnlySpan<byte> Payload => _data[40..];
    
    public ReadOnlySpan<byte> RawData => _data;
}