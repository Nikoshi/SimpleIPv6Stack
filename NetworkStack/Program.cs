using NetworkStack;

Console.WriteLine("Starte den minimalen Netzwerkstack...");

// Instanziieren unseres Stacks
MyNetworkStack stack = new MyNetworkStack();

// ---------------------------------------------------------
// SIMULATION: Wir bauen ein künstliches Ethernet-Frame
// ---------------------------------------------------------
byte[] dummyPacket =
[
    // --- ETHERNET HEADER (14 Bytes) ---
    0x00, 0x11, 0x22, 0x33, 0x44, 0x55, // Ziel-MAC
    0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, // Quell-MAC
    0x86, 0xDD,                         // EtherType (IPv6)

    // --- IPv6 HEADER (40 Bytes) ---
    0x60, 0x00, 0x00, 0x00, // Version (6), Traffic Class, Flow Label
    0x00, 0x0C,             // Payload Length: 12 Bytes (8 Bytes UDP Header + 4 Bytes Nutzdaten)
    0x11,                   // Next Header: 17 (UDP)
    0x40,                   // Hop Limit: 64
    // Quell-IP (fe80::1)
    0xfe, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
    // Ziel-IP (fe80::2)
    0xfe, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02,

    // --- UDP HEADER (8 Bytes) ---
    0x30, 0x39, // Quellport: 12345 (0x3039)
    0x13, 0x88, // Zielport: 5000 (0x1388)
    0x00, 0x0C, // UDP Länge: 12 Bytes (Header + Daten)
    0xF7, 0x36, // Checksumme

    // --- NUTZDATEN (4 Bytes) ---
    0x54, 0x65, 0x73, 0x74  // ASCII-Werte für "Test"
];

Console.WriteLine("Simuliere den Empfang eines Pakets von der Netzwerkkarte...\n");
            
// Wir werfen das künstliche Paket in unseren Stack
stack.OnPacketReceived(dummyPacket);

Console.WriteLine("\nTest beendet. Drücken Sie eine beliebige Taste.");
Console.ReadKey();