using System.Diagnostics;
using System.IO;

namespace Ultron.Desktop.Services;

/// <summary>
/// Kaynak kartındaki belgeyi açar. PDF'i istenen sayfada açmak için Edge'e "dosya#page=12" verilir
/// (varsayılan PDF programına sayfa bilgisi geçirilemiyor); Edge yoksa ya da PDF değilse varsayılan programla açılır.
/// </summary>
public static class DocumentOpener
{
    private static readonly string[] EdgePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
    ];

    public static void Open(string path, int? page)
    {
        try
        {
            if (!File.Exists(path))
            {
                DesktopLog.Write("Belge bulunamadı: " + path);
                return;
            }

            var edge = EdgePaths.FirstOrDefault(File.Exists);
            if (page is { } p && edge is not null && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(edge, $"\"{new Uri(path).AbsoluteUri}#page={p}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DesktopLog.Write("Belge açılamadı: " + ex.Message);
        }
    }
}
