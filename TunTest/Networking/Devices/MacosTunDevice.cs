using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using TunTest.Networking.Packets;

namespace TunTest.Networking.Devices;

public sealed class MacosTunDevice : ITunDevice, IPacketSender
{
    private const int PfSystem = 32;
    private const int SockDgram = 2;
    private const int SysProtoControl = 2;

    private const int AfInet = 2;
    private const int AfInet6 = 30;

    private const int UtunOptIfName = 2;

    // _IOC(IOC_INOUT, 'N', 3, 100)
    private const ulong CtlIoCInfo = 0xc0644e03;

    private const string UtunControlName =
        "com.apple.net.utun_control";

    private readonly int _fd;

    public string Name { get; }

    /*
     * macOS:
     *
     * struct ctl_info {
     *     u_int32_t ctl_id;
     *     char      ctl_name[MAX_KCTL_NAME]; // 96 Bytes
     * };
     */
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CtlInfo
    {
        public uint Id;

        public fixed byte Name[96];
    }

    /*
     * macOS:
     *
     * struct sockaddr_ctl {
     *     u_char      sc_len;
     *     u_char      sc_family;
     *     u_int16_t   ss_sysaddr;
     *     u_int32_t   sc_id;
     *     u_int32_t   sc_unit;
     *     u_int32_t   sc_reserved[5];
     * };
     */
    [StructLayout(LayoutKind.Sequential)]
    private struct SockAddrCtl
    {
        public byte Length;
        public byte Family;
        public ushort SysAddr;
        public uint Id;
        public uint Unit;

        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
    }

    [DllImport("libc", EntryPoint = "socket", SetLastError = true)]
    private static extern int Socket(
        int domain,
        int type,
        int protocol);

    /*
     * ioctl() ist in libc eine variadische Funktion:
     *
     * int ioctl(int fd, unsigned long request, ...);
     *
     * Auf Apple Silicon müssen wir den Aufruf deshalb über die
     * ARM64-kompatible P/Invoke-Signatur abbilden.
     *
     * Der eigentliche Pointer auf ctlInfo landet dadurch dort,
     * wo ihn die native ABI erwartet.
     */
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern unsafe int Ioctl(
        int fd,
        ulong request,
        IntPtr dummy3,
        IntPtr dummy4,
        IntPtr dummy5,
        IntPtr dummy6,
        IntPtr dummy7,
        IntPtr dummy8,
        void* arg);

