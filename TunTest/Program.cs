using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.RuntimeInformation;

namespace TunTest;

class Program
{
    static void Main()
    {
        if (!IsOSPlatform(OSPlatform.OSX))
        {
            Console.WriteLine("Dieses Demoprogramm erfordert macOS.");
            return;
        }

        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        Console.WriteLine("Initialisiere macOS utun-Schnittstelle...");

        try
        {
            using var tun = new MacosTunDevice();
            Console.WriteLine($"Schnittstelle '{tun.Name}' wurde erfolgreich erstellt!");
            Console.WriteLine("Warte auf IP-Pakete (Strg+C zum Beenden)...");

            // Wir leihen uns einen Puffer aus dem Pool (GC-Entlastung)
            var rentedArray = ArrayPool<byte>.Shared.Rent(2048);
            var buffer = rentedArray.AsSpan();

            try
            {
                var stack = new Ipv6Stack(tun, IPAddress.Parse("fd00::2"));
                while (true)
                {
                    // Blockiert im Kernel, wacht im Mikrosekundenbereich bei Paket-Eingang auf
                    var bytesRead = tun.Read(buffer);
                    if (bytesRead <= 0) continue;

                    var ipPacket = buffer[..bytesRead];
                    stack.ProcessPacket(ipPacket);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedArray);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fehler: {ex.Message}");
            Console.ResetColor();
        }
    }
}

