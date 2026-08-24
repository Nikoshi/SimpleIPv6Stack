using System.Buffers.Binary;
using System.Net;

namespace NetworkStack;

public class MyNetworkStack
{
    public void OnPacketReceived(byte[] rawPacketData)
    {
        var packetSpan = new ReadOnlySpan<byte>(rawPacketData);
        ParseEthernetLayer(packetSpan);
    }

    private void ParseEthernetLayer(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 14) 
        {
            Console.WriteLine("Fehler: Frame ist zu kurz für einen Ethernet-Header.");
            return;
        }

        var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));

        if (etherType == 0x86DD) 
        {
            Console.WriteLine($"[Link Layer] IPv6 EtherType (0x{etherType:X4}) erkannt.");
            Console.WriteLine("[Link Layer] Entferne Ethernet-Header (14 Bytes) und reiche Payload weiter...");
                
            ReadOnlySpan<byte> ipv6Payload = frame.Slice(14);
            ParseIPv6Layer(ipv6Payload);
        }
        else
        {
            Console.WriteLine($"[Link Layer] Frame ignoriert. Unbekannter EtherType: 0x{etherType:X4}");
        }
    }

    private void ParseIPv6Layer(ReadOnlySpan<byte> ipv6Packet)
    {
        // 1. Längenprüfung: Ein IPv6 Header MUSS mindestens 40 Bytes haben
        if (ipv6Packet.Length < 40)
        {
            Console.WriteLine("[Internet Layer] Fehler: Paket zu kurz für IPv6-Header.");
            return;
        }

        // 2. Next Header auslesen (Offset 6)
        // Da es nur 1 Byte ist, brauchen wir kein BinaryPrimitives, sondern greifen direkt über den Index zu.
        var nextHeader = ipv6Packet[6];

        // 3. Payload-Länge auslesen (Offset 4, 2 Bytes, Big-Endian)
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(ipv6Packet.Slice(4, 2));

        // 4. IP-Adressen extrahieren (Offset 8 und 24, jeweils 16 Bytes)
        var sourceIpBytes = ipv6Packet.Slice(8, 16);
        var destIpBytes = ipv6Packet.Slice(24, 16);
        
        var sourceIp = new IPAddress(sourceIpBytes);
        var destIp = new IPAddress(destIpBytes);

        Console.WriteLine($"[Internet Layer] Paket von {sourceIp} an {destIp}");
        Console.WriteLine($"[Internet Layer] Payload: {payloadLength} Bytes, Next Header ID: {nextHeader}");

        // 5. Weiterleiten an die Transportschicht, wenn es UDP ist
        if (nextHeader == 17) 
        {
            Console.WriteLine("[Internet Layer] UDP erkannt. Reiche Daten an Transport Layer weiter...");
        
            // Wir schneiden die 40 Bytes IPv6-Header ab, der Rest ist das UDP-Datagramm
            var udpPayload = ipv6Packet.Slice(40);
        
            ParseUdpLayer(sourceIpBytes, destIpBytes, udpPayload); // Diese Methode bauen wir als Nächstes!
        }
        else
        {
            Console.WriteLine($"[Internet Layer] Ignoriere Protokoll-ID {nextHeader}. Wir verarbeiten nur UDP (17).");
        }
    }

    private void ParseUdpLayer(ReadOnlySpan<byte> sourceIp, ReadOnlySpan<byte> destIp, ReadOnlySpan<byte> udpPayload)
    {
        if (udpPayload.Length < 8)
        {
            Console.WriteLine("[UDP Layer] Fehler: Paket zu kurz für UDP-Header.");
            return;
        }
    
        // Die 4 Felder des UDP-Headers sind ALLE exakt 16 Bit (2 Bytes) groß:
        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(udpPayload[..2]);
        var destPort = BinaryPrimitives.ReadUInt16BigEndian(udpPayload.Slice(2, 2));
        var length = BinaryPrimitives.ReadUInt16BigEndian(udpPayload.Slice(4, 2));
        var receivedCheckSum = BinaryPrimitives.ReadUInt16BigEndian(udpPayload.Slice(6, 2));
    
        var calculatedCheckSum = CalculateUdpChecksum(sourceIp, destIp, udpPayload);
        
        // Die Nutzdaten beginnen ab Byte 8 und gehen bis zum Ende
        var dataBytes = udpPayload[8..];
        var dataText = System.Text.Encoding.UTF8.GetString(dataBytes);
    
        Console.WriteLine($"[UDP Layer] Paket von Port {sourcePort} an Port {destPort}");
        Console.WriteLine($"[UDP Layer] Payload-Länge: {length} Bytes, Checksumme: 0x{receivedCheckSum:X4}");

        Console.WriteLine(calculatedCheckSum);
        
        Console.WriteLine(receivedCheckSum == calculatedCheckSum
            ? "[UDP Layer] Checksum ist okay!"
            : "[UDP Layer] Checksum ist nicht okay!");

        Console.WriteLine($"[UDP Layer] Extrahierte Daten: '{dataText}'\n");
    }

    private ushort CalculateUdpChecksum(ReadOnlySpan<byte> sourceIp, ReadOnlySpan<byte> destIp,
        ReadOnlySpan<byte> udpPacket)
    {
        uint sum = 0;

        // IP-Adressen
        for (var i = 0; i < 16; i += 2)
        {
            var p1 = BinaryPrimitives.ReadUInt16BigEndian(sourceIp.Slice(i, 2));
            var p2 = BinaryPrimitives.ReadUInt16BigEndian(destIp.Slice(i, 2));
            sum += p1;
            sum += p2;
        }

        // Protokoll ID 17 => UDP
        sum += 17;
        
        // UDP Paket
        sum += (uint)udpPacket.Length;
        for (var i = 0; i < udpPacket.Length - 1; i+=2)
        {
            if (i == 6) continue;
            sum += BinaryPrimitives.ReadUInt16BigEndian(udpPacket.Slice(i, 2));
        }
        
        // Wenn das Paket eine ungerade Länge hat, verarbeiten wir das letzte Byte
        if (udpPacket.Length % 2 != 0)
        {
            // Wir nehmen das letzte Byte und verschieben es um 8 Bit nach links (virtuelles 0-Padding rechts)
            sum += (uint)(udpPacket[^1] << 8);
        }
        
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) +  (sum >> 16);
        
        return (ushort)~sum;
    }
}