using System.Text.RegularExpressions;

namespace Ultron.Core.Chat;

/// <summary>
/// Cevap bittikten sonra uygulanan küçük düzeltmeler. Prompt kuralı ("sonuna soru ekleme") bu modelde tutmuyor
/// (canlı denemede tekrar tekrar "…hangisi daha uygun?" ile bitti); takip soruları zaten kartta soruluyor.
/// </summary>
public static partial class AnswerCleanup
{
    /// <summary>Son cümle soruysa ve öncesinde içerik varsa onu atar. Cevabın kendisi tek bir soruysa dokunmaz.</summary>
    public static string RemoveTrailingQuestion(string answer)
    {
        var trimmed = answer.TrimEnd();
        if (!trimmed.EndsWith('?'))
            return answer;

        var before = trimmed[..LastSentenceStart(trimmed[..^1])].TrimEnd();
        return before.Length < 20 ? answer : before;
    }

    /// <summary>
    /// Son cümlenin başı: son satır sonu ya da arkasından boşluk gelen son ". ! : ?" sonrası. "Program.cs", "ASP.NET" içindeki nokta cümle
    /// sonu değil (gece sınavı: cevaplar "…Startup/Program." ve "ASP." diye kesiliyordu).
    /// </summary>
    private static int LastSentenceStart(string text) =>
        new[] { text.LastIndexOf('\n'), text.LastIndexOf(". ", StringComparison.Ordinal), text.LastIndexOf("! ", StringComparison.Ordinal),
                text.LastIndexOf(": ", StringComparison.Ordinal), text.LastIndexOf("? ", StringComparison.Ordinal) }.Max() + 1;

    /// <summary>
    /// Model cevabın sonuna "…daha fazla örnek verebilirim.", "İstersen anlatabilirim." gibi teklif ya da soru ekliyor (gece sınavı
    /// 2026-10-28: 40 cevabın 14'ünde). Yazılım ve açıklama cevaplarında da atılır; "Claude Code'a vereyim mi?" kasıtlıdır, kalır.
    /// </summary>
    public static string Tidy(string answer)
    {
        var text = BracketNote().Replace(answer, "");
        for (var i = 0; i < 2; i++) // "…anlatabilirim. Hangisini istersin?" gibi iki cümlelik kapanış
        {
            var trimmed = text.TrimEnd();
            if (trimmed.EndsWith("vereyim mi?", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("Claude Code", StringComparison.Ordinal))
                break;
            var next = trimmed.EndsWith('?') ? RemoveTrailingQuestion(trimmed) : RemoveTrailingOffer(trimmed);
            if (next.Length == trimmed.Length)
                break;
            text = next;
        }
        return text.TrimEnd();
    }

    private static readonly string[] OfferWords =
    [
        "verebilirim", "yardımcı olabilirim", "anlatabilirim", "açıklayabilirim", "gösterebilirim", "önerebilirim", "paylaşabilirim",
        "ister misin", "istersen", "istersem", "sorabilirsin", "bana sorabilirsin", "söylersen", "inceleyebiliriz", "bakabiliriz",
        "konuşabiliriz", "devam edebiliriz", "bilmek faydalı olur", "bilmek faydalı",
    ];

    /// <summary>Son cümle bir teklifse ("…verebilirim.") ve öncesinde yeterli içerik varsa atar.</summary>
    internal static string RemoveTrailingOffer(string answer)
    {
        var body = answer.TrimEnd().TrimEnd('.', '!');
        if (body.EndsWith("```", StringComparison.Ordinal))
            return answer; // kod bloğuyla biten cevap
        var start = LastSentenceStart(body);
        var last = body[start..];
        if (!OfferWords.Any(w => last.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return answer;
        var before = answer[..start].TrimEnd();
        return before.Length < 40 ? answer : before;
    }

    [GeneratedRegex(@"\s*\[(?:genel bilgi|genel|kendi bilgim|bilgi)\]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketNote();

    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["eta"] = "η", ["Delta"] = "Δ", ["delta"] = "δ", ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["lambda"] = "λ",
        ["rho"] = "ρ", ["mu"] = "μ", ["sigma"] = "σ", ["theta"] = "θ", ["pi"] = "π", ["omega"] = "ω", ["epsilon"] = "ε",
        ["cdot"] = "·", ["times"] = "×", ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥", ["approx"] = "≈",
        ["neq"] = "≠", ["infty"] = "∞", ["rightarrow"] = "→", ["to"] = "→", ["degree"] = "°", ["circ"] = "°",
    };

    /// <summary>
    /// "$\eta = 1 - T_L / T_H$" → "η = 1 - T_L / T_H". Masaüstü ve Telegram LaTeX çizmiyor; prompt'taki "düz metin yaz"
    /// kuralına model uymadı (Faz 10 canlı denemesi). Para birimi gibi tek "$" içeren metne dokunulmaz.
    /// </summary>
    public static string PlainMath(string answer)
    {
        if (!answer.Contains('$') && !answer.Contains('\\'))
            return answer;
        var text = InlineMath().Replace(answer, m => Latex(m.Groups[1].Value));
        return text;
    }

    private static string Latex(string math)
    {
        var text = Fraction().Replace(math.Replace(@"^\circ", @"\circ"), "$1/$2"); // 30^\circ → 30°
        text = Sqrt().Replace(text, "√$1");
        text = Text().Replace(text, "$1");
        text = Command().Replace(text, m => Symbols.TryGetValue(m.Groups[1].Value, out var s) ? s : m.Groups[1].Value);
        return text.Replace("{", "").Replace("}", "").Trim();
    }

    // $...$ ya da $$...$$ (aynı satırda, içinde ters bölü, alt çizgi, ^ ya da = olan matematik; "5$ ile 10$" değil).
    [GeneratedRegex(@"\${1,2}([^$\n]*?[\\_^=][^$\n]*?)\${1,2}")]
    private static partial Regex InlineMath();

    [GeneratedRegex(@"\\frac\{([^{}]*)\}\{([^{}]*)\}")]
    private static partial Regex Fraction();

    [GeneratedRegex(@"\\sqrt\{([^{}]*)\}")]
    private static partial Regex Sqrt();

    [GeneratedRegex(@"\\(?:text|mathrm|mathbf)\{([^{}]*)\}")]
    private static partial Regex Text();

    [GeneratedRegex(@"\\([a-zA-Z]+)")]
    private static partial Regex Command();
}
