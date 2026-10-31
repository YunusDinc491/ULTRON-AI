using System.Windows;
using System.Windows.Input;
using Ultron.Contracts;
using Ultron.Desktop.Services;
using Ultron.Desktop.Voice;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ultron.Desktop.Widget;

/// <summary>
/// Masaüstünde hep duran Ultron simgesi (kullanıcının isteği). Arayüz küçültülünce ya da gizlenince simge ekranda kalır.
/// Simge sadece arayüz kapalıyken görünür (arayüz açılınca gizlenir, küçültülünce gelir). Sol tık: arayüzü aç. Sürükle: taşı (yeri hatırlanır). Sağ tık: arayüz açılmadan dinler, cevabı yanında
/// açılan kartta gösterir ve (ses açıksa) söyler. Konuşurken tekrar sağ tık: hemen bitir; Ultron konuşurken sağ tık: sustur ve dinle.
/// </summary>
public partial class WidgetWindow : Window
{
    private const int MaxSpokenSentences = 2; // (2026-10-27): sesli cevap 1-2 cümle

    private readonly UltronClient _client;
    private readonly MainWindow _main;
    private readonly VoiceRecorder _recorder = new();
    private readonly SpeechPlayer _player;
    private readonly AnswerCard _card = new();
    private Point _downAt;
    private bool _pressed, _dragged;
    private CancellationTokenSource? _finishListening, _abort;
    private bool _busy;

