using System.IO;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

namespace Ultron.Desktop.Services;

/// <summary>Host'taki sohbet hub'ına SignalR bağlantısı. Bağlantı koparsa kendiliğinden yeniden dener.</summary>
public sealed class UltronClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public UltronClient(Uri hostUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(hostUrl, ChatHubContract.Path))
            .WithAutomaticReconnect()
            .Build();
        _connection.Reconnecting += _ => Notify();
        _connection.Reconnected += _ => Notify();
        _connection.Closed += _ => Notify();
        _connection.On<ReminderDueDto>(ChatHubContract.ReminderDue, due => ReminderDue?.Invoke(due));
        _connection.On<BriefingDto>(ChatHubContract.Briefing, b => Briefing?.Invoke(b));
        _connection.On<string>(ChatHubContract.Suggestion, s => Suggestion?.Invoke(s));
        _connection.On<string>(ChatHubContract.ServiceWarning, s => ServiceWarning?.Invoke(s));
        _connection.On<bool>(ChatHubContract.WakeWordChanged, on => WakeWordChanged?.Invoke(on));
        _connection.On<AppearanceDto>(ChatHubContract.AppearanceChanged, a => AppearanceChanged?.Invoke(a));
        _connection.On<string, string>(ChatHubContract.CategoryCreated, (name, example) => CategoryCreated?.Invoke(name, example));
        _connection.On<ApprovalRequestDto>(ChatHubContract.ApprovalRequested, a => ApprovalRequested?.Invoke(a));
        _connection.On<ApprovalClosedDto>(ChatHubContract.ApprovalClosed, a => ApprovalClosed?.Invoke(a));
        _connection.On<CodingJobDto>(ChatHubContract.CodingUpdate, j => CodingUpdate?.Invoke(j));
        _connection.On<StudioUpdateDto>(ChatHubContract.StudioUpdate, u => StudioUpdate?.Invoke(u));
        _connection.On<int, int, int>(ChatHubContract.StudioReviewed, (reviewed, recipes, rules) => StudioReviewed?.Invoke(reviewed, recipes, rules));
        _connection.On<LessonUpdateDto>(ChatHubContract.LessonUpdate, u => LessonUpdate?.Invoke(u));
        _connection.On<int, int, int>(ChatHubContract.LearningReady, (reviewed, learned, pending) => LearningReady?.Invoke(reviewed, learned, pending));
    }

    /// <summary>Host'tan: onaylı bir işlem için onay kartı.</summary>
    public event Action<ApprovalRequestDto>? ApprovalRequested;

    /// <summary>Host'tan: onay kararı verildi ya da süre doldu.</summary>
    public event Action<ApprovalClosedDto>? ApprovalClosed;

    public Task<bool> ResolveApprovalAsync(long id, bool approve) =>
        _connection.InvokeAsync<bool>(ChatHubContract.ResolveApproval, id, approve);

    /// <summary>Host'tan: kodlama işi (Claude Code) güncellendi.</summary>
    public event Action<CodingJobDto>? CodingUpdate;

    public Task<string> ReplyCodingAsync(string answer) =>
        _connection.InvokeAsync<string>(ChatHubContract.ReplyCoding, answer);

    public Task<bool> CancelCodingAsync() =>
        _connection.InvokeAsync<bool>(ChatHubContract.CancelCoding);

    public Task<MobilePanelDto> GetMobilePanelAsync() => _connection.InvokeAsync<MobilePanelDto>(ChatHubContract.GetMobilePanel);

    public Task<MobilePairingDto> NewMobilePairingCodeAsync() => _connection.InvokeAsync<MobilePairingDto>(ChatHubContract.NewMobilePairingCode);

    public Task<bool> RemoveMobileDeviceAsync(string id) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveMobileDevice, id);

    // ---- İlk açılış sihirbazı (Faz 26) ----

    public Task<SetupStatusDto> GetSetupStatusAsync() => _connection.InvokeAsync<SetupStatusDto>(ChatHubContract.GetSetupStatus);

    /// <param name="step">ChatHubContract.InstallOllama, PullModels, DownloadVoiceModels, InstallDocker ya da SetupSearxng.</param>
    public IAsyncEnumerable<SetupProgressDto> RunSetupStepAsync(string step, CancellationToken cancellationToken) =>
        _connection.StreamAsync<SetupProgressDto>(step, cancellationToken);

    public Task<string?> SaveSetupKeyAsync(string kind, string value) => _connection.InvokeAsync<string?>(ChatHubContract.SaveSetupKey, kind, value);

    public Task<string?> SaveSetupProfileAsync(SetupProfileDto profile) => _connection.InvokeAsync<string?>(ChatHubContract.SaveSetupProfile, profile);

    public Task CompleteSetupAsync() => _connection.InvokeAsync(ChatHubContract.CompleteSetup);

    public Task<InfoPanelDto> GetInfoPanelAsync() => _connection.InvokeAsync<InfoPanelDto>(ChatHubContract.GetInfoPanel);

    public Task<ProStatusDto> GetProStatusAsync() => _connection.InvokeAsync<ProStatusDto>(ChatHubContract.GetProStatus);

    // ---- İş arayüzü (Faz 16) ----

    /// <summary>Host'tan: Claude Code'un bileşen işi ilerledi ya da bitti.</summary>
    public event Action<StudioUpdateDto>? StudioUpdate;

    /// <summary>Host'tan: gece bileşen denetimi bitti (incelenen, öğrenilen tarif, yeni kural).</summary>
    public event Action<int, int, int>? StudioReviewed;

    public Task<IReadOnlyList<StudioProjectDto>> ListStudioProjectsAsync() =>
        _connection.InvokeAsync<IReadOnlyList<StudioProjectDto>>(ChatHubContract.ListStudioProjects);

    public Task<StudioProjectDto> CreateStudioProjectAsync(string name) =>
        _connection.InvokeAsync<StudioProjectDto>(ChatHubContract.CreateStudioProject, name);

    public Task<bool> DeleteStudioProjectAsync(long id) =>
        _connection.InvokeAsync<bool>(ChatHubContract.DeleteStudioProject, id);

    public Task<StudioOpenDto?> OpenStudioProjectAsync(long id) =>
        _connection.InvokeAsync<StudioOpenDto?>(ChatHubContract.OpenStudioProject, id);

    public IAsyncEnumerable<StudioEventDto> SendStudioMessageAsync(long projectId, string text, byte[]? image, CancellationToken cancellationToken = default) =>
        _connection.StreamAsync<StudioEventDto>(ChatHubContract.SendStudioMessage, projectId, text, image, cancellationToken);

    public IAsyncEnumerable<StudioEventDto> FixStudioComponentAsync(long projectId, long versionId, string report, CancellationToken cancellationToken = default) =>
        _connection.StreamAsync<StudioEventDto>(ChatHubContract.FixStudioComponent, projectId, versionId, report, cancellationToken);

    public Task ReportStudioCheckAsync(long versionId, bool passed, string report) =>
        _connection.InvokeAsync(ChatHubContract.ReportStudioCheck, versionId, passed, report);

    public Task<string> RateStudioVersionAsync(long versionId, int rating, string? note) =>
        _connection.InvokeAsync<string>(ChatHubContract.RateStudioVersion, versionId, rating, note);

    public Task<bool> SelectStudioVersionAsync(long projectId, long versionId) =>
        _connection.InvokeAsync<bool>(ChatHubContract.SelectStudioVersion, projectId, versionId);

    public Task<string> StartClaudeComponentAsync(long projectId, string? request) =>
        _connection.InvokeAsync<string>(ChatHubContract.StartClaudeComponent, projectId, request);

    public Task<StudioProfileDto> GetStudioProfileAsync() =>
        _connection.InvokeAsync<StudioProfileDto>(ChatHubContract.GetStudioProfile);

    public Task<bool> AddDesignRuleAsync(string text) => _connection.InvokeAsync<bool>(ChatHubContract.AddDesignRule, text);

    public Task<bool> RemoveDesignRuleAsync(long id) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveDesignRule, id);

    public Task<string> AddStudioReferenceAsync(string title, string text, byte[]? image) =>
        _connection.InvokeAsync<string>(ChatHubContract.AddStudioReference, title, text, image);

    public Task<bool> RemoveStudioRecipeAsync(long id) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveStudioRecipe, id);

    /// <summary>"is", "ders" ya da "ana": İş ve Ders modunda ses modelleri ekran kartından boşaltılır.</summary>
    public Task<string> SetModeAsync(string mode) => _connection.InvokeAsync<string>(ChatHubContract.SetMode, mode);

    // ---- Cevap paketleri (Faz 19) ----

    public Task<string> GetAnswerTierAsync() => _connection.InvokeAsync<string>(ChatHubContract.GetAnswerTier);

    public Task SetAnswerTierAsync(string tier) => _connection.InvokeAsync(ChatHubContract.SetAnswerTier, tier);

    public Task<IReadOnlyList<ProAnswerDto>> ListProAnswersAsync() => _connection.InvokeAsync<IReadOnlyList<ProAnswerDto>>(ChatHubContract.ListProAnswers);

    public Task<bool> RemoveProAnswerAsync(long id) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveProAnswer, id);

    public Task<IReadOnlyList<KnownSiteDto>> ListSitesAsync() => _connection.InvokeAsync<IReadOnlyList<KnownSiteDto>>(ChatHubContract.ListSites);

    public Task<string> AddSiteAsync(string url, string? name, string? purpose) => _connection.InvokeAsync<string>(ChatHubContract.AddSite, url, name, purpose);

    public Task<bool> RemoveSiteAsync(long id) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveSite, id);

    public Task<bool> CrawlSiteAsync(long id) => _connection.InvokeAsync<bool>(ChatHubContract.CrawlSite, id);

    // ---- Gelişim panosu (Faz 18) ----

    public Task<GrowthDto> GetGrowthAsync() => _connection.InvokeAsync<GrowthDto>(ChatHubContract.GetGrowth);

    // ---- Ders arayüzü (Faz 17) ----

    /// <summary>Host'tan: Claude Code'un ders sorusu işi ilerledi ya da bitti.</summary>
    public event Action<LessonUpdateDto>? LessonUpdate;

    public Task<LessonListDto> ListLessonsAsync() => _connection.InvokeAsync<LessonListDto>(ChatHubContract.ListLessons);

    public Task<LessonSessionDto> CreateLessonAsync(string name, string? course) => _connection.InvokeAsync<LessonSessionDto>(ChatHubContract.CreateLesson, name, course);

    public Task<bool> UpdateLessonAsync(long id, string name, string instructions) => _connection.InvokeAsync<bool>(ChatHubContract.UpdateLesson, id, name, instructions);

    public Task<bool> DeleteLessonAsync(long id) => _connection.InvokeAsync<bool>(ChatHubContract.DeleteLesson, id);

    public Task<LessonOpenDto?> OpenLessonAsync(long id) => _connection.InvokeAsync<LessonOpenDto?>(ChatHubContract.OpenLesson, id);

    public IAsyncEnumerable<LessonEventDto> AskLessonAsync(long sessionId, string text, byte[]? image, CancellationToken cancellationToken = default) =>
        _connection.StreamAsync<LessonEventDto>(ChatHubContract.AskLesson, sessionId, text, image, cancellationToken);

    public Task<string> AskLessonClaudeAsync(long sessionId, long messageId) => _connection.InvokeAsync<string>(ChatHubContract.AskLessonClaude, sessionId, messageId);

    public Task<string> ReadLessonPhotoAsync(byte[] image) => _connection.InvokeAsync<string>(ChatHubContract.ReadLessonPhoto, image);

    public Task<DocumentAddResultDto> AddLessonTextAsync(long sessionId, string title, string text, bool exam) =>
        _connection.InvokeAsync<DocumentAddResultDto>(ChatHubContract.AddLessonText, sessionId, title, text, exam);

    /// <summary>Dosya kaynak: dersin belge koleksiyonuna yüklenir ("ders:&lt;id&gt;"); geçmiş sınavın adı "Sınav - " ile başlar.</summary>
    public Task<DocumentAddResultDto> UploadLessonFileAsync(long sessionId, string fileName, bool exam, Stream content, CancellationToken cancellationToken = default) =>
        UploadDocumentAsync((exam ? "Sınav - " : "") + fileName, "ders:" + sessionId, content, cancellationToken);

    public Task<bool> RemoveLessonSourceAsync(long sessionId, long documentId) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveLessonSource, sessionId, documentId);

    public Task<LessonNoteDto?> AddLessonNoteAsync(long sessionId, string text) => _connection.InvokeAsync<LessonNoteDto?>(ChatHubContract.AddLessonNote, sessionId, text);

    public Task<bool> RemoveLessonNoteAsync(long noteId) => _connection.InvokeAsync<bool>(ChatHubContract.RemoveLessonNote, noteId);

    public Task<LessonMaterialDto?> MakeLessonMaterialAsync(long sessionId, string kind, string? topic) =>
        _connection.InvokeAsync<LessonMaterialDto?>(ChatHubContract.MakeLessonMaterial, sessionId, kind, topic);

    public Task<bool> SaveLessonMaterialAsync(long materialId, string title, string content) =>
        _connection.InvokeAsync<bool>(ChatHubContract.SaveLessonMaterial, materialId, title, content);

    public Task<bool> DeleteLessonMaterialAsync(long materialId) => _connection.InvokeAsync<bool>(ChatHubContract.DeleteLessonMaterial, materialId);

    public Task<LessonMaterialDto?> MakeLessonQuizAsync(long sessionId, string? topic, int count) =>
        _connection.InvokeAsync<LessonMaterialDto?>(ChatHubContract.MakeLessonQuiz, sessionId, topic, count);

    public Task<QuizGradeDto?> GradeLessonQuizAsync(long materialId, IReadOnlyList<string> answers) =>
        _connection.InvokeAsync<QuizGradeDto?>(ChatHubContract.GradeLessonQuiz, materialId, answers);

    public Task<LessonPlanDto> MakeLessonPlanAsync(long sessionId, string? examName, DateTime examDateLocal, bool reminders) =>
        _connection.InvokeAsync<LessonPlanDto>(ChatHubContract.MakeLessonPlan, sessionId, examName, examDateLocal, reminders);

    /// <summary>Host'tan: gece denetimi bitti (okunan cevap, kendiliğinden öğrenilen, onay bekleyen).</summary>
    public event Action<int, int, int>? LearningReady;

    public Task<bool> UndoLearningAsync(long id) =>
        _connection.InvokeAsync<bool>(ChatHubContract.UndoLearning, id);

    public Task<LearningListDto> ListLearningAsync() =>
        _connection.InvokeAsync<LearningListDto>(ChatHubContract.ListLearning);

    public Task<bool> ResolveLearningAsync(long id, bool accept) =>
        _connection.InvokeAsync<bool>(ChatHubContract.ResolveLearning, id, accept);

    public Task<int> AcceptAllLearningAsync() =>
        _connection.InvokeAsync<int>(ChatHubContract.AcceptAllLearning);

    public Task<string> RunAuditNowAsync() =>
        _connection.InvokeAsync<string>(ChatHubContract.RunAuditNow);

    /// <summary>Host'tan: sabah brifingi.</summary>
    public event Action<BriefingDto>? Briefing;

    /// <summary>Kendiliğinden öneri (Faz 8).</summary>
    public event Action<string>? Suggestion;

    /// <summary>Açılışta başlatılamayan servis (Faz 14).</summary>
    public event Action<string>? ServiceWarning;

    /// <summary>"Ultron" deyince dinleme ayarı değişti.</summary>
    public event Action<bool>? WakeWordChanged;

    /// <summary>Formül/yazı boyutu konuşarak değişti.</summary>
    public event Action<AppearanceDto>? AppearanceChanged;

    public Task<AppearanceDto> GetAppearanceAsync() => _connection.InvokeAsync<AppearanceDto>(ChatHubContract.GetAppearance);

    /// <summary>Ultron yeni bir hafıza kategorisi açtı (ad, örnek bilgi).</summary>
    public event Action<string, string>? CategoryCreated;

    public Task<bool> GetWakeWordAsync() => _connection.InvokeAsync<bool>(ChatHubContract.GetWakeWord);

    public Task SetWakeWordAsync(bool on) => _connection.InvokeAsync(ChatHubContract.SetWakeWord, on);

    /// <summary>Host'tan: hatırlatma zamanı geldi (SignalR iş parçacığında gelir).</summary>
    public event Action<ReminderDueDto>? ReminderDue;

    public HubConnectionState State => _connection.State;

    /// <summary>Bağlantı durumu değişince (arayüz "bağlanıyor…" gösterebilsin).</summary>
    public event Action? StateChanged;

    private readonly Lock _startGate = new();
    private Task? _starting;

    /// <summary>
    /// Bağlanır. Uygulama (tepside, penceresiz açılışta hatırlatmalar için) ve arayüz aynı anda çağırabilir:
    /// ikisi de aynı bağlanma işini bekler.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Task start;
        lock (_startGate)
        {
            if (_starting is null || (_starting.IsCompleted && _connection.State == HubConnectionState.Disconnected))
                _starting = _connection.StartAsync(cancellationToken);
            start = _starting;
        }
        await start;
        StateChanged?.Invoke();
    }

    public Task<ConversationDto> GetCurrentConversationAsync() =>
        _connection.InvokeAsync<ConversationDto>(ChatHubContract.GetCurrentConversation);

    public Task<ConversationDto> StartNewConversationAsync() =>
        _connection.InvokeAsync<ConversationDto>(ChatHubContract.StartNewConversation);

    public Task<StatusDto> GetStatusAsync() =>
        _connection.InvokeAsync<StatusDto>(ChatHubContract.GetStatus);

    public Task<MemoryListDto> ListMemoriesAsync() =>
        _connection.InvokeAsync<MemoryListDto>(ChatHubContract.ListMemories);

    public Task<MemoryItemDto> AddMemoryAsync(string content, string category) =>
        _connection.InvokeAsync<MemoryItemDto>(ChatHubContract.AddMemory, content, category);

    public Task UpdateMemoryAsync(long id, string content, string category) =>
        _connection.InvokeAsync(ChatHubContract.UpdateMemory, id, content, category);

    public Task DeleteMemoryAsync(long id) =>
        _connection.InvokeAsync(ChatHubContract.DeleteMemory, id);

    public Task RestoreMemoryAsync(long id) =>
        _connection.InvokeAsync(ChatHubContract.RestoreMemory, id);

    public Task<CategoryDto> AddCategoryAsync(string name, string description, string drawer) =>
        _connection.InvokeAsync<CategoryDto>(ChatHubContract.AddCategory, name, description, drawer);

    public Task UpdateCategoryAsync(string key, string name, string description, bool isCore) =>
        _connection.InvokeAsync(ChatHubContract.UpdateCategory, key, name, description, isCore);

    public Task<CategoryDto?> ResolveCategorySuggestionAsync(long id, bool accept) =>
        _connection.InvokeAsync<CategoryDto?>(ChatHubContract.ResolveCategorySuggestion, id, accept);

    /// <summary>Klavye/fare kaç saniyedir kullanılmadı (Host, kullanıcı uzaktaysa bildirimleri Telegram'a da gönderir).</summary>
    public Task ReportPresenceAsync(int idleSeconds) =>
        _connection.State == HubConnectionState.Connected
            ? _connection.InvokeAsync(ChatHubContract.ReportPresence, idleSeconds)
            : Task.CompletedTask;

    public Task<bool> AcknowledgeReminderAsync(long id) =>
        _connection.InvokeAsync<bool>(ChatHubContract.AcknowledgeReminder, id);

    public Task<IReadOnlyList<DocumentDto>> ListDocumentsAsync() =>
        _connection.InvokeAsync<IReadOnlyList<DocumentDto>>(ChatHubContract.ListDocuments);

    /// <summary>Dosyayı 256 KB'lık parçalar hâlinde akıtarak yükler (hub'ın tek mesaj sınırına takılmaz).</summary>
    public Task<DocumentAddResultDto> UploadDocumentAsync(string fileName, string? collection, Stream content, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync<DocumentAddResultDto>(ChatHubContract.UploadDocument, fileName, collection, ReadParts(content, cancellationToken), cancellationToken);

    public Task<bool> DeleteDocumentAsync(long id) =>
        _connection.InvokeAsync<bool>(ChatHubContract.DeleteDocument, id);

    public Task<DocumentAddResultDto> ReprocessDocumentAsync(long id) =>
        _connection.InvokeAsync<DocumentAddResultDto>(ChatHubContract.ReprocessDocument, id);

    public Task<IReadOnlyList<FollowUpDto>> SuggestFollowUpsAsync(string question, string answer) =>
        _connection.InvokeAsync<IReadOnlyList<FollowUpDto>>(ChatHubContract.SuggestFollowUps, question, answer);

    public Task SetDocumentCollectionAsync(long id, string collection) =>
        _connection.InvokeAsync(ChatHubContract.SetDocumentCollection, id, collection);

    private static async IAsyncEnumerable<byte[]> ReadParts(Stream content, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[256 * 1024];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            yield return buffer[..read];
    }

    public Task PrepareVoiceAsync() =>
        _connection.InvokeAsync(ChatHubContract.PrepareVoice);

    /// <summary>Wake word adayı için: Host meşgulse beklemeden boş döner (kuyruk birikmez).</summary>
    public Task<string> CheckWakeWordAsync(byte[] wav, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync<string>(ChatHubContract.CheckWakeWord, wav, cancellationToken);

    public Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync<string>(ChatHubContract.Transcribe, wav, cancellationToken);

    public Task<byte[]> SpeakAsync(string text, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync<byte[]>(ChatHubContract.Speak, text, cancellationToken);

    public IAsyncEnumerable<ChatEventDto> SendAsync(Guid conversationId, string text, CancellationToken cancellationToken = default) =>
        _connection.StreamAsync<ChatEventDto>(ChatHubContract.SendMessage, conversationId, text, cancellationToken);

    /// <summary>Görselli soru: görsel (PNG/JPEG) en fazla 4 MB.</summary>
    public IAsyncEnumerable<ChatEventDto> SendWithImageAsync(Guid conversationId, string text, byte[] image, bool fromScreen, CancellationToken cancellationToken = default) =>
        _connection.StreamAsync<ChatEventDto>(ChatHubContract.SendMessageWithImage, conversationId, text, image, fromScreen, cancellationToken);

    private Task Notify()
    {
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
