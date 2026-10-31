using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;
using Ultron.Core.Documents;
using Ultron.Core.Memories;
using Ultron.Core.Voice;
using Ultron.Host.Status;

namespace Ultron.Host.Hubs;

/// <summary>
/// Masaüstü (ve ileride diğer istemciler) Ultron'la bu hub üzerinden konuşur.
/// SendMessage bir akış döndürür: istemci olayları geldikçe alır (cevap kelime kelime akar).
/// Metot adları <see cref="ChatHubContract"/> ile aynı olmalı.
/// </summary>
public sealed class ChatHub(
    ChatOrchestrator orchestrator,
    IConversationStore conversations,
    StatusService status,
    IMemoryStore memories,
    MemoryService memoryService,
    ICategoryStore categories,
    ISpeechToText speechToText,
    ITextToSpeech textToSpeech,
    Ultron.Core.Reminders.ReminderService reminders,
    Ultron.Host.Telegram.PresenceTracker presence,
    DocumentService documents,
    FollowUpService followUps,
    Ultron.Core.Tools.ApprovalBroker approvals,
    ConnectionTracker connections,
    Ultron.Core.Coding.CodingService coding,
    Ultron.Core.Settings.UltronSettings settings,
    Ultron.Core.Learning.LearningService learning,
    Ultron.Core.Studio.StudioService studio,
    Ultron.Core.Voice.IVoiceResources voiceResources,
    Ultron.Core.Lessons.LessonService lessons,
    Ultron.Core.Growth.GrowthService growth,
    Ultron.Core.Pro.ProService pro,
    Ultron.Core.Sites.SiteService sites,
    Ultron.Core.Mobile.MobileDevices mobileDevices,
    Ultron.Infrastructure.Setup.SetupService setup,
    Ultron.Host.Info.InfoPanelService info,
    Ultron.Core.Pro.IProResearcher proResearcher) : Hub
{
    public override Task OnConnectedAsync()
    {
        connections.Connected();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Disconnected();
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Claude Code'un sorusuna cevap ya da "devam et" (masaüstündeki kodlama şeridi).</summary>
    public async Task<string> ReplyCoding(string answer)
    {
        try
        {
            var job = await coding.ReplyAsync(answer ?? "");
            return $"Cevabın #{job.Id} işine iletildi.";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public bool CancelCoding() => coding.Cancel();

    /// <summary>Onay kartı kararı (Onayla / Reddet). İstek süresi dolduysa false.</summary>
    public bool ResolveApproval(long id, bool approve) => approvals.Resolve(id, approve);

    /// <summary>Masaüstü 30 sn'de bir: klavye/fare kaç saniyedir kullanılmadı (uzaktaysa bildirimler Telegram'a da gider).</summary>
    public void ReportPresence(int idleSeconds) => presence.Report(TimeSpan.FromSeconds(Math.Max(0, idleSeconds)));

    /// <summary>"Okudum": tekrar eden (önemli / çok önemli) hatırlatma susar.</summary>
    public Task<bool> AcknowledgeReminder(long id) => reminders.AcknowledgeAsync(id, Context.ConnectionAborted);

    /// <summary>Hub mesaj sınırı da buna göre ayarlı (Program.cs). 16 kHz mono 16 bit: 60 sn ≈ 1,9 MB.</summary>
    public const int MaxAudioBytes = 4 * 1024 * 1024;

    private const int InitialMessageCount = 50;
    private const int RecentlyDeletedCount = 20;

    public Task<StatusDto> GetStatus() => status.GetAsync(Context.ConnectionAborted);

    /// <summary>Son konuşma (yoksa yeni) ve son mesajları: uygulama açılınca ekranı doldurmak için.</summary>
    public async Task<ConversationDto> GetCurrentConversation()
    {
        var conversation = await conversations.GetLatestAsync() ?? await conversations.StartAsync();
        return await ToDtoAsync(conversation);
    }

    public async Task<ConversationDto> StartNewConversation() =>
        await ToDtoAsync(await conversations.StartAsync());

    /// <summary>Ölçüm/test konuşması (betikler kullanır): Gelişim panosuna sayılmaz.</summary>
    public async Task<ConversationDto> StartTestConversation()
    {
        var conversation = await conversations.StartAsync();
        await conversations.SetTitleAsync(conversation.Id, Ultron.Core.Conversations.Conversation.TestTitle);
        return await ToDtoAsync(conversation);
    }

    public async IAsyncEnumerable<ChatEventDto> SendMessage(
        Guid conversationId,
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        await foreach (var e in orchestrator.SendAsync(conversationId, text.Trim(), cancellationToken))
            yield return ChatEventMapper.ToDto(e);
    }

    /// <summary>Görselli soru (Faz 11). Masaüstü görseli göndermeden önce en fazla ~1600 px'e küçültür; sınır 4 MB.</summary>
    public async IAsyncEnumerable<ChatEventDto> SendMessageWithImage(
        Guid conversationId,
        string text,
        byte[] image,
        bool fromScreen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (image is null || image.Length < 16 || image.Length > MaxAudioBytes)
            throw new HubException("Görsel geçersiz ya da çok büyük (en fazla 4 MB).");

        await foreach (var e in orchestrator.SendAsync(conversationId, (text ?? "").Trim(), new ImageInput(image, fromScreen), cancellationToken))
            yield return ChatEventMapper.ToDto(e);
    }

    // ---- Ses ----

    /// <summary>"Ultron" deyince dinleme açık mı (ayar wake_word).</summary>
    public async Task<bool> GetWakeWord() =>
        await settings.GetAsync(Ultron.Core.Settings.UltronSettings.WakeWord, Context.ConnectionAborted) == "acik";

    /// <summary>Tepsi menüsünden aç/kapat; değişiklik tüm istemcilere <see cref="ChatHubContract.WakeWordChanged"/> ile gider.</summary>
    public Task SetWakeWord(bool on) =>
        settings.SetAsync(Ultron.Core.Settings.UltronSettings.WakeWord, on ? "acik" : "kapali", Context.ConnectionAborted);

    /// <summary>Mikrofon açılınca çağrılır: Whisper kullanıcı konuşurken yüklensin.</summary>
    public Task PrepareVoice() => speechToText.WarmUpAsync(Context.ConnectionAborted);

    /// <summary>Konuşma kaydı (WAV) → yazı. Bir şey anlaşılmadıysa boş döner.</summary>
    public async Task<string> Transcribe(byte[] wav)
    {
        if (wav is null || wav.Length < 44 || wav.Length > MaxAudioBytes)
            throw new HubException("Ses kaydı geçersiz ya da çok uzun.");
        Interlocked.Increment(ref _userTranscribing); // bu sırada "Ultron" kontrolleri bekletilmez, atlanır
        try
        {
            return await speechToText.TranscribeAsync(wav, Context.ConnectionAborted);
        }
        finally
        {
            Interlocked.Decrement(ref _userTranscribing);
        }
    }

    private static int _wakeChecking;
    private static int _userTranscribing;

    /// <summary>
    /// "Ultron" dendi mi kontrolü (wake word adayı). Gerçek sesli sorudan ayrı ve kuyruksuz: önceki kontrol sürüyorsa ya da
    /// kullanıcının sorusu yazıya çevriliyorsa beklemeden boş döner; 6 sn'de kendini keser. (2026-10-29: YouTube sesi mikrofona
    /// girince adaylar Whisper'da sıraya birikiyor, 1,5 sn ses 20-177 sn sürüyor, GPU %100 kalıyordu: masaüstü 5 sn'de vazgeçse de
    /// Host işi bırakmıyordu.)
    /// </summary>
    public async Task<string> CheckWakeWord(byte[] wav)
    {
        if (wav is null || wav.Length < 44 || wav.Length > 2_000_000 || Volatile.Read(ref _userTranscribing) > 0
            || Interlocked.CompareExchange(ref _wakeChecking, 1, 0) != 0)
            return ChatHubContract.WakeCheckBusy; // ret değil: masaüstü bunu "bilinmiyor" sayar, gürültü molasını tetiklemez
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            return await speechToText.TranscribeQuickAsync(wav, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return ChatHubContract.WakeCheckBusy;
        }
        finally
        {
            Interlocked.Exchange(ref _wakeChecking, 0);
        }
    }

    /// <summary>Tek cümle (ya da kısa paragraf) → Ultron sesiyle WAV. Okunacak bir şey yoksa boş.</summary>
    public Task<byte[]> Speak(string text) =>
        string.IsNullOrWhiteSpace(text) || text.Length > 2000
            ? Task.FromResult(Array.Empty<byte>())
            : textToSpeech.SynthesizeAsync(text, Context.ConnectionAborted);

    /// <summary>Sorunun altındaki +: bu soru-cevap için seçenekli takip soruları (artık her cevaptan sonra otomatik değil).</summary>
    public async Task<IReadOnlyList<FollowUpDto>> SuggestFollowUps(string question, string answer)
    {
        var ct = Context.ConnectionAborted;
        var recall = await memoryService.RecallAsync(question, ct);
        var questions = await followUps.SuggestAsync(question, answer ?? "", recall, ct);
        return questions.Select(q => new FollowUpDto(q.Question, q.Options.Select(o => new FollowUpOptionDto(o.Label, o.Answer)).ToList())).ToList();
    }

    // ---- Belgeler (Faz 10) ----

    public async Task<IReadOnlyList<DocumentDto>> ListDocuments() =>
        (await documents.ListAsync(Context.ConnectionAborted)).Select(ToDto).ToList();

    /// <summary>
    /// Dosya parça parça akar (istemciden sunucuya akış): tek mesaj sınırına (ses için ~5,6 MB) takılmadan 50 MB'a kadar.
    /// </summary>
    public async Task<DocumentAddResultDto> UploadDocument(string fileName, string? collection, IAsyncEnumerable<byte[]> content)
    {
        var limit = documents.MaxBytes;
        using var buffer = new MemoryStream();
        await foreach (var part in content.WithCancellation(Context.ConnectionAborted))
        {
            if (buffer.Length + part.Length > limit)
                return new(nameof(DocumentAddStatus.TooLarge), $"{Path.GetFileName(fileName)} çok büyük (sınır {limit / 1024 / 1024} MB).", null);
            buffer.Write(part);
        }

        var result = await documents.AddFileAsync(fileName, buffer.ToArray(), collection, Context.ConnectionAborted);
        return new(result.Status.ToString(), result.Message, result.Document is { } d ? ToDto(d) : null);
    }

    public Task<bool> DeleteDocument(long id) => documents.DeleteAsync(id, Context.ConnectionAborted);

    /// <summary>Belgeyi kayıtlı kopyasından baştan okur (yazı tanıma gelmeden eklenmiş resimli PDF için).</summary>
    public async Task<DocumentAddResultDto> ReprocessDocument(long id)
    {
        var result = await documents.ReprocessAsync(id, Context.ConnectionAborted);
        return new(result.Status.ToString(), result.Message, result.Document is { } d ? ToDto(d) : null);
    }

    public Task SetDocumentCollection(long id, string collection) =>
        string.IsNullOrWhiteSpace(collection) || collection.Length > 60
            ? throw new HubException("Koleksiyon adı boş olamaz ve 60 karakteri geçemez.")
            : documents.SetCollectionAsync(id, collection, Context.ConnectionAborted);

    private static DocumentDto ToDto(Document d) =>
        new(d.Id, d.FileName, d.Collection, d.Kind.ToString(), d.PageCount, d.ChunkCount, d.AddedAtUtc, d.StoredPath);

    // ---- Hafıza paneli ----

    public async Task<MemoryListDto> ListMemories()
    {
        var ct = Context.ConnectionAborted;
        var active = await memories.GetActiveAsync(ct);
        var deleted = await memories.GetRecentlyDeactivatedAsync(RecentlyDeletedCount, ct);
        var all = await categories.GetAllAsync(ct);
        var suggestions = await categories.GetPendingSuggestionsAsync(ct);
        return new MemoryListDto(
            active.OrderByDescending(m => m.UpdatedAtUtc).Select(ToDto).ToList(),
            deleted.Select(ToDto).ToList(),
            all.Select(ToDto).ToList(),
            suggestions.Select(s => new CategorySuggestionDto(s.Id, s.Name, s.Description, s.Example)).ToList(),
            MemoryDrawers.All.Select(d => new DrawerDto(d.Key, d.Name, d.Description)).ToList());
    }

    public async Task<MemoryItemDto> AddMemory(string content, string category) =>
        ToDto(await memoryService.AddManualAsync(Validate(content), await ValidCategoryAsync(category), Context.ConnectionAborted));

    public async Task UpdateMemory(long id, string content, string category) =>
        await memoryService.UpdateManualAsync(id, Validate(content), await ValidCategoryAsync(category), Context.ConnectionAborted);

    /// <summary>Kalıcı silme yok: kayıt pasifleşir, "Silinenler"den geri alınabilir.</summary>
    public Task DeleteMemory(long id) => memories.DeactivateAsync(id, Context.ConnectionAborted);

    public Task RestoreMemory(long id) => memories.ReactivateAsync(id, Context.ConnectionAborted);

    public async Task<CategoryDto> AddCategory(string name, string description, string drawer)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40)
            throw new HubException("Kategori adı boş olamaz ve 40 karakteri geçemez.");
        if (MemoryDrawers.Find(drawer) is null)
            throw new HubException($"Bilinmeyen çekmece: {drawer}");
        return ToDto(await categories.AddAsync(name, description ?? "", drawer, Context.ConnectionAborted));
    }

    public Task UpdateCategory(string key, string name, string description, bool isCore) =>
        string.IsNullOrWhiteSpace(name) || name.Length > 40
            ? throw new HubException("Kategori adı boş olamaz ve 40 karakteri geçemez.")
            : categories.UpdateAsync(key, name, description ?? "", isCore, Context.ConnectionAborted);

    // ---- Mobil (Faz 25): bağlı cihazlar ve yedek ızgarası; sadece bu bilgisayardan (telefon kendi eşleşmesini yönetemez) ----

    private void LocalOnly()
    {
        if (Context.GetHttpContext()?.Connection.RemoteIpAddress is { } ip && !System.Net.IPAddress.IsLoopback(ip))
            throw new HubException("Bu işlem sadece bilgisayardaki Ultron'dan yapılabilir.");
    }

    public MobilePanelDto GetMobilePanel()
    {
        LocalOnly();
        var list = mobileDevices.List()
            .Select(d => new MobileDeviceDto(d.Id, d.Name, d.PairedAtUtc.ToLocalTime(), d.LastSeenUtc?.ToLocalTime()))
            .ToList();
        return new MobilePanelDto(Ultron.Host.Mobile.MobileAccess.TailscaleAddress()?.ToString(), list);
    }

    public MobilePairingDto NewMobilePairingCode()
    {
        LocalOnly();
        return new MobilePairingDto(mobileDevices.NewPairingCode(), Ultron.Host.Mobile.MobileAccess.TailscaleAddress()?.ToString(), DateTime.Now.AddMinutes(10));
    }

    /// <summary>Eşleşmeyi kaldırır (telefon kaybolursa).</summary>
    public bool RemoveMobileDevice(string id)
    {
        LocalOnly();
        return mobileDevices.Remove(id);
    }

    // ---- İlk açılış sihirbazı (Faz 26): program kurar, dosya indirir, anahtar yazar → sadece bu bilgisayardan ----

    public async Task<SetupStatusDto> GetSetupStatus()
    {
        LocalOnly();
        var s = await setup.GetStatusAsync(Context.ConnectionAborted);
        return new SetupStatusDto(s.Completed, s.OllamaInstalled, s.OllamaRunning, s.MissingModels, s.MissingVoiceFiles, s.VoiceDownloadBytes,
            s.TavilyKey, s.SearxngRunning, s.DockerInstalled, s.GroqKey, s.ElevenLabsKey, s.Address, s.City);
    }

    public IAsyncEnumerable<SetupProgressDto> InstallOllama(CancellationToken cancellationToken)
    {
        LocalOnly();
        return Progress(setup.InstallOllamaAsync(cancellationToken));
    }

    public IAsyncEnumerable<SetupProgressDto> PullModels(CancellationToken cancellationToken)
    {
        LocalOnly();
        return Progress(setup.PullModelsAsync(cancellationToken));
    }

    public IAsyncEnumerable<SetupProgressDto> DownloadVoiceModels(CancellationToken cancellationToken)
    {
        LocalOnly();
        return Progress(setup.DownloadVoiceModelsAsync(cancellationToken));
    }

    public IAsyncEnumerable<SetupProgressDto> InstallDocker(CancellationToken cancellationToken)
    {
        LocalOnly();
        return Progress(setup.InstallDockerAsync(cancellationToken));
    }

    public IAsyncEnumerable<SetupProgressDto> SetupSearxng(CancellationToken cancellationToken)
    {
        LocalOnly();
        return Progress(setup.SetupSearxngAsync(cancellationToken));
    }

    private static async IAsyncEnumerable<SetupProgressDto> Progress(IAsyncEnumerable<Ultron.Infrastructure.Setup.SetupProgress> source)
    {
        await foreach (var p in source)
            yield return new SetupProgressDto(p.Text, p.Percent, p.Done, p.Error);
    }

    public Task<string?> SaveSetupKey(string kind, string value)
    {
        LocalOnly();
        return setup.SaveKeyAsync(kind, value, Context.ConnectionAborted);
    }

    public Task<string?> SaveSetupProfile(SetupProfileDto profile)
    {
        LocalOnly();
        return setup.SaveProfileAsync(new Ultron.Infrastructure.Setup.SetupProfile(profile.Address, profile.City, profile.Work, profile.Interests), Context.ConnectionAborted);
    }

    public Task CompleteSetup()
    {
        LocalOnly();
        return setup.CompleteAsync(Context.ConnectionAborted);
    }

    /// <summary>Ana ekranın sağ bilgi paneli (masaüstü dakikada bir sorar; kurlar/hava Host'ta önbellekte).</summary>
    public Task<InfoPanelDto> GetInfoPanel() => info.GetAsync(Context.ConnectionAborted);

    // ---- İş arayüzü (Faz 16) ----

    public async Task<IReadOnlyList<StudioProjectDto>> ListStudioProjects() =>
        (await studio.ProjectsAsync(Context.ConnectionAborted)).Select(StudioMapper.ToDto).ToList();

    public async Task<StudioProjectDto> CreateStudioProject(string name) =>
        StudioMapper.ToDto(await studio.CreateProjectAsync(name ?? "", Context.ConnectionAborted));

    public Task<bool> DeleteStudioProject(long id) => studio.DeleteProjectAsync(id, Context.ConnectionAborted);

    public async Task<StudioOpenDto?> OpenStudioProject(long id) =>
        await studio.OpenAsync(id, Context.ConnectionAborted) is { } open
            ? new StudioOpenDto(StudioMapper.ToDto(open.Project), open.Messages.Select(StudioMapper.ToDto).ToList(), open.Versions.Select(StudioMapper.ToDto).ToList())
            : null;

    /// <summary>Bileşen isteği: model yazdıkça parçalar akar (önizleme canlı güncellenir), sonunda yeni sürüm.</summary>
    public async IAsyncEnumerable<StudioEventDto> SendStudioMessage(long projectId, string text, byte[]? image, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (image is { Length: > MaxAudioBytes })
            throw new HubException("Görsel çok büyük (en fazla 4 MB).");
        await foreach (var e in studio.GenerateAsync(projectId, (text ?? "").Trim(), image is { Length: > 16 } ? image : null, cancellationToken))
            yield return StudioMapper.ToDto(e);
    }

    public async IAsyncEnumerable<StudioEventDto> FixStudioComponent(long projectId, long versionId, string report, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var e in studio.FixAsync(projectId, versionId, report ?? "", cancellationToken))
            yield return StudioMapper.ToDto(e);
    }

    public Task ReportStudioCheck(long versionId, bool passed, string report) => studio.ReportCheckAsync(versionId, passed, report ?? "", Context.ConnectionAborted);

    public Task<string> RateStudioVersion(long versionId, int rating, string? note) => studio.RateAsync(versionId, rating, note, Context.ConnectionAborted);

    public Task<bool> SelectStudioVersion(long projectId, long versionId) => studio.SelectVersionAsync(projectId, versionId, Context.ConnectionAborted);

    /// <summary>Kullanıcı "Claude'a yaptır" dedi (kart): Claude Code arka planda yazar, ilerleme StudioUpdate ile gelir.</summary>
    public Task<string> StartClaudeComponent(long projectId, string? request) => studio.StartClaudeAsync(projectId, request, Context.ConnectionAborted);

    public async Task<StudioProfileDto> GetStudioProfile()
    {
        var ct = Context.ConnectionAborted;
        await studio.EnsureSeededAsync(ct);
        var stats = await studio.StatsAsync(ct);
        return new StudioProfileDto(
            (await studio.RulesAsync(ct)).Select(r => new DesignRuleDto(r.Id, r.Text, r.Source, r.CreatedAtUtc.ToLocalTime())).ToList(),
            (await studio.LearnedRecipesAsync(ct)).Select(r => new StudioRecipeDto(r.Id, r.Title, r.Kind.ToString(), r.Description, r.CreatedAtUtc.ToLocalTime())).ToList(),
            stats.Versions, stats.ByUltron, stats.ByClaude, stats.ThumbsUp, stats.ThumbsDown, stats.Recipes, voiceResources.Released);
    }

    public async Task<bool> AddDesignRule(string text) => await studio.AddRuleAsync(text ?? "", "kullanıcı", Context.ConnectionAborted) is not null;

    public Task<bool> RemoveDesignRule(long id) => studio.RemoveRuleAsync(id, Context.ConnectionAborted);

    public async Task<string> AddStudioReference(string title, string text, byte[]? image)
    {
        var recipe = await studio.AddReferenceAsync(title ?? "", text ?? "", image is { Length: > 16 } ? image : null, Context.ConnectionAborted);
        return $"Referans tarif defterine eklendi: {recipe.Title}";
    }

    public Task<bool> RemoveStudioRecipe(long id) => studio.RemoveRecipeAsync(id, Context.ConnectionAborted);

    // ---- Cevap paketleri (Faz 19) ----

    /// <summary>"ultron" ya da "pro".</summary>
    public async Task<string> GetAnswerTier() => AnswerTiers.Key(await AnswerTiers.GetAsync(settings, Context.ConnectionAborted));

    /// <summary>Pro ancak bağlı bir hesap (Claude, Codex ya da Gemini) varsa seçilir; yoksa paket değişmez (GetProStatus nedenini söyler).</summary>
    public async Task SetAnswerTier(string tier)
    {
        var parsed = AnswerTiers.Parse(tier);
        if (parsed == AnswerTier.Pro && !(await proResearcher.GetStatusAsync(Context.ConnectionAborted)).Ready)
            return;
        await AnswerTiers.SetAsync(settings, parsed, Context.ConnectionAborted);
    }

    public async Task<ProStatusDto> GetProStatus()
    {
        var s = await proResearcher.GetStatusAsync(Context.ConnectionAborted);
        return new ProStatusDto(s.Ready, s.Provider, s.Message);
    }

    public Task<string> GetUserAddress() => settings.GetAsync(Ultron.Core.Settings.UltronSettings.UserAddress, Context.ConnectionAborted);

    public Task<AppearanceDto> GetAppearance() => ReadAppearanceAsync(settings, Context.ConnectionAborted);

    internal static async Task<AppearanceDto> ReadAppearanceAsync(Ultron.Core.Settings.UltronSettings settings, CancellationToken cancellationToken = default) =>
        new(int.Parse(await settings.GetAsync(Ultron.Core.Settings.UltronSettings.FormulaSize, cancellationToken), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(await settings.GetAsync(Ultron.Core.Settings.UltronSettings.TextSize, cancellationToken), System.Globalization.CultureInfo.InvariantCulture));

    public async Task<IReadOnlyList<ProAnswerDto>> ListProAnswers() =>
        (await pro.LearnedAsync(Context.ConnectionAborted))
            .Select(a => new ProAnswerDto(a.Id, a.Question, a.Answer, a.Lifetime, a.ExpiresAtUtc?.ToLocalTime(), a.CreatedAtUtc.ToLocalTime(), a.Uses, a.Origin)).ToList();

    public Task<bool> RemoveProAnswer(long id) => pro.RemoveAsync(id, Context.ConnectionAborted);

    // ---- Bildiğim siteler (Faz 20) ----

    public async Task<IReadOnlyList<KnownSiteDto>> ListSites() =>
        (await sites.ListAsync(Context.ConnectionAborted))
            .Select(s => new KnownSiteDto(s.Id, s.Name, s.Url, s.Purpose, s.PageCount, s.LastCrawledUtc?.ToLocalTime(), s.LastResult)).ToList();

    public async Task<string> AddSite(string url, string? name, string? purpose) =>
        (await sites.AddAsync(url ?? "", name, purpose, Context.ConnectionAborted)).Message;

    public Task<bool> RemoveSite(long id) => sites.RemoveAsync(id, Context.ConnectionAborted);

    public Task<bool> CrawlSite(long id) => Task.FromResult(sites.RequestCrawl(id));

    // ---- Gelişim panosu (Faz 18) ----

    public async Task<GrowthDto> GetGrowth()
    {
        var o = await growth.OverviewAsync(Context.ConnectionAborted);
        return new GrowthDto(o.Weeks,
            o.Cards.Select(c => new GrowthCardDto(c.Group, c.Title, c.Value, c.Note, c.Trend)).ToList(),
            o.Series.Select(s => new GrowthSeriesDto(s.Group, s.Title, s.Unit, s.Values, s.HigherIsBetter)).ToList(),
            o.Reports.Select(r => new NightlyReportDto(r.Area, r.Summary, r.AtUtc.ToLocalTime())).ToList(),
            o.WeakTopics.Select(t => new WeakTopicRowDto(t.Session, t.Topic, t.Misses, t.Hits)).ToList());
    }

    // ---- Ders arayüzü (Faz 17) ----

    public async Task<LessonListDto> ListLessons()
    {
        var ct = Context.ConnectionAborted;
        var sessions = await lessons.SessionsAsync(ct);
        var linked = sessions.Select(s => s.Course).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new LessonListDto(sessions.Select(LessonMapper.ToDto).ToList(), (await lessons.CoursesAsync(ct)).Where(c => !linked.Contains(c)).ToList(), voiceResources.Released);
    }

    public async Task<LessonSessionDto> CreateLesson(string name, string? course) =>
        LessonMapper.ToDto(await lessons.CreateSessionAsync(name ?? "", course, Context.ConnectionAborted));

    public Task<bool> UpdateLesson(long id, string name, string instructions) => lessons.UpdateSessionAsync(id, name ?? "", instructions ?? "", Context.ConnectionAborted);

    public Task<bool> DeleteLesson(long id) => lessons.DeleteSessionAsync(id, Context.ConnectionAborted);

    public async Task<LessonOpenDto?> OpenLesson(long id)
    {
        var ct = Context.ConnectionAborted;
        if (await lessons.GetSessionAsync(id, ct) is not { } session)
            return null;
        return new LessonOpenDto(LessonMapper.ToDto(session),
            (await lessons.MessagesAsync(id, ct)).Select(LessonMapper.ToDto).ToList(),
            (await lessons.NotesAsync(id, ct)).Select(LessonMapper.ToDto).ToList(),
            (await lessons.MaterialsAsync(id, ct)).Select(LessonMapper.ToDto).ToList(),
            (await lessons.TopicsAsync(id, ct)).Select(LessonMapper.ToDto).ToList(),
            (await lessons.SourcesAsync(id, ct)).Select(ToDto).ToList());
    }

    public async IAsyncEnumerable<LessonEventDto> AskLesson(long sessionId, string text, byte[]? image, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var e in lessons.AskAsync(sessionId, text ?? "", image, cancellationToken))
            yield return LessonMapper.ToDto(e);
    }

    /// <summary>Kullanıcı "Claude'a sor" dedi (kart): Claude Code arka planda cevaplar, sonuç LessonUpdate ile gelir.</summary>
    public Task<string> AskLessonClaude(long sessionId, long messageId) => lessons.AskClaudeAsync(sessionId, messageId, Context.ConnectionAborted);

    /// <summary>Defter/tahta fotoğrafı → metin taslağı (kaydedilmez; kullanıcı düzeltip AddLessonText ile onaylar).</summary>
    public Task<string> ReadLessonPhoto(byte[] image) => lessons.ReadPhotoAsync(image, Context.ConnectionAborted);

    public async Task<DocumentAddResultDto> AddLessonText(long sessionId, string title, string text, bool exam)
    {
        var result = await lessons.AddSourceTextAsync(sessionId, title ?? "", text ?? "", exam, Context.ConnectionAborted);
        return new(result.Status.ToString(), result.Message, result.Document is { } d ? ToDto(d) : null);
    }

    public Task<bool> RemoveLessonSource(long sessionId, long documentId) => lessons.RemoveSourceAsync(sessionId, documentId, Context.ConnectionAborted);

    public async Task<LessonNoteDto?> AddLessonNote(long sessionId, string text) =>
        await lessons.AddNoteAsync(sessionId, text ?? "", "kullanıcı", Context.ConnectionAborted) is { } note ? LessonMapper.ToDto(note) : null;

    public Task<bool> RemoveLessonNote(long noteId) => lessons.RemoveNoteAsync(noteId, Context.ConnectionAborted);

    public async Task<LessonMaterialDto?> MakeLessonMaterial(long sessionId, string kind, string? topic) =>
        await lessons.MakeMaterialAsync(sessionId, kind ?? "", topic, Context.ConnectionAborted) is { } m ? LessonMapper.ToDto(m) : null;

    public Task<bool> SaveLessonMaterial(long materialId, string title, string content) => lessons.SaveMaterialAsync(materialId, title ?? "", content ?? "", Context.ConnectionAborted);

    public Task<bool> DeleteLessonMaterial(long materialId) => lessons.DeleteMaterialAsync(materialId, Context.ConnectionAborted);

    public async Task<LessonMaterialDto?> MakeLessonQuiz(long sessionId, string? topic, int count) =>
        await lessons.MakeQuizAsync(sessionId, topic, count, Context.ConnectionAborted) is { } m ? LessonMapper.ToDto(m) : null;

    public async Task<QuizGradeDto?> GradeLessonQuiz(long materialId, IReadOnlyList<string> answers) =>
        await lessons.GradeQuizAsync(materialId, answers ?? [], Context.ConnectionAborted) is { } g ? LessonMapper.ToDto(g) : null;

    public async Task<LessonPlanDto> MakeLessonPlan(long sessionId, string? examName, DateTime examDateLocal, bool reminders)
    {
        var (plan, message) = await lessons.MakePlanAsync(sessionId, examName, examDateLocal, reminders, Context.ConnectionAborted);
        return new LessonPlanDto(message, plan is null ? null : LessonMapper.ToDto(plan));
    }

    /// <summary>"is" ve "ders": ses modelleri ekran kartından boşaltılır (model yazarken yer ona kalır); "ana": geri yüklenir.</summary>
    public async Task<string> SetMode(string mode)
    {
        if (mode is not null && (mode.Equals("is", StringComparison.OrdinalIgnoreCase) || mode.Equals("ders", StringComparison.OrdinalIgnoreCase)))
        {
            await voiceResources.ReleaseAsync(Context.ConnectionAborted);
            return "Ses modelleri ekran kartından boşaltıldı.";
        }
        await voiceResources.RestoreAsync(Context.ConnectionAborted);
        return "Ses modelleri yeniden yükleniyor.";
    }

    // ---- Öğrenme paneli (Faz 15 d) ----

    public async Task<LearningListDto> ListLearning()
    {
        var ct = Context.ConnectionAborted;
        var pending = await learning.PendingAsync(ct);
        var last = await learning.LastAuditUtcAsync(ct);
        var approved = await learning.ApprovedCountAsync(ct);
        var learned = await learning.LearnedAsync(30, ct);
        return new LearningListDto(pending.Select(ToDto).ToList(), last?.ToLocalTime(), learning.IsAuditing, approved, learned.Select(ToDto).ToList());
    }

    private static LearningItemDto ToDto(Ultron.Core.Learning.AnswerReview r) =>
        new(r.Id, r.AskedAtUtc.ToLocalTime(), r.Question, r.Answer, r.Verdict?.ToString() ?? "", r.Correction ?? "", r.Reason, r.Reference,
            r.PlainSummary, r.AutoApproved, r.DecidedAtUtc?.ToLocalTime());

    public Task<bool> UndoLearning(long id) => learning.UndoAsync(id, Context.ConnectionAborted);

    public Task<bool> ResolveLearning(long id, bool accept) => learning.DecideAsync(id, accept, Context.ConnectionAborted);

    public Task<int> AcceptAllLearning() => learning.AcceptAllAsync(Context.ConnectionAborted);

    /// <summary>"Şimdi denetle": arka planda başlar (Claude Code birkaç dakika sürebilir); bitince LearningReady gelir.</summary>
    public string RunAuditNow()
    {
        if (learning.IsAuditing)
            return "Denetim zaten çalışıyor.";
        _ = Task.Run(() => learning.AuditAsync(CancellationToken.None));
        return "Denetim başladı; bitince bildirim gelecek.";
    }

    /// <summary>Boş klasörü siler (aktif kaydı varsa ya da yerleşikse silmez, false döner).</summary>
    public Task<bool> DeleteCategory(string key) => categories.DeleteIfEmptyAsync(key, Context.ConnectionAborted);

    public async Task<CategoryDto?> ResolveCategorySuggestion(long id, bool accept) =>
        await categories.ResolveSuggestionAsync(id, accept, Context.ConnectionAborted) is { } added ? ToDto(added) : null;

    private static string Validate(string content) =>
        string.IsNullOrWhiteSpace(content) || content.Length > 1000
            ? throw new HubException("Hafıza metni boş olamaz ve 1000 karakteri geçemez.")
            : content;

    private async Task<string> ValidCategoryAsync(string key) =>
        (await categories.GetAllAsync(Context.ConnectionAborted)).FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Key
            ?? throw new HubException($"Bilinmeyen kategori: {key}");

    private static MemoryItemDto ToDto(Memory m) =>
        new(m.Id, m.Category, m.Content, m.IsActive, m.UpdatedAtUtc);

    private static CategoryDto ToDto(MemoryCategory c) =>
        new(c.Key, c.Name, c.Description, c.IsCore, c.IsBuiltIn, c.Drawer);

    private async Task<ConversationDto> ToDtoAsync(Conversation conversation)
    {
        var messages = await conversations.GetRecentMessagesAsync(conversation.Id, InitialMessageCount);
        return new ConversationDto(
            conversation.Id,
            conversation.StartedAtUtc,
            messages.Select(m => new MessageDto(m.Id, m.Role.ToString(), m.Content, m.CreatedAtUtc)).ToList());
    }
}
