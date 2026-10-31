using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

namespace Ultron.Mobile.Services;

/// <summary>
/// Telefondan bilgisayardaki Ultron'a bağlantı (Faz 25). Adres: bilgisayarın Tailscale adresi (100.x.x.x), port 5199.
/// İlk açılışta Ultron'un gösterdiği 6 haneli kodla eşleşilir; dönen cihaz anahtarı telefonun güvenli deposunda (SecureStorage)
/// saklanır, her istekte gönderilir. Hafıza ve sohbetler bilgisayarda: telefon sadece kumanda.
/// </summary>
public sealed class UltronConnection : IAsyncDisposable
{
    private const string AddressKey = "ultron_adres", TokenKey = "ultron_anahtar", NameKey = "ultron_cihaz";
    public const int Port = 5199;

    private HubConnection? _hub;

    public string? Address { get; private set; }

    public string? DeviceName { get; private set; }

    public bool Paired { get; private set; }

    public HubConnection Hub => _hub ?? throw new InvalidOperationException("Ultron'a bağlı değil.");

    public HubConnectionState State => _hub?.State ?? HubConnectionState.Disconnected;

    public event Action? StateChanged;

    /// <summary>Kayıtlı eşleşme var mı (uygulama açılışında).</summary>
    public async Task<bool> LoadAsync()
    {
        Address = Preferences.Get(AddressKey, null);
        DeviceName = Preferences.Get(NameKey, null);
        Paired = Address is not null && await SecureStorage.GetAsync(TokenKey) is { Length: > 0 };
        return Paired;
    }

    public Uri BaseUri => new($"http://{Address}:{Port}/");

    /// <returns>Hata mesajı; başarılıysa null.</returns>
    public async Task<string?> PairAsync(string address, string code, string deviceName)
    {
        address = address.Trim().Replace("http://", "").TrimEnd('/');
        if (address.Contains(':'))
            address = address[..address.IndexOf(':')];
        using var http = new HttpClient { BaseAddress = new Uri($"http://{address}:{Port}/"), Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            using var response = await http.PostAsJsonAsync("mobil/eslestir", new { code = code.Trim(), name = deviceName });
            if (!response.IsSuccessStatusCode)
                return response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Kod yanlış ya da süresi geçmiş. Ultron'dan yeni kod al." : $"Ultron cevap vermedi ({(int)response.StatusCode}).";
            var result = await response.Content.ReadFromJsonAsync<PairResult>();
            if (result?.Token is not { Length: > 0 } token)
                return "Eşleşme cevabı okunamadı.";
            Preferences.Set(AddressKey, address);
            Preferences.Set(NameKey, result.Name ?? deviceName);
            await SecureStorage.SetAsync(TokenKey, token);
            Address = address;
            DeviceName = result.Name ?? deviceName;
            Paired = true;
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Bilgisayara ulaşılamadı. Tailscale iki cihazda da açık mı, adres doğru mu?";
        }
    }

    public async Task UnpairAsync()
    {
        await DisconnectAsync();
        Preferences.Remove(AddressKey);
        Preferences.Remove(NameKey);
        SecureStorage.Remove(TokenKey);
        Paired = false;
    }

    public async Task<string?> TokenAsync() => await SecureStorage.GetAsync(TokenKey);

    /// <summary>Hub'a bağlanır (zaten bağlıysa bir şey yapmaz); kopunca kendiliğinden yeniden dener.</summary>
    public async Task ConnectAsync()
    {
        if (_hub is { State: HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting })
            return;
        var token = await TokenAsync() ?? throw new InvalidOperationException("Eşleşme yok.");
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(BaseUri, ChatHubContract.Path.TrimStart('/')), o =>
            {
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                o.Headers["X-Ultron-Token"] = token;
            })
            .WithAutomaticReconnect()
            .WithServerTimeout(TimeSpan.FromMinutes(3))
            .Build();
        _hub.Reconnecting += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnected += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Closed += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
        await _hub.StartAsync();
        StateChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        if (_hub is not null)
            await _hub.DisposeAsync();
        _hub = null;
        StateChanged?.Invoke();
    }

    /// <summary>Cihaz anahtarlı HttpClient (güncelleme uçları).</summary>
    public async Task<HttpClient> HttpAsync()
    {
        var http = new HttpClient { BaseAddress = BaseUri, Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.Add("X-Ultron-Token", await TokenAsync());
        return http;
    }

    /// <summary>Bilgisayardaki Ultron daha yeni bir APK sunuyorsa onu döner (karar 2026-10-30: güncelleme uygulama içinden).</summary>
    public async Task<UpdateInfo?> CheckUpdateAsync()
    {
        using var http = await HttpAsync();
        http.Timeout = TimeSpan.FromSeconds(15);
        using var response = await http.GetAsync("mobil/guncelleme");
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            return null;
        var info = await response.Content.ReadFromJsonAsync<UpdateInfo>();
        return info is not null && int.TryParse(AppInfo.Current.BuildString, out var current) && info.Surum > current ? info : null;
    }

    /// <summary>APK'yı önbelleğe indirir; dosya yolunu döner. Kurulumu Android'in kendi onay ekranı yapar.</summary>
    public async Task<string> DownloadUpdateAsync(Action<double> progress)
    {
        using var http = await HttpAsync();
        using var response = await http.GetAsync("mobil/guncelleme/apk", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var path = Path.Combine(FileSystem.CacheDirectory, "Ultron-mobil.apk");
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(path);
        var buffer = new byte[1 << 16];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0)
                progress((double)done / total);
        }
        return path;
    }

    public ValueTask DisposeAsync() => _hub?.DisposeAsync() ?? ValueTask.CompletedTask;

    public sealed record UpdateInfo(int Surum, string Ad, long Boyut);

    private sealed record PairResult(string? Token, string? Id, string? Name, string? Profile);
}
