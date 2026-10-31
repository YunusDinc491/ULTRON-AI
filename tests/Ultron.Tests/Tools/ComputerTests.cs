using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Computer;
using Ultron.Core.Tools;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Tools;

public sealed class ComputerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "ultron-bilgisayar-" + Guid.NewGuid().ToString("N"));
    private readonly ToolHarness _h = new();
    private readonly FakeComputer _computer = new();
    private readonly ComputerPermissions _permissions;

    public ComputerTests()
    {
        Directory.CreateDirectory(_temp);
        _permissions = new ComputerPermissions(_h.Settings, _h.Time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
            Directory.Delete(_temp, recursive: true);
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    // ---- Yol güvenliği ----

    [Fact]
    public void Goreli_yol_workspace_e_gore_kisa_adlar_bilinir_sistem_klasoru_tanınır()
    {
        Assert.Equal(Path.Combine(ComputerPermissions.Workspace, "notlar.txt"), PathGuard.Resolve("notlar.txt"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), PathGuard.Resolve("Masaüstü"));
        Assert.Equal(ComputerPermissions.Workspace, PathGuard.Resolve(null));
        Assert.True(PathGuard.IsSystem(@"C:\Windows\System32\drivers"));
        Assert.True(PathGuard.IsSystem(@"C:\"));
        Assert.False(PathGuard.IsSystem(ComputerPermissions.Workspace));
    }

    [Fact]
    public async Task Iki_nokta_ile_izinli_klasorden_kacilamaz()
    {
        var escape = PathGuard.Resolve(@"..\..\gizli.txt")!;

        Assert.False(PathGuard.IsInside(escape, ComputerPermissions.Workspace));
        Assert.False(await _permissions.IsAllowedPathAsync(escape));
        Assert.True(await _permissions.IsAllowedPathAsync(Path.Combine(ComputerPermissions.Workspace, "alt", "a.txt")));
        Assert.False(PathGuard.IsInside(ComputerPermissions.Workspace + "-baska", ComputerPermissions.Workspace)); // önek benzerliği kaçış değil
    }

    // ---- Yetki deposu ----

    [Fact]
    public async Task Varsayilan_izinli_uygulamalar_ekli_adlar_eklerle_bulunur()
    {
        Assert.Equal("code", (await _permissions.FindAppAsync("VS Code'u"))?.Command);
        Assert.Equal("notepad.exe", (await _permissions.FindAppAsync("Not Defteri'ni"))?.Command);
        Assert.Null(await _permissions.FindAppAsync("Spotify"));

        await _permissions.AddAppAsync("Spotify", @"C:\Spotify.lnk");
        Assert.Equal(@"C:\Spotify.lnk", (await _permissions.FindAppAsync("spotify'ı"))?.Command);
        Assert.True(await _permissions.RemoveAppAsync("SPOTIFY"));
        Assert.Null(await _permissions.FindAppAsync("Spotify"));
    }

    [Fact]
    public async Task Sureli_izin_suresi_dolunca_kalkar()
    {
        await _permissions.GrantAsync(RunCommandTool.GrantScope, TimeSpan.FromMinutes(30));
        Assert.True(await _permissions.HasGrantAsync(RunCommandTool.GrantScope));

        _h.Time.Advance(TimeSpan.FromMinutes(31));

        Assert.False(await _permissions.HasGrantAsync(RunCommandTool.GrantScope));
    }

    // ---- Seviyeler ----

    [Fact]
    public async Task Dosya_yazma_seviyesi_yola_gore_degisir()
    {
        ITool tool = new WriteFileTool(_permissions);

        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args(new { yol = "plan.md", icerik = "x" })));
        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { yol = Path.Combine(_temp, "a.txt"), icerik = "x" })));
        Assert.Equal(PermissionLevel.Yasak, await tool.LevelForAsync(Args(new { yol = @"C:\Windows\win.ini", icerik = "x" })));

        await _permissions.AddFolderAsync(_temp);
        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args(new { yol = Path.Combine(_temp, "a.txt"), icerik = "x" })));
    }

    [Theory]
    [InlineData("git status", PermissionLevel.Onayli)]
    [InlineData("format C:", PermissionLevel.Yasak)]
    [InlineData("del /s /q C:\\Users\\x", PermissionLevel.Yasak)]
    [InlineData("rd /s /q build", PermissionLevel.Yasak)]
    [InlineData("reg delete HKCU\\Software\\X", PermissionLevel.Yasak)]
    [InlineData("shutdown /s /t 0", PermissionLevel.Yasak)]
    [InlineData("powershell -enc ZQBjAGgAbwA=", PermissionLevel.Yasak)]
    public async Task Komut_onayli_tehlikeli_komut_yasak(string command, PermissionLevel expected)
    {
        ITool tool = new RunCommandTool(_permissions, _computer);
        Assert.Equal(expected, await tool.LevelForAsync(Args(new { komut = command })));
    }

    [Fact]
    public async Task Sureli_komut_izniyle_komut_onaysiz_ama_tehlikeli_komut_yine_yasak()
    {
        ITool tool = new RunCommandTool(_permissions, _computer);
        await _permissions.GrantAsync(RunCommandTool.GrantScope, TimeSpan.FromHours(1));

        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args(new { komut = "dir" })));
        Assert.Equal(PermissionLevel.Yasak, await tool.LevelForAsync(Args(new { komut = "format D:" })));
    }

    [Fact]
    public async Task Yetki_artirma_her_zaman_onayli_azaltma_serbest_sistem_klasoru_yasak()
    {
        ITool tool = new ChangePermissionTool(_permissions, _computer, new InMemoryCategoryStore());

        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { islem = "uygulama_ekle", ad = "spotify" })));
        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { islem = "sureli_izin", ad = "komut_calistir", sure_dakika = 30 })));
        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { islem = "klasor_ekle", yol = _temp })));
        Assert.Equal(PermissionLevel.Yasak, await tool.LevelForAsync(Args(new { islem = "klasor_ekle", yol = @"C:\Windows" })));
        Assert.Equal(PermissionLevel.Serbest, await tool.LevelForAsync(Args(new { islem = "uygulama_cikar", ad = "paint" })));
        Assert.StartsWith("YETKİ ARTIRMA", tool.Describe(Args(new { islem = "uygulama_ekle", ad = "spotify" })));
    }

    [Fact]
    public async Task Uygulama_izinliyse_bildirerek_degilse_onayli_acilir()
    {
        ITool tool = new OpenAppTool(_permissions, _computer);

        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args(new { uygulama = "not defteri" })));
        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { uygulama = "spotify" })));

        var result = await tool.ExecuteAsync(Args(new { uygulama = "VS Code", hedef = _temp }));
        Assert.True(result.Ok);
        Assert.Equal(("code", _temp), Assert.Single(_computer.Launched));
    }

    // ---- Araçlar çalışınca ----

    [Fact]
    public async Task Dosya_yazilir_eklenir_okunur_listelenir_silme_geri_donusume()
    {
        await _permissions.AddFolderAsync(_temp);
        var path = Path.Combine(_temp, "alt", "liste.txt");

        Assert.True((await new WriteFileTool(_permissions).ExecuteAsync(Args(new { yol = path, icerik = "süt" }))).Ok);
        Assert.True((await new WriteFileTool(_permissions).ExecuteAsync(Args(new { yol = path, icerik = "ekmek", ekle = true }))).Ok);
        var read = await new ReadFileTool(_permissions).ExecuteAsync(Args(new { yol = path }));
        var list = await new ListFilesTool(_permissions).ExecuteAsync(Args(new { klasor = Path.Combine(_temp, "alt") }));
        var delete = await new DeleteFileTool(_computer).ExecuteAsync(Args(new { yol = path }));

        Assert.Contains("süt", read.ForModel);
        Assert.Contains("ekmek", read.ForModel);
        Assert.Contains("VERİ", read.ForModel);
        Assert.Contains("liste.txt", list.ForModel);
        Assert.True(delete.Ok);
        Assert.Equal([path], _computer.Recycled);
    }

    [Fact]
    public async Task Yetki_konusarak_eklenir_ve_cikarilir()
    {
        var tool = new ChangePermissionTool(_permissions, _computer, new InMemoryCategoryStore());
        _computer.Installed["spotify"] = @"C:\Start\Spotify.lnk";

        Assert.True((await tool.ExecuteAsync(Args(new { islem = "uygulama_ekle", ad = "Spotify" }))).Ok);
        Assert.True((await tool.ExecuteAsync(Args(new { islem = "klasor_ekle", yol = _temp }))).Ok);
        Assert.True((await tool.ExecuteAsync(Args(new { islem = "sureli_izin", ad = "komut_calistir", sure_dakika = 999 }))).Ok);

        var snapshot = await _permissions.GetAsync();
        Assert.Contains(snapshot.Apps, a => a.Command == @"C:\Start\Spotify.lnk");
        Assert.Contains(_temp, snapshot.Folders);
        Assert.Equal(_h.Time.GetUtcNow().UtcDateTime.AddMinutes(480), Assert.Single(snapshot.Grants).UntilUtc); // en fazla 8 saat

        Assert.True((await tool.ExecuteAsync(Args(new { islem = "izni_kaldir", ad = "komut_calistir" }))).Ok);
        Assert.Empty((await _permissions.GetAsync()).Grants);
    }

    [Fact]
    public async Task Hafiza_paylasimi_onayli_kaldirma_serbest_bilinmeyen_kategori_reddedilir()
    {
        ITool tool = new ChangePermissionTool(_permissions, _computer, new InMemoryCategoryStore());

        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { islem = "hafiza_paylas", ad = "Okul" })));
        Assert.Equal(PermissionLevel.Serbest, await tool.LevelForAsync(Args(new { islem = "hafiza_paylasma", ad = "Okul" })));
        Assert.StartsWith("YETKİ ARTIRMA", tool.Describe(Args(new { islem = "hafiza_paylas", ad = "Okul" })));
        Assert.Empty((await _permissions.GetAsync()).SharedMemory); // varsayılan: hiçbir şey paylaşılmaz

        Assert.True((await tool.ExecuteAsync(Args(new { islem = "hafiza_paylas", ad = "okul" }))).Ok);
        Assert.Equal(["Okul"], (await _permissions.GetAsync()).SharedMemory);
        Assert.False((await tool.ExecuteAsync(Args(new { islem = "hafiza_paylas", ad = "Gizli Kasa" }))).Ok);

        Assert.True((await tool.ExecuteAsync(Args(new { islem = "hafiza_paylasma", ad = "Okul" }))).Ok);
        Assert.Empty((await _permissions.GetAsync()).SharedMemory);
    }

    // ---- Onay akışı (ToolRunner) ----

    [Theory]
    [InlineData(ApprovalDecision.Approved, true, "onaylandı")]
    [InlineData(ApprovalDecision.Rejected, false, "reddedildi")]
    [InlineData(ApprovalDecision.Unavailable, false, "onay sorulamadı")]
    public async Task Onayli_arac_sadece_onaylanirsa_calisir_ve_denetim_kaydina_yazilir(ApprovalDecision decision, bool runs, string audit)
    {
        var approvals = new FixedApprovals(decision);
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("", [new ToolCall("komut_calistir", Args(new { komut = "git status" }))]));
        var runner = new ToolRunner(chat, [new RunCommandTool(_permissions, _computer)], _h.Audit, _h.Time, NullLogger<ToolRunner>.Instance, approvals);

        var result = await runner.RunAsync([new(Ultron.Core.Chat.Role.User, "git durumuna bak")]);

        Assert.Equal("Komut çalıştır: git status", Assert.Single(approvals.Asked));
        Assert.Equal(runs, _computer.Commands.Count == 1);
        Assert.Equal(runs, Assert.Single(result.Uses).Ok);
        var entry = Assert.Single(_h.Audit.Entries);
        Assert.Equal((PermissionLevel.Onayli, audit), (entry.Level, entry.Approval));
    }

    [Fact]
    public async Task Yasak_islem_onay_bile_sorulmadan_reddedilir()
    {
        var approvals = new FixedApprovals(ApprovalDecision.Approved);
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("", [new ToolCall("komut_calistir", Args(new { komut = "format C:" }))]));
        var runner = new ToolRunner(chat, [new RunCommandTool(_permissions, _computer)], _h.Audit, _h.Time, NullLogger<ToolRunner>.Instance, approvals);

        await runner.RunAsync([new(Ultron.Core.Chat.Role.User, "diski biçimlendir")]);

        Assert.Empty(approvals.Asked);
        Assert.Empty(_computer.Commands);
        Assert.Equal(PermissionLevel.Yasak, Assert.Single(_h.Audit.Entries).Level);
    }

    // ---- Onay aracısı ----

    [Fact]
    public async Task Onay_araci_karari_bekler_suresi_dolarsa_yapilmaz_kimse_yoksa_sorulamaz()
    {
        var notifier = new RecordingApprovalNotifier();
        var broker = new ApprovalBroker(notifier, TimeProvider.System, NullLogger<ApprovalBroker>.Instance) { Timeout = TimeSpan.FromMilliseconds(300) };

        var pending = broker.RequestAsync("dosya_sil", "Sil: a.txt");
        var request = await notifier.Shown.Task;
        Assert.True(broker.Resolve(request.Id, approve: true));
        Assert.Equal(ApprovalDecision.Approved, await pending);
        Assert.False(broker.Resolve(request.Id, approve: false)); // ikinci cevap yok sayılır
        Assert.Equal((request.Id, ApprovalDecision.Approved), notifier.Closed.Single());

        Assert.Equal(ApprovalDecision.TimedOut, await broker.RequestAsync("dosya_sil", "Sil: b.txt"));

        notifier.Available = false;
        Assert.Equal(ApprovalDecision.Unavailable, await broker.RequestAsync("dosya_sil", "Sil: c.txt"));
        Assert.Empty(broker.Pending);
    }

    private sealed class FakeComputer : IComputer
    {
        public List<(string Command, string? Target)> Launched { get; } = [];
        public List<string> Recycled { get; } = [];
        public List<string> Commands { get; } = [];
        public Dictionary<string, string> Installed { get; } = [];

        public void Launch(string command, string? target = null) => Launched.Add((command, target));

        public string? FindInstalledApp(string name) => Installed.GetValueOrDefault(name.ToLowerInvariant());

        public void Open(string path) => Launched.Add(("open", path));

        public void Recycle(string path) => Recycled.Add(path);

        public Task<CommandResult> RunAsync(string command, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(new CommandResult(0, "ok", false));
        }
    }

    private sealed class FixedApprovals(ApprovalDecision decision) : IApprovalService
    {
        public List<string> Asked { get; } = [];

        public Task<ApprovalDecision> RequestAsync(string tool, string summary, CancellationToken cancellationToken = default)
        {
            Asked.Add(summary);
            return Task.FromResult(decision);
        }
    }

    private sealed class RecordingApprovalNotifier : IApprovalNotifier
    {
        public bool Available { get; set; } = true;
        public TaskCompletionSource<ApprovalRequest> Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(long, ApprovalDecision)> Closed { get; } = [];

        public Task<bool> NotifyAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Shown.TrySetResult(request);
            return Task.FromResult(Available);
        }

        public Task ClosedAsync(long id, ApprovalDecision decision, CancellationToken cancellationToken = default)
        {
            Closed.Add((id, decision));
            return Task.CompletedTask;
        }
    }
}
