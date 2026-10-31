using System.Globalization;
using System.Text.Json;

namespace Ultron.Core.Tools;

/// <summary>Model parametreleri bazen nesne, bazen JSON metni olarak verir; sayıları da bazen metin yazar. Hepsini tolere eder.</summary>
public static class ToolArgs
{
    public static JsonElement Normalize(JsonElement args) =>
        args.ValueKind == JsonValueKind.String && args.GetString() is { } s && s.TrimStart().StartsWith('{')
            ? JsonDocument.Parse(s).RootElement.Clone()
            : args;

    public static string? String(JsonElement args, string name)
    {
        args = Normalize(args);
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()?.Trim() is { Length: > 0 } text ? text : null,
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    public static long? Long(JsonElement args, string name) =>
        String(args, name) is { } s && long.TryParse(s.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
