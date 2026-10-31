using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Ultron.Desktop.Services;

/// <summary>Bir anlık sistem ölçümü (sol paneldeki çizgi grafikler). Ölçülemeyen değer null.</summary>
public sealed record MetricsSample(
    double Cpu,
    double RamUsedGb,
    double RamTotalGb,
    double DiskActive,
    double DiskUsedPercent,
    double? GpuUtil,
    double? GpuTemp,
    double? VramUsedGb,
    double? VramTotalGb,
    int? BatteryPercent,
    bool? Charging,
    double UltronRamGb);

/// <summary>
/// Saniyede bir gerçek sistem verisi: CPU (GetSystemTimes), RAM (GlobalMemoryStatusEx), disk etkinliği (performans sayacı)
/// ve C: doluluğu, ekran kartı kullanımı/sıcaklığı/VRAM (nvidia-smi, 3 sn'de bir), pil, Ultron'un süreçlerinin (Ollama ve
/// ses servisi dahil) kullandığı RAM. CPU sıcaklığı yönetici yetkisi + sürücü gerektirdiği için ölçülmüyor; ekran kartı ölçülüyor.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private readonly Timer _timer;
    private readonly PerformanceCounter? _disk;
    private long _idle, _kernel, _user;
    private (double Util, double Temp, double Used, double Total)? _gpu;
    private double _ultronRam;
    private int _tick;
    private bool _gpuBusy;

    public event Action<MetricsSample>? Sample;

    public SystemMonitor()
    {
        try
        {
            _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            _disk.NextValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            DesktopLog.Write("Disk sayacı açılamadı: " + ex.Message);
        }
        ReadCpu(out _idle, out _kernel, out _user);
        _timer = new Timer(_ => Measure(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Measure()
    {
        try
        {
            _tick++;
            if (_tick % 3 == 1 && !_gpuBusy)
                _ = Task.Run(ReadGpuAsync);
            if (_tick % 5 == 1)
                _ultronRam = UltronProcessesRamGb();

            var cpu = CpuPercent();
            var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            GlobalMemoryStatusEx(ref mem);
            var total = mem.TotalPhys / 1024d / 1024 / 1024;
            var used = total - mem.AvailPhys / 1024d / 1024 / 1024;
            var drive = new DriveInfo("C");
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            var hasBattery = power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;

            Sample?.Invoke(new MetricsSample(
                cpu, used, total,
                Math.Clamp(_disk?.NextValue() ?? 0, 0, 100),
                100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize,
                _gpu?.Util, _gpu?.Temp, _gpu?.Used / 1024, _gpu?.Total / 1024,
                hasBattery ? (int)Math.Round(power.BatteryLifePercent * 100) : null,
                hasBattery ? power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online : null,
                _ultronRam));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            DesktopLog.Write("Ölçüm hatası: " + ex.Message);
        }
    }

    /// <summary>İki ölçüm arasındaki boşta/çekirdek/kullanıcı zamanlarından CPU yüzdesi.</summary>
    private double CpuPercent()
    {
        ReadCpu(out var idle, out var kernel, out var user);
        var total = (kernel - _kernel) + (user - _user); // kernel süresi boşta süresini de içerir
        var busy = total - (idle - _idle);
        (_idle, _kernel, _user) = (idle, kernel, user);
        return total <= 0 ? 0 : Math.Clamp(100d * busy / total, 0, 100);
    }

    private async Task ReadGpuAsync()
    {
        _gpuBusy = true;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi",
                "--query-gpu=utilization.gpu,temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null)
                return;
            var line = (await p.StandardOutput.ReadToEndAsync()).Split('\n')[0];
            var parts = line.Split(',').Select(x => double.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (parts.Length == 4)
                _gpu = (parts[0], parts[1], parts[2], parts[3]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FormatException or InvalidOperationException)
        {
            _gpu = null; // NVIDIA kartı / sürücü yoksa
        }
        finally
        {
            _gpuBusy = false;
        }
    }

    /// <summary>Ultron'u oluşturan süreçler: masaüstü, Host, ses servisi (Python) ve Ollama (dil modeli).</summary>
    private static double UltronProcessesRamGb()
    {
        double bytes = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (name is "Ultron" or "Ultron.Host" || name.StartsWith("ollama", StringComparison.OrdinalIgnoreCase)
                    || (name == "python" && (p.MainModule?.FileName ?? "").Contains(@"\Ultron\", StringComparison.OrdinalIgnoreCase)))
                    bytes += p.WorkingSet64;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Erişilemeyen sistem süreçleri atlanır.
            }
            finally
            {
                p.Dispose();
            }
        }
        return bytes / 1024 / 1024 / 1024;
    }

    private static void ReadCpu(out long idle, out long kernel, out long user)
    {
        GetSystemTimes(out var i, out var k, out var u);
        (idle, kernel, user) = (i, k, u);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    public void Dispose()
    {
        _timer.Dispose();
        _disk?.Dispose();
    }
}
