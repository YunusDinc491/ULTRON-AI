// Ultron'un her zaman çalışan servisi. Masaüstü uygulaması (Faz 5) ve ileride Telegram (Faz 9)
// buna bağlanır. Sadece bu bilgisayardan erişilir (127.0.0.1).

using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Host.Hubs;
using Ultron.Host.Status;
using Ultron.Infrastructure;

// Masaüstü çıktıyı host.log'a yönlendiriyor; varsayılan kod sayfasında Türkçe harfler bozuluyordu.
Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);
// Faz 25: Tailscale kuruluysa onun özel adresi de dinlenir (telefon her yerden bağlanır); ev ağına/internete açılmaz.
if (Ultron.Host.Mobile.MobileAccess.TailscaleAddress() is { } tailscale)
{
    builder.WebHost.ConfigureKestrel(o => o.Listen(tailscale, 5199));
    // AllowedHosts sadece localhost'u tanıyor: telefonun isteği "Host: 100.x.y.z:5199" ile gelir, eklenmezse 400 döner.
    builder.Configuration["AllowedHosts"] = $"{builder.Configuration["AllowedHosts"]};{tailscale}";
}
// Gizli ayarlar (Telegram bot token'ı) her ortamda user-secrets'tan okunur (varsayılan sadece "Development"ta okur).
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddUltronInfrastructure(builder.Configuration);
// Testlerde "Telegram:Start=false": gerçek bot token'ı kayıtlıyken testler bota bağlanıp mesajları "çalmasın".
builder.Services.AddTelegram(builder.Configuration, startBot: builder.Configuration.GetValue("Telegram:Start", true));
builder.Services.AddSingleton<Ultron.Host.Telegram.PresenceTracker>();
if (builder.Configuration.GetValue("Voice:StartChatterbox", true))
    builder.Services.AddChatterboxLauncher();
// Varsayılan 32 KB sınırı ses kaydına yetmez (base64 ile ~%33 büyür).
// Aynı bağlantıdan birden çok çağrı aynı anda: resimli PDF okunurken (dakikalar sürebilir) sohbet beklemesin.
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = ChatHub.MaxAudioBytes * 4 / 3 + 1024;
    o.MaximumParallelInvocationsPerClient = 4;
});
builder.Services.AddSingleton<StatusService>();
builder.Services.AddSingleton<HubReminderNotifier>();
// Onay kartı (Faz 12): masaüstü kartı + uzaktaysa Telegram düğmeleri.
builder.Services.AddSingleton<ConnectionTracker>();
builder.Services.AddSingleton<Ultron.Core.Tools.IApprovalNotifier, ApprovalNotifier>();
builder.Services.AddSingleton<Ultron.Core.Coding.ICodingNotifier, CodingNotifier>();
builder.Services.AddSingleton<Ultron.Core.Learning.ILearningNotifier, HubLearningNotifier>();
builder.Services.AddSingleton<Ultron.Core.Studio.IStudioNotifier, HubStudioNotifier>();
builder.Services.AddSingleton<Ultron.Core.Lessons.ILessonNotifier, HubLessonNotifier>();
// Ultron MCP sunucusu (Faz 13): Claude Code kodlama işindeyken proje notları ve kullanıcının paylaştığı hafıza (işe özel anahtarla).
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<Ultron.Host.Mcp.UltronMcpTools>();
// Bildirimler masaüstüne her zaman, Telegram'a kullanıcı bilgisayar başında değilse.
builder.Services.AddSingleton<Ultron.Host.Telegram.FanOutNotifier>();
builder.Services.AddSingleton<Ultron.Core.Reminders.IReminderNotifier>(sp => sp.GetRequiredService<Ultron.Host.Telegram.FanOutNotifier>());
builder.Services.AddSingleton<Ultron.Core.Briefing.IBriefingNotifier>(sp => sp.GetRequiredService<Ultron.Host.Telegram.FanOutNotifier>());
builder.Services.AddSingleton<Ultron.Core.Suggestions.ISuggestionNotifier>(sp => sp.GetRequiredService<Ultron.Host.Telegram.FanOutNotifier>());
builder.Services.AddSingleton<Ultron.Core.Sites.ISiteNotifier>(sp => sp.GetRequiredService<Ultron.Host.Telegram.FanOutNotifier>());
// Faz 25: mobil cihazlar (eşleştirme, bölüm) ve telefon güncellemesi. Faz 26: fotoğraf yedeği kalktı, telefon sadece sohbet.
builder.Services.AddSingleton(sp => new Ultron.Core.Mobile.MobileDevices(TimeProvider.System));
builder.Services.AddSingleton(new Ultron.Core.Mobile.MobileUpdate());
// Ana ekranın sağ bilgi paneli (2026-10-31): takvim, hava, döviz, Ultron.
builder.Services.AddHttpClient<Ultron.Host.Info.InfoPanelService>();
if (builder.Configuration.GetValue("Reminders:Scheduler", true))
    builder.Services.AddReminderScheduler();
