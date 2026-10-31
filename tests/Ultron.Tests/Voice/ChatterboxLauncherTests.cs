using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Infrastructure.Voice;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Voice;

public class ChatterboxLauncherTests
{
    private const string Url = "http://127.0.0.1:5201";

    private static ChatterboxLauncher Launcher(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new VoiceOptions()), NullLogger<ChatterboxLauncher>.Instance);

    [Fact]
    public async Task Ses_ureten_sunucu_saglam_sayilir()
    {
        var handler = new StubHttpHandler().On(Url + "/tts", new string('x', 2000), "audio/wav", charset: null);
        Assert.True(await Launcher(handler).CanSpeakAsync(Url, default));
    }

    [Fact]
    public async Task Baglantiyi_kesen_ya_da_hata_veren_ya_da_bos_donen_sunucu_bozuk_sayilir()
    {
        Assert.False(await Launcher(new ClosingHandler()).CanSpeakAsync(Url, default)); // 2026-10-27'deki durum
        Assert.False(await Launcher(new StubHttpHandler().On(Url + "/tts", "{}", "application/json", status: HttpStatusCode.InternalServerError)).CanSpeakAsync(Url, default));
        Assert.False(await Launcher(new StubHttpHandler().On(Url + "/tts", "", "audio/wav", charset: null)).CanSpeakAsync(Url, default));
    }

    [Fact]
    public async Task Istek_govdesinin_uzunlugu_belli_python_sunucusu_parcali_govdeyi_okuyamaz()
    {
        // 2026-10-27: PostAsJsonAsync parça parça (chunked) gönderiyordu → sunucu 400 → Ultron fark edilmeden Piper'la konuşuyordu.
        var body = ChatterboxTextToSpeech.Body(new { text = "Patron, ders dokuzda." });
        Assert.NotNull(body.Headers.ContentLength);
        Assert.Contains("\"text\"", await body.ReadAsStringAsync());
    }

    [Fact]
    public void Netstat_ciktisindan_dinleyen_surec()
    {
        const string output = """

            Etkin Bağlantılar

              İl     Yerel Adres            Yabancı Adres          Durum           PID
              TCP    127.0.0.1:5199         0.0.0.0:0              LISTENING       4100
              TCP    127.0.0.1:5201         127.0.0.1:61234        ESTABLISHED     23904
              TCP    127.0.0.1:5201         0.0.0.0:0              DİNLİYOR        23904
              TCP    127.0.0.1:52010        0.0.0.0:0              LISTENING       777
            """;
        Assert.Equal(23904, ChatterboxLauncher.ParseListeningPid(output, 5201));
        Assert.Null(ChatterboxLauncher.ParseListeningPid(output, 5300));
    }

    [Theory]
    [InlineData(@"C:\Users\kullanici\Ultron\python\cpython-3.14.9-windows-x86_64-none\python.exe", true)]
    [InlineData(@"C:\Users\kullanici\Ultron\tts\.venv\Scripts\python.exe", true)]
    [InlineData(@"C:\Program Files\Python314\python.exe", false)]           // başka bir Python
    [InlineData(@"C:\Users\kullanici\Ultron\tools\uv\uv.exe", false)]          // Ultron'un ama Python değil
    [InlineData(@"C:\Users\kullanici\UltronBaska\python.exe", false)]          // klasör adı benziyor
    [InlineData(null, false)]
    public void Sadece_Ultronun_kendi_Pythonu_kapatilir(string? path, bool expected) =>
        Assert.Equal(expected, ChatterboxLauncher.IsUltronPython(path, @"C:\Users\kullanici\Ultron"));

    private sealed class ClosingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("An error occurred while sending the request.", new IOException("The response ended prematurely."));
    }
}
