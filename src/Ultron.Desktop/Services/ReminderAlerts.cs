using System.Collections.Concurrent;
using Ultron.Contracts;

namespace Ultron.Desktop.Services;

/// <summary>
/// "Okudum" bekleyen (önemli / çok önemli) hatırlatmalar. Okundu üç yoldan gelebilir: kartındaki düğme,
/// Windows bildirimine tıklama ya da "okudum" yazmak/söylemek. Hepsi buradan geçer ki arayüz ve uygulama aynı şeyi bilsin.
/// </summary>
public static class ReminderAlerts
{
    private static readonly ConcurrentDictionary<long, ReminderDueDto> Active = new();

    /// <summary>Bir hatırlatma okundu (kart düğmesi gizlenir).</summary>
    public static event Action<long>? Acknowledged;

    public static bool HasActive => !Active.IsEmpty;

    public static void Track(ReminderDueDto due)
    {
        if (due.NeedsAcknowledge)
            Active[due.Id] = due;
    }

    public static bool IsActive(long id) => Active.ContainsKey(id);

    public static async Task AcknowledgeAsync(UltronClient client, long id)
    {
        if (!Active.TryRemove(id, out _))
            return;
        try
        {
            await client.AcknowledgeReminderAsync(id);
        }
        catch (Exception ex)
        {
            DesktopLog.Write($"Okundu bildirilemedi #{id}: {ex.Message}");
        }
        Acknowledged?.Invoke(id);
    }

    public static async Task AcknowledgeAllAsync(UltronClient client)
    {
        foreach (var id in Active.Keys.ToList())
            await AcknowledgeAsync(client, id);
    }

    // "Okudum", "tamam okudum", "gördüm", "tamam" (hatırlatma çalarken söylenen "tamam" da okundu sayılır).
    private static readonly HashSet<string> AckPhrases =
    [
        "okudum", "tamam okudum", "okudum tamam", "gordum", "tamam gordum", "tamam", "tamamdir", "anladim", "tamam anladim", "ok",
    ];

    public static bool IsAcknowledgeText(string text) => AckPhrases.Contains(Normalize(text));

    /// <summary>Türkçe küçük harf, ı/ş/ç/ğ/ö/ü → i/s/c/g/o/u, noktalama → boşluk ("Okudum." → "okudum").</summary>
    private static string Normalize(string text)
    {
        var chars = text.ToLower(new System.Globalization.CultureInfo("tr-TR")).Select(c => c switch
        {
            'ı' => 'i', 'ş' => 's', 'ç' => 'c', 'ğ' => 'g', 'ö' => 'o', 'ü' => 'u',
            _ when char.IsLetterOrDigit(c) => c,
            _ => ' ',
        });
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
