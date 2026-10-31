using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ultron.Core.Briefing;
using Ultron.Core.Calendar;
using Ultron.Core.Computer;
using Ultron.Core.Documents;
using Ultron.Infrastructure.Documents;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Tools;
using Ultron.Infrastructure.Reminders;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;
using Ultron.Core.Embeddings;
using Ultron.Core.Memories;
using Ultron.Core.Routing;
using Ultron.Core.Voice;
using Ultron.Core.Web;
using Ultron.Infrastructure.Voice;
using Ultron.Infrastructure.Web;
using Ultron.Infrastructure.Memories;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Persistence;

namespace Ultron.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Ultron servislerini kaydeder. Host ve Cli aynı satırla kullanır.</summary>
    public static IServiceCollection AddUltronInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<MemoryOptions>(configuration.GetSection(MemoryOptions.SectionName));

        services.AddHttpClient<IChatModel, OllamaChatModel>(ConfigureOllamaClient);
        if (!string.IsNullOrWhiteSpace(configuration[$"{OllamaOptions.SectionName}:{nameof(OllamaOptions.CodeModel)}"]))
            services.AddHttpClient<ICodeChatModel, OllamaCodeChatModel>(ConfigureOllamaClient);
        services.AddHttpClient<IEmbeddingModel, OllamaEmbeddingModel>(ConfigureOllamaClient);

        services.Configure<WebOptions>(configuration.GetSection(WebOptions.SectionName));
        services.AddHttpClient<SearxngWebSearch>((sp, http) =>
        {
            http.BaseAddress = sp.GetRequiredService<IOptions<WebOptions>>().Value.SearxngUrl;
            http.Timeout = TimeSpan.FromSeconds(30); // SearXNG birden çok motoru sorgular, birkaç saniye sürebilir
        });
        // Faz 20: önce Tavily (anahtar varsa, kota bitmediyse), sonra SearXNG.
        services.AddHttpClient<TavilyWebSearch>();
        // Faz 26: ilk açılış sihirbazı. Model ve ses dosyaları GB'larca: istek süresi sınırsız, ilerleme akışla gelir.
        services.AddHttpClient<Ultron.Infrastructure.Setup.SetupService>(c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddTransient<IWebSearch, FallbackWebSearch>();
        services.AddSingleton<IHeadlessBrowser, EdgeHeadlessBrowser>();
        services.AddHttpClient<IWebPageReader, HttpWebPageReader>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, // yönlendirmeler okuyucuda adres kontrolüyle izleniyor
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            });

        var dbPath = (configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new()).ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        services.AddDbContextFactory<UltronDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<IConversationStore, EfConversationStore>();
        services.AddSingleton<IMemoryStore, EfMemoryStore>();
        services.AddSingleton<ICategoryStore, EfCategoryStore>();

        services.Configure<WebAnswerOptions>(configuration.GetSection(WebAnswerOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IWebCacheStore, EfWebCacheStore>();
        services.AddSingleton<WebAnswerService>();

        services.Configure<VoiceOptions>(configuration.GetSection(VoiceOptions.SectionName));
        services.AddSingleton<WhisperSpeechToText>();
        // Faz 23: önce Groq (anahtar varsa), yoksa/hata olursa yerel Whisper; "Ultron mu dendi" kontrolü hep yerel.
        services.AddHttpClient<GroqSpeechToText>();
        services.AddSingleton<ISpeechToText>(sp => sp.GetRequiredService<GroqSpeechToText>());
        services.TryAddSingleton<Ultron.Core.Voice.IVoiceResources, Ultron.Core.Voice.NoVoiceResources>(); // Host'ta Chatterbox'la birlikte gerçeği kaydedilir
        services.AddSingleton<PiperTextToSpeech>();
        services.AddHttpClient<ChatterboxTextToSpeech>(); // servis yoksa Piper'a düşer
        // Faz 23: önce ElevenLabs (anahtar varsa, kota bitmediyse), yoksa Chatterbox.
        services.AddHttpClient<ElevenLabsTextToSpeech>();
        services.AddSingleton<ITextToSpeech>(sp => sp.GetRequiredService<ElevenLabsTextToSpeech>());

        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.SectionName));
        services.AddSingleton<ChatOrchestrator>();

        // Araçlar (Faz 7): hatırlatıcı, hava durumu, hesap. Bildirimi Host masaüstüne iletir; Cli'de sadece günlüğe yazılır.
        services.AddSingleton<IReminderStore, EfReminderStore>();
        services.AddSingleton<IToolAuditLog, EfToolAuditLog>();
        services.AddSingleton<ReminderPhrasing>();
        services.AddSingleton<ReminderService>();
        services.TryAddSingleton<IReminderNotifier, LogReminderNotifier>();
        services.Configure<WeatherOptions>(configuration.GetSection(WeatherOptions.SectionName));
        services.AddHttpClient<IWeatherService, OpenMeteoWeather>(http => http.Timeout = TimeSpan.FromSeconds(25)); // konum servisi bazen yavaş
        services.AddSingleton<ITool, SetReminderTool>();
        services.AddSingleton<ITool, ListRemindersTool>();
        services.AddSingleton<ITool, CancelReminderTool>();
        services.AddSingleton<ITool, WeatherTool>();
        services.AddSingleton<ITool, CalculatorTool>();

        // Faz 8: konuşarak ayarlanabilen tercihler, ders programı, sabah brifingi.
        services.AddSingleton<ISettingsStore, EfSettingsStore>();
        services.AddSingleton<UltronSettings>();
        services.AddSingleton<ICalendarStore, EfCalendarStore>();
        services.TryAddSingleton<IBriefingNotifier, LogBriefingNotifier>();
        services.AddSingleton<BriefingService>();
        // Faz 8 kalanı: öneri motoru ve sabah uyandırma (Windows Görev Zamanlayıcısı).
        services.TryAddSingleton<Ultron.Core.Suggestions.ISuggestionNotifier, Ultron.Core.Suggestions.LogSuggestionNotifier>();
        services.AddSingleton<Ultron.Core.Suggestions.SuggestionService>();
        services.AddSingleton<IWakeScheduler, Briefing.WindowsWakeScheduler>();
        services.AddSingleton<WakeForBriefingService>();
        services.AddSingleton<ITool, ShowSettingsTool>();
        services.AddSingleton<ITool, ChangeSettingTool>();
        services.AddSingleton<ITool, AddClassTool>();
        services.AddSingleton<ITool, ListClassesTool>();
        services.AddSingleton<ITool, DeleteClassTool>();
        services.AddSingleton<ITool, DaySummaryTool>();

        // Faz 12: bilgisayar kontrolü (uygulama, dosya, komut) + konuşarak yetki; onaylı işlemler onay kartıyla (Host IApprovalService verir).
        services.TryAddSingleton<IApprovalNotifier, NoApprovalNotifier>();
        services.AddSingleton<ApprovalBroker>();
        services.AddSingleton<IApprovalService>(sp => sp.GetRequiredService<ApprovalBroker>());
        services.AddSingleton<ComputerPermissions>();
        services.AddSingleton<IComputer, Computer.WindowsComputer>();
        // Faz 21: pencereler, ses/medya, güç, dosya düzenleme, PDF, Ultron tarayıcısı.
        services.AddSingleton<IDesktop, Computer.WindowsDesktop>();
        services.AddSingleton<IPdfTools, Computer.PdfTools>();
        services.AddSingleton(sp => new FileOrganizer(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IUltronBrowser, Computer.EdgeCdpBrowser>();
        services.AddSingleton<ITool, ListWindowsTool>();
        services.AddSingleton<ITool, WindowTool>();
        services.AddSingleton<ITool, ArrangeWindowsTool>();
        services.AddSingleton<ITool, VolumeTool>();
        services.AddSingleton<ITool, MediaTool>();
        services.AddSingleton<ITool, PowerTool>();
        services.AddSingleton<ITool, FindFilesTool>();
        services.AddSingleton<ITool, MoveFileTool>();
        services.AddSingleton<ITool, OrganizeFolderTool>();
        services.AddSingleton<ITool, UndoFilesTool>();
        services.AddSingleton<ITool, PdfTool>();
        services.AddSingleton<ITool, BrowserTool>();
        services.AddSingleton<ITool, OpenAppTool>();
        services.AddSingleton<ITool, OpenPathTool>();
        services.AddSingleton<ITool, ListFilesTool>();
        services.AddSingleton<ITool, ReadFileTool>();
        services.AddSingleton<ITool, WriteFileTool>();
        services.AddSingleton<ITool, DeleteFileTool>();
        services.AddSingleton<ITool, RunCommandTool>();
        services.AddSingleton<ITool, ShowPermissionsTool>();
        services.AddSingleton<ITool, ChangePermissionTool>();

        // Faz 13: kodlama ajanı (Claude Code, headless). Bildirimi Host masaüstüne/Telegram'a iletir.
        services.AddSingleton<Ultron.Core.Coding.ICodingJobStore, EfCodingJobStore>();
        services.Configure<Coding.ClaudeCodeOptions>(configuration.GetSection(Coding.ClaudeCodeOptions.SectionName));
        services.AddSingleton<Ultron.Core.Coding.ICodeAgent, Coding.ClaudeCodeAgent>();
        services.AddSingleton<Ultron.Core.Pro.IProResearcher, Pro.ProResearchers>(); // Ultron Pro: Claude, Codex ya da Gemini (2026-10-31)
        services.TryAddSingleton<Ultron.Core.Coding.ICodingNotifier, Ultron.Core.Coding.LogCodingNotifier>();
        services.AddSingleton<Ultron.Core.Coding.CodingAccess>();
        services.AddSingleton<Ultron.Core.Coding.CodingService>();
        services.AddSingleton<ITool, Ultron.Core.Coding.StartCodingTool>();
        services.AddSingleton<ITool, Ultron.Core.Coding.CodingStatusTool>();
        services.AddSingleton<ITool, Ultron.Core.Coding.CodingReplyTool>();
        services.AddSingleton<ITool, Ultron.Core.Coding.CodingCancelTool>();

        // Faz 15 (d): yazılım cevaplarının gece denetimi (Claude Code) ve kullanıcının onayladığı düzeltmeler.
        services.Configure<Ultron.Core.Learning.LearningOptions>(configuration.GetSection(Ultron.Core.Learning.LearningOptions.SectionName));
        services.AddSingleton<Ultron.Core.Learning.IAnswerReviewStore, EfAnswerReviewStore>();
        services.TryAddSingleton<Ultron.Core.Learning.ILearningNotifier, Ultron.Core.Learning.LogLearningNotifier>();
        services.AddSingleton<Ultron.Core.Learning.LearningService>();

        // Faz 16: İş arayüzü (bileşen üretimi, tarif defteri, zevk profili, Claude Code'a devretme).
        services.Configure<Ultron.Core.Studio.StudioOptions>(configuration.GetSection(Ultron.Core.Studio.StudioOptions.SectionName));
        services.AddSingleton<Ultron.Core.Studio.IStudioStore, EfStudioStore>();
        services.TryAddSingleton<Ultron.Core.Studio.IStudioNotifier, Ultron.Core.Studio.LogStudioNotifier>();
        services.AddSingleton<Ultron.Core.Studio.StudioService>();

        // Ders arayüzü (Faz 17).
        services.Configure<Ultron.Core.Lessons.LessonOptions>(configuration.GetSection(Ultron.Core.Lessons.LessonOptions.SectionName));
        services.AddSingleton<Ultron.Core.Lessons.ILessonStore, EfLessonStore>();
        services.TryAddSingleton<Ultron.Core.Lessons.ILessonNotifier, Ultron.Core.Lessons.LogLessonNotifier>();
        services.AddSingleton<Ultron.Core.Lessons.LessonService>();

        // Gelişim panosu (Faz 18).
        services.AddSingleton<Ultron.Core.Growth.IGrowthStore, EfGrowthStore>();
        services.AddSingleton<Ultron.Core.Growth.GrowthService>();

        // Cevap paketleri (Faz 19): Medium derin web, PRO (Claude araştırır, cevap hafızaya).
        services.AddSingleton<Ultron.Core.Web.DeepWebService>();
        services.Configure<Ultron.Core.Pro.ProOptions>(configuration.GetSection(Ultron.Core.Pro.ProOptions.SectionName));
        services.AddSingleton<Ultron.Core.Pro.IProAnswerStore, EfProAnswerStore>();
        services.AddSingleton<Ultron.Core.Pro.ProService>();

        // Kendi hakkındaki istekler: görünüm ayarı ya da geliştirme listesi ("yaptım" yalanı yok).
        services.Configure<Ultron.Core.Self.ImprovementOptions>(configuration.GetSection(Ultron.Core.Self.ImprovementOptions.SectionName));
        services.AddSingleton<Ultron.Core.Self.IImprovementLog, Ultron.Core.Self.FileImprovementLog>();
        services.AddSingleton<Ultron.Core.Self.SelfRequests>();

        // Faz 10: belge sorgulama (PDF, Word, metin) ve araştırma raporları.
        services.Configure<DocumentOptions>(configuration.GetSection(DocumentOptions.SectionName));
        services.AddSingleton<IDocumentStore, EfDocumentStore>();
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddSingleton<DocumentService>();
        services.AddSingleton<DocumentReader>();
        // Resim/taranmış PDF sayfaları: Windows OCR, az yazı ya da formül/şema varsa gemma4 görsel okuma (tasarım kararı).
        services.AddSingleton<IPdfPageRenderer, WindowsPdfRenderer>();
        services.AddSingleton<IOcrEngine, WindowsOcrEngine>();
        services.AddHttpClient<IVisionReader, OllamaVisionReader>(ConfigureOllamaClient);
        services.AddSingleton<ScannedPageReader>();
        services.AddSingleton<Ultron.Core.Research.ResearchService>();
        // Faz 14: günlük yedek (veritabanı + belgeler + ayarlar + ses) ve konuşarak yedek/geri yükleme.
        services.Configure<Backup.BackupOptions>(configuration.GetSection(Backup.BackupOptions.SectionName));
        services.AddSingleton<Ultron.Core.Backup.IBackupArchiver>(sp => new Backup.ZipBackupArchiver(
            dbPath,
            sp.GetRequiredService<IOptions<DocumentOptions>>().Value.ResolveFolder(),
            sp.GetRequiredService<IOptions<Backup.BackupOptions>>().Value,
            sp.GetRequiredService<IDbContextFactory<UltronDbContext>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Backup.ZipBackupArchiver>>()));
        services.AddSingleton<Ultron.Core.Backup.BackupService>();
        services.AddSingleton<ITool, Ultron.Core.Backup.BackupNowTool>();
        services.AddSingleton<ITool, Ultron.Core.Backup.ListBackupsTool>();
        services.AddSingleton<ITool, Ultron.Core.Backup.RestoreBackupTool>();
        // Faz 15: Yazılım Kütüphanesi (resmi dokümantasyon, haftalık güncelleme, hibrit arama).
        var libraryOptions = configuration.GetSection(Library.LibraryOptions.SectionName).Get<Library.LibraryOptions>() ?? new();
        services.AddSingleton(libraryOptions);
        services.AddSingleton<Ultron.Core.Library.ILibraryStore, EfLibraryStore>();
        services.AddSingleton<Ultron.Core.Library.ILibraryFetcher, Library.GitLibraryFetcher>();
        services.AddSingleton(sp =>
        {
            var library = ActivatorUtilities.CreateInstance<Ultron.Core.Library.LibraryService>(sp);
            library.Sources = Library.LibrarySourceFile.Load(libraryOptions, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Ultron.Core.Library.LibraryService>>());
            return library;
        });
        services.AddSingleton<ITool, Ultron.Core.Library.LibraryStatusTool>();
        services.AddSingleton<ITool, Ultron.Core.Library.LibraryUpdateTool>();

        // Bildiğim siteler (Faz 20): okulun sitesi gibi takip edilen siteler her gece taranır, sorular kopyadan cevaplanır.
        services.AddSingleton<Ultron.Core.Sites.ISiteStore, EfSiteStore>();
        services.AddSingleton<Ultron.Core.Sites.SchoolAssistant>(); // Faz 24: yeni duyuru bildirimi + sınav hatırlatmaları
        services.AddSingleton<Ultron.Core.Sites.SiteService>();
        services.AddSingleton<ITool, Ultron.Core.Sites.AddSiteTool>();
        services.AddSingleton<ITool, Ultron.Core.Sites.ListSitesTool>();
        services.AddSingleton<ITool, Ultron.Core.Sites.RemoveSiteTool>();
        services.AddSingleton<ITool, Ultron.Core.Sites.CrawlSiteTool>();
        services.AddSingleton<Services.DependencyStatus>();
        services.TryAddSingleton<Services.IServiceWarningNotifier, Services.LogServiceWarningNotifier>();
        services.AddSingleton<ToolRunner>();
        services.AddSingleton<FollowUpService>();
        services.AddSingleton<IntentRouter>();
        services.AddSingleton<MemoryExtractor>();
        services.TryAddSingleton<ICategoryNotifier, LogCategoryNotifier>();
        services.AddSingleton<MemoryService>();
        services.AddSingleton<MemoryQueue>();
        services.AddSingleton<IMemoryQueue>(sp => sp.GetRequiredService<MemoryQueue>());
        services.AddHostedService<MemoryWorker>();

        return services;
    }

    /// <summary>Telegram botu (uzun yoklama). Sadece Ultron.Host çağırır; token yoksa servis hiçbir şey yapmadan durur.</summary>
    public static IServiceCollection AddTelegram(this IServiceCollection services, IConfiguration configuration, bool startBot = true)
    {
        services.Configure<Telegram.TelegramOptions>(configuration.GetSection(Telegram.TelegramOptions.SectionName));
        // Uzun yoklama 25 sn bekletir; zaman aşımı ondan uzun olmalı.
        services.AddHttpClient<Telegram.TelegramApi>(http => http.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<Telegram.TelegramState>();
        if (startBot)
        {
            services.AddHostedService<Telegram.TelegramBridge>();
        }
        return services;
    }

    /// <summary>Zamanı gelen hatırlatmaları çaldıran arka plan servisi. Sadece Ultron.Host çağırır (Cli ve testler değil).</summary>
    public static IServiceCollection AddReminderScheduler(this IServiceCollection services)
    {
        services.AddHostedService<ReminderScheduler>();
        return services;
    }

    /// <summary>Günlük yedek zamanlayıcısı. Sadece Ultron.Host çağırır (testler gerçek yedek klasörüne yazmasın).</summary>
    public static IServiceCollection AddBackupScheduler(this IServiceCollection services) => services.AddHostedService<Backup.BackupScheduler>();

    /// <summary>Haftalık kütüphane güncellemesi. Sadece Ultron.Host çağırır (testler gerçek depoları indirmesin).</summary>
    public static IServiceCollection AddLibraryScheduler(this IServiceCollection services) => services.AddHostedService<Library.LibraryScheduler>();

    public static IServiceCollection AddSiteScheduler(this IServiceCollection services) => services.AddHostedService<Sites.SiteScheduler>();

    /// <summary>Gece denetimi zamanlayıcısı (Faz 15 d). Sadece Host'ta; testlerde ve değerlendirmede kapalı.</summary>
    public static IServiceCollection AddLearningScheduler(this IServiceCollection services) => services.AddHostedService<Learning.LearningScheduler>();

    /// <summary>Embedding modeli değiştiyse kayıtlı vektörleri arka planda yeniden üretir. Sadece Ultron.Host çağırır (testler Ollama'ya gitmesin).</summary>
    public static IServiceCollection AddEmbeddingMigration(this IServiceCollection services) => services.AddHostedService<Embeddings.EmbeddingMigration>();

    /// <summary>Açılışta Ollama ve SearXNG kapalıysa başlatır, olmazsa bildirir. Sadece Ultron.Host çağırır.</summary>
    public static IServiceCollection AddDependencyStarter(this IServiceCollection services) => services.AddHostedService<Services.DependencyStarter>();

    /// <summary>
    /// Chatterbox ses servisini (Python) Host'la birlikte başlatıp kapatır. Sadece Ultron.Host çağırır:
    /// Cli ve testler ses üretmez, gereksiz yere ekran kartına model yüklenmesin.
    /// </summary>
    public static IServiceCollection AddChatterboxLauncher(this IServiceCollection services)
    {
        // Tek örnek: İş modunda boşaltma (VoiceResources) aynı süreci kapatıp açabilsin.
        services.AddHttpClient(nameof(ChatterboxLauncher));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ChatterboxLauncher>(sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ChatterboxLauncher))));
        services.AddHostedService(sp => sp.GetRequiredService<ChatterboxLauncher>());
        services.AddSingleton<Ultron.Core.Voice.IVoiceResources, VoiceResources>();
        return services;
    }

    /// <summary>Bekleyen migration'ları uygular; veritabanı yoksa oluşturur. Uygulama açılışında bir kez çağrılır.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<UltronDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await EfMemoryStore.RebuildSearchIndexIfNeededAsync(db, cancellationToken);
    }

    private static void ConfigureOllamaClient(IServiceProvider sp, HttpClient http)
    {
        http.BaseAddress = sp.GetRequiredService<IOptions<OllamaOptions>>().Value.BaseUrl;
        // Model ilk istekte diskten yüklenir; varsayılan 100 sn yerine geniş tut.
        http.Timeout = TimeSpan.FromMinutes(5);
    }
}
