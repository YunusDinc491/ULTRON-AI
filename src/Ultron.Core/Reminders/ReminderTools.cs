using System.Globalization;
using System.Text;
using System.Text.Json;
using Ultron.Core.Text;
using Ultron.Core.Tools;

namespace Ultron.Core.Reminders;

/// <summary>"Yarın 9'da beni ara diye hatırlat" → hatırlatıcı. Zamanı model hesaplar (prompt'ta şu anki tarih var), kod doğrular.</summary>
public sealed class SetReminderTool(ReminderService reminders) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "hatirlatici_kur",
        "Kullanıcı bir şeyi hatırlatmanı istediğinde hatırlatıcı kurar. Zamanı, sistemdeki bugünün tarih ve saatine göre hesapla " +
        "(\"yarın 9'da\" → yarının tarihi 09:00, \"20 dakika sonra\" → şimdiki saat + 20 dk).",
        """
        {"type":"object","properties":{
          "metin":{"type":"string","description":"Neyi hatırlatacağın, kısa (ör. 'Annemi ara')"},
          "zaman":{"type":"string","description":"Yerel saatle ilk çalma zamanı, biçim YYYY-MM-DDTHH:MM (ör. 2026-10-28T09:00)"},
          "tekrar":{"type":"string","enum":["yok","her_gun","hafta_ici","her_hafta"],"description":"Tekrar etmiyorsa 'yok'"},
          "onem":{"type":"string","enum":["onemsiz","onemli","cok_onemli"],"description":"Kullanıcı söylediyse önem seviyesi; söylemediyse boş bırak (araç kayıtlı tercihe bakar ya da sormanı ister)"}
        },"required":["metin","zaman"]}
        """);

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var text = ToolArgs.String(arguments, "metin");
        var when = ToolArgs.String(arguments, "zaman");
        if (text is null || when is null)
            return ToolResult.Fail("'metin' ve 'zaman' gerekli.");
        if (!TryParseLocal(when, out var dueLocal))
            return ToolResult.Fail($"'{when}' zamanı anlaşılamadı; YYYY-MM-DDTHH:MM biçiminde ver.");

        var now = reminders.LocalNow;
        if (dueLocal < now.AddMinutes(-1))
            return ToolResult.Fail($"{reminders.Format(dueLocal)} geçmişte kalıyor (şu an {reminders.Format(now)}). Tarihi bugüne göre yeniden hesapla.");

        var recurrence = ToolArgs.String(arguments, "tekrar") switch
        {
            "her_gun" => ReminderRecurrence.HerGun,
            "hafta_ici" => ReminderRecurrence.HaftaIci,
            "her_hafta" => ReminderRecurrence.HerHafta,
            _ => ReminderRecurrence.Yok,
        };

        // Önem: söylendiyse bu iş için tercih olarak kaydedilir; söylenmediyse kayıtlı tercih kullanılır, o da yoksa sorulur.
        var priority = ParsePriority(ToolArgs.String(arguments, "onem"));
        // Model önemi kendisi tahmin edip yazabiliyor (canlı deneme 3/3); kullanıcı önemden hiç bahsetmediyse yok sayılır.
        if (priority is not null && ToolRunContext.UserText is { } said && !MentionsPriority(said))
            priority = null;
        var remembered = false;
        if (priority is { } chosen)
            await reminders.RememberPreferenceAsync(text, chosen, cancellationToken);
        else if (await reminders.FindPreferenceAsync(text, cancellationToken) is { } saved)
            (priority, remembered) = (saved, true);
        else
            return ToolResult.NeedInfo(await AskPriorityAsync(cancellationToken), PriorityQuestion);

        var reminder = await reminders.AddAsync(text, dueLocal, recurrence, priority.Value, cancellationToken: cancellationToken);
        return ToolResult.Success(
            $"Hatırlatıcı kuruldu (#{reminder.Id}): \"{reminder.Text}\", {reminders.Format(dueLocal)}, {ReminderSchedule.Describe(recurrence)}, " +
            $"önem: {ReminderSchedule.Describe(priority.Value)}{(remembered ? " (kullanıcının bu iş için daha önce seçtiği önem)" : "")}.");
    }

    public const string PriorityQuestion = "Bu ne kadar önemli: önemsiz, önemli mi, çok önemli mi?";

    private async Task<string> AskPriorityAsync(CancellationToken cancellationToken)
    {
        var preferences = (await reminders.GetPreferencesAsync(cancellationToken)).Take(15).ToList();
        var known = preferences.Count == 0 ? "" :
            " Kayıtlı tercihler: " + string.Join("; ", preferences.Select(p => $"'{p.Text}' → {ReminderSchedule.Describe(p.Priority)}")) +
            ". Bu istek bunlardan biriyle AYNI işse o önemi 'onem' olarak verip aracı hemen tekrar çağır.";
        return "BİLGİ GEREKLİ: hatırlatıcı henüz KURULMADI. Kullanıcıya 'sen' diye hitap ederek, tek kısa ve samimi bir soruyla önemini sor " +
               "(ör. 'Bu ne kadar önemli: önemsiz, önemli mi, çok önemli mi?'). Seçenekler: " +
               "önemsiz (bir kez gösterilir), önemli (okudum diyene kadar 10 dakikada bir tekrarlar) ya da " +
               "çok önemli (okudum diyene kadar 10 saniyede bir tekrarlar)." + known;
    }

    /// <summary>"çok önemli", "önemsiz", "acil", "önemli değil"… Kullanıcı önemden bahsetti mi?</summary>
    internal static bool MentionsPriority(string userText)
    {
        var text = " " + TurkishText.Normalize(userText) + " ";
        return text.Contains("onem", StringComparison.Ordinal) || text.Contains(" acil", StringComparison.Ordinal);
    }

    internal static ReminderPriority? ParsePriority(string? value) => TurkishText.Normalize(value ?? "").Replace(' ', '_') switch
    {
        "onemsiz" => ReminderPriority.Onemsiz,
        "onemli" => ReminderPriority.Onemli,
        "cok_onemli" or "cokonemli" => ReminderPriority.CokOnemli,
        _ => null,
    };

    internal static bool TryParseLocal(string value, out DateTime local)
    {
        // Model bazen saniye, "Z" ya da saat dilimi ekliyor: hepsi yerel saat kabul edilir (Ultron tek kullanıcılı, tek bilgisayar).
        var cleaned = value.Trim().Replace(' ', 'T');
        var plus = cleaned.IndexOfAny(['+', 'Z'], 10);
        if (plus > 0)
            cleaned = cleaned[..plus];
        return DateTime.TryParseExact(cleaned, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
    }
}

