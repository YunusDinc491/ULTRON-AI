namespace Ultron.Infrastructure.Voice;

/// <summary>
/// "Ultron havası" efekti, beş basit adım:
/// 1) Hızlandırma: perdeyi değiştirmeden konuşmayı hızlandırır (WSOLA: sesi küçük parçalara bölüp daha sık aralıkla
///    üst üste bindirir; parçalar en uyumlu noktadan eklendiği için "sincap sesi" ya da cızırtı olmaz).
/// 2) Kalınlaştırma: sesi daha yavaş "çalınmış" gibi yeniden örnekle (plak yavaş dönünce ses kalınlaşır).
/// 3) Robot tınısı: sesi alçak frekanslı bir sinüsle çarpar (halka modülasyonu: bilim kurgu robotlarının klasik sesi).
/// 4) Metalik tını: 6 ms'lik geri beslemeli gecikme (tarak filtresi) → metal rezonans.
/// 5) Kısa yankı: 90 ms gecikmeyle zayıf bir kopya → büyük bir salonda konuşuyormuş hissi.
/// </summary>
public static class VoiceEffects
{
    public static Wav.Audio ApplyUltron(Wav.Audio input, UltronVoiceEffect effect)
    {
        if (!effect.Enabled)
            return input;

        var rate = input.SampleRate;
        var samples = TimeStretch(input.Samples, rate, effect.Speed);
        samples = PitchDown(samples, effect.PitchSemitones);

        if (effect.RobotMix > 0)
            samples = RingModulate(samples, rate, effect.RobotFrequency, (float)effect.RobotMix);
        if (effect.MetallicMix > 0)
            samples = Comb(samples, delay: (int)(rate * 0.006), feedback: 0.55f, mix: (float)effect.MetallicMix);
        if (effect.EchoMix > 0)
            samples = Echo(samples, delay: (int)(rate * 0.09), mix: (float)effect.EchoMix);

        Normalize(samples, peak: 0.9f);
        return new Wav.Audio(samples, rate);
    }

    /// <summary>Perdeyi düşürür (ses de aynı oranda yavaşlar; konuşma hızı TTS tarafında telafi edilir).</summary>
    private static float[] PitchDown(float[] input, double semitones)
    {
        if (semitones == 0)
            return input;
        var factor = Math.Pow(2, semitones / 12.0); // -1.5 yarım ton → 0,917
        var output = new float[(int)(input.Length / factor)];
        for (var i = 0; i < output.Length; i++)
        {
            var pos = i * factor;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, input.Length - 1);
            var t = (float)(pos - i0);
            output[i] = input[i0] * (1 - t) + input[i1] * t;
        }
        return output;
    }

    /// <summary>
    /// Perdeyi koruyarak hızlandırır (speed 1,2 = %20 daha hızlı). 40 ms'lik Hann pencereleri %50 örtüşmeyle eklenir;
    /// her pencere, bir öncekinin doğal devamına en çok benzeyen yerden (±10 ms içinde) alınır.
    /// </summary>
    internal static float[] TimeStretch(float[] input, int rate, double speed)
    {
        if (Math.Abs(speed - 1) < 0.01 || input.Length < rate / 10)
            return input;

        var n = rate / 25;                 // pencere: 40 ms
        var hs = n / 2;                    // çıkıştaki adım
        var ha = (int)Math.Round(hs * speed); // girişteki adım
        var tol = rate / 100;              // arama payı: ±10 ms
        var window = new float[n];
        for (var i = 0; i < n; i++)
            window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / n);

        var frames = Math.Max(1, (input.Length - n - tol) / ha);
        var output = new float[frames * hs + n];
        var previous = 0;
        for (var k = 0; k < frames; k++)
        {
            var ideal = k * ha;
            var best = ideal;
            if (k > 0)
            {
                // Önceki parçanın doğal devamı (previous + hs) ile en çok örtüşen başlangıcı bul.
                var natural = previous + hs;
                var bestScore = double.MinValue;
                for (var d = -tol; d <= tol; d += 2)
                {
                    var start = ideal + d;
                    if (start < 0 || start + n > input.Length || natural + hs > input.Length)
                        continue;
                    double score = 0;
                    for (var i = 0; i < hs; i += 2)
                        score += input[start + i] * input[natural + i];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = start;
                    }
                }
            }
            if (best + n > input.Length)
                break;
            var at = k * hs;
            for (var i = 0; i < n; i++)
                output[at + i] += input[best + i] * window[i];
            previous = best;
        }
        return output;
    }

    /// <summary>Halka modülasyonu: sesin bir kısmı düşük frekanslı sinüsle çarpılır; mix arttıkça daha "robot".</summary>
    private static float[] RingModulate(float[] input, int rate, double frequency, float mix)
    {
        var output = new float[input.Length];
        var step = 2 * Math.PI * frequency / rate;
        for (var i = 0; i < input.Length; i++)
            output[i] = input[i] * (1 - mix) + input[i] * (float)Math.Sin(step * i) * mix;
        return output;
    }

    private static float[] Comb(float[] input, int delay, float feedback, float mix)
    {
        var buffer = new float[input.Length];
        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var delayed = i >= delay ? buffer[i - delay] : 0;
            buffer[i] = input[i] + delayed * feedback;
            output[i] = input[i] * (1 - mix) + buffer[i] * mix;
        }
        return output;
    }

    private static float[] Echo(float[] input, int delay, float mix)
    {
        var output = new float[input.Length + delay];
        for (var i = 0; i < input.Length; i++)
        {
            output[i] += input[i];
            output[i + delay] += input[i] * mix;
        }
        return output;
    }

    private static void Normalize(float[] samples, float peak)
    {
        var max = 0f;
        foreach (var s in samples)
            max = Math.Max(max, Math.Abs(s));
        if (max < 1e-4f)
            return;
        var gain = peak / max;
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }
}
