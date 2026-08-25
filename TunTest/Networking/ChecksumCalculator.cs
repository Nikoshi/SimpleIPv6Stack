using System.Buffers.Binary;

namespace TunTest.Networking;

public static class ChecksumCalculator
{
    public static void Accumulate(ReadOnlySpan<byte> data, ref uint sum)
    {
        for (var i = 0; i < data.Length - 1; i += 2) 
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i, 2));
        
        // Falls die Länge ungerade ist, müssen wir das letzte Byte mit einer 0 "aufpolstern"
        if (data.Length % 2 != 0)
            sum += (uint)(data[^1] << 8);
    }

    public static ushort Finalize(uint sum)
    {
        // Falten, bis kein Überlauf (größer als 16 Bit) mehr da ist
        while ((sum >> 16) != 0) 
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }
}