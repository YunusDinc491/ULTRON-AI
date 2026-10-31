using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Ultron.Core.Computer;

namespace Ultron.Infrastructure.Computer;

/// <summary>
/// Windows masaüstü kontrolü (Faz 21): pencereler user32 ile, ses Core Audio (Windows'un ses arayüzü) ile, medya klavyenin medya
/// tuşları taklit edilerek, güç işleri Windows'un kendi komutlarıyla. Ek program kurulmaz.
/// </summary>
internal sealed partial class WindowsDesktop(ILogger<WindowsDesktop> logger) : IDesktop
{
    // ---- Pencereler ----

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var result = new List<WindowInfo>();
        var own = Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0)
                return true;
            if ((GetWindowLongPtr(h, GwlExStyle).ToInt64() & WsExToolWindow) != 0)
                return true;
            if (DwmGetWindowAttribute(h, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true; // görünmeyen (başka masaüstündeki/askıdaki) uygulama pencereleri
            var title = new StringBuilder(512);
            GetWindowText(h, title, title.Capacity);
            GetWindowThreadProcessId(h, out var pid);
            if (pid == own || title.ToString() is "Program Manager" or "Windows Input Experience")
                return true;
            string process;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                process = p.ProcessName;
            }
            catch (ArgumentException)
            {
                return true;
            }
            result.Add(new WindowInfo(h.ToInt64(), title.ToString(), process, IsIconic(h)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public bool Focus(long handle)
    {
        var h = new IntPtr(handle);
        if (IsIconic(h))
            ShowWindow(h, SwRestore);
        // Arka plandaki bir işlem pencereyi öne getiremez (Windows engeller); kısa bir Alt basımı bu kilidi açar.
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        var ok = SetForegroundWindow(h);
        keybd_event(VkMenu, 0, KeyEventFKeyUp, UIntPtr.Zero);
        return ok;
    }

    public bool Minimize(long handle) => ShowWindow(new IntPtr(handle), SwMinimize);

    public bool Maximize(long handle) => ShowWindow(new IntPtr(handle), SwMaximize);

    public bool Close(long handle) => PostMessage(new IntPtr(handle), WmClose, IntPtr.Zero, IntPtr.Zero);

    public bool Snap(long handle, bool left)
    {
        var h = new IntPtr(handle);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, MonitorDefaultToNearest), ref info))
            return false;
        var w = info.Work;
        var half = (w.Right - w.Left) / 2;
        ShowWindow(h, SwRestore);
        var ok = SetWindowPos(h, IntPtr.Zero, left ? w.Left : w.Left + half, w.Top, half, w.Bottom - w.Top, SwpNoZOrder | SwpShowWindow);
        Focus(handle);
        return ok;
    }

    // ---- Ses (Core Audio) ----

    public int? GetVolume() => WithVolume(v => { Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out var level)); return (int?)Math.Round(level * 100); });

    public void SetVolume(int percent) => WithVolume(v =>
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx));
        return (int?)percent;
    });

    public void SetMute(bool mute) => WithVolume(v =>
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.SetMute(mute, ref ctx));
        return (int?)0;
    });

    public bool? IsMuted()
    {
        bool? muted = null;
        WithVolume(v => { Marshal.ThrowExceptionForHR(v.GetMute(out var m)); muted = m; return (int?)0; });
        return muted;
    }

    private int? WithVolume(Func<IAudioEndpointVolume, int?> action)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj));
            return action((IAudioEndpointVolume)obj);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            logger.LogWarning(ex, "Ses ayarına erişilemedi");
            return null;
        }
    }

    // ---- Medya ve güç ----

    public void Press(MediaKey key)
    {
        byte vk = key switch { MediaKey.Next => 0xB0, MediaKey.Previous => 0xB1, MediaKey.Stop => 0xB2, _ => 0xB3 };
        keybd_event(vk, 0, KeyEventFExtendedKey, UIntPtr.Zero);
        keybd_event(vk, 0, KeyEventFExtendedKey | KeyEventFKeyUp, UIntPtr.Zero);
    }

    public void Power(PowerAction action)
    {
        logger.LogInformation("Güç işlemi: {Action}", action);
        switch (action)
        {
            case PowerAction.Lock:
                LockWorkStation();
                break;
            case PowerAction.Sleep:
                SetSuspendState(false, false, false);
                break;
            case PowerAction.Restart:
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 15") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                break;
            case PowerAction.Shutdown:
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 15") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
                break;
        }
    }

    // ---- Windows API ----

    private const int GwlExStyle = -20, DwmwaCloaked = 14, SwRestore = 9, SwMinimize = 6, SwMaximize = 3;
    private const long WsExToolWindow = 0x80;
    private const uint WmClose = 0x10, SwpNoZOrder = 0x4, SwpShowWindow = 0x40, MonitorDefaultToNearest = 2, KeyEventFExtendedKey = 1, KeyEventFKeyUp = 2;
    private const byte VkMenu = 0x12;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
