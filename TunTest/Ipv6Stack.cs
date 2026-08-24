using System.Buffers.Binary;
using System.Net;

namespace TunTest;

public class Ipv6Stack : IPacketProcessor
{
    private readonly IPacketSender _packetSender;
    private readonly byte[] _myIpBytes;

    public Ipv6Stack(IPacketSender packetSender, IPAddress myIp)
    {
        _packetSender = packetSender;
        _myIpBytes = myIp.GetAddressBytes();

        if (_myIpBytes.Length != 16)
            throw new ArgumentException("Der Stack unterstützt nur IPv6-Adressen!");
    }

    public void ProcessPacket(ReadOnlySpan<byte> packetData)
    {
        var ipv6Header = new Ipv6HeaderView(packetData);
        // Prüfen, ob Paket wirklich IPv6, sonst verwerfen
        if (ipv6Header.Version != 6) return;

        switch (ipv6Header.NextHeader)
        {
            case 58: // ICMPv6
                HandleIcmpv6(ipv6Header);
                break;
            default:
                // Protokoll verwerfen, da nicht unterstützt
                break;
        }
    }

    private void HandleIcmpv6(Ipv6HeaderView requestIpv6)
    {
        var icmpHeader = new Icmpv6HeaderView(requestIpv6.Payload);

        switch (icmpHeader.Type)
        {
            case 128: // Echo Request (Ping)
                HandleIcmpv6EchoRequest(requestIpv6, icmpHeader);
                break;
            case 135: // Neighbor Solicitation
                HandleNeighborSolicitation(requestIpv6, icmpHeader);
                break;
            default:
                // Unbekannter ICMP-Typ -> verwerfen
            break;
        }
    }

    private void HandleIcmpv6EchoRequest(Ipv6HeaderView requestIpv6, Icmpv6HeaderView icmpRequest)
    {
        // Nur auf Pings antworten, die exakt an unsere IP gerichtet sind
        if (!requestIpv6.DestinationAddressBytes.SequenceEqual(_myIpBytes))
        {
            // Paket ignorieren, da es nicht für uns bestimmt ist
            return;
        }
        
        // Speicher für Antwort reservieren
        // 40 Byte IPv6-Header + Nutzdaten
        var totalLength = 40 + requestIpv6.PayloadLength;
        Span<byte> replyBuffer = stackalloc byte[totalLength];
        
        // IPv6-Header klonen und IPs tauschen
        var ipv6ReplySpan = replyBuffer[..40];
        requestIpv6.RawData[..40].CopyTo(ipv6ReplySpan);
        
        requestIpv6.DestinationAddressBytes.CopyTo(ipv6ReplySpan.Slice(8, 16));
        requestIpv6.SourceAddressBytes.CopyTo(ipv6ReplySpan.Slice(24, 16));

        // ICMPv6-Header kopieren & anpassen
        var icmpReplySpan = replyBuffer[40..];
        icmpRequest.RawData.CopyTo(icmpReplySpan);

        icmpReplySpan[0] =  129; // Echo Reply
        icmpReplySpan[2] = 0x00; // Checksum
        icmpReplySpan[3] = 0x00; //   nullen
        
        // Checksum anpassen
        uint checksum = 0;
        
        // Pseudo-Header
        ChecksumCalculator.Accumulate(ipv6ReplySpan.Slice(8, 16),ref checksum);
        ChecksumCalculator.Accumulate(ipv6ReplySpan.Slice(24,16), ref checksum);
        
        checksum += requestIpv6.PayloadLength;
        checksum += 58; // Next-Header ICMPv6
        
        ChecksumCalculator.Accumulate(icmpReplySpan, ref checksum);
        
        // Checksumme eintragen und senden
        var finalizedChecksum = ChecksumCalculator.Finalize(checksum);
        BinaryPrimitives.WriteUInt16BigEndian(icmpReplySpan.Slice(2, 2), finalizedChecksum);
    
        _packetSender.SendPacket(replyBuffer);
    }
    
    private void HandleNeighborSolicitation(Ipv6HeaderView requestIpv6, Icmpv6HeaderView icmpHeader)
    {
        var nsView = new NeighborSolicitationView(icmpHeader.Payload);

        if (!nsView.TargetAddress.SequenceEqual(_myIpBytes))
        {
            // Nicht für uns → ignorieren
            return;
        }
        
        Console.WriteLine("[NDP] Jemand sucht nach uns! Sende Neighbor Advertisement...");
        
        // Gesamtlänge: 40 (IPv6) + 24 (ICMPv6 NA) = 64 Bytes
        Span<byte> replyBuffer = stackalloc byte[64];
        
        // IPv6-Header klonen und anpassen
        var ipv6ReplySpan = replyBuffer[..40];
        requestIpv6.RawData[..40].CopyTo(ipv6ReplySpan);
        
        // Eigene IP als Source (Offset 8) eintragen
        _myIpBytes.CopyTo(ipv6ReplySpan.Slice(8, 16));
        
        // SourceAddressBytes als Destination (Offset 24) eintragen
        requestIpv6.SourceAddressBytes.CopyTo(ipv6ReplySpan.Slice(24, 16));
        
        // Payload-Length im IPv6-Header anpassen! (Offset 4 und 5).
        // Der NA Payload ist exakt 24 Bytes lang.
        BinaryPrimitives.WriteUInt16BigEndian(ipv6ReplySpan.Slice(4, 2), 24);
        
        // ICMPv6 Header für NA (Type 136) bauen
        var icmpReplySpan = replyBuffer[40..];
        icmpReplySpan.Clear();

        icmpReplySpan[0] = 136; // Type: Neighbor Advertisement
        // Code (Index 1) bleibt 0
        // Checksum (Index 2..3) bleibt vorerst 0
        
        // Flags setzen (Solicited + Override)
        icmpReplySpan[4] = 0x60;
        
        // Trage eigene IP in den icmpReplySpan ab Offset 8 ein (Target Address)
        _myIpBytes.CopyTo(icmpReplySpan.Slice(8, 16));
        
        // Checksum berechnen
        uint checksum = 0;
        
        // Pseudo-Header
        ChecksumCalculator.Accumulate(ipv6ReplySpan.Slice(8, 16),ref checksum);
        ChecksumCalculator.Accumulate(ipv6ReplySpan.Slice(24,16), ref checksum);
        
        checksum += 24; // Länge NA Paket
        checksum += 58; // Next-Header ICMPv6
        
        ChecksumCalculator.Accumulate(icmpReplySpan, ref checksum);
        
        // Checksumme eintragen und senden
        var finalizedChecksum = ChecksumCalculator.Finalize(checksum);
        BinaryPrimitives.WriteUInt16BigEndian(icmpReplySpan.Slice(2, 2), finalizedChecksum);

        // Absenden!
        _packetSender.SendPacket(replyBuffer);
    }
}