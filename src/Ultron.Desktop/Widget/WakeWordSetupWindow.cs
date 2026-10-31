using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ultron.Desktop.Services;
using Ultron.Desktop.Voice;

namespace Ultron.Desktop.Widget;

/// <summary>
/// "Ultron" ses örneklerinin kaydı (Faz 14). kullanıcı birkaç kez (<see cref="WakeWordRules.SamplesWanted"/>) "Ultron" der; her kayıt Whisper'la doğrulanır (gerçekten "Ultron" mu),
/// sessizliği kırpılıp %USERPROFILE%\Ultron\voices\wake\ultron-N.wav olarak saklanır. Ses bu bilgisayardan çıkmaz.
/// Tepsiden açılır; wake word açık ama örnek yoksa Ultron bildirimle hatırlatır.
/// </summary>
public sealed class WakeWordSetupWindow : Window
{
    private static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4));

    private readonly UltronClient _client;
    private readonly WakeWordListener _listener;
    private readonly VoiceRecorder _recorder = new();
    private readonly TextBlock _progress = new() { FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 44, Foreground = Muted };
    private readonly Button _record = new() { Content = "Kaydet", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _reset = new() { Content = "Baştan başla", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _close = new() { Content = "Kapat", Padding = new Thickness(14, 8, 14, 8) };
    private CancellationTokenSource? _abort;

    public WakeWordSetupWindow(UltronClient client, WakeWordListener listener)
    {
        _client = client;
        _listener = listener;
        Title = "Ultron · Ses örnekleri";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = Back;
        Foreground = Text;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var b in new[] { _record, _reset, _close })
        {
            b.Background = b == _record ? Accent : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A));
            b.Foreground = Text;
            b.BorderThickness = new Thickness(0);
            buttons.Children.Add(b);
        }
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = $"\"{WakeWordRules.Word}\" ses örneklerin", FontSize = 18, FontWeight = FontWeights.SemiBold },
                new TextBlock
                {
                    Text = $"Ultron seni bu örneklerden tanır. \"Kaydet\"e bas, normal sesinle bir kez \"{WakeWordRules.Word}\" de ve sus. " +
                           $"{WakeWordRules.SamplesWanted} kez tekrarla; biraz farklı tonlarda söylersen daha iyi tanır. Kayıtlar sadece bu bilgisayarda kalır.",
                    TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 8, 0, 0),
                },
                _progress,
                _status,
                buttons,
            },
        };

        _record.Click += async (_, _) => await RecordOneAsync();
        _reset.Click += (_, _) => ResetSamples();
        _close.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _abort?.Cancel();
            _listener.ReloadSamples();
        };
        UpdateProgress();
    }

    private static string[] Existing() =>
        Directory.Exists(WakeWordRules.SamplesFolder) ? Directory.GetFiles(WakeWordRules.SamplesFolder, WakeWordRules.SamplePrefix + "*.wav") : [];

    private void UpdateProgress()
    {
        var count = Existing().Length;
        _progress.Text = $"{Math.Min(count, WakeWordRules.SamplesWanted)} / {WakeWordRules.SamplesWanted}";
        _progress.Foreground = count >= WakeWordRules.SamplesWanted ? Brushes.LightGreen : Text;
        if (count >= WakeWordRules.SamplesWanted)
        {
            _status.Text = $"Tamam! Artık \"{WakeWordRules.Word}\" deyince dinliyorum. İstersen birkaç örnek daha ekleyebilirsin (farklı mesafe ya da ton).";
            _record.Content = "Bir örnek daha";
        }
    }

    private async Task RecordOneAsync()
    {
        _record.IsEnabled = _reset.IsEnabled = false;
        _abort = new CancellationTokenSource();
        try
        {
            _status.Foreground = Text;
            _status.Text = $"Dinliyorum… şimdi \"{WakeWordRules.Word}\" de.";
            using var finish = new CancellationTokenSource();
            var wav = await _recorder.RecordAsync(finish.Token, _abort.Token, noSpeechTimeoutMs: 6000);
            if (wav is null)
            {
                Warn("Sesini duyamadım. Mikrofona biraz daha yakın, normal sesle tekrar dene.");
                return;
            }

            var samples = VoiceTemplateMatcher.TrimSilence(WakeWordListener.ReadSamples(new MemoryStream(wav)));
            var seconds = samples.Length / (double)VoiceTemplateMatcher.SampleRate;
            if (seconds is < 0.25 or > 2.0)
            {
                Warn(seconds < 0.25 ? "Çok kısa oldu, tekrar dene." : $"Uzun oldu: sadece tek bir kez \"{WakeWordRules.Word}\" de, sonra sus.");
                return;
            }

            _status.Text = "Kontrol ediyorum…";
            var heard = (await _client.TranscribeAsync(wav, _abort.Token)).Trim();
            if (!WakeWordRules.IsWakeText(heard))
            {
                Warn($"\"{heard}\" duydum; sadece \"{WakeWordRules.Word}\" demen gerekiyor. Tekrar dene.");
                return;
            }

            Directory.CreateDirectory(WakeWordRules.SamplesFolder);
            var next = WakeWordRules.NextSampleNumber(Existing());
            await File.WriteAllBytesAsync(Path.Combine(WakeWordRules.SamplesFolder, $"{WakeWordRules.SamplePrefix}{next}.wav"), WakeWordListener.ToWav(samples));
            DesktopLog.Write($"Wake word ses örneği kaydedildi: {next} ({seconds:0.00} sn)");
            _status.Foreground = Brushes.LightGreen;
            _status.Text = $"Aldım ({seconds:0.0} sn). " + (Existing().Length < WakeWordRules.SamplesWanted ? "Bir daha \"Kaydet\"." : "");
            UpdateProgress();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DesktopLog.Write("Ses örneği kaydı hatası: " + ex.Message);
            Warn("Bir sorun oldu: " + ex.Message);
        }
        finally
        {
            _record.IsEnabled = _reset.IsEnabled = true;
        }
    }

    private void ResetSamples()
    {
        if (MessageBox.Show(this, $"Kayıtlı bütün \"{WakeWordRules.Word}\" ses örnekleri silinsin mi?", "Ultron", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        foreach (var file in Existing())
            File.Delete(file);
        _record.Content = "Kaydet";
        _status.Foreground = Muted;
        _status.Text = "Silindi. Baştan kaydedebilirsin.";
        UpdateProgress();
    }

    private void Warn(string message)
    {
        _status.Foreground = Accent;
        _status.Text = message;
    }
}
