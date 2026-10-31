namespace Ultron.Infrastructure.Voice;

/// <summary>
/// appsettings.json içindeki "Voice" bölümü. Model dosyaları repo dışında: %USERPROFILE%\Ultron\models.
/// Bilerek AppData değil: Claude uygulaması (MSIX paketi) kendi açtığı süreçlerin AppData yazımlarını sanal bir kopyaya
/// yönlendiriyor; oradan indirilen modeller Ultron dışarıdan açılınca görünmüyordu.
/// </summary>
public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    /// <summary>Boşsa %USERPROFILE%\Ultron\models.</summary>
    public string? ModelsDirectory { get; set; }

    public string WhisperModel { get; set; } = @"whisper\ggml-large-v3-turbo-q5_0.bin";

    /// <summary>
    /// "Ultron mı dendi?" doğrulaması için küçük model, İŞLEMCİDE (2026-10-30: ekran kartı dolu iken turbo 1 sn sesi 13 sn'de
    /// doğrulayamıyordu). Dosya yoksa büyük model kullanılır.
    /// </summary>
    public string WakeModel { get; set; } = @"whisper\ggml-small-q5_1.bin";

    public string Language { get; set; } = "tr";

    /// <summary>Whisper'ın kullanacağı işlemci çekirdeği (0 = otomatik).</summary>
    public int WhisperThreads { get; set; }

    public string PiperExecutable { get; set; } = @"piper\piper\piper.exe";

    public string PiperVoice { get; set; } = @"piper\tr_TR-dfki-medium.onnx";

    /// <summary>Piper konuşma hızı: küçüldükçe hızlanır. Efektteki kalınlaştırma sesi yavaşlattığı için biraz hızlı üretilir.</summary>
    public double PiperLengthScale { get; set; } = 0.92;

    public UltronVoiceEffect Effect { get; set; } = new();

    /// <summary>"Chatterbox" (canlı, tonlamalı; Python servisi, ekran kartı) ya da "Piper" (hızlı, düz). Chatterbox hazır değilse Piper'a düşülür.</summary>
    public string Engine { get; set; } = "Chatterbox";

    public ChatterboxOptions Chatterbox { get; set; } = new();

    public string ResolveRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron");

    public string ResolveModelsDirectory() =>
        string.IsNullOrWhiteSpace(ModelsDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "models")
            : Environment.ExpandEnvironmentVariables(ModelsDirectory);

    public string Resolve(string relative) => Path.Combine(ResolveModelsDirectory(), relative);
}

/// <summary>
/// Chatterbox Multilingual servisi (tools/tts-server/server.py). Host açılışta kendisi başlatır.
/// Yollar boşsa %USERPROFILE%\Ultron altındaki varsayılanlar kullanılır.
/// </summary>
public sealed class ChatterboxOptions
{
    public string Url { get; set; } = "http://127.0.0.1:5201";

    /// <summary>Boşsa %USERPROFILE%\Ultron\tts\.venv\Scripts\python.exe.</summary>
    public string? Python { get; set; }

    /// <summary>Boşsa Host'un klasöründen yukarı çıkılıp tools\tts-server\server.py aranır.</summary>
    public string? Script { get; set; }

    /// <summary>Referans ses: Ultron bu sesin tınısıyla konuşur. Boşsa %USERPROFILE%\Ultron\voices\referans.wav (yoksa modelin kendi sesi).</summary>
    public string? ReferenceVoice { get; set; }

    /// <summary>Duygu/vurgu yoğunluğu (0,25-2). Yüksek = daha canlı, daha dramatik.</summary>
    public double Exaggeration { get; set; } = 0.6;

    /// <summary>
    /// Düşük = daha yavaş, daha oturaklı konuşma. 0,8: hız Chatterbox'ın kendi ritminden gelir, zaman esnetme az kalır
    /// (kullanıcı örnekleri dinleyip "D"yi seçti, 2026-10-27).
    /// </summary>
    public double CfgWeight { get; set; } = 0.8;

    public double Temperature { get; set; } = 0.8;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Chatterbox sesi zaten doğal; Piper'daki ağır efekt yerine hafif bir "Ultron" dokunuşu.</summary>
    /// <remarks>
    /// 2026-10-27: kullanıcı 4 örneği dinleyip "D · hafif Ultron dokunuşu"nu seçti. Önceki 1,55 kat + belirgin robot/metal ayarı, aslında hep
    /// Piper'a uygulanıyordu (Chatterbox isteği 400 alıyordu); gerçek Chatterbox'ta hafif efekt + 1,15 kat yeterince hızlı ve akıcı.
    /// </remarks>
    public UltronVoiceEffect Effect { get; set; } = new() { PitchSemitones = 0, Speed = 1.15, RobotMix = 0.1, MetallicMix = 0.08, EchoMix = 0.03 };
}

/// <summary>"Ultron havası": hafif kalınlaştırma, metalik tını, kısa yankı. Hepsi kapatılabilir.</summary>
public sealed class UltronVoiceEffect
{
    public bool Enabled { get; set; } = true;

    /// <summary>Perde kaydırma (yarım ton, eksi = kalın). Ses yavaşlayarak kalınlaşır; Piper hızıyla dengelenir.</summary>
    public double PitchSemitones { get; set; } = -1.5;

    /// <summary>Metalik tını: çok kısa geri beslemeli gecikme (tarak filtresi) karışım oranı (0 = kapalı).</summary>
    public double MetallicMix { get; set; } = 0.25;

    /// <summary>Yankı karışımı (0 = kapalı).</summary>
    public double EchoMix { get; set; } = 0.12;

    /// <summary>Konuşma hızı (perde değişmez): 1 = olduğu gibi, 1,2 = %20 daha hızlı.</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>Robot tınısı (halka modülasyonu) karışımı: 0 = kapalı, 0,3 = hafif, 0,6 = belirgin.</summary>
    public double RobotMix { get; set; }

    /// <summary>Robot tınısının frekansı (Hz). Düşük = titreşimli, yüksek = metalik.</summary>
    public double RobotFrequency { get; set; } = 60;
}
