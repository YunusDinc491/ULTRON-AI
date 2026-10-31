using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ultron.Desktop.Voice;

/// <summary>
/// "Ultron" deyince uyanır (Faz 14). Mikrofon sürekli açık (16 kHz mono); ses sadece bu bilgisayarda işlenir, kaydedilmez.
/// Sessizlikten sonra başlayan her konuşmanın başı (<see cref="SpeechOnsetSegmenter"/>) kullanıcının ses örnekleriyle karşılaştırılır
/// (<see cref="VoiceTemplateMatcher"/>, işlemcide, milisaniyeler); benziyorsa o parça Whisper'a sorulur, gerçekten "Ultron" denmişse
/// <see cref="Detected"/> çalışır. Kayıt sürerken ya da Ultron kendi adını söylerken yok sayılır.
/// Ses örnekleri: %USERPROFILE%\Ultron\voices\wake\ultron-*.wav (tepsiden "ses örneklerimi kaydet"). Örnek yoksa dinlemez.
/// Mikrofon başka uygulamada meşgulse ya da çıkarılırsa 10 sn'de bir yeniden dener.
/// </summary>
public sealed class WakeWordListener : IDisposable
{
    private const int SampleRate = VoiceTemplateMatcher.SampleRate;
    private static readonly WaveFormat Format = new(SampleRate, 16, 1);

    private readonly Func<byte[], CancellationToken, Task<string>> _transcribe;
    private readonly VoiceTemplateMatcher _matcher = new();
    private readonly SpeechOnsetSegmenter _segmenter = new();
    private readonly Lock _gate = new();
    private WaveInEvent? _waveIn;
    private System.Threading.Timer? _retry;
    private long _quietUntil;
    private int _checking;
    private bool _enabled, _disposed;

    public WakeWordListener(Func<byte[], CancellationToken, Task<string>> transcribe)
    {
        _transcribe = transcribe;
        ReloadSamples();
    }

    /// <summary>Doğrulanmış "Ultron" (ses iş parçacığından gelir).</summary>
    public event Action? Detected;

    /// <summary>Durum değişti (dinliyor / kapalı / ses örneği yok / mikrofon yok): tepsi için.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Arayüz açıkken "Ultron": arayüzdeki mikrofon akışı dinlesin (UltronApp abone olur).</summary>
    public static event Action? MainWakeRequested;

    public static void RaiseMainWake() => MainWakeRequested?.Invoke();

    public int SampleCount => _matcher.TemplateCount;

