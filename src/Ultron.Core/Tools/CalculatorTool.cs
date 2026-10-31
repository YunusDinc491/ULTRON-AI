using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ultron.Core.Tools;

/// <summary>
/// Dört işlem hesap makinesi. Küçük modeller büyük sayılarda hata yapıyor; hesabı kod yapar.
/// Sadece rakam, işaret ve parantez kabul edilir: ifade kod olarak çalıştırılamaz.
/// </summary>
public sealed partial class CalculatorTool : ITool
{
    public ToolDefinition Definition { get; } = new(
        "hesapla",
        "Matematik işlemi hesaplar (+ - * / ve parantez; ondalık için nokta). Yüzde için önce çarpmaya çevir: '%18 KDV 250' → 250*0.18.",
        """{"type":"object","properties":{"ifade":{"type":"string","description":"Ör. (1250*12)/7"}},"required":["ifade"]}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var expression = ToolArgs.String(arguments, "ifade")?.Replace('×', '*').Replace('÷', '/').Replace('x', '*');
        if (expression is null || !Allowed().IsMatch(expression))
            return Task.FromResult(ToolResult.Fail("İfadede sadece sayı, + - * / ve parantez olabilir."));
        // Türkçe ondalık virgülü: "2,5" → 2.5 (binlik ayırıcı noktası kullanılmasın diye modelden nokta istiyoruz).
        expression = DecimalComma().Replace(expression, "$1.$2");
        try
        {
            var value = Convert.ToDouble(new DataTable().Compute(expression, null), CultureInfo.InvariantCulture);
            if (double.IsInfinity(value) || double.IsNaN(value))
                return Task.FromResult(ToolResult.Fail("Sonuç tanımsız (sıfıra bölme olabilir)."));
            return Task.FromResult(ToolResult.Success($"{expression} = {Math.Round(value, 6).ToString("#,0.######", new CultureInfo("tr-TR"))}"));
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or InvalidCastException or OverflowException or DivideByZeroException)
        {
            return Task.FromResult(ToolResult.Fail("İfade hesaplanamadı: " + ex.Message));
        }
    }

    [GeneratedRegex(@"^[0-9+\-*/().,\s]{1,200}$")]
    private static partial Regex Allowed();

    [GeneratedRegex(@"(\d),(\d)")]
    private static partial Regex DecimalComma();
}
