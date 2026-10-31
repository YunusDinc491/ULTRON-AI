using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Voice;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Ultron.Infrastructure.Voice;

/// <summary>
/// Whisper (whisper.cpp, Whisper.net üzerinden) ile Türkçe konuşmayı yazıya çevirir.
/// Model (~550 MB) ilk kullanımda bir kez yüklenir ve bellekte kalır; her istek kendi işlemcisini (processor) açar.
/// </summary>
internal sealed class WhisperSpeechToText(IOptions<VoiceOptions> options, ILogger<WhisperSpeechToText> logger) : ISpeechToText, IDisposable
{
    private Lazy<WhisperFactory>? _factory;
    private readonly object _gate = new();

    /// <summary>İş modunda (Faz 16) model ekran kartından çıkarılır; o sırada gelen ses yazıya çevrilmez.</summary>
    public bool Released { get; private set; }

    public void Release()
    {
        lock (_gate)
        {
            Released = true;
            if (_factory is { IsValueCreated: true } old)
                old.Value.Dispose();
            _factory = NewFactory();
            _warmedUp = false;
        }
        logger.LogInformation("Whisper ekran kartından boşaltıldı (İş modu)");
    }

    public void Restore() => Released = false; // model ilk kullanımda (ya da ısınmada) yeniden yüklenir

    private Lazy<WhisperFactory> NewFactory() => new(() =>
    {
        var path = options.Value.Resolve(options.Value.WhisperModel);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Whisper modeli bulunamadı: {path}");

        // Önce ekran kartı (Vulkan: NVIDIA sürücüsü yeterli, CUDA kurulumu gerekmez), olmazsa işlemci.
        // Ölçüm (Faz 6): CPU'da 10 sn konuşma 17 sn sürüyordu, sohbet için fazla yavaş.
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        var factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = true });
        logger.LogInformation("Whisper yüklendi: {Runtime}", RuntimeOptions.LoadedLibrary);
        return factory;
    });

    /// <summary>Doğrulama modeli (işlemcide); dosya yoksa null → büyük model.</summary>
    private Lazy<WhisperFactory?> WakeFactory => _wakeFactory ??= new(() =>
    {
        var path = options.Value.Resolve(options.Value.WakeModel);
        if (!File.Exists(path))
            return null;
        var factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
        logger.LogInformation("Doğrulama Whisper'ı yüklendi (işlemci): {Path}", Path.GetFileName(path));
        return factory;
    });

    private Lazy<WhisperFactory?>? _wakeFactory;

    private Lazy<WhisperFactory> Factory
    {
        get
        {
            lock (_gate)
                return _factory ??= NewFactory();
        }
    }

    public Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken = default) => RunAsync(wav, careful: true, cancellationToken);

    public Task<string> TranscribeQuickAsync(byte[] wav, CancellationToken cancellationToken = default) => RunAsync(wav, careful: false, cancellationToken);

    /// <summary>
    /// Sesli sorular için bağlam ipucu (Whisper önceki konuşma sanır): Türkçe yazım, noktalama ve kullanıcının sık kullandığı özel adlar.
    /// "Ultron" bilerek yok (aşağıda: "Ultron" kontrolünde ipucu kullanılmaz).
    /// </summary>
    internal const string Prompt = "Merhaba, bugün derslerim, sınavlarım ve planlarım hakkında konuşalım. YouTube'da şarkı açabilir misin?";

    /// <param name="careful">
    /// Sesli soru (2026-10-29, kullanıcı: "uzun bir şey söyleyince hiç anlamıyor, genelde yanlış yazıyor"): sesi yükselt, beam search (5 aday),
    /// bağlam ipucu. "Ultron" kontrolü: hızlı tahmin, ipucusuz.
    /// </param>
    private async Task<string> RunAsync(byte[] wav, bool careful, CancellationToken cancellationToken)
    {
        if (Released)
            return "";
        var sw = Stopwatch.StartNew();
        // Whisper 16 kHz mono PCM ister: ne gelirse çevir. Kısık mikrofonda ses seviyesi düzeltilir (en yüksek tepe ~%90'a, en fazla 8 kat).
        var audio = Normalize(Wav.Resample(Wav.Read(wav), 16000));
        using var stream = new MemoryStream(Wav.Write(audio));

        // Doğrulama ("Ultron mu?") küçük modelle işlemcide; sesli soru büyük modelle ekran kartında.
        var wake = careful ? null : WakeFactory.Value;
        var builder = (wake ?? Factory.Value).CreateBuilder().WithLanguage(options.Value.Language);
        if (wake is not null)
            builder = builder.WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
        else if (options.Value.WhisperThreads > 0)
            builder = builder.WithThreads(options.Value.WhisperThreads);
        if (careful)
            builder = builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5)).WithPrompt(Prompt);
        await using var processor = builder.Build();

        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
            text.Append(segment.Text);

        var result = text.ToString().Trim();
        if (careful && IsPromptEcho(result))
            result = ""; // sessiz kayıtta Whisper ipucunu tekrar edebilir
        logger.LogInformation("STT{Mode} {Seconds:F1} sn ses → {Ms} ms: {Text}", careful ? "" : " (hızlı)", audio.Samples.Length / 16000.0, sw.ElapsedMilliseconds, result);
        return SpeechText.IsLikelyHallucination(result) ? "" : result;
    }

    /// <summary>Tepe değeri %90'a çeker (en fazla 8 kat); zaten yeterince yüksek ya da sessiz kayda dokunmaz.</summary>
    internal static Wav.Audio Normalize(Wav.Audio audio)
    {
        var peak = 0f;
        foreach (var s in audio.Samples)
            peak = Math.Max(peak, Math.Abs(s));
        if (peak is < 0.01f or >= 0.5f)
            return audio;
        var gain = Math.Min(8f, 0.9f / peak);
        return audio with { Samples = audio.Samples.Select(s => s * gain).ToArray() };
    }

    internal static bool IsPromptEcho(string text) =>
        text.Length > 0 && Prompt.Contains(text.Trim().TrimEnd('.', '!', '?'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fabrika modeli tembel yükler; ağırlıklar ancak ilk işlemede ekran kartına çıkar (ölçüm: ilk kayıt ~4 sn gecikiyordu).
    /// Bu yüzden 1 sn sessizlik bir kez işlenir.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (_warmedUp || Released)
            return;
        var sw = Stopwatch.StartNew();
        using var stream = new MemoryStream(Wav.Write(new Wav.Audio(new float[16000], 16000)));
        await using var processor = Factory.Value.CreateBuilder().WithLanguage(options.Value.Language).Build();
        await foreach (var _ in processor.ProcessAsync(stream, cancellationToken)) { }
        // Doğrulama modeli de (işlemcide, küçük): ilk çağrı beklemesin.
        if (WakeFactory.Value is { } wake)
        {
            using var wakeStream = new MemoryStream(Wav.Write(new Wav.Audio(new float[16000], 16000)));
            await using var wakeProcessor = wake.CreateBuilder().WithLanguage(options.Value.Language).Build();
            await foreach (var _ in wakeProcessor.ProcessAsync(wakeStream, cancellationToken)) { }
        }
        _warmedUp = true;
        logger.LogInformation("Whisper ısındı: {Ms} ms", sw.ElapsedMilliseconds);
    }

    private volatile bool _warmedUp;

    public void Dispose()
    {
        if (_factory is { IsValueCreated: true })
            _factory.Value.Dispose();
    }
}
