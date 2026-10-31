using System.Net;
using Ultron.Core.Mobile;
using Ultron.Host.Mobile;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Host;

/// <summary>Faz 25: telefon eşleştirme, cihaz anahtarı, telefon güncellemesi, Tailscale adresi (Faz 26: fotoğraf yedeği kalktı).</summary>
public sealed class Faz25MobileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ultron-mobil-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Kod_tek_kullanimlik_anahtar_gecerli_kaldirilinca_gecersiz()
    {
        var devices = new MobileDevices(_time, _folder);
        var code = devices.NewPairingCode();

        Assert.Null(devices.Pair("000000", "Telefon"));
        var paired = devices.Pair(code, "Telefonum");
        Assert.NotNull(paired);
        Assert.Null(devices.Pair(code, "Başka")); // aynı kod ikinci kez çalışmaz
        Assert.Equal("ana", paired.Value.Device.Profile);
        Assert.Equal(paired.Value.Device.Id, devices.Authenticate(paired.Value.Token)?.Id);
        Assert.Null(devices.Authenticate("yanlis"));
        Assert.DoesNotContain(paired.Value.Token, File.ReadAllText(Path.Combine(_folder, "cihazlar.json"))); // anahtar saklanmaz

        Assert.True(devices.Remove(paired.Value.Device.Id));
        Assert.Null(devices.Authenticate(paired.Value.Token));
    }

    [Fact]
    public void Kodun_suresi_10_dakika()
    {
        var devices = new MobileDevices(_time, _folder);
        var code = devices.NewPairingCode();
        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(devices.Pair(code, "Telefon"));
    }

    [Fact]
    public void Guncelleme_sadece_apk_ve_gecerli_surum_dosyasi_varsa_sunulur()
    {
        Directory.CreateDirectory(_folder);
        var update = new MobileUpdate(_folder);
        Assert.Null(update.Latest());

        File.WriteAllBytes(update.ApkPath, new byte[1234]);
        Assert.Null(update.Latest()); // sürüm dosyası olmadan (eski elle derlenmiş APK) sunulmaz

        File.WriteAllText(Path.Combine(_folder, MobileUpdate.InfoName), "{bozuk");
        Assert.Null(update.Latest());

        File.WriteAllText(Path.Combine(_folder, MobileUpdate.InfoName), """{"surum":3,"ad":"1.3"}""");
        Assert.Equal(new MobileRelease(3, "1.3", 1234), update.Latest());
    }

    [Theory]
    [InlineData("100.101.5.7", true)]
    [InlineData("100.63.0.1", false)]
    [InlineData("192.168.1.20", false)]
    public void Tailscale_adresi_taninir(string ip, bool expected)
    {
        Assert.Equal(expected, MobileAccess.IsTailscale(IPAddress.Parse(ip)));
    }

    [Fact]
    public void Bes_yanlis_denemeden_sonra_kod_gecersiz()
    {
        var devices = new MobileDevices(_time, _folder);
        var code = devices.NewPairingCode();
        for (var i = 0; i < MobileDevices.MaxWrongAttempts; i++)
            Assert.Null(devices.Pair(code == "111111" ? "222222" : "111111", "Saldırgan"));

        Assert.Null(devices.Pair(code, "Telefon")); // doğru kod bile artık geçmez: yeni kod alınmalı
        Assert.NotNull(devices.Pair(devices.NewPairingCode(), "Telefon"));
    }

    [Theory]
    [InlineData("/hub", "", true)]                                   // Ultron'un kendi .NET istemcileri Origin göndermez
    [InlineData("/hub", "https://kotu-site.example", false)]         // web sitesinden WebSocket: reddedilir
    [InlineData("/mobil/eslestir", "http://localhost:3000", false)]
    [InlineData("/eklenti", "chrome-extension://abcdef", true)]      // eşleştirme kodlu Chrome eklentisi
    [InlineData("/hub", "chrome-extension://abcdef", false)]         // eklenti bile hub'a bağlanamaz
    public void Tarayicidan_gelen_istek_reddedilir(string path, string origin, bool allowed) =>
        Assert.Equal(allowed, Ultron.Host.Security.BrowserGuard.IsAllowed(new Microsoft.AspNetCore.Http.PathString(path), origin));
}