    public string Status { get; private set; } = "kapalı";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (value)
                    TryStart();
                else
                    StopMicrophone("kapalı");
            }
        }
    }

    /// <summary>Ses örneklerini diskten yeniden okur (kayıt ekranı bitince) ve gerekiyorsa dinlemeyi başlatır.</summary>
    public void ReloadSamples()
    {
        lock (_gate)
        {
            _matcher.Clear();
            if (Directory.Exists(WakeWordRules.SamplesFolder))
                foreach (var file in Directory.GetFiles(WakeWordRules.SamplesFolder, WakeWordRules.SamplePrefix + "*.wav").Order())
                {
                    try { _matcher.AddTemplate(ReadSamples(file)); }
                    catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException) { DesktopLog.Write("Ses örneği okunamadı: " + ex.Message); }
                }
            // Karşılaştırılacak baş kısım: örneklerin ~1,6 katı (devam eden cümlede sadece "Ultron" kısmı).
            _segmenter.MaxMs = Math.Clamp(_matcher.AverageTemplateFrames * 16, 1000, 2500);
            DesktopLog.Write($"Wake word ses örneği: {_matcher.TemplateCount}");
            if (_matcher.TemplateCount == 0)
                StopMicrophone("ses örneği yok");
            else if (_enabled)
                TryStart();
        }
    }

    private void TryStart()
    {
        if (_disposed || !_enabled || _waveIn is not null)
            return;
        if (_matcher.TemplateCount == 0)
        {
            SetStatus("ses örneği yok");
            return;
        }
        try
        {
            if (WaveInEvent.DeviceCount == 0)
                throw new InvalidOperationException("mikrofon yok");
            var (device, name) = Microphone.Pick();
            var waveIn = new WaveInEvent { DeviceNumber = Math.Max(0, device), WaveFormat = Format, BufferMilliseconds = 100, NumberOfBuffers = 3 };
            waveIn.DataAvailable += OnData;
            waveIn.RecordingStopped += (_, e) =>
            {
                if (e.Exception is null)
                    return;
                DesktopLog.Write("Wake word mikrofonu durdu: " + e.Exception.Message);
                lock (_gate)
                    StopMicrophone("mikrofon kesildi, yeniden deneniyor");
                ScheduleRetry();
            };
            waveIn.StartRecording();
            _waveIn = waveIn;
            _segmenter.Reset();
            SetStatus("dinliyor");
            _deviceName = name;
            WatchDevice();
            DesktopLog.Write($"Wake word dinleniyor (\"{WakeWordRules.Word}\", {_matcher.TemplateCount} ses örneği, mikrofon: {name})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NAudio.MmException)
        {
            DesktopLog.Write("Wake word başlatılamadı: " + ex.Message);
            StopMicrophone("mikrofon açılamadı, yeniden deneniyor");
            ScheduleRetry();
        }
    }

    private string? _deviceName;
    private System.Threading.Timer? _deviceWatch;

    /// <summary>
    /// 10 sn'de bir: Windows'un varsayılan mikrofonu değişti mi (mikrofon sonradan takıldı/çıkarıldı)? Değiştiyse yenisine geçilir.
    /// Eskiden dinleyici açılıştaki cihazda saatlerce kalıyordu.
    /// </summary>
    private void WatchDevice()
    {
        _deviceWatch ??= new System.Threading.Timer(_ =>
        {
            string current;
            try
            {
                current = Microphone.Pick().Name;
            }
            catch (Exception)
            {
                return;
            }
            lock (_gate)
            {
                if (_waveIn is null || current == _deviceName)
                    return;
                DesktopLog.Write($"Wake word: mikrofon değişti ({_deviceName} → {current}), yeniden bağlanılıyor");
                StopMicrophone("mikrofon değişti");
                TryStart();
            }
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void ScheduleRetry()
    {
        _retry?.Dispose();
        _retry = new System.Threading.Timer(_ =>
        {
            lock (_gate)
                TryStart();
        }, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var count = e.BytesRecorded / 2;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;

        IReadOnlyList<float[]> segments;
        lock (_gate)
            segments = _segmenter.Feed(samples);
        foreach (var segment in segments)
            OnSegment(segment);
    }

    private void OnSegment(float[] segment)
    {
        var now = Environment.TickCount64;
        // Kayıt sürüyorsa (kullanıcı zaten konuşuyor), Ultron kendi adını söylüyorsa ya da önceki aday hâlâ kontrol ediliyorsa yok say.
        if (now < Interlocked.Read(ref _quietUntil) || VoiceRecorder.AnyRecording || SpeechPlayer.SelfNameAudible
            || Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                double distance;
                lock (_gate)
                    distance = _matcher.Distance(segment);
                Report(distance, segment.Length);
                if (distance >= WakeWordRules.DistanceThreshold)
                {
                    // Teşhis: eşiğe yakın kalanlar ("Ultron" dedim ama uyanmadı) görülebilsin.
                    if (distance < WakeWordRules.DistanceThreshold + 1)
                        DesktopLog.Write($"Wake word: eşiğe yakın parça (uzaklık {distance:0.00}, {segment.Length / (double)SampleRate:0.0} sn), aday değil");
                    return;
                }

                string heard;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    heard = await _transcribe(ToWav(Padded(segment)), timeout.Token);
                }
                catch (Exception ex)
                {
                    // Doğrulanamayan aday reddedilir (2026-10-27: Whisper 5 sn'de cevap vermeyince doğrulamasız kabul yanlış uyandırdı).
                    DesktopLog.Write($"Wake word adayı (uzaklık {distance:0.00}) doğrulanamadı, yok sayıldı: {ex.Message}");
                    // Zaman aşımı ret sayılmaz (2026-10-30: soğuk model iptalleri gürültü molasını tetikleyip kullanıcının "Ultron"larını yutuyordu).
                    return;
                }
                if (heard == Ultron.Contracts.ChatHubContract.WakeCheckBusy)
                {
                    DesktopLog.Write($"Wake word adayı (uzaklık {distance:0.00}) atlandı: Ultron meşgul");
                    return; // ret değil
                }
                var ok = WakeWordRules.IsWakeText(heard);
                DesktopLog.Write($"Wake word adayı (uzaklık {distance:0.00}) → Whisper: \"{heard}\" → {(ok ? "kabul" : "ret")}");
                if (!ok)
                {
                    NoteRejection();
                    return;
                }
                if (VoiceRecorder.AnyRecording)
                    return;
                Interlocked.Exchange(ref _quietUntil, Environment.TickCount64 + (long)WakeWordRules.Cooldown.TotalMilliseconds);
                Detected?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        });
    }

    private readonly Queue<long> _rejections = new();

    /// <summary>
    /// Gürültülü ortam (2026-10-29: YouTube videosundaki konuşma mikrofona giriyordu): 30 sn içinde 3 aday reddedildiyse "Ultron"
    /// algılama 20 sn dinlenir. Bu sırada düğme ve sağ tık çalışır; video bitince algılama kendiliğinden döner.
    /// </summary>
    private void NoteRejection()
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            _rejections.Enqueue(now);
            while (_rejections.Count > 0 && now - _rejections.Peek() > WakeWordRules.NoisyWindowMs)
                _rejections.Dequeue();
            if (_rejections.Count < WakeWordRules.NoisyRejections)
                return;
            _rejections.Clear();
        }
        Interlocked.Exchange(ref _quietUntil, now + WakeWordRules.NoisyPauseMs);
        DesktopLog.Write($"Wake word: ortam gürültülü (video/konuşma), algılama {WakeWordRules.NoisyPauseMs / 1000} sn dinleniyor");
    }

    private int _segmentsSinceReport;
    private double _bestSinceReport = double.PositiveInfinity;
    private long _lastReport = Environment.TickCount64;

    /// <summary>5 dakikada bir özet: kaç konuşma parçası yakalandı, en yakını ne kadardı (mikrofon ve parçacı çalışıyor mu).</summary>
    private void Report(double distance, int samples)
    {
        lock (_gate)
        {
            _segmentsSinceReport++;
            _bestSinceReport = Math.Min(_bestSinceReport, distance);
            if (Environment.TickCount64 - _lastReport < 5 * 60_000)
                return;
            DesktopLog.Write($"Wake word özeti (son 5 dk): {_segmentsSinceReport} konuşma parçası, en yakın uzaklık {_bestSinceReport:0.00}");
            _segmentsSinceReport = 0;
            _bestSinceReport = double.PositiveInfinity;
            _lastReport = Environment.TickCount64;
        }
    }

    /// <summary>İki yana 0,3 sn sessizlik: Whisper çok kısa seste kelimeyi daha iyi yakalar.</summary>
    private static float[] Padded(float[] samples)
    {
        var pad = SampleRate * 3 / 10;
        var result = new float[samples.Length + 2 * pad];
        samples.CopyTo(result, pad);
        return result;
    }

    public static byte[] ToWav(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)v;
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(output, Format))
            writer.Write(pcm, 0, pcm.Length);
        return output.ToArray();
    }

    /// <summary>WAV (herhangi bir biçim) → 16 kHz mono float.</summary>
    public static float[] ReadSamples(string path) => ReadSamples(File.OpenRead(path));

    public static float[] ReadSamples(Stream wav)
    {
        using var reader = new WaveFileReader(wav);
        ISampleProvider provider = reader.ToSampleProvider();
        if (provider.WaveFormat.Channels > 1)
            provider = provider.ToMono();
        if (provider.WaveFormat.SampleRate != SampleRate)
            provider = new WdlResamplingSampleProvider(provider, SampleRate);
        var result = new List<float>();
        var buffer = new float[SampleRate];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
            result.AddRange(buffer.AsSpan(0, n));
        return result.ToArray();
    }

    private void StopMicrophone(string status)
    {
        if (_waveIn is { } waveIn)
        {
            _waveIn = null;
            waveIn.DataAvailable -= OnData;
            try { waveIn.StopRecording(); } catch (NAudio.MmException) { }
            waveIn.Dispose();
        }
        _segmenter.Reset();
        SetStatus(status);
    }

    private void SetStatus(string status)
    {
        if (Status == status)
            return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    /// <summary>"Dinliyorum" sesi: kısa, yükselen iki ton (ses kapalıysa çalınmaz).</summary>
    public static void PlayChime()
    {
        if (!DesktopSettings.SoundOn)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                ISampleProvider Tone(double hz, double seconds) => new SignalGenerator(22050, 1) { Frequency = hz, Gain = 0.12, Type = SignalGeneratorType.Sin }
                    .Take(TimeSpan.FromSeconds(seconds));
                using var output = new WaveOutEvent { DesiredLatency = 80 };
                var done = new TaskCompletionSource();
                output.PlaybackStopped += (_, _) => done.TrySetResult();
                output.Init(new ConcatenatingSampleProvider([Tone(880, 0.07), Tone(1320, 0.09)]));
                output.Play();
                await done.Task;
            }
            catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException)
            {
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopMicrophone("kapalı");
            _retry?.Dispose();
            _deviceWatch?.Dispose();
        }
    }
}
