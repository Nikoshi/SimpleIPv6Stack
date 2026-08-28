using System.Buffers.Binary;

namespace TunTest.Core.IPv6;

public ref struct Ipv6Writer
{
    private readonly Span<byte> _buffer;
    private int _payloadLengthOffset = -1;
    private int _headerEndOffset = 0;

    public Ipv6Writer(Span<byte> buffer)
    {
        if (buffer.Length < 40)
            throw new ArgumentException("Buffer zu klein für IPv6 Header!");
        
        _buffer = buffer;
    }

    public Span<byte> WriteHeader(ReadOnlySpan<byte> sourceIp, ReadOnlySpan<byte> destIp, byte nextHeader, byte hopLimit = 64)
    {
        // Für unseren Forschungs-Stack belassen wir TC und Flow Label vorerst auf 0
        const uint version = 6;
        const uint trafficClass = 0; 
        const uint flowLabel = 0;    

        // Schieben und zusammenkleben
        // ReSharper disable once ShiftExpressionZeroLeftOperand
        const uint versionTcFlow = (version << 28) | (trafficClass << 20) | flowLabel;

        // Als Big-Endian in den Puffer schreiben
        BinaryPrimitives.WriteUInt32BigEndian(_buffer[..4], versionTcFlow);
        
        _payloadLengthOffset = 4;
       
        _buffer[6] = nextHeader;
        _buffer[7] = hopLimit;

        if (sourceIp.Length != 16)
            throw new ArgumentException("Source IP muss exakt 16 Bytes lang sein.", nameof(sourceIp));
        if (destIp.Length != 16)
            throw new ArgumentException("Destination IP muss exakt 16 Bytes lang sein.", nameof(destIp));
        
        sourceIp.CopyTo(_buffer.Slice(8, 16));
        destIp.CopyTo(_buffer.Slice(24, 16));
        
        _headerEndOffset = 40;

        // Gibt exakt den Puffer ab Offset 40 für UDP/ICMP zurück
        return _buffer[_headerEndOffset..];
    }

    public void FinalizePacket(ushort payloadLength)
    {
        if (_payloadLengthOffset == -1)
            throw new InvalidOperationException("WriteHeader muss zuerst aufgerufen werden!");

        BinaryPrimitives.WriteUInt16BigEndian(_buffer.Slice(_payloadLengthOffset, 2), payloadLength);
    }
}