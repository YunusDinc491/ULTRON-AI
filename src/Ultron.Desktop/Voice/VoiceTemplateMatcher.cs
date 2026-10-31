namespace Ultron.Desktop.Voice;

/// <summary>
/// Kişisel ses örnekleriyle "Ultron" eşleştirme (Faz 14, kullanıcının seçimi). Fotoğraf albümü benzetmesi: kullanıcının "Ultron" deyişinin
/// birkaç kaydı saklanır; her konuşma başlangıcının ses parmak izi (MFCC) bu kayıtlarla zaman esnetmeli karşılaştırılır (DTW:
/// hızlı ya da yavaş söylense de hece hece hizalanır). En yakın örneğe uzaklık eşiğin altındaysa aday; Whisper son kararı verir.
/// Saf mantık (ses donanımı bilmez): 16 kHz mono float örneklerle çalışır, test edilebilir.
/// sherpa-onnx İngilizce modeli kullanıcının Türkçe "Ultron"unu 9 denemenin 1'inde yakalayabildiği için bunun yerine geçti (2026-10-27).
/// </summary>
public sealed class VoiceTemplateMatcher
{
    public const int SampleRate = 16000;
    private const int FrameLength = 400; // 25 ms
    private const int FrameShift = 160;  // 10 ms
    private const int FftSize = 512;
    private const int MelBands = 26;
    private const int Coefficients = 13;

