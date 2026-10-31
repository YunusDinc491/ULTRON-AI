using System.Text.RegularExpressions;
using Markdig;

namespace Ultron.Desktop.Services;

/// <summary>
/// Modelin Markdown cevabını (kalın, liste, kod) HTML'e çevirir.
/// Ham HTML kapalı: cevap web kaynaklarından beslendiği için içine sızmış bir &lt;script&gt; çalışmasın.
/// </summary>
public static partial class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    // Akış sürerken metnin sonuna eklenen işaret: Markdown onu son paragrafın İÇİNDE bırakır,
    // böylece yanıp sönen imleç yeni satıra düşmeden yazının hemen ardında durur.
    private const string CaretMarker = "ULTRONCARET";

    public static string ToHtml(string markdown, bool withCaret = false)
    {
        var html = Markdown.ToHtml(withCaret ? markdown + CaretMarker : markdown, Pipeline);
        html = html.Replace(CaretMarker, "<span class=\"caret\"></span>");
        // Kaynak atıfları: "[1]" → küçük kırmızı numara rozeti.
        return Citation().Replace(html, m => $"<sup class=\"cite\">{m.Groups[1].Value}</sup>");
    }

    [GeneratedRegex(@"\[(\d{1,2})\]")]
    private static partial Regex Citation();
}
