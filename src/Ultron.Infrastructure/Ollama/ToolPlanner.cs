using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ultron.Core.Chat;
using Ultron.Core.Tools;

namespace Ultron.Infrastructure.Ollama;

/// <summary>
/// Araç kararını JSON şemasıyla aldırır: {"islemler": [{"arac": "&lt;araç adı&gt;", "parametreler": {...}}, …], "soru": "..."}.
/// Tek mesajda birden çok istek her zaman olur (2026-10-27): hepsi aynı turda listelenir; birbirine bağlı olanlar devam turunda.
/// gemma4'ün yerel araç çağırması uzun konuşmada oynaktı (aracı atlıyor, gereksiz soru soruyor, çağrıyı düz metin yazıyordu);
/// şemaya zorlanan karar aynı cümlelerde 9/9 doğru ve daha hızlı. "arac" alanı sunulan araç adlarıyla sınırlı (enum).
/// </summary>
internal static class ToolPlanner
{
    public const string None = "yok";

    private const string Instructions = """

        Görevin: kullanıcının SON mesajındaki BÜTÜN istekleri yerine getirecek araç işlemlerini "islemler" listesine sırayla yazmak.
        - Mesajda birden çok istek varsa HER biri için ayrı işlem yaz; aynı araç birden çok kez olabilir.
          Örnek: "yarın 9'da ilacımı, 18'de de spor yapmayı hatırlat, bir de hava nasıl?" → hatirlatici_kur, hatirlatici_kur, hava_durumu.
        - Bir isteğin parametresi başka bir işlemin SONUCUNA bağlıysa (ör. "hatırlatmalarımı listele, sonra yarınkini iptal et": iptal için
          listedeki numara gerekir) sadece önce yapılması gerekeni yaz; sonuç gelince devam turunda kalanını yaparsın.
        - Mesajda olmayan bilgiyi sorma; isteğe bağlı parametreleri boş bırak. Bir şey "zaten yapılmış" gibi davranma: istenen işi yap.
        - Sadece ZORUNLU bir bilgi gerçekten eksikse ya da hiçbir araç uymuyorsa "islemler" boş olsun ve "soru" alanına tek kısa, samimi soru
          (ya da araç uymuyorsa kısa bir cevap) yaz. Bazı istekler yapılabiliyorsa onları yap, eksik olanı soru alanında sor.
        - Önceki mesajlarda Ultron bir şey sorduysa (ör. hatırlatmanın önemi), kullanıcının cevabını o isteğin parametresi olarak kullan.
        - Kullanıcı yeni bir işlem istemiyor, sadece bilgi veriyor ya da sohbet ediyorsa (ör. "dersler ana kampüste", "notlarımı kağıda alırım")
          "islemler" boş olsun ve "soru" alanını BOŞ bırak. Daha önce yapılmış bir işlemi (aynı dersi, aynı hatırlatmayı) TEKRARLAMA.
        Araçlar:
        """;

    public static IReadOnlyList<ChatMessage> BuildMessages(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools)
    {
        var system = messages.FirstOrDefault(m => m.Role == Role.System)?.Content ?? "";
        var prompt = new StringBuilder(system).Append(Instructions);
        foreach (var t in tools)
            prompt.Append("\n- ").Append(t.Name).Append(": ").Append(t.Description).Append(' ').Append(DescribeParameters(t.ParametersSchema));

        // Geçmiş düz metne çevrilir: araç çağrısı/sonucu mesajları şemalı çağrıda sorun çıkarmasın.
        var result = new List<ChatMessage> { new(Role.System, prompt.ToString()) };
        foreach (var m in messages.Where(m => m.Role != Role.System))
            result.Add(m switch
            {
                { Role: Role.Tool } => new ChatMessage(Role.User, $"[{m.ToolName} aracının sonucu] {m.Content}"),
                { ToolCalls.Count: > 0 } => new ChatMessage(Role.Assistant, "[araç çağrıldı: " + string.Join(", ", m.ToolCalls!.Select(c => $"{c.Name}({c.Arguments.GetRawText()})")) + "]"),
                _ => new ChatMessage(m.Role, m.Content),
            });
        return result;
    }

