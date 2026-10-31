using System.Globalization;
using Microsoft.Extensions.Logging;
using Ultron.Core.Text;

namespace Ultron.Core.Reminders;

/// <summary>
/// Hatırlatıcıların iş mantığı: kurma (doğrulamayla), listeleme, iptal ve zamanı gelenleri çaldırma.
/// Zamanlayıcı (Host) sadece belirli aralıklarla <see cref="FireDueAsync"/> çağırır.
/// </summary>
public sealed class ReminderService(IReminderStore store, IReminderNotifier notifier, ReminderPhrasing phrasing, TimeProvider time,
    ILogger<ReminderService> logger, Settings.UltronSettings? settings = null)
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>Zamanı bundan daha eskiyse "kaçırıldı" sayılır (Ultron kapalıydı).</summary>
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(2);

    public DateTime LocalNow => time.GetLocalNow().DateTime;

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), time.LocalTimeZone);

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), time.LocalTimeZone);

    public string Format(DateTime local) => local.ToString("d MMMM dddd HH:mm", Turkish);

    public async Task<Reminder> AddAsync(string text, DateTime dueLocal, ReminderRecurrence recurrence,
        ReminderPriority priority = ReminderPriority.Onemsiz, CancellationToken cancellationToken = default)
    {
        var reminder = await store.AddAsync(new Reminder
        {
            Text = text.Trim(),
            DueAtUtc = ToUtc(dueLocal),
            Recurrence = recurrence,
            Priority = priority,
            Status = ReminderStatus.Bekliyor,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        logger.LogInformation("Hatırlatıcı #{Id}: {Text} @ {Due} ({Recurrence}, {Priority})", reminder.Id, reminder.Text, dueLocal, recurrence, priority);
        return reminder;
    }

    /// <summary>Kullanıcının bekleyen hatırlatıcıları.</summary>
    public async Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        await store.GetPendingAsync(cancellationToken);

    public Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default) => store.CancelAsync(id, cancellationToken);

    // ---- Önem tercihleri: aynı iş için bir kez sorulur ----

    public static string PreferenceKey(string text) => TurkishText.Normalize(text);

    public async Task<ReminderPriority?> FindPreferenceAsync(string text, CancellationToken cancellationToken = default)
    {
        var key = PreferenceKey(text);
        return (await store.GetPreferencesAsync(cancellationToken)).FirstOrDefault(p => p.Key == key)?.Priority;
    }

    public Task<IReadOnlyList<ReminderPreference>> GetPreferencesAsync(CancellationToken cancellationToken = default) =>
        store.GetPreferencesAsync(cancellationToken);

    public Task RememberPreferenceAsync(string text, ReminderPriority priority, CancellationToken cancellationToken = default) =>
        store.SetPreferenceAsync(new ReminderPreference
        {
            Key = PreferenceKey(text),
            Text = text.Trim(),
            Priority = priority,
            UpdatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);

    /// <summary>
    /// Zamanı gelenleri bildirir. Önemsiz: bir kez (tekrarlıysa sonraki zamana ilerler). Önemli / çok önemli:
    /// "okudum" denene kadar 10 dk / 10 sn'de bir tekrar (<see cref="AcknowledgeAsync"/>).
    /// </summary>
    public async Task<int> FireDueAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var due = await store.GetDueAsync(nowUtc, cancellationToken);
        foreach (var r in due)
        {
            var dueLocal = ToLocal(r.DueAtUtc);
            var repeat = r.Status == ReminderStatus.Caliyor;
            var missed = !repeat && nowUtc - r.DueAtUtc > MissedAfter;
            r.SpokenText = repeat && r.SpokenText is not null ? r.SpokenText : await phrasing.PhraseAsync(r.Text, missed, cancellationToken);
            // Sessiz saatlerde önemsiz/önemli hatırlatma sesli okunmaz (sadece bildirim); çok önemli yine okunur.
            var quiet = settings is not null && await settings.IsQuietAsync(ToLocal(nowUtc), cancellationToken);
            await notifier.NotifyAsync(new ReminderDue(r.Id, r.Text, dueLocal, missed, r.SpokenText, r.Priority, repeat, quiet), cancellationToken);

            r.LastFiredAtUtc = nowUtc;
            if (ReminderSchedule.NagInterval(r.Priority) is { } interval)
            {
                r.Status = ReminderStatus.Caliyor;
                r.NagAtUtc = nowUtc + interval;
            }
            else
                Advance(r, nowUtc);
            await store.UpdateAsync(r, cancellationToken);
            logger.LogInformation("Hatırlatma çaldı #{Id}: {Text} ({Priority}{Repeat})", r.Id, r.Text, r.Priority, repeat ? ", tekrar" : "");
        }
        return due.Count;
    }

    /// <summary>"Okudum": tekrar durur; tekrarlı hatırlatıcı bir sonraki zamanına ilerler.</summary>
    /// <returns>Çalan bir hatırlatma değilse false.</returns>
    public async Task<bool> AcknowledgeAsync(long id, CancellationToken cancellationToken = default)
    {
        var r = await store.GetAsync(id, cancellationToken);
        if (r is not { Status: ReminderStatus.Caliyor })
            return false;
        Advance(r, time.GetUtcNow().UtcDateTime);
        await store.UpdateAsync(r, cancellationToken);
        logger.LogInformation("Hatırlatma okundu #{Id}: {Text}", r.Id, r.Text);
        return true;
    }

    private void Advance(Reminder r, DateTime nowUtc)
    {
        r.NagAtUtc = null;
        r.SpokenText = null;
        var next = ReminderSchedule.Next(ToLocal(r.DueAtUtc), r.Recurrence, ToLocal(nowUtc));
        if (next is { } n)
        {
            r.DueAtUtc = ToUtc(n);
            r.Status = ReminderStatus.Bekliyor;
        }
        else
            r.Status = ReminderStatus.Calindi;
    }
}
