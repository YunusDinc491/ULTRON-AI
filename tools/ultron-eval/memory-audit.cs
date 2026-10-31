// Ultron · Hafıza denetimi (SADECE OKUR, hiçbir kaydı değiştirmez): çalışan Host'tan aktif hafıza kayıtlarını alır;
// tekrarları, kişisel olmayan genel bilgi kayıtlarını, bozuk/çok kısa kayıtları ve olası çelişkileri raporlar.
// Rapor kişisel bilgi içerdiği için repoya değil %USERPROFILE%\Ultron\raporlar\ altına yazılır.
// Çalıştırma (Ultron açıkken): dotnet run tools/ultron-eval/memory-audit.cs
#:property PublishAot=false
#:package Microsoft.AspNetCore.SignalR.Client@10.0.14
#:project ../../src/Ultron.Core/Ultron.Core.csproj

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Core.Text;

Console.OutputEncoding = Encoding.UTF8;
var conn = new HubConnectionBuilder().WithUrl("http://127.0.0.1:5199/hub/chat").Build();
await conn.StartAsync();
var list = await conn.InvokeAsync<JsonElement>("ListMemories");
var items = list.GetProperty("active").EnumerateArray()
    .Select(m => (Id: m.GetProperty("id").GetInt64(), Category: m.GetProperty("category").GetString()!, Content: m.GetProperty("content").GetString()!))
    .ToList();

static HashSet<string> Tokens(string s) =>
    TurkishText.Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).Select(w => w.Length > 5 ? w[..5] : w).ToHashSet();

static double Jaccard(HashSet<string> a, HashSet<string> b) => a.Count + b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / a.Union(b).Count();

var tokens = items.ToDictionary(i => i.Id, i => Tokens(i.Content));
var duplicates = new List<string>();
var conflicts = new List<string>();
for (var x = 0; x < items.Count; x++)
    for (var y = x + 1; y < items.Count; y++)
    {
        var (a, b) = (items[x], items[y]);
        var sim = Jaccard(tokens[a.Id], tokens[b.Id]);
        var numsA = Regex.Matches(a.Content, @"\d+").Select(m => m.Value).ToHashSet();
        var numsB = Regex.Matches(b.Content, @"\d+").Select(m => m.Value).ToHashSet();
        if (sim >= 0.75)
            duplicates.Add($"#{a.Id} ↔ #{b.Id} (benzerlik {sim:P0}): \"{a.Content}\" / \"{b.Content}\"");
        else if (sim >= 0.45 && numsA.Count > 0 && numsB.Count > 0 && !numsA.SetEquals(numsB))
            conflicts.Add($"#{a.Id} ↔ #{b.Id} (aynı konu, farklı sayı): \"{a.Content}\" / \"{b.Content}\"");
    }

// Kişisel olmayan genel bilgi: kullanıcıya dair hiçbir işaret yok ve tanım cümlesi kalıbı ("X, … ölçüsüdür / -dır").
string[] personal = ["sev", "var", "yok", "ister", "oku", "calis", "yasa", "kardes", "anne", "baba", "sevgili", "arkadas", "ders", "hoca", "alerj", "kendi", "onun", "si ", "i "];
var general = items.Where(i => i.Category == "GenelBilgi" || Regex.IsMatch(TurkishText.Normalize(i.Content), @"(olcusudur|demektir|ifade eder|tanimlanir|anlamina gelir)"))
    .Where(i => !personal.Any(p => (" " + TurkishText.Normalize(i.Content) + " ").Contains(p, StringComparison.Ordinal)) || i.Category == "GenelBilgi")
    .ToList();
var broken = items.Where(i => i.Content.Trim().Length < 8 || !Regex.IsMatch(i.Content, @"\p{L}{3,}") || i.Content.Contains("Kullanıcı", StringComparison.Ordinal)).ToList();

var sb = new StringBuilder($"# Hafıza denetimi · {DateTime.Now:yyyy-MM-dd HH:mm}\n\nSadece okundu, hiçbir kayıt değiştirilmedi. Aktif kayıt: {items.Count}.\n\n");
sb.AppendLine("## Kategori dağılımı\n");
foreach (var g in items.GroupBy(i => i.Category).OrderByDescending(g => g.Count()))
    sb.AppendLine($"- {g.Key}: {g.Count()}");
void Section(string title, IEnumerable<string> lines, string hint)
{
    var l = lines.ToList();
    sb.AppendLine($"\n## {title} ({l.Count})\n\n_{hint}_\n");
    foreach (var line in l)
        sb.AppendLine("- " + line);
    if (l.Count == 0)
        sb.AppendLine("- Yok.");
}
Section("Muhtemel tekrarlar", duplicates, "Aynı bilginin iki kaydı. /hafiza panelinden birini silmek yeterli.");
Section("Muhtemel çelişkiler", conflicts, "Aynı konu, farklı değer (ör. eski ve yeni bilgi). Hangisinin doğru olduğuna sen karar ver.");
Section("Kişisel olmayan genel bilgi", general.Select(i => $"#{i.Id} [{i.Category}] \"{i.Content}\""), "Senin hakkında değil, ansiklopedik bilgi gibi görünüyor (ör. soru sorduğunda yanlışlıkla kaydedilmiş). Bu hata 27 Ekim'de düzeltildi.");
Section("Bozuk / çok kısa / eski biçim", broken.Select(i => $"#{i.Id} [{i.Category}] \"{i.Content}\""), "Çok kısa, anlamsız ya da eski 'Kullanıcı … belirtiyor' biçimi.");

var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "raporlar");
Directory.CreateDirectory(dir);
var path = Path.Combine(dir, $"hafiza-denetimi-{DateTime.Now:yyyyMMdd-HHmm}.md");
File.WriteAllText(path, sb.ToString());
Console.WriteLine($"Aktif kayıt {items.Count} · tekrar {duplicates.Count} · çelişki {conflicts.Count} · genel bilgi {general.Count} · bozuk {broken.Count}");
Console.WriteLine("Rapor: " + path);
