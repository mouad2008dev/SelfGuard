using System.Runtime.InteropServices;

namespace SelfGuardService;

/// <summary>P/Invoke wrapper for WinDivert.dll (ships alongside WinDivert64.sys).</summary>
internal static class WinDivert
{
    public const short LAYER_NETWORK = 0;

    [Flags] public enum Flags : ulong { SNIFF = 0x0001, DROP = 0x0002, RECV_ONLY = 0x0004, SEND_ONLY = 0x0008 }

    [StructLayout(LayoutKind.Sequential)]
    public struct Address
    {
        public long Timestamp;
        public byte  Layer;
        public byte  Event;
        public byte  Flags;
        public byte  Reserved1;
        public uint  Reserved2;
        public ulong Data1;   // Network sniffed data (IfIdx/SubIfIdx packed)
        public ulong Data2;
    }

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(string filter, short layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, uint packetLen,
        out uint readLen, ref Address addr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(IntPtr handle, byte[] pPacket, uint packetLen,
        out uint sendLen, ref Address addr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);

    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
}
