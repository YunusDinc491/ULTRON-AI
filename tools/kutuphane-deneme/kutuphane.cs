#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj
#:package Microsoft.Extensions.Configuration.Json@10.0.0
#:package Microsoft.Extensions.Logging.Console@10.0.0

// Kütüphane uçtan uca deneme (geçici veritabanı ve klasör): bir kaynağı indir, işle, ara; süreleri ölç.
// Kullanım (proje kökünden, Ollama açıkken): dotnet run tools/kutuphane-deneme/kutuphane.cs -- mdn files/en-us/web/javascript
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ultron.Core.Library;
using Ultron.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;
var root = Path.Combine(Path.GetTempPath(), "ultron-kutuphane-deneme");
var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "src", "Ultron.Host", "appsettings.json"), optional: true)
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = Path.Combine(root, "deneme.db"), ["Library:Folder"] = root }).Build();
await using var sp = new ServiceCollection().AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning)).AddUltronInfrastructure(config).BuildServiceProvider();
await sp.InitializeDatabaseAsync();
var library = sp.GetRequiredService<LibraryService>();
// Kullanım: <kaynak> [klasör…] [--sorular "soru 1" "soru 2" …]
var split = Array.IndexOf(args, "--sorular");
var paths = (split < 0 ? args[1..] : args[1..split]);
var questions = split < 0
    ? ["map ile forEach arasındaki fark nedir", "Promise.all bir tanesi hata verirse ne olur", "closure nedir örnekle", "async await ile hata yakalama"]
    : args[(split + 1)..];
var source = LibrarySources.Default.Single(s => s.Id == args[0]);
if (paths.Length > 0)
    source = source with { Paths = paths };
library.Sources = [source];

var sw = Stopwatch.StartNew();
using var progress = new Timer(_ => Console.WriteLine($"  … {library.Progress} ({sw.Elapsed:mm\\:ss})"), null, 30_000, 30_000);
Console.WriteLine(await library.SyncAllAsync());
Console.WriteLine($"Süre: {sw.Elapsed:mm\\:ss} · {await library.DescribeAsync()}");

foreach (var q in questions)
{
    sw.Restart();
    var hits = await library.SearchAsync(q, 3);
    Console.WriteLine($"\n? {q}  [{sw.ElapsedMilliseconds} ms]");
    foreach (var h in hits)
        Console.WriteLine($"  - {h.Page.Title} · {h.Page.Url}\n    {h.Chunk.Text.Split('\n').Skip(1).FirstOrDefault()?[..Math.Min(140, h.Chunk.Text.Split('\n').Skip(1).FirstOrDefault()?.Length ?? 0)]}");
}
