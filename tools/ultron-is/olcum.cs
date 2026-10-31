#:property PublishAot=false
#:package Microsoft.AspNetCore.SignalR.Client@10.0.14
#:project ../../src/Ultron.Contracts/Ultron.Contracts.csproj

// İş arayüzü ölçümü (Faz 16): çalışan Ultron'a (hub) bileşen isteklerini gönderir, her isteği ayrı projede yürütür, son sürümün
// kodunu, süresini ve tarif bilgisini kaydeder. Başta İş moduna geçer (ses modelleri ekran kartından boşalır), ekran kartı
// belleğini önce/sonra ölçer, sonda ana moda döner. Galeri: dotnet run tools/ultron-is/galeri.cs -- --sonuc <json>
// Kullanım (proje kökünden, Ultron açıkken): dotnet run tools/ultron-is/olcum.cs -- tests/ultron-is/bilesen30.json [etiket] [--id b01,b02]
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

Console.OutputEncoding = Encoding.UTF8;
var file = args[0];
var label = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "olcum";
var only = args.SkipWhile(a => a != "--id").Skip(1).FirstOrDefault()?.Split(',').ToHashSet();
var requests = JsonNode.Parse(File.ReadAllText(file))!["istekler"]!.AsArray().Select(n => n!.AsObject())
    .Where(n => only is null || only.Contains(n["id"]!.GetValue<string>())).ToList();

await using var hub = new HubConnectionBuilder().WithUrl("http://127.0.0.1:5199" + ChatHubContract.Path).WithServerTimeout(TimeSpan.FromMinutes(10)).Build();
await hub.StartAsync();

if (args.SkipWhile(a => a != "--duzelt").Skip(1).FirstOrDefault() is { } problemsFile)
{
    // İkinci aşama: masaüstündeki "kontrol sorun bulursa bir kez kendi düzeltmesi" adımının aynısı (galeriden alınan sorunlarla).
    var previous = JsonNode.Parse(File.ReadAllText(args.SkipWhile(a => a != "--sonuc").Skip(1).First()))!.AsObject();
    var problems = JsonNode.Parse(File.ReadAllText(problemsFile))!.AsObject();
    await hub.InvokeAsync<string>(ChatHubContract.SetMode, "is");
    foreach (var node in previous["sonuclar"]!.AsArray())
    {
        var id = node!["id"]!.GetValue<string>();
        if (problems[id] is not JsonArray list || list.Count == 0 || node["surum_id"] is null)
            continue;
        var report = string.Join("\n", list.Select(x => x!.GetValue<string>()));
        var sw = Stopwatch.StartNew();
        StudioVersionDto? fixedVersion = null;
        await foreach (var e in hub.StreamAsync<StudioEventDto>(ChatHubContract.FixStudioComponent, node["proje_id"]!.GetValue<long>(), node["surum_id"]!.GetValue<long>(), report))
            if (e.Type == "completed")
                fixedVersion = e.Version;
        Console.WriteLine($"{id} düzeltme {sw.Elapsed.TotalSeconds:F1} sn → {(fixedVersion is null ? "kod yok" : "v" + fixedVersion.Number)}");
        if (fixedVersion is not null)
        {
            node["ilk_kod"] = node["kod"]?.DeepClone();
            node["kod"] = fixedVersion.Code;
            node["surum"] = fixedVersion.Number;
            node["surum_id"] = fixedVersion.Id;
            node["kaynak"] = fixedVersion.Source;
            node["duzeltme_sorunlari"] = report;
        }
    }
    await hub.InvokeAsync<string>(ChatHubContract.SetMode, "ana");
    var fixedPath = Path.Combine("tests", "ultron-is", "sonuclar", $"{DateTime.Now:yyyyMMdd-HHmm}-{label}-duzeltilmis.json");
    File.WriteAllText(fixedPath, previous.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine("Düzeltilmiş sonuç: " + fixedPath);
    return;
}

var vramBefore = Vram();
Console.WriteLine("Mod: " + await hub.InvokeAsync<string>(ChatHubContract.SetMode, "is"));
await Task.Delay(TimeSpan.FromSeconds(8));
var vramStudio = Vram();
Console.WriteLine($"Ekran kartı belleği: ana mod {vramBefore} MB → İş modu {vramStudio} MB");

var results = new JsonArray();
var total = Stopwatch.StartNew();
foreach (var r in requests)
{
    var id = r["id"]!.GetValue<string>();
    var messages = r["mesajlar"]!.AsArray().Select(m => m!.GetValue<string>()).ToList();
    var project = await hub.InvokeAsync<StudioProjectDto>(ChatHubContract.CreateStudioProject, $"Ölçüm {label} {id}");
    var sw = Stopwatch.StartNew();
    StudioVersionDto? last = null;
    string recipes = "", error = "";
    bool? known = null;
    var firstChunkMs = new List<long>();
    foreach (var text in messages)
    {
        var turn = Stopwatch.StartNew();
        var gotChunk = false;
        try
        {
            await foreach (var e in hub.StreamAsync<StudioEventDto>(ChatHubContract.SendStudioMessage, project.Id, text, (byte[]?)null))
            {
                switch (e.Type)
                {
                    case "started" when known is null:
                        known = e.KnownPattern;
                        recipes = string.Join(" | ", e.Recipes ?? []);
                        break;
                    case "chunk" when !gotChunk:
                        gotChunk = true;
                        firstChunkMs.Add(turn.ElapsedMilliseconds);
                        break;
                    case "completed":
                        last = e.Version;
                        break;
                    case "failed":
                        error = e.Text ?? "hata";
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
    }
    var seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
    Console.WriteLine($"{id} {seconds,6} sn  bilinen:{known}  tarif:{(recipes.Length == 0 ? "-" : recipes)}  {(last is null ? "KOD YOK " + error : $"v{last.Number} {last.Code.Length} karakter")}");
    results.Add(new JsonObject
    {
        ["id"] = id, ["tur"] = r["tur"]?.GetValue<string>(), ["istek"] = string.Join(" → ", messages), ["sure_sn"] = seconds,
        ["ilk_parca_ms"] = firstChunkMs.Count == 0 ? null : (long)firstChunkMs.Average(), ["bilinen"] = known, ["tarifler"] = recipes,
        ["proje_id"] = project.Id, ["surum_id"] = last?.Id, ["kaynak"] = last?.Source, ["surum"] = last?.Number, ["kod"] = last?.Code, ["aciklama"] = last?.Explanation, ["hata"] = error.Length == 0 ? null : error,
    });
}

Console.WriteLine("Mod: " + await hub.InvokeAsync<string>(ChatHubContract.SetMode, "ana"));
var outDir = Path.Combine("tests", "ultron-is", "sonuclar");
Directory.CreateDirectory(outDir);
var path = Path.Combine(outDir, $"{DateTime.Now:yyyyMMdd-HHmm}-{label}.json");
File.WriteAllText(path, new JsonObject
{
    ["etiket"] = label, ["tarih"] = DateTime.Now.ToString("O"), ["vram_ana_mb"] = vramBefore, ["vram_is_mb"] = vramStudio,
    ["toplam_dk"] = Math.Round(total.Elapsed.TotalMinutes, 1), ["sonuclar"] = results,
}.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"Toplam {total.Elapsed.TotalMinutes:F1} dk · kod üretilen {results.Count(x => x!["kod"] is not null)}/{results.Count} · sonuç: {path}");

static int Vram()
{
    try
    {
        var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return int.TryParse(text.Trim(), out var mb) ? mb : -1;
    }
    catch
    {
        return -1;
    }
}
