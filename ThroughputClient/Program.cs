using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

var targetEndPoint = new IPEndPoint(IPAddress.Parse("fd00::2"), 1234);
using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

const int payloadSize = 1024;
byte[] payload = new byte[payloadSize];
Random.Shared.NextBytes(payload);

long totalBytesSent = 0;
long totalPacketsSent = 0;
long errorCount = 0;

var stopwatch = Stopwatch.StartNew();
var intervalStopwatch = Stopwatch.StartNew();

long bytesInInterval = 0;
long packetsInInterval = 0;

Console.WriteLine($"[Benchmark] Target: {targetEndPoint} | Payload-Größe: {payloadSize} Bytes");
Console.WriteLine(new string('-', 65));

while (stopwatch.ElapsedMilliseconds < 5000)
{
    try
    {
        socket.SendTo(payload, targetEndPoint);
        totalBytesSent += payloadSize;
        totalPacketsSent++;
        bytesInInterval += payloadSize;
        packetsInInterval++;
    }
    catch (SocketException)
    {
        errorCount++;
    }

    if (intervalStopwatch.ElapsedMilliseconds >= 1000)
    {
        double intervalSeconds = intervalStopwatch.Elapsed.TotalSeconds;
        double intervalMbps = (bytesInInterval * 8) / (1024.0 * 1024.0) / intervalSeconds;
        double intervalPps = packetsInInterval / intervalSeconds;

        Console.WriteLine($"[Live] {intervalMbps,7:F2} Mbps | {intervalPps,8:N0} Pakete/s | Fehler: {errorCount}");

        bytesInInterval = 0;
        packetsInInterval = 0;
        intervalStopwatch.Restart();
    }
}

stopwatch.Stop();
double totalSeconds = stopwatch.Elapsed.TotalSeconds;
double totalMegabits = (totalBytesSent * 8) / (1024.0 * 1024.0);
double avgGbps = totalMegabits / 1000.0 / totalSeconds;
double avgMbps = totalMegabits / totalSeconds;
double avgPps = totalPacketsSent / totalSeconds;

Console.WriteLine(new string('-', 65));
Console.WriteLine("Test-Statistik (Endergebnis):");
Console.WriteLine($"  Laufzeit:          {totalSeconds:F3} Sekunden");
Console.WriteLine($"  Datenmenge:        {totalBytesSent / 1024.0 / 1024.0:F2} MB ({totalBytesSent:N0} Bytes)");
Console.WriteLine($"  Gesamtpakete:      {totalPacketsSent:N0}");
Console.WriteLine($"  Durchschnitt:      {avgMbps:F2} Mbps ({avgGbps:F3} Gbps)");
Console.WriteLine($"  Paketrate (PPS):   {avgPps:N0} Pakete/Sekunde");
Console.WriteLine($"  Socket-Fehler:     {errorCount}");