namespace Ultron.Infrastructure.Setup;

/// <summary>Kurulumun durumu (Host bunu masaüstüne SetupStatusDto olarak verir).</summary>
public sealed record SetupStatus(
    bool Completed,
    bool OllamaInstalled,
    bool OllamaRunning,
    IReadOnlyList<string> MissingModels,
    IReadOnlyList<string> MissingVoiceFiles,
    long VoiceDownloadBytes,
    bool TavilyKey,
    bool SearxngRunning,
    bool DockerInstalled,
    bool GroqKey,
    bool ElevenLabsKey,
    string Address,
    string City);

/// <summary>Uzun süren kurulum adımının ilerlemesi. Percent 0-100 (bilinmiyorsa null); Error doluysa adım başarısız.</summary>
public sealed record SetupProgress(string Text, double? Percent = null, bool Done = false, string? Error = null);

/// <summary>Kısa form: hepsi isteğe bağlı.</summary>
public sealed record SetupProfile(string Address, string City, string Work, string Interests);
