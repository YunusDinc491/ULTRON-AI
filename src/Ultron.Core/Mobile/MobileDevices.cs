using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ultron.Core.Mobile;

/// <summary>Eşleşmiş telefon: adı, hangi bölüme (profil) bağlı, anahtarının özeti (anahtarın kendisi saklanmaz).</summary>
public sealed record MobileDevice(string Id, string Name, string Profile, string TokenHash, DateTime PairedAtUtc, DateTime? LastSeenUtc = null);

/// <summary>
/// Mobil cihazlar (Faz 25, tasarım kararı): telefon Ultron'un verdiği eşleştirme koduyla bir kez tanıtılır, cihaza özel uzun bir
/// anahtar alır ve "bölümüne" (kullanıcı) bağlanır. Hafıza bilgisayarda kalır. Kayıt: %USERPROFILE%\Ultron\mobil\cihazlar.json
/// (anahtarların sadece SHA-256 özeti). Benzetme: apartman kapısının kodu bir kez söylenir, sonra her daireye kendi anahtarı verilir.
/// </summary>
public sealed class MobileDevices(TimeProvider time, string? folder = null)
{
    public static readonly string DefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "mobil");

    private readonly string _folder = folder ?? DefaultFolder;
    private readonly object _gate = new();
    private string? _pairingCode;
    private DateTime _pairingUntilUtc;
    private int _wrongAttempts;

    /// <summary>Bir eşleştirme kodu için en fazla yanlış deneme.</summary>
    public const int MaxWrongAttempts = 5;

    private string FilePath => Path.Combine(_folder, "cihazlar.json");

    /// <summary>10 dakika geçerli, 6 haneli eşleştirme kodu (masaüstünde gösterilir).</summary>
    public string NewPairingCode()
    {
        lock (_gate)
        {
            _pairingCode = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
            _pairingUntilUtc = time.GetUtcNow().UtcDateTime.AddMinutes(10);
            _wrongAttempts = 0;
            return _pairingCode;
        }
    }

    /// <returns>Cihaz ve ona özel anahtar (bir kez gösterilir); kod yanlış ya da süresi geçmişse null.</returns>
    public (MobileDevice Device, string Token)? Pair(string code, string deviceName, string profile = "ana")
    {
        lock (_gate)
        {
            if (_pairingCode is null || time.GetUtcNow().UtcDateTime > _pairingUntilUtc)
                return null;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code.Trim()), Encoding.UTF8.GetBytes(_pairingCode)))
            {
                // Deneme sınırı (güvenlik, 2026-10-31): 6 hane tahminle bulunamasın; 5 yanlıştan sonra kod geçersiz, yenisi alınmalı.
                if (++_wrongAttempts >= MaxWrongAttempts)
                    _pairingCode = null;
                return null;
            }
            _pairingCode = null; // kod tek kullanımlık
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var name = string.IsNullOrWhiteSpace(deviceName) ? "Telefon" : deviceName.Trim()[..Math.Min(deviceName.Trim().Length, 60)];
            var device = new MobileDevice(Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(), name, profile, Hash(token), time.GetUtcNow().UtcDateTime);
            var all = Load();
            all.Add(device);
            Save(all);
            return (device, token);
        }
    }

    /// <summary>Anahtar geçerli mi; geçerliyse cihaz (son görülme güncellenir).</summary>
    public MobileDevice? Authenticate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var hash = Hash(token.Trim());
        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(d => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(d.TokenHash), Encoding.UTF8.GetBytes(hash)));
            if (index < 0)
                return null;
            var seen = all[index] with { LastSeenUtc = time.GetUtcNow().UtcDateTime };
            if (all[index].LastSeenUtc is not { } last || seen.LastSeenUtc!.Value - last > TimeSpan.FromMinutes(1))
            {
                all[index] = seen;
                Save(all);
            }
            return seen;
        }
    }

    public IReadOnlyList<MobileDevice> List()
    {
        lock (_gate)
            return Load();
    }

    /// <summary>Cihazın eşleşmesini kaldırır (telefon kaybolursa).</summary>
    public bool Remove(string id)
    {
        lock (_gate)
        {
            var all = Load();
            var removed = all.RemoveAll(d => d.Id == id) > 0;
            if (removed)
                Save(all);
            return removed;
        }
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private List<MobileDevice> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<MobileDevice>>(File.ReadAllText(FilePath), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void Save(List<MobileDevice> all)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
    }
}
