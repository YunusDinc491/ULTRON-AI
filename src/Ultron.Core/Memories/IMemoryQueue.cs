namespace Ultron.Core.Memories;

/// <param name="PreviousAssistant">Ultron'un bir önceki mesajı: "Evet" gibi kısa cevapların bağlamı.</param>
/// <param name="LearnedAnswer">Doluysa bu iş web/araştırma cevabından öğrenilen bilgileri çıkarır (UserText = soru).</param>
public sealed record MemoryJob(string UserText, long? SourceMessageId, string? PreviousAssistant = null, string? LearnedAnswer = null);

/// <summary>
/// Bilgi çıkarma kuyruğu. Cevap bilgi çıkarmayı beklemez: mesaj kuyruğa atılır,
/// arka plandaki işçi sırayla işler.
/// </summary>
public interface IMemoryQueue
{
    void Enqueue(MemoryJob job);

    /// <summary>Kuyruktaki işler bitene (veya süre dolana) kadar bekler. Program kapanırken kullanılır.</summary>
    Task WaitUntilIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Ultron yeni bir hafıza kategorisi açtı (masaüstünde kısa not).</summary>
public interface ICategoryNotifier
{
    Task NotifyCategoryCreatedAsync(MemoryCategory category, string example, CancellationToken cancellationToken = default);
}

public sealed class LogCategoryNotifier(Microsoft.Extensions.Logging.ILogger<LogCategoryNotifier> logger) : ICategoryNotifier
{
    public Task NotifyCategoryCreatedAsync(MemoryCategory category, string example, CancellationToken cancellationToken = default)
    {
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "YENİ KATEGORİ: {Name} ({Example})", category.Name, example);
        return Task.CompletedTask;
    }
}
