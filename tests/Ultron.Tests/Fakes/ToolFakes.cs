using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Briefing;
using Ultron.Core.Calendar;
using Ultron.Core.Chat;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Tools;

namespace Ultron.Tests.Fakes;

internal sealed class InMemoryReminderStore : IReminderStore
{
    private long _nextId = 1;

    public List<Reminder> Items { get; } = [];

    public Task<Reminder> AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        reminder.Id = _nextId++;
        Items.Add(reminder);
        return Task.FromResult(reminder);
    }

    public List<ReminderPreference> Preferences { get; } = [];

    public Task<Reminder?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Reminder>>(Items.Where(r => r.Status is ReminderStatus.Bekliyor or ReminderStatus.Caliyor).OrderBy(r => r.DueAtUtc).ToList());

    public Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Reminder>>(Items.Where(r =>
            (r.Status == ReminderStatus.Bekliyor && r.DueAtUtc <= nowUtc) || (r.Status == ReminderStatus.Caliyor && r.NagAtUtc <= nowUtc)).ToList());

    public Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var r = Items.FirstOrDefault(x => x.Id == id && x.Status is ReminderStatus.Bekliyor or ReminderStatus.Caliyor);
        if (r is not null)
            r.Status = ReminderStatus.Iptal;
        return Task.FromResult(r is not null);
    }

    public Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default) => Task.CompletedTask; // aynı nesne

    public Task<IReadOnlyList<ReminderPreference>> GetPreferencesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReminderPreference>>(Preferences.ToList());

    public Task SetPreferenceAsync(ReminderPreference preference, CancellationToken cancellationToken = default)
    {
        Preferences.RemoveAll(p => p.Key == preference.Key);
        Preferences.Add(preference);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingNotifier : IReminderNotifier
{
    public List<ReminderDue> Notified { get; } = [];

    public Task NotifyAsync(ReminderDue due, CancellationToken cancellationToken = default)
    {
        Notified.Add(due);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryToolAudit : IToolAuditLog
{
    public List<ToolAuditEntry> Entries { get; } = [];

    public Task AddAsync(ToolAuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ToolAuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ToolAuditEntry>>(Entries.AsEnumerable().Reverse().Take(limit).ToList());
}

internal sealed class FixedWeather(WeatherReport? report) : IWeatherService
{
    public List<string?> Requests { get; } = [];

    public Task<WeatherReport?> GetAsync(string? city, CancellationToken cancellationToken = default)
    {
        Requests.Add(city);
        return Task.FromResult(report);
    }
}

/// <summary>Testlerde araç altyapısını tek satırla kurar.</summary>
internal sealed class ToolHarness
{
    public ManualTimeProvider Time { get; }
    public InMemoryReminderStore Store { get; } = new();
    public RecordingNotifier Notifier { get; } = new();
    public InMemoryToolAudit Audit { get; } = new();
    public ReminderService Reminders { get; }

    /// <summary>Hatırlatma cümlesini üreten sahte model: tanımadığı JSON isteğinde hata verir → hazır cümleye düşülür.</summary>
    public ScriptedChatModel Phraser { get; } = new();
    public FixedWeather Weather { get; } = new(new WeatherReport("İstanbul", new WeatherNow(17, 16, 2, 12),
        [new WeatherDay(new DateOnly(2026, 10, 26), 13, 18, 20, 3)]));

    public InMemorySettingsStore SettingsStore { get; } = new();
    public UltronSettings Settings { get; }
    public InMemoryCalendarStore Calendar { get; } = new();
    public RecordingBriefingNotifier BriefingNotifier { get; } = new();
    public BriefingService Briefing { get; }

    public ToolHarness(DateTimeOffset? now = null)
    {
        Time = new ManualTimeProvider(now ?? new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
        Settings = new UltronSettings(SettingsStore, Time);
        Reminders = new ReminderService(Store, Notifier, new ReminderPhrasing(Phraser, NullLogger<ReminderPhrasing>.Instance), Time,
            NullLogger<ReminderService>.Instance, Settings);
        Briefing = new BriefingService(Settings, Calendar, Weather, Reminders, Phraser, BriefingNotifier, Time, NullLogger<BriefingService>.Instance);
    }

    public IReadOnlyList<ITool> Tools =>
    [
        new SetReminderTool(Reminders), new ListRemindersTool(Reminders), new CancelReminderTool(Reminders),
        new WeatherTool(Weather, Settings), new CalculatorTool(),
        new ShowSettingsTool(Settings), new ChangeSettingTool(Settings),
        new AddClassTool(Calendar, Time), new ListClassesTool(Calendar), new DeleteClassTool(Calendar),
        new DaySummaryTool(Briefing, Time),
    ];

    public ToolRunner Runner(IChatModel chat) => new(chat, Tools, Audit, Time, NullLogger<ToolRunner>.Instance);
}

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Values));

    public Task SetAsync(string key, string value, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCalendarStore : ICalendarStore
{
    private long _nextId = 1;

    public List<CalendarEvent> Items { get; } = [];

    public Task<CalendarEvent> AddAsync(CalendarEvent item, CancellationToken cancellationToken = default)
    {
        item.Id = _nextId++;
        Items.Add(item);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<CalendarEvent>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CalendarEvent>>(Items.ToList());

    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.RemoveAll(x => x.Id == id) > 0);

    public Task UpdateAsync(CalendarEvent item, CancellationToken cancellationToken = default) => Task.CompletedTask; // aynı nesne
}

internal sealed class RecordingBriefingNotifier : IBriefingNotifier
{
    public List<(string Text, bool Late)> Delivered { get; } = [];

    public Task NotifyBriefingAsync(string text, bool late, CancellationToken cancellationToken = default)
    {
        Delivered.Add((text, late));
        return Task.CompletedTask;
    }
}
