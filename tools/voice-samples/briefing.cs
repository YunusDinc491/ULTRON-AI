// Ultron · Sabah brifingi denemesi: geçici veritabanında örnek ders + hatırlatma ile bugünün brifingini gerçek modelle yazdırır.
// Çalıştırma (proje kökünden, Ollama açıkken): dotnet run tools/voice-samples/briefing.cs
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ultron.Core.Briefing;
using Ultron.Core.Calendar;
using Ultron.Core.Reminders;
using Ultron.Infrastructure;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var db = Path.Combine(Path.GetTempPath(), $"ultron-brifing-{Guid.NewGuid():N}.db");
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = db }).Build();
var services = new ServiceCollection().AddLogging().AddUltronInfrastructure(config).BuildServiceProvider();
await services.InitializeDatabaseAsync();

var now = DateTime.Now;
await services.GetRequiredService<ICalendarStore>().AddAsync(new CalendarEvent
    { Title = "Termodinamik II", Day = now.DayOfWeek, Start = new(9, 0), End = new(11, 0), Location = "B-204" });
await services.GetRequiredService<ICalendarStore>().AddAsync(new CalendarEvent
    { Title = "Akışkanlar Mekaniği", Day = now.DayOfWeek, Start = new(13, 0), End = new(15, 0) });
await services.GetRequiredService<ReminderService>().AddAsync("İlacını iç", now.Date.AddHours(20), ReminderRecurrence.Yok, ReminderPriority.Onemli);

var briefing = services.GetRequiredService<BriefingService>();
var today = DateOnly.FromDateTime(now);
Console.WriteLine("VERİLER:\n" + await briefing.BuildFactsAsync(today) + "\n");
Console.WriteLine("BRİFİNG:\n" + await briefing.ComposeAsync(today, late: false) + "\n");
Console.WriteLine("GEÇ BRİFİNG:\n" + await briefing.ComposeAsync(today, late: true));
await services.DisposeAsync();
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // havuzdaki bağlantı dosyayı kilitli tutuyor
foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*"))
    File.Delete(f);
