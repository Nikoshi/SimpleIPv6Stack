using System.Runtime.InteropServices;
using System.Text;

namespace TunTest;

public class MacosTunDevice : ITunDevice, IPacketSender
{
    private readonly int _fd;
    public string Name { get; private set; } = "utun?";

    private const int PF_SYSTEM = 32;
    private const int SOCK_DGRAM = 2;
    private const int SYSPROTO_CONTROL = 2;
    private const ulong CTLIOCGINFO = 0xc0644e03;

    [DllImport("libc", EntryPoint = "socket", SetLastError = true)]
    private static extern int Socket(int domain, int type, int protocol);

    // DER ARM64-TRICK: Wir füllen x2 bis x7 mit Dummy-IntPtrs.
    // Unser echter Pointer rutscht dadurch als 9. Argument direkt auf den Stack (SP)!
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
    private static extern unsafe int Connect(int fd, void* addr, int addrLen);

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern unsafe int GetSockOpt(int fd, int level, int optname, void* optval, ref int optLen);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe int ReadRaw(int fd, void* buf, int count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe int WriteRaw(int fd, void* buf, int count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    public unsafe MacosTunDevice(int utunUnit = 0)
    {
        _fd = Socket(PF_SYSTEM, SOCK_DGRAM, SYSPROTO_CONTROL);
        if (_fd < 0)
        {
            throw new Exception($"Konnte System-Socket nicht öffnen. errno: {Marshal.GetLastPInvokeError()}");
        }

        IntPtr ctlInfoAlloc = Marshal.AllocHGlobal(100);
        IntPtr sockAddrAlloc = Marshal.AllocHGlobal(32);
        IntPtr nameBufAlloc = Marshal.AllocHGlobal(64);

        try
        {
            byte* ctlInfo = (byte*)ctlInfoAlloc.ToPointer();
            
            // Speicher sauber nullen
            for (int i = 0; i < 100; i++)
            {
                *(ctlInfo + i) = 0;
            }
            
            byte[] nameBytes = Encoding.ASCII.GetBytes("com.apple.net.utun_control");
            for (int i = 0; i < nameBytes.Length; i++)
            {
                *(ctlInfo + 4 + i) = nameBytes[i];
            }

            // Wir übergeben 6x IntPtr.Zero für die Register x2-x7.
            // 'ctlInfo' landet punktgenau auf dem Stack.
            if (Ioctl(_fd, CTLIOCGINFO, 
                      IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 
                      ctlInfo) < 0)
            {
                throw new Exception($"ioctl(CTLIOCGINFO) fehlgeschlagen. errno: {Marshal.GetLastPInvokeError()}");
            }

            uint ctlId = *(uint*)ctlInfo;

            byte* sockAddr = (byte*)sockAddrAlloc.ToPointer();
            for (int i = 0; i < 32; i++)
            {
                *(sockAddr + i) = 0;
            }
            
            *sockAddr = 32;                               // sc_len
            *(sockAddr + 1) = PF_SYSTEM;                  // sc_family
            *(ushort*)(sockAddr + 2) = SYSPROTO_CONTROL;  // ss_sysaddr
            *(uint*)(sockAddr + 4) = ctlId;               // sc_id
            *(uint*)(sockAddr + 8) = (uint)utunUnit;      // sc_unit (0 = Auto)

            if (Connect(_fd, sockAddr, 32) < 0)
            {
                throw new Exception($"connect() zu utun fehlgeschlagen. errno: {Marshal.GetLastPInvokeError()}");
            }

            byte* nameBuf = (byte*)nameBufAlloc.ToPointer();
            int optLen = 64;
            for (int i = 0; i < 64; i++)
            {
                *(nameBuf + i) = 0;
            }

            if (GetSockOpt(_fd, SYSPROTO_CONTROL, 2, nameBuf, ref optLen) == 0)
            {
                Name = Marshal.PtrToStringAnsi((IntPtr)nameBuf) ?? "utun?";
            }
            else
            {
                Name = $"utun{(utunUnit == 0 ? "x" : (utunUnit - 1).ToString())}";
            }
        }
        catch
        {
            Close(_fd);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(ctlInfoAlloc);
            Marshal.FreeHGlobal(sockAddrAlloc);
            Marshal.FreeHGlobal(nameBufAlloc);
        }
    }

    public unsafe int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0) return 0;

        fixed (byte* p = buffer)
        {
            int result = ReadRaw(_fd, p, buffer.Length);
            
            if (result > 4)
            {
                buffer.Slice(4, result - 4).CopyTo(buffer);
                return result - 4;
            }
            return 0;
        }
    }

    public unsafe void Write(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0) return;

        // Absolut klammerfreier Hochleistungszugriff auf das erste Byte
        ref readonly byte firstByte = ref MemoryMarshal.GetReference(packet);
        byte ipVersion = (byte)(firstByte >> 4);

        byte[] piBuffer = new byte[packet.Length + 4];
        Span<byte> piSpan = piBuffer.AsSpan();
        
        ref byte piTarget = ref MemoryMarshal.GetReference(piSpan.Slice(3, 1));
        if (ipVersion == 6)
        {
            piTarget = 0x1e; // AF_INET6
        }
        else if (ipVersion == 4)
        {
            piTarget = 0x02; // AF_INET
        }

        packet.CopyTo(piSpan.Slice(4));

        fixed (byte* p = piBuffer)
        {
            WriteRaw(_fd, p, piBuffer.Length);
        }
    }

    public void Dispose()
    {
        if (_fd >= 0) Close(_fd);
    }

    public void SendPacket(ReadOnlySpan<byte> packet)
    {
        Write(packet);
    }
}