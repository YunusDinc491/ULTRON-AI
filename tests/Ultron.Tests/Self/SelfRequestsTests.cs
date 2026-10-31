using Microsoft.Extensions.Options;
using Ultron.Core.Self;
using Ultron.Core.Settings;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Self;

/// <summary>"Formülleri büyüt" gerçekten değişir; yapamadığı istekte "yaptım" yerine geliştirme listesi.</summary>
public sealed class SelfRequestsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ultron-istek-{Guid.NewGuid():N}.md");
    private readonly UltronSettings _settings = new(new InMemorySettingsStore(), new ManualTimeProvider(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)));
    private readonly SelfRequests _self;

    public SelfRequestsTests() =>
        _self = new SelfRequests(_settings, new FileImprovementLog(Options.Create(new ImprovementOptions { FilePath = _file })),
            new ManualTimeProvider(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)));

    public void Dispose() => File.Delete(_file);

    [Theory]
    [InlineData("matematiksel formülleri daha büyük yap")]
    [InlineData("formülleri büyüt")]
    [InlineData("Formüller biraz daha büyük olsun")]
    [InlineData("formülleri büyütür müsün?")]
    public async Task Formula_bigger_really_changes_setting(string text)
    {
        var reply = await _self.HandleAsync(text, "ders");

        Assert.Equal("115", await _settings.GetAsync(UltronSettings.FormulaSize));
        Assert.Contains("büyüttüm", reply);
        Assert.False(File.Exists(_file));
    }

    [Fact]
    public async Task More_continues_previous_direction_and_reset_restores_default()
    {
        var first = await _self.HandleAsync("yazıyı küçült", "sohbet");
        Assert.Equal("85", await _settings.GetAsync(UltronSettings.TextSize));

        await _self.HandleAsync("biraz daha", "sohbet", first);
        Assert.Equal("80", await _settings.GetAsync(UltronSettings.TextSize)); // alt sınır 80

        await _self.HandleAsync("yazıyı eski haline getir", "sohbet");
        Assert.Equal("100", await _settings.GetAsync(UltronSettings.TextSize));

        await _self.HandleAsync("formülleri %150 yap", "ders");
        Assert.Equal("150", await _settings.GetAsync(UltronSettings.FormulaSize));
    }

    [Theory]
    [InlineData("arayüzün rengini mora değiştir")]
    [InlineData("Ultron'un ana ekranına saat ekle")]
    [InlineData("kendini geliştir, sesli not alma özelliği ekle")]
    public async Task Unsupported_request_is_logged_not_faked(string text)
    {
        var reply = await _self.HandleAsync(text, "sohbet");

        Assert.Contains("kendim değiştiremiyorum", reply);
        Assert.DoesNotContain("yaptım", reply);
        var lines = await File.ReadAllLinesAsync(_file);
        Assert.Single(lines, l => l.StartsWith("- [ ]") && l.EndsWith(text));

        await _self.HandleAsync(text, "sohbet"); // aynı istek iki kez yazılmaz
        Assert.Single(await File.ReadAllLinesAsync(_file), l => l.StartsWith("- [ ]"));
    }

    [Theory]
    [InlineData("Bernoulli formülü nedir?")]
    [InlineData("ideal gaz formülü büyük basınçlarda geçerli mi")]
    [InlineData("ekran kartı ne işe yarar")]
    [InlineData("React'te bir buton yap")]
    [InlineData("uygulamama karanlık tema özelliği ekle")]
    [InlineData("bu yazılım projesini büyütmek istiyorum")]
    [InlineData("biraz daha anlat")]
    public async Task Ordinary_messages_pass_through(string text)
    {
        Assert.Null(await _self.HandleAsync(text, "sohbet", "Isı pompası şöyle çalışır…"));
        Assert.Equal("100", await _settings.GetAsync(UltronSettings.FormulaSize));
        Assert.False(File.Exists(_file));
    }

    [Fact]
    public async Task Settings_tool_accepts_relative_size()
    {
        var tool = new ChangeSettingTool(_settings);
        using var args = System.Text.Json.JsonDocument.Parse("""{"ayar":"formul_boyutu","deger":"büyüt"}""");

        var result = await tool.ExecuteAsync(args.RootElement);

        Assert.True(result.Ok);
        Assert.Equal("115", await _settings.GetAsync(UltronSettings.FormulaSize));
    }
}
