using Ultron.Core.Text;

namespace Ultron.Core.Computer;

/// <summary>Açık bir pencere: tanıtıcı, başlık, uygulama (işlem adı), küçültülmüş mü.</summary>
public sealed record WindowInfo(long Handle, string Title, string Process, bool Minimized);

public enum MediaKey { PlayPause, Next, Previous, Stop }

public enum PowerAction { Lock, Sleep, Restart, Shutdown }

/// <summary>
/// Masaüstü kontrolü (Faz 21, kullanıcının kararları): pencereler (kapatma hariç serbest), ses ve medya (serbest), güç (kilitleme
/// serbest; uyku/yeniden başlatma/kapatma onaylı). Araçlar karar verir; bu katman sadece Windows'ta uygular.
/// </summary>
public interface IDesktop
{
    IReadOnlyList<WindowInfo> ListWindows();

    bool Focus(long handle);

    bool Minimize(long handle);

    bool Maximize(long handle);

    /// <summary>Pencereye "kapan" der (uygulama kaydedilmemiş iş varsa kendisi sorar); zorla kapatmaz.</summary>
    bool Close(long handle);

    /// <summary>İki pencereyi ekranın sol ve sağ yarısına yerleştirir (biri verilirse sadece o, belirtilen yarıya).</summary>
    bool Snap(long handle, bool left);

    /// <summary>Ana ses düzeyi 0-100; okunamazsa null.</summary>
    int? GetVolume();

    void SetVolume(int percent);

    void SetMute(bool mute);

    bool? IsMuted();

    void Press(MediaKey key);

    void Power(PowerAction action);
}

/// <summary>Söylenen adı açık pencereyle eşleştirir: "chrome", "vs code", "termodinamik pdf'i", "spotify".</summary>
public static class WindowMatch
{
    /// <summary>Konuşmadaki adlar → işlem adı (Windows'taki .exe adı).</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["vs code"] = "code", ["vscode"] = "code", ["visual studio code"] = "code", ["kod"] = "code",
        ["chrome"] = "chrome", ["krom"] = "chrome", ["edge"] = "msedge", ["tarayici"] = "msedge",
        ["dosya gezgini"] = "explorer", ["gezgin"] = "explorer", ["not defteri"] = "notepad", ["hesap makinesi"] = "calculatorapp",
        ["word"] = "winword", ["excel"] = "excel", ["powerpoint"] = "powerpnt", ["spotify"] = "spotify", ["discord"] = "discord",
        ["whatsapp"] = "whatsapp", ["telegram"] = "telegram", ["ultron"] = "ultron", ["terminal"] = "windowsterminal",
        ["visual studio"] = "devenv", ["teams"] = "ms-teams", ["steam"] = "steam",
    };

    private static readonly string[] Filler = ["penceresi", "penceresini", "pencere", "uygulamasi", "uygulamasini", "programi", "programini", "dosyasi", "dosyasini", "i", "u", "yi", "yu", "ni", "nu", "pdf i", "pdfi"];

    /// <returns>En iyi eşleşen pencere; birden çok eşit aday varsa hepsi (kullanıcıya sorulur).</returns>
    public static IReadOnlyList<WindowInfo> Find(IReadOnlyList<WindowInfo> windows, string query)
    {
        var q = Clean(query);
        if (q.Length == 0)
            return [];
        var process = Aliases.TryGetValue(q, out var alias) ? alias
            : Aliases.Where(a => q.StartsWith(a.Key + " ", StringComparison.Ordinal)).Select(a => a.Value).FirstOrDefault();
        if (process is not null)
        {
            var byProcess = windows.Where(w => string.Equals(w.Process, process, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byProcess.Count > 0)
                return byProcess.Take(1).ToList(); // aynı uygulamanın en öndeki penceresi
        }
        var exactProcess = windows.Where(w => TurkishText.Normalize(w.Process) == q.Replace(" ", "")).ToList();
        if (exactProcess.Count > 0)
            return exactProcess.Take(1).ToList();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToList();
        if (words.Count == 0)
            return [];
        var scored = windows
            .Select(w => (w, score: words.Count(word => TurkishText.Normalize(w.Title + " " + w.Process).Contains(word, StringComparison.Ordinal))))
            .Where(x => x.score > 0).OrderByDescending(x => x.score).ToList();
        if (scored.Count == 0)
            return [];
        var best = scored[0].score;
        return scored.Where(x => x.score == best).Select(x => x.w).Take(5).ToList();
    }

    private static string Clean(string query)
    {
        var words = TurkishText.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(w));
        return string.Join(' ', words).Trim();
    }

    public static string Describe(WindowInfo w) => $"{(w.Title.Length > 70 ? w.Title[..70] + "…" : w.Title)} ({w.Process}{(w.Minimized ? ", küçük" : "")})";
}
