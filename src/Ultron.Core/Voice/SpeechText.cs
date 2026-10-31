using System.Text.RegularExpressions;

namespace Ultron.Core.Voice;

/// <summary>
/// Ekrandaki metin ile sesli okunacak metin aynı değil: markdown işaretleri, bağlantılar ve kaynak numaraları
/// sesli okununca saçma durur. Ayrıca Whisper sessiz/gürültülü kayıtlarda bilinen kalıp cümleler uydurur.
/// </summary>
public static partial class SpeechText
{
    /// <summary>Markdown metni seslendirmeye hazırlar. Okunacak bir şey kalmazsa boş döner.</summary>
    public static string ForSpeech(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var s = CodeBlock().Replace(Chat.AnswerCleanup.PlainMath(markdown), " Kod ekranda. "); // LaTeX formül → düz metin
        s = Link().Replace(s, "$1");                 // [metin](adres) → metin
        s = Url().Replace(s, "");                    // çıplak adresler okunmaz
        s = SourceRef().Replace(s, "");              // [1], [2] kaynak numaraları
        s = LinePrefix().Replace(s, "");             // başlık, liste, alıntı işaretleri
        s = s.Replace("|", " ").Replace("`", "").Replace("*", "").Replace("_", " ").Replace("~", "");
        s = Spaces().Replace(s, " ").Trim();
        return s.Any(char.IsLetterOrDigit) ? s : "";
    }

    private static readonly System.Globalization.CultureInfo Turkish = new("tr-TR");

    // Whisper'ın Türkçe sessizlikte ürettiği tipik "altyazı" uydurmaları (eğitim verisindeki altyazı imzalarından).
    private static readonly string[] Hallucinations =
    [
        "altyazı", "abone ol", "izlediğiniz için teşekkür", "videoyu beğen", "bir sonraki videoda görüşmek",
    ];

    /// <summary>Yazıya çevrilen metin gerçek konuşma değil de bilinen bir uydurma mı?</summary>
    public static bool IsLikelyHallucination(string transcript)
    {
        // ToLowerInvariant "İ"yi "i̇" (iki karakter) yapar; Türkçe kültürle küçült.
        var t = transcript.Trim().ToLower(Turkish);
        if (t.Length == 0)
            return true;
        if (!t.Any(char.IsLetter))
            return true; // "...", "♪" gibi
        return t.Length < 60 && Hallucinations.Any(h => t.Contains(h));
    }

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex SourceRef();

    [GeneratedRegex(@"^\s*(#{1,6}\s+|[-*+]\s+|>\s*|\d+[.)]\s+)", RegexOptions.Multiline)]
    private static partial Regex LinePrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
