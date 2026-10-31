using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ultron.Core.Memories;

namespace Ultron.Infrastructure.Memories;

/// <summary>Arka planda kuyruktaki mesajlardan bilgi çıkarır. Bir işin hatası diğerlerini durdurmaz.</summary>
internal sealed class MemoryWorker(MemoryQueue queue, MemoryService memory, ILogger<MemoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (job.LearnedAnswer is { } answer)
                    await memory.ProcessLearnedAsync(job.UserText, answer, job.SourceMessageId, stoppingToken);
                else
                    await memory.ProcessAsync(job.UserText, job.SourceMessageId, stoppingToken, job.PreviousAssistant);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Mesajdan bilgi çıkarılamadı: {Text}", job.UserText);
            }
            finally
            {
                queue.MarkDone();
            }
        }
    }
}
