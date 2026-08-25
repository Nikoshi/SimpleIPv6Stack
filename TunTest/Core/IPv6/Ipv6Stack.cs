using System.Buffers.Binary;
using System.Net;
using TunTest.Core.ICMPv6;
using TunTest.Networking.Packets;

namespace TunTest.Core.IPv6;

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

        // Prüfen: Ist das Paket für uns?
        var isForUs = ipv6Header.DestinationAddressBytes.SequenceEqual(_myIpBytes);
        
        // Routing für später
        if (!isForUs)
        {
            if (ipv6Header.HopLimit <= 1)
            {
                SendIcmpv6Error(ipv6Header, 3, 0);
                return; // Abgelaufene Pakete verwerfen
            }
            
            // (Später kommt hier die Weiterleitungs-Logik hin, falls HopLimit > 1)
        }

        // Unabhängig vom Routing verwerfen wir ab hier alle Pakete die nicht für uns sind!
        if (!isForUs)
            return;
        
        switch (ipv6Header.NextHeader)
        {
            case 58: // ICMPv6
                HandleIcmpv6(ipv6Header);
                break;
            default:
                // Protokoll wird (noch) nicht unterstützt
                Console.WriteLine($"[Stack] Unbekanntes Protokoll {ipv6Header.NextHeader}. Sende ICMPv6 Unreachable...");
                
                // Type 1 (Unreachable), Code 4 (Port/Protocol Unreachable)
                SendIcmpv6Error(ipv6Header, type: 1, code: 4);
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
            case 133: // Router Solicitation
                // HandleRouterSolicitation(requestIpv6, icmpHeader);
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

    private void SendIcmpv6Error(Ipv6HeaderView offendingPacket, byte type, byte code)
    {
        // RFC 4443: Niemals auf Multicast-Pakete mit ICMP-Fehlern antworten!
        if (offendingPacket.DestinationAddressBytes[0] == 0xFF)
            return; // Stumm verwerfen
        
        // Längen berechnen (Die eiserne MTU-Regel)
        // das Gesamtpaket darf 1280 Bytes nicht überschreiten. 
        // Overhead: 40 Bytes (IPv6 Header) + 8 Bytes (ICMPv6 Error Header) = 48 Bytes
        const ushort maxOriginalLength = 1280 - 48;
        
        var originalLength = Math.Min(offendingPacket.RawData.Length, maxOriginalLength);
        
        // Berechne die Payload-Länge für den neuen IPv6-Header (8 Bytes ICMP + originalLength)
        var payloadLength = (ushort)(8 + originalLength);
        
        // Berechne die totalLength für dein stackalloc (40 Bytes IPv6 + payloadLength)
        var totalLength = 40 + payloadLength;
        
        // Speicher allokieren (Zero Allocation!)
        Span<byte> replyBuffer = stackalloc byte[totalLength];
        
        // IPv6-Header klonen und anpassen
        var ipv6ReplySpan = replyBuffer[..40];
        offendingPacket.RawData[..40].CopyTo(ipv6ReplySpan);
        
        ipv6ReplySpan[6] =  58; // Unsere Antwort ist ein ICMPv6 Paket (genauer ein Fehler)
        
        // Eigene IP als NEUE Source (Offset 8) eintragen
        _myIpBytes.CopyTo(ipv6ReplySpan.Slice(8, 16));
        
        // Alte Source des offendingPacket als NEUE Destination (Offset 24) eintragen
        offendingPacket.SourceAddressBytes.CopyTo(ipv6ReplySpan.Slice(24, 16));
        
        // payloadLength als 16-Bit Big-Endian an Offset 4 eintragen
        BinaryPrimitives.WriteUInt16BigEndian(ipv6ReplySpan.Slice(4, 2), payloadLength);
        
        // ICMPv6-Header (8 Bytes) bauen
        var icmpReplySpan = replyBuffer[40..];
        
        // Super wichtig: Das setzt die Checksumme (Offset 2-3) und 
        // die 4 "Unused" Bytes (Offset 4-7) brav auf 0x00!
        icmpReplySpan.Clear();

        icmpReplySpan[0] = type;
        icmpReplySpan[1] = code;
        
        // Den Beweis anhängen!
        offendingPacket.RawData[..originalLength].CopyTo(icmpReplySpan.Slice(8, originalLength));
        
        // 6. Checksumme berechnen
        uint checksum = 0;
        
        ChecksumCalculator.Accumulate(ipv6ReplySpan.Slice(8, 16),ref checksum);
        ChecksumCalculator.Accumulate(ipv6ReplySpan.Slice(24,16), ref checksum);
        
        checksum += payloadLength; // Länge Payload
        checksum += 58; // Next-Header ICMPv6
        
        ChecksumCalculator.Accumulate(icmpReplySpan, ref checksum);
        
        // Checksumme eintragen und senden
        var finalizedChecksum = ChecksumCalculator.Finalize(checksum);
        BinaryPrimitives.WriteUInt16BigEndian(icmpReplySpan.Slice(2, 2), finalizedChecksum);

        // Absenden!
        _packetSender.SendPacket(replyBuffer);
    }
}