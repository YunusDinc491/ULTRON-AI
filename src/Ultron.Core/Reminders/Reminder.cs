namespace Ultron.Core.Reminders;

public enum ReminderRecurrence
{
    Yok,
    HerGun,
    HaftaIci,
    HerHafta,
}

public enum ReminderStatus
{
    Bekliyor,
    /// <summary>Çaldı, kullanıcı "okudum" diyene kadar tekrar ediyor (önemli / çok önemli).</summary>
    Caliyor,
    Calindi,
    Iptal,
}

/// <summary>
/// tasarım kararı (2026-10-27): önemsiz bir kez gösterilir; önemli "okudum" denene kadar 10 dakikada bir,
/// çok önemli 10 saniyede bir tekrarlanır.
/// </summary>
public enum ReminderPriority
{
    Onemsiz,
    Onemli,
    CokOnemli,
}

public sealed class Reminder
{
    public long Id { get; set; }

    public required string Text { get; set; }

    /// <summary>Bir sonraki çalma zamanı (UTC). Tekrarlı hatırlatıcıda her çalışta (ya da "okudum"da) ileri alınır.</summary>
    public DateTime DueAtUtc { get; set; }

    public ReminderRecurrence Recurrence { get; set; }

    public ReminderPriority Priority { get; set; } = ReminderPriority.Onemsiz;

    public ReminderStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastFiredAtUtc { get; set; }

    /// <summary>Çalıyorken bir sonraki tekrar zamanı.</summary>
    public DateTime? NagAtUtc { get; set; }

    /// <summary>İlk çalışta kurulan samimi cümle; tekrarlarda yeniden üretilmez.</summary>
    public string? SpokenText { get; set; }

}

/// <summary>"Su iç" için hangi önem seviyesinin seçildiği: aynı iş tekrar istendiğinde sorulmaz.</summary>
public sealed class ReminderPreference
{
    /// <summary>Normalleştirilmiş metin (TurkishText.Normalize).</summary>
    public required string Key { get; set; }

    public required string Text { get; set; }

    public ReminderPriority Priority { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Zamanı gelen hatırlatma; arayüze (bildirim, kart, ses) gider.</summary>
/// <param name="Missed">Ultron kapalıyken zamanı geçmişti; açılışta bildiriliyor.</param>
/// <param name="Spoken">Kullanıcıya söylenecek samimi cümle ("Patron, hatırlatmamı istemiştin, su içmen gerekiyor.").</param>
/// <param name="Repeat">"Okudum" denmediği için tekrar çalıyor.</param>
/// <param name="Quiet">Sessiz saatlerde: arayüz (çok önemli değilse) sesli okumaz.</param>
public sealed record ReminderDue(long Id, string Text, DateTime DueAtLocal, bool Missed, string Spoken, ReminderPriority Priority, bool Repeat, bool Quiet = false);

public interface IReminderStore
{
    Task<Reminder> AddAsync(Reminder reminder, CancellationToken cancellationToken = default);

    Task<Reminder?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Henüz çalmamış ya da çalıp "okudum" bekleyenler.</summary>
    Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Zamanı gelmiş bekleyenler + tekrar zamanı gelmiş çalanlar.</summary>
    Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <returns>Bulunamadıysa ya da zaten bitmişse false.</returns>
    Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default);

    Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReminderPreference>> GetPreferencesAsync(CancellationToken cancellationToken = default);

    Task SetPreferenceAsync(ReminderPreference preference, CancellationToken cancellationToken = default);
}

/// <summary>Zamanı gelen hatırlatmayı kullanıcıya iletir (Host'ta: masaüstüne SignalR olayı).</summary>
public interface IReminderNotifier
{
    Task NotifyAsync(ReminderDue due, CancellationToken cancellationToken = default);
}

public static class ReminderSchedule
{
    /// <summary>Tekrarlı hatırlatıcının <paramref name="afterLocal"/>'dan sonraki ilk zamanı (yerel saatle hesaplanır: "her gün 9'da" yaz/kış saatinde de 9).</summary>
    public static DateTime? Next(DateTime dueLocal, ReminderRecurrence recurrence, DateTime afterLocal)
    {
        if (recurrence == ReminderRecurrence.Yok)
            return null;
        var next = dueLocal;
        do
        {
            next = recurrence == ReminderRecurrence.HerHafta ? next.AddDays(7) : next.AddDays(1);
            while (recurrence == ReminderRecurrence.HaftaIci && next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                next = next.AddDays(1);
        }
        while (next <= afterLocal);
        return next;
    }

    /// <summary>"Okudum" denene kadar tekrar aralığı; önemsiz için null (bir kez).</summary>
    public static TimeSpan? NagInterval(ReminderPriority priority) => priority switch
    {
        ReminderPriority.CokOnemli => TimeSpan.FromSeconds(10),
        ReminderPriority.Onemli => TimeSpan.FromMinutes(10),
        _ => null,
    };

    public static string Describe(ReminderPriority priority) => priority switch
    {
        ReminderPriority.CokOnemli => "çok önemli",
        ReminderPriority.Onemli => "önemli",
        _ => "önemsiz",
    };

    public static string Describe(ReminderRecurrence recurrence) => recurrence switch
    {
        ReminderRecurrence.HerGun => "her gün",
        ReminderRecurrence.HaftaIci => "hafta içi her gün",
        ReminderRecurrence.HerHafta => "her hafta",
        _ => "tek sefer",
    };
}
