using System.Net;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Reminders;
using Ultron.Host.Hubs;
using Ultron.Host.Telegram;
using Ultron.Infrastructure.Telegram;
using Ultron.Infrastructure.Voice;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Host;

public class TelegramTextTests
{
    [Fact]
    public void Markdown_telegram_icin_sadelestirilir()
    {
        Assert.Equal("Termodinamik\nBugün dersin var.", TelegramBridge.ForTelegram("### Termodinamik\nBugün **dersin** var."));
    }

    [Fact]
    public void Sesli_ozet_listeden_once_en_fazla_uc_cumle()
    {
        var summary = TelegramBridge.SpokenSummary("Bir. İki. Üç. Dört.\n\n- madde\n- madde");
        Assert.Equal("Bir. İki. Üç.", summary);
        Assert.Equal("Kısa özet.", TelegramBridge.SpokenSummary("Kısa özet.\n1. Adım\n2. Adım"));
    }

    [Fact]
    public void Opus_kodlama_ve_cozme_sureyi_korur()
    {
        const int rate = 16000;
        var tone = new Wav.Audio(Enumerable.Range(0, rate * 2).Select(i => MathF.Sin(2 * MathF.PI * 300 * i / rate) * 0.4f).ToArray(), rate);

        var ogg = OpusAudio.EncodeOgg(tone);
        var back = OpusAudio.DecodeOgg(ogg);

        Assert.Equal(48000, back.SampleRate);
        Assert.InRange(back.Samples.Length / 48000.0, 1.9, 2.1);
        Assert.True(back.Samples.Max() > 0.2f); // ses kaybolmamış
        Assert.True(ogg.Length < 30000);       // 2 sn ≈ 8 KB (32 kbit/s)
    }
}

public class PresenceTests
{
    [Fact]
    public void Rapor_yoksa_ya_da_bes_dakikadir_bostaysa_uzakta()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        var presence = new PresenceTracker(time);
        Assert.True(presence.IsAway); // masaüstü hiç bildirmedi (kapalı)

        presence.Report(TimeSpan.FromSeconds(20));
        Assert.False(presence.IsAway);

        presence.Report(TimeSpan.FromMinutes(6));
        Assert.True(presence.IsAway);

        presence.Report(TimeSpan.Zero);
        time.Advance(TimeSpan.FromMinutes(2)); // masaüstü kapandı, rapor kesildi
        Assert.True(presence.IsAway);
    }
}

public class FanOutNotifierTests
{
    private static readonly ReminderDue Due = new(7, "Su iç", new DateTime(2026, 10, 27, 12, 0, 0), false,
        "Patron, hatırlatmamı istemiştin, su içmen gerekiyor.", ReminderPriority.CokOnemli, false);

    [Fact]
    public async Task Bilgisayar_basindaysa_sadece_masaustune()
    {
        var (notifier, desktop, telegram, presence, _) = Create(paired: true);
        presence.Report(TimeSpan.FromSeconds(10));

        await notifier.NotifyAsync(Due);

        Assert.Single(desktop.Sent);
        Assert.Empty(telegram.Requests);
    }

    [Fact]
    public async Task Uzaktaysa_telegrama_okudum_dugmesiyle_gider()
    {
        var (notifier, desktop, telegram, _, _) = Create(paired: true); // rapor yok = uzakta

        await notifier.NotifyAsync(Due);
        await notifier.NotifyBriefingAsync("Günaydın Patron.", late: false);

        Assert.Equal(2, desktop.Sent.Count);
        Assert.Equal(2, telegram.Requests.Count);
        Assert.Contains("ack:7", telegram.Bodies[0]);
        Assert.Contains("Okudum", telegram.Bodies[0]);
        // Gövde JSON; Türkçe harfler \u kaçışlı yazılır, metin çözülerek karşılaştırılır.
        Assert.Contains("Günaydın Patron.", System.Text.Json.JsonDocument.Parse(telegram.Bodies[1]).RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Oneri_masaustune_her_zaman_uzaktaysa_telegrama_da()
    {
        var (notifier, desktop, telegram, presence, _) = Create(paired: true);
        await notifier.NotifySuggestionAsync("Patron, yarın yağmur var, şemsiyeni hazırla.");
        Assert.Equal(Ultron.Contracts.ChatHubContract.Suggestion, Assert.Single(desktop.Sent).Method);
        Assert.Single(telegram.Requests);

        presence.Report(TimeSpan.FromSeconds(10));
        await notifier.NotifySuggestionAsync("Patron, ikinci öneri.");
        Assert.Equal(2, desktop.Sent.Count);
        Assert.Single(telegram.Requests);
    }

    [Fact]
    public async Task Eslesmemisse_telegrama_gitmez_ve_hata_bildirimi_bozmaz()
    {
        var (notifier, desktop, telegram, _, _) = Create(paired: false);
        await notifier.NotifyAsync(Due);
        Assert.Empty(telegram.Requests);

        var (failing, desktop2, _, _, _) = Create(paired: true, telegramStatus: HttpStatusCode.InternalServerError);
        await failing.NotifyAsync(Due); // istisna fırlatmamalı
        Assert.Single(desktop2.Sent);
    }

    private static (FanOutNotifier, RecordingHub, CapturingHandler, PresenceTracker, TelegramState) Create(bool paired, HttpStatusCode telegramStatus = HttpStatusCode.OK)
    {
        var hub = new RecordingHub();
        var handler = new CapturingHandler(telegramStatus);
        var api = new TelegramApi(new HttpClient(handler), Options.Create(new TelegramOptions { BotToken = "test" }));
        var state = new TelegramState { ChatId = paired ? 42 : null };
        var presence = new PresenceTracker(new ManualTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)));
        var notifier = new FanOutNotifier(new HubReminderNotifier(hub, NullLogger<HubReminderNotifier>.Instance), api, state, presence,
            hub, NullLogger<FanOutNotifier>.Instance);
        return (notifier, hub, handler, presence, state);
    }

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var body = status == HttpStatusCode.OK
                ? """{"ok":true,"result":{"message_id":1,"chat":{"id":42}}}"""
                : """{"ok":false,"description":"sunucu hatası"}""";
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>SignalR yerine: masaüstüne giden mesajları kaydeder.</summary>
    private sealed class RecordingHub : IHubContext<ChatHub>
    {
        private readonly RecordingClients _clients = new();
        public List<(string Method, object?[] Args)> Sent => _clients.Sent;
        public IHubClients Clients => _clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingClients : IHubClients, IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = [];
        public IClientProxy All => this;

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return Task.CompletedTask;
        }

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
    }
}
