namespace Ultron.Desktop.Voice;

/// <summary>
/// Sürekli mikrofon akışından "sessizlikten sonra başlayan konuşma"nın başını keser (wake word için). Kapıdaki zil gibi:
/// biri konuşmaya başlayınca çalar, konuşma sürerken tekrar çalmaz. Her parça en fazla <see cref="MaxMs"/> uzunluğundadır
/// (“Ultron, yarın hava nasıl?” cümlesinin sadece başı karşılaştırılır); kısa bir ön pay (100 ms) eklenir, ilk hece kırpılmasın.
/// Ortam gürültüsü yavaşça izlenir, eşik ona göre ayarlanır (fan sesli odada da çalışsın). Saf mantık: 16 kHz float örnekler.
/// </summary>
public sealed class SpeechOnsetSegmenter
{
    private const int Window = 160; // 10 ms
    private const int PreRollWindows = 10;

    private readonly Queue<float[]> _preRoll = new();
    private readonly List<float> _current = [];
    private readonly float[] _pending = new float[Window];
    private int _pendingCount;
    private int _quietMs = 10_000, _silenceInSegmentMs, _speechMs;
    private double _noise = 0.005;
    private bool _inSegment, _waitingForQuiet;

    /// <summary>Parçanın üst sınırı (ses örneklerinin ortalama süresinin ~1,6 katı; 1-2,5 sn).</summary>
    public int MaxMs { get; set; } = 1600;

    /// <summary>Gürültü tabanı ne kadar düşük olursa olsun eşik bunun altına inmez (sessiz oda).</summary>
    public double MinThreshold { get; init; } = 0.012;

    /// <summary>Konuşma sayılması için en az bu kadar ses (kısa bir tık, kapı sesi parça açmasın).</summary>
    public int MinSpeechMs { get; init; } = 150;

    private double Threshold => Math.Max(MinThreshold, _noise * 3);

    /// <summary>Yeni örnekleri verir; tamamlanan parçaları döndürür (çoğu çağrıda boş).</summary>
    public IReadOnlyList<float[]> Feed(ReadOnlySpan<float> samples)
    {
        List<float[]>? done = null;
        foreach (var s in samples)
        {
            _pending[_pendingCount++] = s;
            if (_pendingCount < Window)
                continue;
            _pendingCount = 0;
            if (Step((float[])_pending.Clone()) is { } segment)
                (done ??= []).Add(segment);
        }
        return done ?? (IReadOnlyList<float[]>)[];
    }

    private float[]? Step(float[] window)
    {
        double sum = 0;
        foreach (var s in window)
            sum += s * s;
        var rms = Math.Sqrt(sum / Window);
        var loud = rms > Threshold;

        if (_inSegment)
        {
            _current.AddRange(window);
            _silenceInSegmentMs = rms < Threshold * 0.7 ? _silenceInSegmentMs + 10 : 0;
            _speechMs = loud ? _speechMs + 10 : _speechMs;
            var lengthMs = _current.Count / 16;
            if (_silenceInSegmentMs < WakeWordRules.SegmentEndSilenceMs && lengthMs < MaxMs)
                return null;
            // Parça bitti: sessizlikle bittiyse yeniden başlangıç beklenir; uzunluk sınırıyla kesildiyse konuşma bitene kadar yeni parça yok.
            _inSegment = false;
            _waitingForQuiet = _silenceInSegmentMs < WakeWordRules.SegmentEndSilenceMs;
            _quietMs = _waitingForQuiet ? 0 : _silenceInSegmentMs;
            var segment = _current.ToArray();
            _current.Clear();
            return _speechMs >= MinSpeechMs ? segment : null;
        }

        if (!loud)
        {
            _noise = _noise * 0.98 + rms * 0.02; // sadece sessiz anlarda: konuşma eşiği yükseltmesin
            _quietMs += 10;
            if (_quietMs >= WakeWordRules.QuietBeforeMs)
                _waitingForQuiet = false;
        }
        else if (!_waitingForQuiet && _quietMs >= WakeWordRules.QuietBeforeMs)
        {
            _inSegment = true;
            _silenceInSegmentMs = 0;
            _speechMs = 10;
            foreach (var w in _preRoll)
                _current.AddRange(w);
            _current.AddRange(window);
            _preRoll.Clear();
            return null;
        }
        else
        {
            _quietMs = 0;
        }

        _preRoll.Enqueue(window);
        if (_preRoll.Count > PreRollWindows)
            _preRoll.Dequeue();
        return null;
    }

    public void Reset()
    {
        _preRoll.Clear();
        _current.Clear();
        _pendingCount = 0;
        _inSegment = false;
        _waitingForQuiet = false;
        _quietMs = 10_000;
    }
}
