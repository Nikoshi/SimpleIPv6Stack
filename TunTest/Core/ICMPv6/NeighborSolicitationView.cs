namespace TunTest;

public readonly ref struct NeighborSolicitationView
{
    private readonly ReadOnlySpan<byte> _data;

    public NeighborSolicitationView(ReadOnlySpan<byte> payloadData)
    {
        // Ein NS-Payload muss mindestens die 16 Bytes der Target Address enthalten
        if (payloadData.Length < 16)
            throw new ArgumentException("Payload zu klein für eine Neighbor Solicitation.");
            
        _data = payloadData;
    }

    // Die IPv6-Adresse, nach der das Betriebssystem sucht
    public ReadOnlySpan<byte> TargetAddress => _data[..16];

    // Alles, was danach kommt, sind die ICMPv6 Optionen 
    // (meistens die Source Link-Layer Address / MAC-Adresse des Anfragers)
    public ReadOnlySpan<byte> Options => _data[16..];
}