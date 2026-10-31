using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ultron.Core.Chat;
using Ultron.Core.Embeddings;
using Ultron.Core.Voice;
using Ultron.Contracts;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Host;

/// <summary>
/// Gerçek ASP.NET Core sunucusu (bellekte) + gerçek SignalR istemcisi + gerçek SQLite dosyası.
/// Sadece model ve embedding sahte: Ollama gerekmez.
/// </summary>
public sealed class ChatHubTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ultron-test-{Guid.NewGuid():N}.db");
    private readonly string _docFolder = Path.Combine(Path.GetTempPath(), $"ultron-test-belgeler-{Guid.NewGuid():N}");
    private readonly ScriptedChatModel _chat = new();
    private readonly FakeVoice _voice = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HubConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:Path", _dbPath);
            b.UseSetting("Documents:Folder", _docFolder);
            b.UseSetting("Voice:StartChatterbox", "false"); // testte Python ses servisi başlamasın
            b.UseSetting("Telegram:Start", "false"); // kayıtlı gerçek bot varsa testler ona bağlanmasın
            // Zamanlayıcılar gerçek sisteme dokunur (uyandırma görevi, yedek klasörü, Ollama/Docker başlatma): testte kapalı.
            b.UseSetting("Reminders:Scheduler", "false");
            b.UseSetting("Backup:Scheduler", "false");
            b.UseSetting("Services:AutoStart", "false");
            b.UseSetting("Library:Scheduler", "false");
            b.UseSetting("Embeddings:Migration", "false");
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IChatModel>(_chat);
                services.AddSingleton<IEmbeddingModel>(new FakeEmbeddingModel([]));
                services.AddSingleton<ISpeechToText>(_voice);
                services.AddSingleton<ITextToSpeech>(_voice);
            });
        });

        var server = _factory.Server;
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hub/chat"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer'da en basit taşıma
            })
            .Build();
        await _connection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            File.Delete(f);
        if (Directory.Exists(_docFolder))
            Directory.Delete(_docFolder, recursive: true);
    }

    [Fact]
    public async Task Belge_parca_parca_yuklenir_sorulunca_sayfali_kaynak_doner_silinebilir()
    {
        // ~600 KB: istemci 256 KB'lık parçalarla akıtır (birden çok parça).
        var text = string.Join(" ", Enumerable.Range(0, 9000).Select(i => $"Satır {i} ısı transferi notu."));
        async IAsyncEnumerable<byte[]> Parts()
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("Kavitasyon pompaya zarar verir. " + text);
            for (var i = 0; i < bytes.Length; i += 256 * 1024)
            {
                await Task.Yield();
                yield return bytes[i..Math.Min(bytes.Length, i + 256 * 1024)];
            }
        }

        var added = await _connection.InvokeAsync<DocumentAddResultDto>("UploadDocument", "Pompa notları.txt", "Akışkanlar", Parts());
        var again = await _connection.InvokeAsync<DocumentAddResultDto>("UploadDocument", "kopya.txt", null, Parts());
        var list = await _connection.InvokeAsync<IReadOnlyList<DocumentDto>>("ListDocuments");

        Assert.Equal("Added", added.Status);
        Assert.Equal("Duplicate", again.Status);
        var doc = Assert.Single(list);
        Assert.Equal(("Pompa notları.txt", "Akışkanlar"), (doc.FileName, doc.Collection));
        Assert.True(File.Exists(doc.Path));
        Assert.StartsWith(_docFolder, doc.Path);

        _chat.RouterJson = """{"sinif":"Belge"}""";
        _chat.AnswerChunks = ["Notlarına göre kavitasyon pompaya zarar verir [1]."];
        var conversation = await _connection.InvokeAsync<ConversationDto>("GetCurrentConversation");
        var events = new List<ChatEventDto>();
        await foreach (var e in _connection.StreamAsync<ChatEventDto>("SendMessage", conversation.Id, "Akışkanlar notlarımda kavitasyon ne diyor?"))
            events.Add(e);

        Assert.Equal("Akışkanlar", events.Single(e => e.Type == "documents").Query);
        var source = events.Single(e => e.Type == "sources").Sources![0];
        Assert.Equal(doc.Path, source.Path);
        Assert.StartsWith("file:///", source.Url);

        Assert.True(await _connection.InvokeAsync<bool>("DeleteDocument", doc.Id));
        Assert.Empty(await _connection.InvokeAsync<IReadOnlyList<DocumentDto>>("ListDocuments"));
        Assert.False(File.Exists(doc.Path));
    }

    [Fact]
    public async Task Gorselli_soru_hub_uzerinden_akar_bozuk_gorsel_reddedilir()
    {
        var conversation = await _connection.InvokeAsync<ConversationDto>("GetCurrentConversation");
        var events = new List<ChatEventDto>();
        await foreach (var e in _connection.StreamAsync<ChatEventDto>("SendMessageWithImage", conversation.Id, "Bu ne?", new byte[100], true))
            events.Add(e);

        Assert.Equal("image", events[0].Type);
        Assert.True(events[0].FromScreen);
        Assert.Equal("completed", events[^1].Type);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in _connection.StreamAsync<ChatEventDto>("SendMessageWithImage", conversation.Id, "Bu ne?", new byte[3], false)) { }
        });
    }

    [Fact]
    public async Task Mesaj_gonderilince_olaylar_akar_ve_konusma_kalici_olur()
    {
        var conversation = await _connection.InvokeAsync<ConversationDto>("GetCurrentConversation");

        var events = new List<ChatEventDto>();
        await foreach (var e in _connection.StreamAsync<ChatEventDto>("SendMessage", conversation.Id, "Merhaba"))
            events.Add(e);

        Assert.Equal(["intent", "memories", "chunk", "chunk", "completed"], events.Select(e => e.Type));
        Assert.Equal("Sohbet", events[0].Intent);
        Assert.Equal("Merhaba.", events[^1].Text);

        // Uygulama yeniden açılmış gibi: aynı konuşma mesajlarıyla geri gelir.
        var reloaded = await _connection.InvokeAsync<ConversationDto>("GetCurrentConversation");
        Assert.Equal(conversation.Id, reloaded.Id);
        Assert.Equal(["User", "Assistant"], reloaded.Messages.Select(m => m.Role));
    }

    [Fact]
    public async Task Yeni_konusma_bos_baslar()
    {
        var first = await _connection.InvokeAsync<ConversationDto>("GetCurrentConversation");
        await foreach (var _ in _connection.StreamAsync<ChatEventDto>("SendMessage", first.Id, "Selam")) { }

        var fresh = await _connection.InvokeAsync<ConversationDto>("StartNewConversation");

        Assert.NotEqual(first.Id, fresh.Id);
        Assert.Empty(fresh.Messages);
    }

    [Fact]
    public async Task Durum_bilgisi_gercek_verilerle_doner()
    {
        var status = await _connection.InvokeAsync<StatusDto>("GetStatus");

        Assert.Equal("gemma4:e4b-it-qat", status.ChatModel);
        Assert.Equal(0, status.MemoryCount);
        Assert.Empty(status.RecentQueries);
    }

    [Fact]
    public async Task Hafiza_paneli_ekle_duzenle_sil_geri_al_ve_kategori_ekle()
    {
        var iliski = await _connection.InvokeAsync<CategoryDto>("AddCategory", "İlişki", "Sevgilisi", "Patron");
        Assert.Equal("Patron", iliski.Drawer);
        var added = await _connection.InvokeAsync<MemoryItemDto>("AddMemory", "Sevgilim kahveyi sütlü içer", iliski.Key);
        await _connection.InvokeAsync("UpdateMemory", added.Id, "Sevgilim kahveyi sade içer", iliski.Key);

        var list = await _connection.InvokeAsync<MemoryListDto>("ListMemories");
        Assert.Equal("Sevgilim kahveyi sade içer", Assert.Single(list.Active).Content);
        Assert.Equal(2, list.Categories.Count); // Sağlık + İlişki
        Assert.Equal(["PATRON", "İŞ", "OKUL", "BİLGİ"], list.Drawers.Select(d => d.Name));

        await _connection.InvokeAsync("DeleteMemory", added.Id);
        list = await _connection.InvokeAsync<MemoryListDto>("ListMemories");
        Assert.Empty(list.Active);
        Assert.Single(list.RecentlyDeleted);

        await _connection.InvokeAsync("RestoreMemory", added.Id);
        Assert.Single((await _connection.InvokeAsync<MemoryListDto>("ListMemories")).Active);

        var category = await _connection.InvokeAsync<CategoryDto>("AddCategory", "Araç", "Araba, bakım", "Patron");
        await Assert.ThrowsAsync<HubException>(() => _connection.InvokeAsync<CategoryDto>("AddCategory", "X", "", "Uydurma"));
        Assert.Equal("Arac", category.Key);
        await Assert.ThrowsAsync<HubException>(() => _connection.InvokeAsync<MemoryItemDto>("AddMemory", "x", "Uydurma"));
    }

    [Fact]
    public async Task Saglik_ucu_cevap_verir()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Uzun_ses_kaydi_hub_sinirina_takilmadan_yaziya_cevrilir()
    {
        // 30 sn, 16 kHz mono 16 bit ≈ 960 KB: varsayılan 32 KB SignalR sınırı bunu reddederdi.
        var wav = new byte[960_044];
        await _connection.InvokeAsync(ChatHubContract.PrepareVoice);
        var text = await _connection.InvokeAsync<string>(ChatHubContract.Transcribe, wav);

        Assert.True(_voice.WarmedUp);
        Assert.Equal("960044 bayt", text);
    }

    [Fact]
    public async Task Gecersiz_ses_kaydi_reddedilir()
    {
        await Assert.ThrowsAsync<HubException>(() => _connection.InvokeAsync<string>(ChatHubContract.Transcribe, new byte[10]));
    }

    [Fact]
    public async Task Speak_metni_sese_cevirir_bos_metin_bos_doner()
    {
        Assert.Equal("Merhaba."u8.ToArray(), await _connection.InvokeAsync<byte[]>(ChatHubContract.Speak, "Merhaba."));
        Assert.Empty(await _connection.InvokeAsync<byte[]>(ChatHubContract.Speak, "  "));
    }
}
