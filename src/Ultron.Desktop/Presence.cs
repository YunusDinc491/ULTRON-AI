using System.Runtime.InteropServices;

namespace Ultron.Desktop;

/// <summary>Windows'a "klavye ya da fare en son ne zaman kullanıldı?" diye sorar (bilgisayar başında mı?).</summary>
internal static class Presence
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;
        // İkisi de açılıştan beri geçen milisaniye (uint): sayaç taşsa bile fark doğru çıkar.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }
}
