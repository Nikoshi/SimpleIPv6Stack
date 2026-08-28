using System.Buffers.Binary;
using TunTest.Networking;

namespace TunTest.Core.UDP;

public ref struct UdpWriter
{
    private readonly Span<byte> _buffer;
    private readonly ReadOnlySpan<byte> _srcIpBytes;
    private readonly ReadOnlySpan<byte> _destIpBytes;

    public UdpWriter(Span<byte> buffer, ReadOnlySpan<byte> sourceIp, ReadOnlySpan<byte> destIp)
    {
        if (buffer.Length < 8)
            throw new ArgumentException("Buffer zu klein für UDP Header!");
        
        if (sourceIp.Length != 16)
            throw new ArgumentException("Source IP muss exakt 16 Bytes lang sein.", nameof(sourceIp));
        if (destIp.Length != 16)
            throw new ArgumentException("Destination IP muss exakt 16 Bytes lang sein.", nameof(destIp));
        
        _buffer = buffer;
        _srcIpBytes = sourceIp;
        _destIpBytes = destIp;
    }

    // Schreibt die 8 Bytes und gibt den freien Platz für den Payload zurück
    public Span<byte> WriteHeader(ushort sourcePort, ushort destinationPort)
    {
        BinaryPrimitives.WriteUInt16BigEndian(_buffer, sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.Slice(2,2), destinationPort);

        // Checksum nullen
        _buffer[6] = 0x00;
        _buffer[7] = 0x00;
        
        // Gibt den Puffer ab Offset 8 (für den eigentlichen Payload) zurück
        return _buffer[8..];
    }

    // Der magische Abschluss für UDP
    public void FinalizePacket(ushort payloadLength)
    {
        // Die komplette UDP-Länge (8 + payloadLength) berechnen
        var udpLength = checked((ushort)(8 + payloadLength));
        
        // Diese Länge als Big-Endian an Offset 4 schreiben
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.Slice(4,2), udpLength);
        
        uint checksum = 0;
        ChecksumCalculator.Accumulate(_srcIpBytes, ref checksum);
        ChecksumCalculator.Accumulate(_destIpBytes, ref checksum);

        checksum += udpLength;
        checksum += 17; // Next-Header UDP

        ChecksumCalculator.Accumulate(_buffer, ref checksum);

        var finalizedChecksum = ChecksumCalculator.Finalize(checksum);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.Slice(6, 2), finalizedChecksum);
    }
}