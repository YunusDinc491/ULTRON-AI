using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Ultron.Core.Briefing;

namespace Ultron.Infrastructure.Briefing;

/// <summary>
/// Windows Görev Zamanlayıcısı'nda "Ultron Sabah Uyandırma" görevi: haftalık, seçili günlerde, "bilgisayarı uyandır" (WakeToRun)
/// ve Ultron'u tepside başlatır (zaten açıksa tek kopya kuralı yenisini kapatır). Kullanıcı görevi: yönetici yetkisi gerekmez.
/// Güç planındaki uyandırma zamanlayıcısı ayarı sadece okunur (powercfg /query), değiştirilmez.
/// </summary>
internal sealed partial class WindowsWakeScheduler(ILogger<WindowsWakeScheduler> logger) : IWakeScheduler
{
    public const string TaskName = "Ultron Sabah Uyandirma";

    public async Task EnsureAsync(TimeOnly at, IReadOnlyCollection<DayOfWeek> days, CancellationToken cancellationToken = default)
    {
        var xml = TaskXml(at, days, UltronExecutable(), DateTime.Now);
        var file = Path.Combine(Path.GetTempPath(), $"ultron-uyandirma-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(file, xml, Encoding.Unicode, cancellationToken); // şema UTF-16 diyor
        try
        {
            var (code, output) = await RunAsync("schtasks", ["/Create", "/TN", TaskName, "/XML", file, "/F"], cancellationToken);
            if (code != 0)
                throw new InvalidOperationException("schtasks: " + output.Trim());
        }
        finally
        {
            File.Delete(file);
        }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        if ((await RunAsync("schtasks", ["/Query", "/TN", TaskName], cancellationToken)).Code == 0)
            await RunAsync("schtasks", ["/Delete", "/TN", TaskName, "/F"], cancellationToken);
    }

    public async Task<WakeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = (await RunAsync("schtasks", ["/Query", "/TN", TaskName], cancellationToken)).Code == 0;
            var (_, power) = await RunAsync("powercfg", ["/query", "SCHEME_CURRENT", "SUB_SLEEP", "RTCWAKE"], cancellationToken);
            var (ac, dc) = ParseWakeTimers(power);
            return new WakeStatus(exists, ac, dc);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new WakeStatus(false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// powercfg çıktısının son iki "0x…" değeri: prizde (AC) ve pilde (DC) ayar indeksi. 0 = kapalı, 1 = etkin, 2 = sadece önemli zamanlayıcılar
    /// (görevlerimiz "önemli" sayılmaz). Dil bağımsız: sadece onaltılık değerlere bakılır.
    /// </summary>
    internal static (bool? Ac, bool? Dc) ParseWakeTimers(string output)
    {
        var values = HexIndex().Matches(output).Select(m => Convert.ToInt32(m.Groups[1].Value, 16)).ToList();
        return values.Count >= 2 ? (values[^2] == 1, values[^1] == 1) : (null, null);
    }

    /// <summary>Masaüstü uygulamasının yolu: "Windows açılışında başlat" kaydından (HKCU Run\Ultron); yoksa sadece uyandırılır.</summary>
    internal static string? UltronExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Ultron") is string value && Regex.Match(value, "^\"([^\"]+)\"") is { Success: true } m && File.Exists(m.Groups[1].Value)
            ? m.Groups[1].Value
            : null;
    }

    internal static string TaskXml(TimeOnly at, IReadOnlyCollection<DayOfWeek> days, string? ultronExe, DateTime now)
    {
        var start = now.Date.Add(at.ToTimeSpan());
        if (start <= now)
            start = start.AddDays(1);
        var dayTags = string.Concat(days.OrderBy(d => ((int)d + 6) % 7).Select(d => $"<{d}/>"));
        var (command, arguments) = ultronExe is null ? ("cmd.exe", "/c exit") : (ultronExe, "--tray");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Ultron: sabah brifingi için bilgisayarı uykudan uyandırır (Ultron'da "sabah uyandırma" ayarı; kapatınca bu görev silinir).</Description>
              </RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByWeek>
                    <DaysOfWeek>{dayTags}</DaysOfWeek>
                    <WeeksInterval>1</WeeksInterval>
                  </ScheduleByWeek>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <WakeToRun>true</WakeToRun>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>false</StartWhenAvailable>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(command)}</Command>
                  <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private async Task<(int Code, string Output)> RunAsync(string file, string[] args, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
            start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken) + await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            logger.LogDebug("{File} {Args} → {Code}: {Output}", file, string.Join(' ', args), process.ExitCode, output.Trim());
        return (process.ExitCode, output);
    }

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})\s*$", RegexOptions.Multiline)]
    private static partial Regex HexIndex();
}