if (builder.Configuration.GetValue("Backup:Scheduler", true))
    builder.Services.AddBackupScheduler();
if (builder.Configuration.GetValue("Services:AutoStart", true))
    builder.Services.AddDependencyStarter();
if (builder.Configuration.GetValue("Library:Scheduler", true))
    builder.Services.AddLibraryScheduler();
if (builder.Configuration.GetValue("Sites:Scheduler", true))
    builder.Services.AddSiteScheduler();
    builder.Services.AddLearningScheduler();
if (builder.Configuration.GetValue("Embeddings:Migration", true))
    builder.Services.AddEmbeddingMigration();
builder.Services.AddSingleton<Ultron.Infrastructure.Services.IServiceWarningNotifier, Ultron.Host.Hubs.HubServiceWarningNotifier>();
builder.Services.AddSingleton<Ultron.Core.Memories.ICategoryNotifier, Ultron.Host.Hubs.HubCategoryNotifier>();
// Faz 22: kendi Chrome'una kurulan Ultron eklentisi (YouTube, sekme okuma).
builder.Services.AddSingleton<Ultron.Host.Chrome.ChromeBridge>();
builder.Services.AddSingleton<Ultron.Core.Computer.IChromeBridge>(sp => sp.GetRequiredService<Ultron.Host.Chrome.ChromeBridge>());
builder.Services.AddSingleton<Ultron.Core.Tools.ITool, Ultron.Core.Computer.YouTubeTool>();
builder.Services.AddSingleton<Ultron.Core.Tools.ITool, Ultron.Core.Computer.ChromeTabTool>();
builder.Services.AddSingleton<Ultron.Core.Tools.ITool, Ultron.Core.Computer.ChromeStatusTool>();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

// "Ultron" deyince dinleme ayarı konuşarak değişirse masaüstü hemen öğrensin (Faz 14).
var wakeHub = app.Services.GetRequiredService<IHubContext<ChatHub>>();
var wakeSettings = app.Services.GetRequiredService<Ultron.Core.Settings.UltronSettings>();
wakeSettings.Changed += key =>
{
    if (key == Ultron.Core.Settings.UltronSettings.WakeWord)
        _ = Task.Run(async () => await wakeHub.Clients.All.SendAsync(ChatHubContract.WakeWordChanged,
            await wakeSettings.GetAsync(Ultron.Core.Settings.UltronSettings.WakeWord) == "acik"));
    // "Formülleri büyüt": açık ekranlar anında yeni boyuta geçer.
    else if (key is Ultron.Core.Settings.UltronSettings.FormulaSize or Ultron.Core.Settings.UltronSettings.TextSize)
        _ = Task.Run(async () => await wakeHub.Clients.All.SendAsync(ChatHubContract.AppearanceChanged, await ChatHub.ReadAppearanceAsync(wakeSettings)));
};

// Uzaktan (telefon) gelen istekler: sadece eşleştirme açık, diğerleri cihaz anahtarı ister (Faz 25).
var mobileDevices = app.Services.GetRequiredService<Ultron.Core.Mobile.MobileDevices>();
app.Use(Ultron.Host.Security.BrowserGuard.InvokeAsync); // web sitelerinin 127.0.0.1:5199'a bağlanmasını engeller (Origin)
app.Use((context, next) => Ultron.Host.Mobile.MobileAccess.GuardAsync(context, next, mobileDevices));
Ultron.Host.Mobile.MobileAccess.MapMobile(app);
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.Map(Ultron.Host.Chrome.ChromeBridge.Path, (HttpContext context, Ultron.Host.Chrome.ChromeBridge chrome) => chrome.AcceptAsync(context));
app.MapHub<ChatHub>(ChatHubContract.Path);
app.UseWhen(c => c.Request.Path.StartsWithSegments("/mcp"), mcp => mcp.UseMiddleware<Ultron.Host.Mcp.McpAccessMiddleware>());
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.Now }));

// "Ultron mı dendi?" doğrulaması yerel Whisper'la yapılır: açılışta ön yüklensin ki ilk seslenişte soğuk model 5 sn'yi aşmasın (2026-10-30).
if (builder.Configuration.GetValue("Voice:WarmUpOnStart", true))
    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
    {
        try
        {
            await app.Services.GetRequiredService<Ultron.Core.Voice.ISpeechToText>().WarmUpAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Whisper ön yüklenemedi");
        }
    }));

app.Run();

// Entegrasyon testlerinde WebApplicationFactory<Program> için.
public partial class Program;