    [DllImport("libc", EntryPoint = "connect", SetLastError = true)]
    private static extern unsafe int Connect(
        int fd,
        void* address,
        uint addressLength);

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern unsafe int GetSockOpt(
        int fd,
        int level,
        int option,
        void* value,
        ref uint valueLength);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe nint ReadRaw(
        int fd,
        void* buffer,
        nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe nint WriteRaw(
        int fd,
        void* buffer,
        nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    public unsafe MacosTunDevice(int? utunUnit = null)
    {
        _fd = Socket(
            PfSystem,
            SockDgram,
            SysProtoControl);

        if (_fd < 0)
            ThrowLastError("socket");

        try
        {
            /*
             * ctl_info vorbereiten.
             */
            var ctlInfo = new CtlInfo();

            var nameBytes =
                Encoding.ASCII.GetBytes(UtunControlName);

            if (nameBytes.Length >= 96)
            {
                throw new InvalidOperationException(
                    "UTUN_CONTROL_NAME ist zu lang.");
            }

            for (var i = 0; i < nameBytes.Length; i++)
                ctlInfo.Name[i] = nameBytes[i];

            /*
             * ioctl(fd, CTLIOCGINFO, &ctlInfo)
             *
             * Wichtig:
             * Auf ARM64 verwenden wir hier bewusst die spezielle
             * P/Invoke-Signatur für die variadische ioctl-Funktion.
             */
            if (Ioctl(
                    _fd,
                    CtlIoCInfo,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    &ctlInfo) < 0)
            {
                ThrowLastError("ioctl(CTLIOCGINFO)");
            }

            /*
             * sockaddr_ctl aufbauen.
             *
             * sc_unit:
             *
             *   0       -> automatisch
             *   n + 1   -> utun<n>
             */
            var address = new SockAddrCtl
            {
                Length = (byte)sizeof(SockAddrCtl),
                Family = PfSystem,
                SysAddr = SysProtoControl,
                Id = ctlInfo.Id,
                Unit = utunUnit.HasValue
                    ? checked((uint)(utunUnit.Value + 1))
                    : 0u,

                Reserved0 = 0,
                Reserved1 = 0,
                Reserved2 = 0,
                Reserved3 = 0,
                Reserved4 = 0
            };

            if (Connect(
                    _fd,
                    &address,
                    (uint)sizeof(SockAddrCtl)) < 0)
            {
                ThrowLastError("connect");
            }

            /*
             * Jetzt existiert das utun Interface.
             */
            Name = GetInterfaceName();
        }
        catch
        {
            Close(_fd);
            throw;
        }
    }

    private unsafe string GetInterfaceName()
    {
        Span<byte> buffer = stackalloc byte[64];

        fixed (byte* p = buffer)
        {
            uint length = (uint)buffer.Length;

            if (GetSockOpt(
                    _fd,
                    SysProtoControl,
                    UtunOptIfName,
                    p,
                    ref length) < 0)
            {
                ThrowLastError(
                    "getsockopt(UTUN_OPT_IFNAME)");
            }
        }

        var zeroIndex = buffer.IndexOf((byte)0);

        if (zeroIndex >= 0)
            buffer = buffer[..zeroIndex];

        return Encoding.ASCII.GetString(buffer);
    }

    public unsafe int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        // Wir lassen 4 Bytes Platz für den utun-Header, den der Kernel voranstellt
        Span<byte> rawBuffer = stackalloc byte[4 + buffer.Length];

        fixed (byte* p = rawBuffer)
        {
            var result = ReadRaw(_fd, p, (nuint)rawBuffer.Length);

            if (result < 0)
                ThrowLastError("read");

            if (result <= 4)
                return 0;

            var packetLength = (int)result - 4;

            // Direkt ohne Selbst-Kopie in den Zielpuffer des Callers schreiben
            rawBuffer[4..(4 + packetLength)].CopyTo(buffer);

            return packetLength;
        }
    }

    public unsafe void Write(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
            return;

        var version = packet[0] >> 4;

        var addressFamily = version switch
        {
            4 => AfInet,
            6 => AfInet6,

            _ => throw new ArgumentException(
                $"Unbekannte IP-Version: {version}",
                nameof(packet))
        };

        /*
         * utun erwartet vor dem eigentlichen Paket:
         *
         *   4 Byte AF_*
         *   IPv4/IPv6 Paket
         *
         * Der AF-Wert ist Host-Endian.
         * Apple Silicon ist Little Endian.
         */
        Span<byte> buffer =
            stackalloc byte[4 + packet.Length];

        BinaryPrimitives.WriteInt32BigEndian(
            buffer[..4],
            addressFamily);

        packet.CopyTo(buffer[4..]);

        fixed (byte* p = buffer)
        {
            var result = WriteRaw(
                _fd,
                p,
                (nuint)buffer.Length);

            if (result < 0)
                ThrowLastError("write");

            if (result != buffer.Length)
            {
                throw new IOException(
                    $"utun write war unvollständig: " +
                    $"{result}/{buffer.Length} Bytes.");
            }
        }
    }

    public void SendPacket(ReadOnlySpan<byte> packet)
        => Write(packet);

    public void Dispose()
    {
        if (_fd >= 0)
            Close(_fd);
    }

    private static void ThrowLastError(string operation)
    {
        var errno = Marshal.GetLastPInvokeError();

        throw new IOException(
            $"{operation} fehlgeschlagen. errno={errno}");
    }
}