    private static readonly float[] Window = Enumerable.Range(0, FrameLength).Select(i => (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FrameLength - 1)))).ToArray();
    private static readonly float[][] MelFilters = BuildMelFilters();
    private static readonly float[,] Dct = BuildDct();

    private readonly List<float[][]> _templates = [];

    public int TemplateCount => _templates.Count;

    /// <summary>Örneklerin ortalama süresi (kare): konuşma başından ne kadarının karşılaştırılacağını belirler.</summary>
    public int AverageTemplateFrames => _templates.Count == 0 ? 0 : (int)_templates.Average(t => t.Length);

    /// <summary>Bir örnek ekler (baştaki ve sondaki sessizlik kırpılır). Çok kısa ya da sessizse false.</summary>
    public bool AddTemplate(float[] samples)
    {
        var trimmed = TrimSilence(samples);
        if (trimmed.Length < SampleRate / 4)
            return false;
        _templates.Add(Features(trimmed));
        return true;
    }

    public void Clear() => _templates.Clear();

    /// <summary>
    /// Konuşma parçasının başı en yakın örneğe ne kadar uzak (küçük = benzer; 0 = aynı). Örnek yoksa +∞.
    /// Parçanın sonu serbest: "Ultron, yarın hava nasıl?" gibi devam eden konuşmada sadece baştaki "Ultron" kısmı hizalanır.
    /// </summary>
    public double Distance(float[] segment)
    {
        if (_templates.Count == 0)
            return double.PositiveInfinity;
        // Sadece karşılaştırılacak baş kısım: parmak izi normalleştirmesi arkadan gelen kelimelerle ("yarın hava nasıl") bozulmasın.
        var head = TrimLeadingSilence(segment);
        var maxSamples = (int)(_templates.Max(t => t.Length) * 1.6) * FrameShift + FrameLength;
        if (head.Length > maxSamples)
            head = head[..maxSamples];
        var features = Features(head);
        return _templates.Min(t => OpenEndDtw(t, features));
    }

    // ---- DTW: örneğin tamamı, parçanın başıyla (sonu serbest) hizalanır; uzaklık yol uzunluğuna bölünür ----

    internal static double OpenEndDtw(float[][] template, float[][] segment)
    {
        var n = template.Length;
        var m = Math.Min(segment.Length, (int)(n * 1.6));
        if (n == 0 || m < n / 2)
            return double.PositiveInfinity;
        var band = Math.Max(10, n / 2); // Sakoe-Chiba bandı: hızlı/yavaş söyleyişe izin, saçma hizalamaya değil
        var prev = new double[m + 1];
        var cur = new double[m + 1];
        var prevLen = new int[m + 1];
        var curLen = new int[m + 1];
        Array.Fill(prev, double.PositiveInfinity);
        prev[0] = 0;
        for (var i = 1; i <= n; i++)
        {
            Array.Fill(cur, double.PositiveInfinity);
            var center = (int)((double)i * m / n);
            for (var j = Math.Max(1, center - band); j <= Math.Min(m, center + band); j++)
            {
                var cost = Euclid(template[i - 1], segment[j - 1]);
                double best = prev[j - 1]; var len = prevLen[j - 1];
                if (prev[j] < best) { best = prev[j]; len = prevLen[j]; }
                if (cur[j - 1] < best) { best = cur[j - 1]; len = curLen[j - 1]; }
                cur[j] = best + cost;
                curLen[j] = len + 1;
            }
            (prev, cur) = (cur, prev);
            (prevLen, curLen) = (curLen, prevLen);
        }
        var result = double.PositiveInfinity;
        for (var j = n / 2; j <= m; j++)
            if (prevLen[j] > 0)
                result = Math.Min(result, prev[j] / prevLen[j]);
        return result;
    }

    private static double Euclid(float[] a, float[] b)
    {
        double sum = 0;
        for (var k = 0; k < a.Length; k++)
        {
            var d = a[k] - b[k];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }

    // ---- Ses parmak izi: MFCC (13 katsayı, 0. katsayı = ses yüksekliği atılır) + ortalama/sapma normalleştirme ----

    internal static float[][] Features(float[] samples)
    {
        var frames = Math.Max(0, (samples.Length - FrameLength) / FrameShift + 1);
        var result = new float[frames][];
        var re = new double[FftSize];
        var im = new double[FftSize];
        for (var f = 0; f < frames; f++)
        {
            Array.Clear(re);
            Array.Clear(im);
            var offset = f * FrameShift;
            for (var i = 0; i < FrameLength; i++)
            {
                var s = samples[offset + i] - (i + offset > 0 ? 0.97f * samples[offset + i - 1] : 0f); // ön vurgu
                re[i] = s * Window[i];
            }
            Fft(re, im);
            var power = new double[FftSize / 2 + 1];
            for (var k = 0; k < power.Length; k++)
                power[k] = re[k] * re[k] + im[k] * im[k];
            var mel = new double[MelBands];
            for (var b = 0; b < MelBands; b++)
            {
                double e = 0;
                var filter = MelFilters[b];
                for (var k = 0; k < filter.Length; k++)
                    e += filter[k] * power[k];
                mel[b] = Math.Log(e + 1e-10);
            }
            var c = new float[Coefficients - 1];
            for (var k = 1; k < Coefficients; k++)
            {
                double sum = 0;
                for (var b = 0; b < MelBands; b++)
                    sum += Dct[k, b] * mel[b];
                c[k - 1] = (float)sum;
            }
            result[f] = c;
        }
        Normalize(result);
        return result;
    }

    /// <summary>Kanal etkisini (mikrofon, oda) azaltmak için her katsayıyı parçanın içinde ortalama 0, sapma 1'e çeker.</summary>
    private static void Normalize(float[][] frames)
    {
        if (frames.Length == 0)
            return;
        for (var k = 0; k < frames[0].Length; k++)
        {
            double mean = 0, sq = 0;
            foreach (var f in frames)
                mean += f[k];
            mean /= frames.Length;
            foreach (var f in frames)
                sq += (f[k] - mean) * (f[k] - mean);
            var sd = Math.Sqrt(sq / frames.Length) + 1e-6;
            foreach (var f in frames)
                f[k] = (float)((f[k] - mean) / sd);
        }
    }

    // ---- Sessizlik kırpma: 10 ms'lik pencerelerin enerjisine göre ----

    internal static float[] TrimSilence(float[] samples)
    {
        var (start, end) = SpeechBounds(samples);
        return start >= end ? [] : samples[start..end];
    }

    private static float[] TrimLeadingSilence(float[] samples)
    {
        var (start, end) = SpeechBounds(samples);
        return start >= end ? samples : samples[start..];
    }

    private static (int Start, int End) SpeechBounds(float[] samples)
    {
        const int win = FrameShift;
        var count = samples.Length / win;
        if (count == 0)
            return (0, 0);
        var energy = new double[count];
        for (var w = 0; w < count; w++)
        {
            double sum = 0;
            for (var i = 0; i < win; i++)
                sum += samples[w * win + i] * samples[w * win + i];
            energy[w] = Math.Sqrt(sum / win);
        }
        var sorted = energy.Order().ToArray();
        var noise = sorted[count / 10];
        var threshold = Math.Max(0.01, noise * 3);
        var first = Array.FindIndex(energy, e => e > threshold);
        var last = Array.FindLastIndex(energy, e => e > threshold);
        if (first < 0)
            return (0, 0);
        return (Math.Max(0, first - 5) * win, Math.Min(count, last + 6) * win); // 50 ms pay
    }

    // ---- Yardımcılar ----

    private static float[][] BuildMelFilters()
    {
        static double Mel(double hz) => 2595 * Math.Log10(1 + hz / 700);
        static double Hz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
        var bins = FftSize / 2 + 1;
        var points = Enumerable.Range(0, MelBands + 2)
            .Select(i => Hz(Mel(60) + i * (Mel(7600) - Mel(60)) / (MelBands + 1)) * FftSize / SampleRate).ToArray();
        var filters = new float[MelBands][];
        for (var b = 0; b < MelBands; b++)
        {
            filters[b] = new float[bins];
            for (var k = 0; k < bins; k++)
            {
                double w = k < points[b] || k > points[b + 2] ? 0
                    : k <= points[b + 1] ? (k - points[b]) / (points[b + 1] - points[b])
                    : (points[b + 2] - k) / (points[b + 2] - points[b + 1]);
                filters[b][k] = (float)Math.Max(0, w);
            }
        }
        return filters;
    }

    private static float[,] BuildDct()
    {
        var dct = new float[Coefficients, MelBands];
        for (var k = 0; k < Coefficients; k++)
            for (var b = 0; b < MelBands; b++)
                dct[k, b] = (float)Math.Cos(Math.PI * k * (b + 0.5) / MelBands);
        return dct;
    }

    /// <summary>Yerinde radix-2 FFT.</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            for (var i = 0; i < n; i += len)
            {
                for (var k = 0; k < len / 2; k++)
                {
                    var wr = Math.Cos(angle * k);
                    var wi = Math.Sin(angle * k);
                    var ur = re[i + k];
                    var ui = im[i + k];
                    var vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi;
                    var vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                    re[i + k] = ur + vr;
                    im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr;
                    im[i + k + len / 2] = ui - vi;
                }
            }
        }
    }
}