public sealed class ListRemindersTool(ReminderService reminders) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "hatirlaticilari_listele",
        "Bekleyen (henüz çalmamış) hatırlatıcıları numaralarıyla listeler. İptal etmeden önce numarayı bulmak için de kullan.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var pending = await reminders.GetPendingAsync(cancellationToken);
        if (pending.Count == 0)
            return ToolResult.Success("Bekleyen hatırlatıcı yok.");
        var sb = new StringBuilder($"{pending.Count} bekleyen hatırlatıcı:\n");
        foreach (var r in pending)
            sb.AppendLine($"#{r.Id} · {reminders.Format(reminders.ToLocal(r.DueAtUtc))} · {r.Text} " +
                          $"({ReminderSchedule.Describe(r.Recurrence)}, {ReminderSchedule.Describe(r.Priority)}{(r.Status == ReminderStatus.Caliyor ? ", ŞU AN ÇALIYOR" : "")})");
        return ToolResult.Success(sb.ToString().TrimEnd());
    }
}

public sealed class CancelReminderTool(ReminderService reminders) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "hatirlatici_iptal",
        "Bir hatırlatıcıyı iptal eder: numarasıyla ya da metninden bir kelimeyle (\"spor olanı\" → metin=\"spor\").",
        """
        {"type":"object","properties":{
          "numara":{"type":"integer","description":"Hatırlatıcı numarası (#3 için 3); bilinmiyorsa boş bırak"},
          "metin":{"type":"string","description":"Numara yoksa hatırlatıcı metninden bir kelime (ör. spor, ilaç)"}
        }}
        """);

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgs.Long(arguments, "numara") is { } id)
            return await reminders.CancelAsync(id, cancellationToken)
                ? ToolResult.Success($"#{id} numaralı hatırlatıcı iptal edildi.")
                : ToolResult.Fail($"#{id} numaralı bekleyen bir hatırlatıcı yok.");

        // Numara yerine metin (model bazen numara alanına da metin yazıyor: "spor hatırlatıcısı numarası").
        var words = Text.TurkishText.Normalize($"{ToolArgs.String(arguments, "metin")} {ToolArgs.String(arguments, "numara")}")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && !StopWords.Contains(w)).ToList();
        if (words.Count == 0)
            return ToolResult.Fail("Hangi hatırlatıcı? Numarası ya da metninden bir kelime gerekli.");
        var pending = await reminders.GetPendingAsync(cancellationToken);
        var matches = pending.Where(r => Text.TurkishText.Normalize(r.Text).Split(' ').Any(t => words.Any(w => t.StartsWith(w[..Math.Min(w.Length, 4)], StringComparison.Ordinal)))).ToList();
        if (matches.Count != 1)
            return ToolResult.Fail(matches.Count == 0
                ? "Bu kelimeyle eşleşen bekleyen hatırlatıcı yok."
                : "Birden fazla hatırlatıcı eşleşti: " + string.Join("; ", matches.Select(r => $"#{r.Id} {r.Text}")) + ". Numarayla iptal et.");
        await reminders.CancelAsync(matches[0].Id, cancellationToken);
        return ToolResult.Success($"#{matches[0].Id} numaralı \"{matches[0].Text}\" hatırlatıcısı iptal edildi.");
    }

    private static readonly HashSet<string> StopWords = ["hatirlatici", "hatirlaticisi", "hatirlaticisini", "hatirlatma", "hatirlatmayi", "numarasi", "numara", "olani", "olan", "iptal"];
}