    public WidgetWindow(UltronClient client, MainWindow main)
    {
        InitializeComponent();
        _client = client;
        _main = main;
        _player = new SpeechPlayer(client.SpeakAsync);
        _player.LevelChanged += level => Dispatcher.InvokeAsync(() => Emblem.SetLevel(level));
        _player.SpeakingChanged += speaking => Dispatcher.InvokeAsync(() =>
        {
            if (speaking)
                Emblem.State = EmblemState.Speaking;
            else if (Emblem.State == EmblemState.Speaking)
                Emblem.State = EmblemState.Idle;
        });
        _recorder.LevelChanged += level => Dispatcher.InvokeAsync(() => Emblem.SetLevel(level));
        _card.Opened += () => _main.ShowAndFocus();
        // kullanıcı: arayüz açılınca simge gitsin, gizlenince / küçültülünce geri gelsin.
        _main.IsVisibleChanged += (_, _) => SyncWithMain();
        Loaded += (_, _) => SyncWithMain();

        var (left, top) = DesktopSettings.WidgetPosition;
        var area = SystemParameters.WorkArea;
        // İlk açılış: sağ altta ama kenar panelinin (440 px) solunda, panelin yazı kutusunu kapatmasın.
        Left = Math.Clamp(left ?? area.Right - 440 - Width - 12, area.Left, area.Right - Width);
        Top = Math.Clamp(top ?? area.Bottom - Height - 24, area.Top, area.Bottom - Height);

        MouseLeftButtonDown += OnLeftDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = OnTalkAsync();
        };
        LocationChanged += (_, _) =>
        {
            if (_card.IsVisible)
                _card.PlaceNear(Bounds);
        };
    }

    private void SyncWithMain()
    {
        if (WindowBridge.Capturing)
            return;
        if (_main.IsVisible)
        {
            Hide();
            _card.Hide();
        }
        else if (!IsVisible)
        {
            Show();
        }
    }

    private Rect Bounds => new(Left, Top, ActualWidth, ActualHeight);

    // ---- Sol tık ve sürükleme ----

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragged = false;
        _downAt = e.GetPosition(this);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed)
            return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _downAt.X) + Math.Abs(p.Y - _downAt.Y) < 6)
            return;
        _dragged = true;
        _pressed = false;
        DragMove(); // bırakılana kadar sürer
        DesktopSettings.WidgetPosition = (Left, Top);
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        var click = _pressed && !_dragged;
        _pressed = false;
        // Simge sadece arayüz kapalıyken görünür: tık = arayüzü aç (simge gizlenir).
        if (click)
            _main.ShowAndFocus();
    }

    // ---- Sağ tık: arayüzsüz konuşma ----

    /// <summary>"Ultron" duyuldu (arayüz kapalıyken): sağ tık gibi dinler; cevaptan sonra 10 sn "Ultron" demeden ek soru beklenir.</summary>
    public Task WakeAsync()
    {
        if (_finishListening is { IsCancellationRequested: false })
            return Task.CompletedTask; // zaten dinliyor
        return OnTalkAsync(wake: true);
    }

    private async Task OnTalkAsync(bool wake = false)
    {
        // Dinlerken tekrar sağ tık: konuşma bitti say.
        if (_finishListening is { IsCancellationRequested: false })
        {
            _finishListening.Cancel();
            return;
        }
        if (_busy && !_player.IsSpeaking)
            return; // düşünüyor: bitmesini bekle
        _player.Stop(); // konuşuyorsa sustur (söz kesme)
        _abort?.Cancel();

        if (_client.State != HubConnectionState.Connected)
        {
            _card.ShowStatus("BAĞLANTI YOK");
            _card.SetAnswer("Ultron'un arka plan servisine bağlanılamadı. Biraz sonra tekrar dene; olmazsa tepsiden \"Yeniden başlat\".");
            _card.PlaceNear(Bounds);
            _card.Finish("BAĞLANTI YOK");
            return;
        }

        _busy = true;
        _abort = new CancellationTokenSource();
        var abort = _abort.Token;
        try
        {
            // Her sesli konuşmada cevaptan sonra ek soru turu (2026-10-29: 10 sn; sağ tıkta da). Sessiz kalınırsa biter.
            for (var followUp = false; ; followUp = true)
            {
                _finishListening = new CancellationTokenSource();
                _card.ShowStatus(followUp ? "DEVAM EDEBİLİRSİN… (10 sn)" : "DİNLİYOR… (bitince sus ya da tekrar sağ tık)");
                _card.PlaceNear(Bounds);
                Emblem.State = EmblemState.Listening;
                _ = _client.PrepareVoiceAsync();

                var wav = await _recorder.RecordAsync(_finishListening.Token, abort, followUp ? WakeWordRules.FollowUpMs : null);
                _finishListening.Dispose();
                _finishListening = null;
                if (wav is null)
                {
                    if (!followUp)
                        _card.SetAnswer("Sesini duyamadım.");
                    _card.Finish(followUp ? "ÇEVRİMİÇİ" : "SESSİZ");
                    return;
                }

                Emblem.State = EmblemState.Thinking;
                _card.ShowStatus("ANLIYOR…");
                var text = (await _client.TranscribeAsync(wav, abort)).Trim();
                if (text.Length == 0)
                {
                    _card.SetAnswer("Ne dediğini anlayamadım, tekrar söyler misin?");
                    _card.Finish("ANLAŞILMADI");
                    return;
                }

                if (ReminderAlerts.HasActive && ReminderAlerts.IsAcknowledgeText(text))
                {
                    await ReminderAlerts.AcknowledgeAllAsync(_client);
                    _card.ShowStatus("TAMAM", text);
                    _card.SetAnswer("Tamam, hatırlatmayı kapattım.");
                    _player.Enqueue("Tamam, hatırlatmayı kapattım.");
                    _card.Finish("ÇEVRİMİÇİ");
                    return;
                }

                _card.SetAnswer("");
                await AnswerAsync(text, abort);
                // Ultron konuşmayı bitirsin, sonra ek soruyu dinle (kendi sesini kaydetmesin).
                while (_player.IsSpeaking)
                    await Task.Delay(150, abort);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DesktopLog.Write("Simgeden konuşma hatası: " + ex.Message);
            _card.SetAnswer("Bir sorun oldu: " + ex.Message);
            _card.Finish("HATA");
        }
        finally
        {
            _busy = false;
            if (!_player.IsSpeaking)
                Emblem.State = EmblemState.Idle;
        }
    }

    private async Task AnswerAsync(string text, CancellationToken cancellationToken)
    {
        _card.ShowStatus("DÜŞÜNÜYOR…", text);
        var conversation = await _client.GetCurrentConversationAsync();
        var splitter = DesktopSettings.SoundOn ? new SentenceSplitter { MaxSentences = MaxSpokenSentences, StopAtList = true, QuickStart = true } : null;
        var answer = "";

        // "Ekrana bak": simge ve kart bir an gizlenir (görüntüye girmesin), ekran görüntüsü soruya eklenir.
        byte[]? screen = null;
        if (Services.ImageTools.IsScreenRequest(text))
        {
            Hide();
            _card.Hide();
            await Task.Delay(250, cancellationToken);
            try
            {
                screen = await Task.Run(Services.ImageTools.CaptureScreen, cancellationToken);
            }
            finally
            {
                Show();
                _card.ShowStatus("EKRANA BAKIYOR…", text);
                _card.PlaceNear(Bounds);
            }
        }

        var events = screen is null
            ? _client.SendAsync(conversation.Id, text, cancellationToken)
            : _client.SendWithImageAsync(conversation.Id, text, screen, fromScreen: true, cancellationToken);
        await foreach (var e in events)
        {
            switch (e.Type)
            {
                case ChatEventTypes.Chunk when e.Text is not null:
                    answer += e.Text;
                    _card.SetAnswer(answer);
                    _card.ShowStatus("YAZIYOR…");
                    if (splitter is not null)
                        foreach (var sentence in splitter.Push(e.Text))
                            _player.Enqueue(sentence);
                    break;
                case ChatEventTypes.Intent when e.Detailed == true && splitter is not null:
                    // Ayrıntı istendi: okunmaz, kartta yazılır.
                    splitter = null;
                    _player.Enqueue("Ekrana yazıyorum.");
                    break;
                case ChatEventTypes.Research when e.Text is not null:
                    _card.ShowStatus("ARAŞTIRMA · " + e.Text.ToUpper(new System.Globalization.CultureInfo("tr-TR")));
                    break;
                case ChatEventTypes.Documents:
                    _card.ShowStatus("NOTLARI OKUYOR…");
                    break;
                case ChatEventTypes.Web:
                    _card.ShowStatus(e.FromCache == true ? "ÖNBELLEK" : "WEB'DE ARIYOR…");
                    break;
                case ChatEventTypes.Completed:
                    if (e.Text is not null)
                        _card.SetAnswer(answer = e.Text);
                    if (splitter?.Flush() is { } rest && (e.Text is null || e.Text.Contains(rest)))
                        _player.Enqueue(rest);
                    if (splitter is { ReachedList: true } or { Truncated: true })
                        _player.Enqueue("Devamını karta yazdım.");
                    break;
            }
        }

        _card.Finish("ÇEVRİMİÇİ");
    }

    public void ShutDown()
    {
        _abort?.Cancel();
        _player.Dispose();
        _card.Close();
        Close();
    }
}
