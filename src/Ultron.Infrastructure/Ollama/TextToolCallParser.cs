using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ultron.Core.Tools;

namespace Ultron.Infrastructure.Ollama;

/// <summary>
/// gemma4, sistem mesajı uzayınca araç çağrısını bazen Ollama'nın tool_calls alanı yerine düz metin olarak yazıyor:
/// <c>hatirlatici_kur(metin="Annemi ara", zaman="2026-10-28T09:00")</c> (canlı denemede 8/8). Bu sınıf o metni gerçek
/// araç çağrısına çevirir. Sadece o turda sunulan araç adları kabul edilir; başka her metin olduğu gibi cevap sayılır.
/// </summary>
internal static partial class TextToolCallParser
{
    public static IReadOnlyList<ToolCall> Parse(string content, IReadOnlyList<ToolDefinition> offered)
    {
        if (string.IsNullOrWhiteSpace(content) || offered.Count == 0)
            return [];
        var names = offered.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var calls = new List<ToolCall>();
        foreach (Match m in Call().Matches(content))
        {
            var name = m.Groups["name"].Value;
            if (!names.Contains(name))
                continue;
            var args = new JsonObject();
            foreach (Match a in Argument().Matches(m.Groups["args"].Value))
                args[a.Groups["key"].Value] = Value(a.Groups["value"].Value);
            calls.Add(new ToolCall(name, JsonDocument.Parse(args.ToJsonString()).RootElement.Clone()));
        }
        return calls;
    }

    private static JsonNode? Value(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] is '"' or '\'' && raw[^1] == raw[0])
            return JsonValue.Create(Regex.Unescape(raw[1..^1]));
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return JsonValue.Create(n);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return JsonValue.Create(d);
        return raw is "true" or "false" ? JsonValue.Create(raw == "true") : JsonValue.Create(raw);
    }

    // araç_adı( ... ) — parantez içinde tırnaklı metin parantez içerebilir.
    [GeneratedRegex("""(?<name>[a-z][a-z0-9_]*)\((?<args>(?:"(?:[^"\\]|\\.)*"|'[^']*'|[^()"'])*)\)""")]
    private static partial Regex Call();

    [GeneratedRegex("""(?<key>[a-zA-Z_][a-zA-Z0-9_]*)\s*[=:]\s*(?<value>"(?:[^"\\]|\\.)*"|'[^']*'|[^,\s)]+)""")]
    private static partial Regex Argument();
}
