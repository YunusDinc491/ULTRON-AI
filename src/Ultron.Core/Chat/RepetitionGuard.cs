namespace Ultron.Core.Chat;

/// <summary>
/// Küçük model bazen döngüye girip aynı parçayı yüzlerce kez yazıyor (gece sınavı t26: "[1] [1] [1] …" 2048 token boyunca).
/// Akış sırasında metnin sonu kısa bir parçanın art arda tekrarından oluşuyorsa akış kesilir ve tekrarlar atılır.
/// </summary>
public sealed class RepetitionGuard
{
    private const int MinRepeats = 8;
    private const int MaxUnit = 40;
    private readonly System.Text.StringBuilder _text = new();

    public bool Tripped { get; private set; }

    /// <summary>Parçayı ekler; döngü yakalandıysa true döner (akış durdurulmalı).</summary>
    public bool Append(string part)
    {
        _text.Append(part);
        if (_text.Length < MinRepeats * 2)
            return false;
        var tail = _text.ToString(Math.Max(0, _text.Length - MinRepeats * MaxUnit), Math.Min(_text.Length, MinRepeats * MaxUnit));
        Tripped = UnitAtEnd(tail) is not null;
        return Tripped;
    }

    /// <summary>Döngüye girmiş metni ilk tekrara kadar kısaltır.</summary>
    public static string Trim(string text)
    {
        if (UnitAtEnd(text) is not { } unit)
            return text;
        var end = text.Length;
        while (end >= unit.Length && string.CompareOrdinal(text, end - unit.Length, unit, 0, unit.Length) == 0)
            end -= unit.Length;
        return text[..end].TrimEnd() + " …";
    }

    /// <summary>Metnin sonu en az <see cref="MinRepeats"/> kez art arda tekrar eden bir parçaysa o parça.</summary>
    internal static string? UnitAtEnd(string text)
    {
        for (var len = 1; len <= MaxUnit && len * MinRepeats <= text.Length; len++)
        {
            var unit = text[^len..];
            if (string.IsNullOrWhiteSpace(unit))
                continue;
            var repeats = 1;
            for (var pos = text.Length - 2 * len; pos >= 0 && string.CompareOrdinal(text, pos, unit, 0, len) == 0; pos -= len)
                repeats++;
            // "==========" gibi tek karakterli çizgiler yanlış alarm vermesin: onlarda en az 40 karakter.
            var line = unit.All(c => c == unit[0]);
            if (line ? repeats * len >= 40 : repeats >= MinRepeats)
                return unit;
        }
        return null;
    }
}
