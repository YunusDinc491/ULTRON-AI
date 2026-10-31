using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Ultron.Contracts;
using Ultron.Desktop.Services;
using Ultron.Desktop.Voice;
using Ultron.Desktop.Widget;

namespace Ultron.Desktop;

public partial class App : Application
{
    private static readonly Uri HostUrl = new("http://127.0.0.1:5199");

    private Mutex? _singleInstance;
    private ServiceProvider? _services;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private WidgetWindow? _widget;
    private SpeechPlayer? _reminderVoice;
    private WakeWordListener? _wake;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Geliştirme: "Ultron.exe --ikon-yaz yol.ico" simgeyi .ico olarak yazar (exe ikonu bundan üretildi) ve çıkar.
        if (e.Args is ["--ikon-yaz", var iconPath])
        {
            File.WriteAllBytes(iconPath, iconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? UltronEmblem.RenderPng(256) : UltronEmblem.RenderIco());
            Shutdown();
            return;
        }

        // Tek kopya: Ultron zaten açıksa ikincisi açılmaz (kısayol ve tepsi ikonu çakışmasın).
        _singleInstance = new Mutex(initiallyOwned: true, "Ultron.Desktop.TekKopya", out var isFirstInstance);
        // "Yeniden başlat"ta eski kopya kapanırken yenisi açılır: kilidi bırakmasını kısa süre bekle.
        if (!isFirstInstance && e.Args.Contains(RestartArg))
        {
            try { isFirstInstance = _singleInstance.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { isFirstInstance = true; }
        }
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools(); // F12 ile geliştirici araçları
#endif
        var launcher = new HostLauncher(HostUrl, Environment.GetEnvironmentVariable("ULTRON_HOST_EXE"));
        var client = new UltronClient(HostUrl);
        services.AddSingleton(launcher);
        services.AddSingleton(client);
        _services = services.BuildServiceProvider();

        _window = new MainWindow(_services);
        _tray = new TrayIcon(_window, ExitApplication, RestartApplication);
        // Masaüstünde hep duran simge: tık = arayüz, sağ tık = arayüzsüz konuş, sürükle = taşı.
        _widget = new WidgetWindow(client, _window);
        _widget.Show();
        _window.HotkeyFailed += () => _tray.ShowMessage("Kısayol kullanılamıyor", "Ctrl+Alt+U başka bir uygulama tarafından kullanılıyor. Ultron'u tepsi ikonundan açabilirsin.");

        DesktopSettings.ApplyAutoStart();

        // Windows açılışında (--tray) pencere gösterilmez; kısayolun çalışması için pencere tutamacı yine de oluşturulur.
        if (e.Args.Contains("--tray"))
            new WindowInteropHelper(_window).EnsureHandle();
        else
            _window.Show();

        // Hatırlatmalar pencere hiç açılmasa da gelsin: Host'u başlatma ve bağlantı uygulama seviyesinde.
        _reminderVoice = new SpeechPlayer(client.SpeakAsync);
        client.ReminderDue += due => Dispatcher.InvokeAsync(() => OnReminder(due));
        client.Briefing += briefing => Dispatcher.InvokeAsync(() =>
        {
            DesktopLog.Write("Sabah brifingi" + (briefing.Late ? " (telafi)" : ""));
            _tray?.ShowMessage("Ultron · Günaydın", briefing.Text);
            if (DesktopSettings.SoundOn)
                _reminderVoice?.Enqueue(briefing.Text);
        });
        // Öneri (Faz 8): bildirim + ses (Host zaten sessiz saat ve ders saatinde önermez); kart arayüzdeki sohbette.
        client.Suggestion += text => Dispatcher.InvokeAsync(() =>
        {
            DesktopLog.Write("Öneri");
            _tray?.ShowMessage("Ultron · Öneri", text);
            if (DesktopSettings.SoundOn)
                _reminderVoice?.Enqueue(text);
        });
        // Gece denetimi (Faz 15 d): öneri varsa tepsi bildirimi; onay Öğrenme panelinde.
        client.LearningReady += (reviewed, learned, pending) => Dispatcher.InvokeAsync(() =>
        {
            DesktopLog.Write($"Gece denetimi: {reviewed} cevap, {learned} öğrenildi, {pending} bekliyor");
            if (pending > 0)
                _tray?.ShowMessage("Ultron · Öğrenme", $"Gece denetimi: {learned} düzeltme öğrenildi, {pending} öneri onayını bekliyor (/ogrenme).");
            else if (learned > 0)
                _tray?.ShowMessage("Ultron · Öğrenme", $"Gece denetimi: {learned} hatamı resmi kaynaklara göre düzelttim (/ogrenme).");
        });
        // Servis uyarısı (Faz 14): Ollama/SearXNG açılışta başlatılamadıysa.
        client.ServiceWarning += text => Dispatcher.InvokeAsync(() =>
        {
            DesktopLog.Write("Servis uyarısı: " + text);
            _tray?.ShowMessage("Ultron · Servis", text);
        });
        // "Ultron" wake word (Faz 14): ayar Host'ta (konuşarak ya da tepsiden değişir); bağlanınca okunur.
        _wake = new WakeWordListener(client.CheckWakeWordAsync);
        _wake.Detected += () => Dispatcher.InvokeAsync(OnWakeWord);
        _wake.StatusChanged += status => Dispatcher.InvokeAsync(() => _tray?.SetWakeWordStatus(status));
        _tray.WakeWordSetupRequested += () => Dispatcher.InvokeAsync(ShowWakeWordSetup);
        WindowBridge.WakeWordSetupRequested += () => Dispatcher.InvokeAsync(ShowWakeWordSetup); // kurulum sihirbazından (Faz 26)
        _tray.WakeWordToggled += on => _ = client.SetWakeWordAsync(on).ContinueWith(t => DesktopLog.Write("Wake word ayarı kaydedilemedi: " + t.Exception?.GetBaseException().Message),
            TaskContinuationOptions.OnlyOnFaulted);
        client.WakeWordChanged += on => Dispatcher.InvokeAsync(() => ApplyWakeWord(on));
        client.StateChanged += () =>
        {
            if (client.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var on = await client.GetWakeWordAsync();
                        await Dispatcher.InvokeAsync(() => ApplyWakeWord(on));
                    }
                    catch (Exception ex)
                    {
                        DesktopLog.Write("Wake word ayarı okunamadı: " + ex.Message);
                    }
                });
        };
        // Bildirime tıklamak = "okudum" (tekrar eden hatırlatmalar susar).
        _tray.MessageClicked += () => _ = ReminderAlerts.AcknowledgeAllAsync(client);
        ReminderAlerts.Acknowledged += _ => Dispatcher.InvokeAsync(() =>
        {
            if (!ReminderAlerts.HasActive)
                _reminderVoice?.Stop();
        });
        // Onay kartı (Faz 12): onaylı işlem için Host sorar; kart arayüz açık olsun olmasın ekranın altında açılır.
        client.ApprovalRequested += request => Dispatcher.InvokeAsync(() => ShowApproval(client, request));
        client.ApprovalClosed += closed => Dispatcher.InvokeAsync(() =>
        {
            if (_approvals.Remove(closed.Id, out var card))
                card.Close();
        });
        // Kodlama işi (Faz 13): durum değişince (soru, bitti, hata, limit) tepside bildirim; ayrıntı arayüzdeki şeritte.
        var codingStatus = new Dictionary<long, string>();
        client.CodingUpdate += job => Dispatcher.InvokeAsync(() =>
        {
            if (codingStatus.GetValueOrDefault(job.Id) == job.Status)
                return;
            codingStatus[job.Id] = job.Status;
            var text = job.Status switch
            {
                "Soruyor" => "Claude soruyor: " + job.Question,
                "Bitti" => "Bitti. Özeti Ultron'da.",
                "Hata" => "Hata: " + job.LastMessage,
                "Bekliyor" => "Claude kullanım limiti doldu; yenilenince devam ettir.",
                _ => null,
            };
            if (text is not null)
                _tray?.ShowMessage($"Ultron · Kodlama #{job.Id} {job.Title}", text);
        });
        _ = ConnectInBackgroundAsync(launcher, client);

        // 30 sn'de bir "klavye/fare kaç saniyedir boşta": kullanıcı uzaktaysa Host hatırlatmaları Telegram'a da gönderir.
        _presenceTimer = new System.Threading.Timer(_ => _ = ReportPresenceAsync(client), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    private System.Threading.Timer? _presenceTimer;

    private readonly Dictionary<long, ApprovalCard> _approvals = [];

    private void ShowApproval(UltronClient client, ApprovalRequestDto request)
    {
        DesktopLog.Write($"Onay isteniyor #{request.Id}: {request.Summary}");
        var card = new ApprovalCard(request);
        card.Decided += async (id, approve) =>
        {
            _approvals.Remove(id);
            DesktopLog.Write($"Onay #{id}: {(approve ? "onaylandı" : "reddedildi")}");
            try { await client.ResolveApprovalAsync(id, approve); }
            catch (Exception ex) { DesktopLog.Write("Onay gönderilemedi: " + ex.Message); }
        };
        _approvals[request.Id] = card;
        card.Show();
    }

    private static async Task ReportPresenceAsync(UltronClient client)
    {
        try
        {
            await client.ReportPresenceAsync((int)Presence.IdleTime().TotalSeconds);
        }
        catch (Exception ex)
        {
            DesktopLog.Write("Bilgisayar başında bilgisi gönderilemedi: " + ex.Message);
        }
    }

    private static async Task ConnectInBackgroundAsync(HostLauncher launcher, UltronClient client)
    {
        try
        {
            await launcher.EnsureRunningAsync();
            await client.StartAsync();
        }
        catch (Exception ex)
        {
            DesktopLog.Write("Arka planda bağlanılamadı: " + ex.Message);
        }
    }

    /// <summary>Hatırlatma: Windows bildirimi + (ses açıksa) sesli. Sohbet kaydına da arayüz ekler.</summary>
    private void OnReminder(ReminderDueDto due)
    {
        DesktopLog.Write($"Hatırlatma #{due.Id} ({due.Priority}){(due.Missed ? " kaçırıldı" : "")}{(due.Repeat ? " tekrar" : "")}");
        ReminderAlerts.Track(due);
        var title = due.Missed ? $"Ultron · Kaçırılan hatırlatma ({due.DueAtLocal:dd.MM HH:mm})" : "Ultron · Hatırlatma";
        _tray?.ShowMessage(due.NeedsAcknowledge ? title + " · okudum için tıkla" : title, due.Spoken);
        // Sessiz saatlerde (23:00-07:00, konuşarak değiştirilebilir) sadece çok önemli hatırlatma sesli okunur.
        if (DesktopSettings.SoundOn && due.ShouldSpeak)
            _reminderVoice?.Enqueue(due.Spoken);
    }

    private void ExitApplication()
    {
        if (_window is not null)
            _window.AllowClose = true;
        Shutdown();
    }

    private const string RestartArg = "--restart";

    /// <summary>
    /// Tepsideki "Yeniden başlat": bütün Host süreçleri kapatılır, Ultron yeni bir kopya olarak açılır ve
    /// yeni kopya Host'u baştan başlatır (yeni kod, ayar ve user-secrets okunur).
    /// </summary>
    private void RestartApplication()
    {
        DesktopLog.Write("Yeniden başlatılıyor (Host dahil)");
        _services?.GetRequiredService<HostLauncher>().StopAllHosts();
        var args = RestartArg + (_window is { IsVisible: true } ? "" : " --tray");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false });
        ExitApplication();
    }

    private void ApplyWakeWord(bool on)
    {
        if (_wake is null)
            return;
        _wake.Enabled = on;
        _tray?.SetWakeWordChecked(on);
        if (on && _wake.SampleCount == 0)
            _tray?.ShowMessage("Ultron · Seni tanımam gerek", $"\"Ultron\" deyince dinleyebilmem için tepsi menüsünden \"Ultron ses örneklerimi kaydet\"i aç ve {WakeWordRules.SamplesWanted} kez \"Ultron\" de.");
    }

    private WakeWordSetupWindow? _wakeSetup;

    /// <summary>Ses örneği kayıt ekranı (tek pencere).</summary>
    private void ShowWakeWordSetup()
    {
        if (_wake is null || _services is null)
            return;
        if (_wakeSetup is { IsLoaded: true })
        {
            _wakeSetup.Activate();
            return;
        }
        _wakeSetup = new WakeWordSetupWindow(_services.GetRequiredService<UltronClient>(), _wake);
        _wakeSetup.Show();
    }

    /// <summary>"Ultron" duyuldu: arayüz açıksa arayüzde, değilse simgede (kartla) dinler. Ultron konuşuyorsa susar.</summary>
    private void OnWakeWord()
    {
        _reminderVoice?.Stop();
        WakeWordListener.PlayChime();
        DesktopLog.Write("Wake word: \"Ultron\"");
        if (_window is { IsVisible: true })
            WakeWordListener.RaiseMainWake();
        else
            _ = _widget?.WakeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wake?.Dispose();
        _widget?.ShutDown();
        _presenceTimer?.Dispose();
        _reminderVoice?.Dispose();
        _tray?.Dispose();
        _services?.Dispose(); // HostLauncher.Dispose: Host'u biz başlattıysak kapatır
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