    /// <summary>Tek mesajda en fazla bu kadar işlem (modelin döngüye girip aynı şeyi listelemesine karşı).</summary>
    public const int MaxCalls = 8;

    public static string Schema(IReadOnlyList<ToolDefinition> tools) => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["islemler"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["arac"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. tools.Select(t => (JsonNode)t.Name)]) },
                        ["parametreler"] = new JsonObject { ["type"] = "object" },
                    },
                    ["required"] = new JsonArray("arac", "parametreler"),
                },
            },
            ["soru"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("islemler"),
    }.ToJsonString();

    /// <summary>Yeni biçim {"islemler":[{arac, parametreler}…], "soru"}; eski tek işlemli biçim ({"arac", "parametreler"}) de okunur.</summary>
    public static ToolTurn Parse(string json, IReadOnlyList<ToolDefinition> tools)
    {
        var root = JsonDocument.Parse(json).RootElement;
        var question = root.TryGetProperty("soru", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString()?.Trim() ?? "" : "";
        var items = root.TryGetProperty("islemler", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [root];

        var calls = new List<ToolCall>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("arac", out var a) || a.GetString() is not { } name
                || name == None || tools.All(t => t.Name != name))
                continue;
            var args = item.TryGetProperty("parametreler", out var p) && p.ValueKind == JsonValueKind.Object ? CleanKeys(p) : JsonDocument.Parse("{}").RootElement.Clone();
            if (calls.Any(c => c.Name == name && c.Arguments.GetRawText() == args.GetRawText()))
                continue; // aynı işlem iki kez yazıldıysa bir kez yapılır
            calls.Add(new ToolCall(name, args));
            if (calls.Count == MaxCalls)
                break;
        }
        // İşlem varsa soru (kalan eksik bilgi) cevapta kullanılmak üzere içerikte taşınır.
        return calls.Count == 0 ? new ToolTurn(question, []) : new ToolTurn(question, calls);
    }

    /// <summary>"Parametreler: gun (zorunlu; pazartesi|sali…), yer (isteğe bağlı; Derslik)…" — modelin okuyacağı kısa özet.</summary>
    private static string DescribeParameters(string schema)
    {
        var root = JsonNode.Parse(schema)!.AsObject();
        if (root["properties"] is not JsonObject props || props.Count == 0)
            return "Parametre yok.";
        var required = root["required"]?.AsArray().Select(x => x!.GetValue<string>()).ToHashSet() ?? [];
        var parts = props.Select(kv =>
        {
            var p = kv.Value!.AsObject();
            var detail = p["enum"] is JsonArray values ? string.Join("|", values.Select(v => v!.GetValue<string>()))
                : p["description"]?.GetValue<string>() ?? p["type"]?.GetValue<string>() ?? "";
            return $"{kv.Key} ({(required.Contains(kv.Key) ? "zorunlu" : "isteğe bağlı")}; {detail})";
        });
        return "Parametreler: " + string.Join(", ", parts) + ".";
    }

    /// <summary>Model bazen anahtarlara "?" ya da Türkçe harf ekliyor ("önem?" → "onem").</summary>
    private static JsonElement CleanKeys(JsonElement p)
    {
        var obj = new JsonObject();
        foreach (var prop in p.EnumerateObject())
        {
            var key = new string(prop.Name.Trim().TrimEnd('?').Select(c => c switch
            {
                'ı' => 'i', 'ş' => 's', 'ç' => 'c', 'ğ' => 'g', 'ö' => 'o', 'ü' => 'u', _ => c,
            }).ToArray());
            obj[key] = JsonNode.Parse(prop.Value.GetRawText());
        }
        return JsonDocument.Parse(obj.ToJsonString()).RootElement.Clone();
    }
}
