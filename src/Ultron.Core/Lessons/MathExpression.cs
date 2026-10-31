using System.Globalization;

namespace Ultron.Core.Lessons;

/// <summary>
/// Ders hesaplarını doğrulamak için küçük ifade hesaplayıcı (Faz 17): + − × ÷ ^, parantez, sqrt, ln, log, exp, sin, cos, tan
/// (derece), abs, pi, e. Türkçe ondalık virgül kabul edilir ("0,622"). Modelin aritmetiği zayıf (gece sınavı: izantropik
/// soruda 674 K, 617 K): adımları model yazar, sayıyı bu sınıf hesaplar.
/// </summary>
public static class MathExpression
{
    public static bool TryEvaluate(string expression, out double value) => TryEvaluate(expression, null, out value);

    /// <param name="variables">Önceki adımların sonuçları ("t2" → 543,4); adlar büyük/küçük harf duyarsız.</param>
    public static bool TryEvaluate(string expression, IReadOnlyDictionary<string, double>? variables, out double value)
    {
        value = double.NaN;
        try
        {
            var parser = new Parser(Normalize(expression), variables);
            value = parser.Parse();
            return double.IsFinite(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Okunur sonuç: 4 anlamlı basamak, Türkçe ondalık virgül ("543,4").</summary>
    public static string Format(double value) =>
        Math.Abs(value) >= 1e6 || (Math.Abs(value) < 1e-3 && value != 0)
            ? value.ToString("0.###E+0", CultureInfo.GetCultureInfo("tr-TR"))
            : Math.Round(value, Math.Abs(value) >= 100 ? 1 : Math.Abs(value) >= 1 ? 3 : 4).ToString("0.####", CultureInfo.GetCultureInfo("tr-TR"));

    private static string Normalize(string s)
    {
        s = s.Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace('−', '-').Replace("**", "^").Replace('π', 'p');
        // "0,622" → "0.622"; virgül sadece iki rakam arasındaysa ondalıktır (fonksiyon argümanı ayıracı kullanılmıyor).
        var chars = s.ToCharArray();
        for (var i = 1; i < chars.Length - 1; i++)
            if (chars[i] == ',' && char.IsDigit(chars[i - 1]) && char.IsDigit(chars[i + 1]))
                chars[i] = '.';
        return new string(chars).Replace(" ", "").ToLowerInvariant();
    }

    /// <summary>"T_2", "m_dot", "P1" gibi değişken adı mı?</summary>
    public static bool IsName(string s) =>
        s.Length is > 0 and <= 20 && char.IsLetter(s[0]) && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    private sealed class Parser(string text, IReadOnlyDictionary<string, double>? variables)
    {
        private int _pos;

        public double Parse()
        {
            var v = Sum();
            if (_pos != text.Length)
                throw new FormatException("Beklenmeyen: " + text[_pos..]);
            return v;
        }

        private double Sum()
        {
            var v = Product();
            while (_pos < text.Length && text[_pos] is '+' or '-')
                v = text[_pos++] == '+' ? v + Product() : v - Product();
            return v;
        }

        private double Product()
        {
            var v = Power();
            while (_pos < text.Length && (text[_pos] is '*' or '/' || char.IsLetter(text[_pos]) || text[_pos] == '('))
            {
                if (text[_pos] is '*' or '/')
                {
                    var op = text[_pos++];
                    var r = Power();
                    v = op == '*' ? v * r : v / r;
                }
                else
                    v *= Power(); // örtük çarpım: 2pi, 3(4+1)
            }
            return v;
        }

        // Eksi işareti üsten sonra gelir: -3^2 = -(3^2) = -9; üs kendisi eksi olabilir: 2^-1 = 0,5.
        private double Power()
        {
            if (_pos < text.Length && text[_pos] is '-' or '+')
                return text[_pos++] == '-' ? -Power() : Power();
            var b = Atom();
            if (_pos < text.Length && text[_pos] == '^')
            {
                _pos++;
                return Math.Pow(b, Power()); // sağdan birleşir: 2^3^2 = 2^9
            }
            return b;
        }

        private double Atom()
        {
            if (_pos >= text.Length)
                throw new FormatException("İfade eksik");
            if (text[_pos] == '(')
            {
                _pos++;
                var v = Sum();
                Expect(')');
                return v;
            }
            if (char.IsDigit(text[_pos]) || text[_pos] == '.')
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.'))
                    _pos++;
                if (_pos < text.Length && text[_pos] == 'e' && _pos + 1 < text.Length && (char.IsDigit(text[_pos + 1]) || text[_pos + 1] is '-' or '+'))
                {
                    _pos += 2;
                    while (_pos < text.Length && char.IsDigit(text[_pos]))
                        _pos++;
                }
                return double.Parse(text[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            var nameStart = _pos;
            while (_pos < text.Length && (char.IsLetter(text[_pos]) || (_pos > nameStart && (char.IsDigit(text[_pos]) || text[_pos] == '_'))))
                _pos++;
            var name = text[nameStart.._pos];
            if (name.Length == 0)
                throw new FormatException("Beklenmeyen: " + text[_pos..]);
            if (variables is not null && (_pos >= text.Length || text[_pos] != '('))
                foreach (var (key, val) in variables)
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                        return val;
            switch (name)
            {
                case "p" or "pi":
                    return Math.PI;
                case "e":
                    return Math.E;
            }
            Expect('(');
            var arg = Sum();
            Expect(')');
            return name switch
            {
                "sqrt" or "karekok" => Math.Sqrt(arg),
                "ln" => Math.Log(arg),
                "log" => Math.Log10(arg),
                "exp" => Math.Exp(arg),
                "abs" => Math.Abs(arg),
                "sin" => Math.Sin(arg * Math.PI / 180),
                "cos" => Math.Cos(arg * Math.PI / 180),
                "tan" => Math.Tan(arg * Math.PI / 180),
                _ => throw new FormatException("Bilinmeyen fonksiyon: " + name),
            };
        }

        private void Expect(char c)
        {
            if (_pos >= text.Length || text[_pos] != c)
                throw new FormatException($"'{c}' bekleniyordu");
            _pos++;
        }
    }
}
