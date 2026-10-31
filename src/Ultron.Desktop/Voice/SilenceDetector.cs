namespace Ultron.Desktop.Voice;

public enum ListenResult
{
    /// <summary>Dinlemeye devam.</summary>
    Continue,
    /// <summary>Konuşma oldu ve ardından yeterince sessiz kalındı: kayıt bitti.</summary>
    SpeechEnded,
    /// <summary>Hiç konuşulmadı: kayıt boşa gitti.</summary>
    NoSpeech,
    /// <summary>Üst süre doldu: o ana kadarki kayıt kullanılır.</summary>
    TooLong,
}

/// <summary>
/// "Bas, konuş, susunca bitsin": mikrofondan gelen her parçanın ses seviyesine (RMS, 0-1) bakarak
/// konuşmanın başlayıp bittiğini anlar. Ortam gürültüsü ilk anlarda ölçülür, eşik ona göre ayarlanır
/// (sessiz odada da fan sesli odada da çalışsın). Saf mantık: test edilebilir, ses donanımı bilmez.
/// </summary>
public sealed class SilenceDetector
{
    public int CalibrationMs { get; init; } = 300;
    public int MinSpeechMs { get; init; } = 200;
    /// <summary>Konuşmadan sonra bu kadar sessizlik kaydı bitirir (2026-10-29: 1,2 sn uzun cümlede düşünme duraklamasında kesiyordu → 2 sn).</summary>
    public int EndSilenceMs { get; init; } = 2000;
    public int NoSpeechTimeoutMs { get; init; } = 7000;
    public int MaxRecordingMs { get; init; } = 45_000;
    /// <summary>Gürültü tabanı ne kadar düşük olursa olsun eşik bunun altına inmez.</summary>
    public float MinThreshold { get; init; } = 0.012f;
    public float NoiseFactor { get; init; } = 3f;

    /// <summary>
    /// Gürültü tabanının üst sınırı: düğmeye basar basmaz konuşulursa ilk anlardaki KONUŞMA gürültü sanılıp eşik konuşmanın üstüne
    /// çıkıyordu ("sesimi tam duymuyor"). Sessiz oda ~0,002-0,01; fan/klima ~0,01-0,015.
    /// </summary>
    public float MaxNoise { get; init; } = 0.015f;

    private int _elapsedMs, _calibratedMs, _speechMs, _silenceMs;
    private float _noise;
    private bool _speaking;

    public bool HeardSpeech => _speaking;

    private float Threshold => Math.Max(MinThreshold, _noise * NoiseFactor);

    public ListenResult Feed(float rms, int durationMs)
    {
        _elapsedMs += durationMs;

        if (_calibratedMs < CalibrationMs)
        {
            // İlk anlar: ortam gürültüsü ölçülür. Ortalama değil EN SESSİZ an alınır (konuşma heceler arasında da düşer, gürültü
            // sabittir) ve üst sınırla kısılır: hemen konuşmaya başlansa da eşik konuşmanın üstüne çıkmaz.
            _noise = Math.Min(MaxNoise, _calibratedMs == 0 ? rms : Math.Min(_noise, rms));
            _calibratedMs += durationMs;
            return ListenResult.Continue;
        }

        var loud = rms > Threshold;
        if (!_speaking)
        {
            _speechMs = loud ? _speechMs + durationMs : 0; // kısa bir tık sesi konuşma sayılmasın
            if (_speechMs >= MinSpeechMs)
                _speaking = true;
            else if (!loud)
                _noise = Math.Min(MaxNoise, _noise * 0.95f + rms * 0.05f); // ortam değişirse eşik yavaşça uyum sağlar
        }
        else
        {
            // Histerezis: kelime arası kısa düşüşler sessizlik sayılmasın.
            _silenceMs = rms < Threshold * 0.7f ? _silenceMs + durationMs : 0;
            if (_silenceMs >= EndSilenceMs)
                return ListenResult.SpeechEnded;
        }

        if (_elapsedMs >= MaxRecordingMs)
            return _speaking ? ListenResult.TooLong : ListenResult.NoSpeech;
        if (!_speaking && _elapsedMs >= NoSpeechTimeoutMs)
            return ListenResult.NoSpeech;
        return ListenResult.Continue;
    }
}
