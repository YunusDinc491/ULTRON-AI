namespace Ultron.Contracts;

// Host ile istemciler (masaüstü, ileride Telegram köprüsü) arasında SignalR üzerinden giden biçimler.
// Bu projede iş mantığı yok: iki taraf da aynı "sözleşmeyi" kullansın diye ayrı tutuluyor.

/// <summary>Sohbet olay türleri ("Type" alanının alabileceği değerler).</summary>
public static class ChatEventTypes
{
    public const string Intent = "intent";
    public const string Memories = "memories";
    public const string Web = "web";
    public const string WebUnavailable = "webUnavailable";
    public const string Chunk = "chunk";
    public const string Sources = "sources";
    public const string Completed = "completed";
    public const string FollowUps = "followUps";
    public const string Tools = "tools";
    public const string Documents = "documents";
    public const string Research = "research";
    public const string Library = "library";
    public const string ResearchSaved = "researchSaved";
    public const string Image = "image";
    public const string Progress = "progress";
}

/// <summary>Düz olay biçimi: istemci "Type" alanına bakıp gerisini okur.</summary>
public sealed record ChatEventDto(
    string Type,
    string? Text = null,
    string? Intent = null,
    IReadOnlyList<MemoryDto>? CoreMemories = null,
    IReadOnlyList<MemoryDto>? RelevantMemories = null,
    bool? FromCache = null,
    string? Query = null,
    IReadOnlyList<SourceDto>? Sources = null,
    long? MessageId = null,
    IReadOnlyList<FollowUpDto>? FollowUps = null,
    IReadOnlyList<ToolUseDto>? Tools = null,
    bool? FromScreen = null,
    bool? Detailed = null);

/// <summary>Cevap verilirken çalışan araç (ör. hatirlatici_kur) ve başarılı olup olmadığı.</summary>
public sealed record ToolUseDto(string Name, bool Ok);

/// <summary>Zamanı gelen hatırlatma (Host → masaüstü). <paramref name="Missed"/>: Ultron kapalıyken zamanı geçmişti.</summary>
/// <param name="Spoken">Söylenecek samimi cümle ("Patron, hatırlatmamı istemiştin, su içmen gerekiyor.").</param>
/// <param name="Priority">"Onemsiz" | "Onemli" | "CokOnemli". Önemli ve çok önemli "okudum" denene kadar tekrar gelir.</param>
/// <param name="Repeat">Aynı hatırlatmanın tekrarı (arayüz yeni kart açmaz).</param>
/// <param name="Quiet">Sessiz saatlerde: çok önemli değilse sesli okunmaz, sadece bildirim.</param>
public sealed record ReminderDueDto(long Id, string Text, DateTime DueAtLocal, bool Missed, string Spoken, string Priority, bool Repeat, bool Quiet = false)
{
    public bool NeedsAcknowledge => Priority != "Onemsiz";

    public bool ShouldSpeak => !Quiet || Priority == "CokOnemli";
}

/// <summary>Kodlama işi (Claude Code, Faz 13). <paramref name="Status"/>: Calisiyor | Soruyor | Bitti | Hata | Bekliyor | Iptal; <paramref name="Question"/> Claude'un sorusu.</summary>
public sealed record CodingJobDto(long Id, string Title, string Folder, string Status, string? Progress, string? LastMessage, string? Question, double CostUsd, DateTime UpdatedAtUtc);

/// <summary>Onay kartı (Host → masaüstü): Ultron onaylı bir işlem yapmak istiyor; <paramref name="ExpiresAtUtc"/> geçerse yapılmaz.</summary>
public sealed record ApprovalRequestDto(long Id, string Tool, string Summary, DateTime ExpiresAtUtc);

/// <summary>Onay kararı verildi ya da süre doldu (Host → masaüstü): kart kapansın. <paramref name="Decision"/>: Approved | Rejected | TimedOut.</summary>
public sealed record ApprovalClosedDto(long Id, string Decision);

/// <summary>Sabah brifingi (Host → masaüstü). <paramref name="Late"/>: bilgisayar kapalıydı, açılışta telafi.</summary>
public sealed record BriefingDto(string Text, bool Late);

/// <summary>Seçenekli takip sorusu.</summary>
public sealed record FollowUpDto(string Question, IReadOnlyList<FollowUpOptionDto> Options);

/// <summary>Düğmede kısa <paramref name="Label"/> görünür; seçilince tam cümle <paramref name="Answer"/> gönderilir.</summary>
public sealed record FollowUpOptionDto(string Label, string Answer);

public sealed record MemoryDto(long Id, string Category, string Content);

/// <summary>Cevabın kaynağı: web sayfası ya da (<paramref name="Path"/> doluysa) bilgi tabanındaki belge, <paramref name="Page"/> sayfasıyla.</summary>
public sealed record SourceDto(int Number, string Title, string Url, string? Path = null, int? Page = null);

/// <summary>Bilgi tabanındaki bir belge (/belgeler paneli).</summary>
public sealed record DocumentDto(long Id, string FileName, string Collection, string Kind, int PageCount, int ChunkCount, DateTime AddedAtUtc, string Path);

/// <summary>Belge ekleme sonucu. <paramref name="Status"/>: Added | Duplicate | Unsupported | TooLarge | NoText.</summary>
public sealed record DocumentAddResultDto(string Status, string Message, DocumentDto? Document);

public sealed record MessageDto(long Id, string Role, string Content, DateTime CreatedAtUtc);

public sealed record ConversationDto(Guid Id, DateTime StartedAtUtc, IReadOnlyList<MessageDto> Messages);

public sealed record OutgoingQueryDto(string Query, DateTime CreatedAtUtc);

/// <summary>Hafıza panelindeki bir kayıt.</summary>
public sealed record MemoryItemDto(long Id, string Category, string Content, bool IsActive, DateTime UpdatedAtUtc);

/// <summary>Hafıza kategorisi: <paramref name="Key"/> kalıcı, <paramref name="Name"/> görünen ad.</summary>
public sealed record CategoryDto(string Key, string Name, string Description, bool IsCore, bool IsBuiltIn, string Drawer);

/// <summary>Hafıza çekmecesi (PATRON, İŞ, OKUL, BİLGİ): kategoriler bunların içinde durur.</summary>
public sealed record DrawerDto(string Key, string Name, string Description);

/// <summary>Modelin önerdiği, onay bekleyen kategori.</summary>
public sealed record CategorySuggestionDto(long Id, string Name, string Description, string? Example);

/// <summary>Hafıza paneli için: aktif kayıtlar, son silinenler (geri alınabilsin), çekmeceler, kategoriler ve bekleyen öneriler.</summary>
public sealed record MemoryListDto(
    IReadOnlyList<MemoryItemDto> Active,
    IReadOnlyList<MemoryItemDto> RecentlyDeleted,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<CategorySuggestionDto> Suggestions,
    IReadOnlyList<DrawerDto> Drawers);

/// <summary>Command Center'daki durum panelleri için: sadece gerçek veriler.</summary>
public sealed record StatusDto(
    string ChatModel,
    string EmbeddingModel,
    int MemoryCount,
    IReadOnlyDictionary<string, int> MemoriesByCategory,
    bool WebOnline,
    IReadOnlyList<OutgoingQueryDto> RecentQueries,
    DateTime ServerTimeUtc,
    IReadOnlyList<ToolAuditDto>? RecentTools = null,
    IReadOnlyList<ReminderDto>? PendingReminders = null,
    string? Telegram = null,
    PermissionsDto? Permissions = null,
    string? CodeAgent = null,
    IReadOnlyList<CodingJobDto>? CodingJobs = null,
    string? WakeForBriefing = null,
    string? Backup = null,
    string? Services = null,
    string? Library = null);

/// <summary>Bilgisayar yetkileri (/durum): izinli uygulamalar, klasörler, süreli izinler ("komut_calistir · 42 dk").</summary>
public sealed record PermissionsDto(IReadOnlyList<string> Apps, IReadOnlyList<string> Folders, IReadOnlyList<string> Grants, IReadOnlyList<string>? SharedMemory = null);

/// <summary>Denetim kaydındaki bir araç çağrısı.</summary>
public sealed record ToolAuditDto(string Tool, string Arguments, string Result, bool Ok, DateTime AtUtc, string? Approval = null, string? Level = null);

public sealed record ReminderDto(long Id, string Text, DateTime DueAtLocal, string Recurrence);

/// <summary>Öğrenme paneli (Faz 15 d): gece denetiminin önerdiği bir düzeltme.</summary>
/// <param name="Verdict">"Eksik" ya da "Yanlis".</param>
/// <param name="PlainSummary">Yazılım bilmeyen birine düzeltmenin anlamı (tek cümle).</param>
/// <param name="AutoApproved">Resmi kaynak gösterildiği için kendiliğinden onaylandı.</param>
public sealed record LearningItemDto(long Id, DateTime AskedAtLocal, string Question, string Answer, string Verdict, string Correction, string? Reason, string? Reference,
    string? PlainSummary = null, bool AutoApproved = false, DateTime? DecidedAtLocal = null);

/// <param name="LastAuditLocal">Son denetim (yoksa null).</param>
/// <param name="ApprovedCount">Şimdiye kadar onaylanan düzeltme sayısı (cevaplarda kullanılanlar).</param>
/// <param name="Items">Kaynak gösterilemediği için tasarım kararını bekleyen öneriler.</param>
/// <param name="Learned">Son öğrenilenler (kendiliğinden ya da kullanıcının onayıyla); "Geri al" ile kaldırılabilir.</param>
public sealed record LearningListDto(IReadOnlyList<LearningItemDto> Items, DateTime? LastAuditLocal, bool Auditing, int ApprovedCount, IReadOnlyList<LearningItemDto>? Learned = null);

// ---- İş arayüzü (Faz 16) ----

public sealed record StudioProjectDto(long Id, string Name, DateTime UpdatedAtLocal, long? CurrentVersionId);

/// <param name="Source">"Ultron", "Duzeltme" ya da "Claude".</param>
/// <param name="Rating">1 👍, -1 👎, 0 yok.</param>
public sealed record StudioVersionDto(long Id, int Number, string Request, string Code, string? Explanation, string Source, DateTime CreatedAtLocal,
    bool? ChecksPassed, string? CheckReport, int Rating, string? Feedback);

public sealed record StudioMessageDto(long Id, bool FromUser, string Content, long? VersionId, DateTime CreatedAtLocal);

public sealed record StudioOpenDto(StudioProjectDto Project, IReadOnlyList<StudioMessageDto> Messages, IReadOnlyList<StudioVersionDto> Versions);

/// <summary>Üretim akışı: "started" (Recipes, KnownPattern), "chunk" (Text), "completed" (Version, Text = açıklama), "failed" (Text).</summary>
public sealed record StudioEventDto(string Type, string? Text = null, IReadOnlyList<string>? Recipes = null, bool? KnownPattern = null, StudioVersionDto? Version = null);

/// <summary>Claude Code'un bileşen işi: ilerleme ya da sonuç.</summary>
public sealed record StudioUpdateDto(long ProjectId, string Status, bool Finished, StudioVersionDto? Version);

public sealed record DesignRuleDto(long Id, string Text, string Source, DateTime CreatedAtLocal);

/// <param name="Kind">"Claude", "Favori", "Referans", "Denetim".</param>
public sealed record StudioRecipeDto(long Id, string Title, string Kind, string Description, DateTime CreatedAtLocal);

/// <summary>Zevk paneli: kurallar, öğrenilen tarifler ve ilerleme (Claude'a gitme ve 👍 oranı).</summary>
public sealed record StudioProfileDto(IReadOnlyList<DesignRuleDto> Rules, IReadOnlyList<StudioRecipeDto> LearnedRecipes, int Versions, int ByUltron, int ByClaude,
    int ThumbsUp, int ThumbsDown, int Recipes, bool VoiceReleased);

// ---- Ders arayüzü (Faz 17) ----

public sealed record LessonSessionDto(long Id, string Name, string? Course, string Instructions, string? ExamName, DateTime? ExamAtLocal, DateTime UpdatedAtLocal);

/// <param name="Author">"ultron", "hesap" (adımları kodla hesaplanmış çözüm) ya da "claude".</param>
/// <param name="Unsure">Ultron emin değil: "Claude'a sorayım mı?" kartı.</param>
public sealed record LessonMessageDto(long Id, bool FromUser, string Content, IReadOnlyList<string> Sources, string Author, bool Unsure, DateTime CreatedAtLocal);

public sealed record LessonNoteDto(long Id, string Text, string Source, DateTime CreatedAtLocal);

/// <summary>Quiz sorusu (doğru cevap istemciye gitmez; puanlamada açıklamayla gelir).</summary>
/// <param name="Type">"coktan", "sayisal" ya da "acik".</param>
public sealed record QuizQuestionDto(string Type, string Question, IReadOnlyList<string> Choices, string? Unit, string Topic);

/// <param name="Kind">"ozet", "formul", "quiz" ya da "plan".</param>
public sealed record LessonMaterialDto(long Id, string Kind, string Title, string Content, DateTime UpdatedAtLocal, IReadOnlyList<QuizQuestionDto>? Quiz);

public sealed record WeakTopicDto(string Topic, int Misses, int Hits, bool IsWeak);

public sealed record LessonOpenDto(LessonSessionDto Session, IReadOnlyList<LessonMessageDto> Messages, IReadOnlyList<LessonNoteDto> Notes,
    IReadOnlyList<LessonMaterialDto> Materials, IReadOnlyList<WeakTopicDto> Topics, IReadOnlyList<DocumentDto> Sources);

/// <param name="Courses">Ders programındaki, henüz oturumu olmayan dersler.</param>
public sealed record LessonListDto(IReadOnlyList<LessonSessionDto> Sessions, IReadOnlyList<string> Courses, bool VoiceReleased);

/// <summary>Soru akışı: "started" (Sources, Solving), "chunk" (Text), "completed" (Message), "failed" (Text).</summary>
public sealed record LessonEventDto(string Type, string? Text = null, IReadOnlyList<string>? Sources = null, bool? Solving = null, LessonMessageDto? Message = null);

/// <summary>Claude Code'un ders sorusu işi: ilerleme ya da cevap.</summary>
public sealed record LessonUpdateDto(long SessionId, string Status, bool Finished, LessonMessageDto? Message);

public sealed record QuizResultDto(int Index, bool Correct, string Feedback);

public sealed record QuizGradeDto(int Correct, int Total, IReadOnlyList<QuizResultDto> Results, IReadOnlyList<string> WeakTopics);

public sealed record LessonPlanDto(string Message, LessonMaterialDto? Material);

// ---- Cevap paketleri (Faz 19) ----

/// <param name="Lifetime">"kalici" ya da "gun"/"hafta"/"ay" (süreli).</param>
/// <summary>Ekran görünümü (konuşarak değişir: "formülleri büyüt"); yüzde, 100 = varsayılan.</summary>
public sealed record AppearanceDto(int FormulaPercent, int TextPercent);

public sealed record ProAnswerDto(long Id, string Question, string Answer, string Lifetime, DateTime? ExpiresAtLocal, DateTime CreatedAtLocal, int Uses, string Origin = "pro");

/// <summary>Takip edilen site (Faz 20, Bildiğim siteler).</summary>
public sealed record KnownSiteDto(long Id, string Name, string Url, string Purpose, int PageCount, DateTime? LastCrawledLocal, string? LastResult);

// ---- Gelişim panosu (Faz 18) ----

/// <param name="Trend">Geçen haftaya göre: +1 iyi yöne, -1 kötü yöne, 0 değişmedi/veri yok.</param>
public sealed record GrowthCardDto(string Group, string Title, string Value, string Note, int Trend);

/// <param name="Values">Haftalık değerler (eskiden yeniye; veri yoksa null).</param>
public sealed record GrowthSeriesDto(string Group, string Title, string Unit, IReadOnlyList<double?> Values, bool HigherIsBetter);

/// <param name="Area">"yazilim", "bilesen" ya da "ders".</param>
public sealed record NightlyReportDto(string Area, string Summary, DateTime AtLocal);

public sealed record WeakTopicRowDto(string Session, string Topic, int Misses, int Hits);

public sealed record GrowthDto(IReadOnlyList<string> Weeks, IReadOnlyList<GrowthCardDto> Cards, IReadOnlyList<GrowthSeriesDto> Series,
    IReadOnlyList<NightlyReportDto> Reports, IReadOnlyList<WeakTopicRowDto> WeakTopics);

/// <summary>Hub adresi ve metot adları: iki tarafta yazım hatası olmasın.</summary>
public static class ChatHubContract
{
    public const string Path = "/hub/chat";
    public const string GetCurrentConversation = nameof(GetCurrentConversation);
    public const string StartNewConversation = nameof(StartNewConversation);

    /// <summary>Ölçüm/test konuşması (Gelişim panosuna sayılmaz). İstemci → Host.</summary>
    public const string StartTestConversation = nameof(StartTestConversation);
    public const string SendMessage = nameof(SendMessage);
    public const string GetStatus = nameof(GetStatus);
    public const string ListMemories = nameof(ListMemories);
    public const string AddMemory = nameof(AddMemory);
    public const string UpdateMemory = nameof(UpdateMemory);
    public const string DeleteMemory = nameof(DeleteMemory);
    public const string RestoreMemory = nameof(RestoreMemory);
    public const string AddCategory = nameof(AddCategory);
    public const string UpdateCategory = nameof(UpdateCategory);
    public const string ResolveCategorySuggestion = nameof(ResolveCategorySuggestion);
    public const string PrepareVoice = nameof(PrepareVoice);
    public const string Transcribe = nameof(Transcribe);

    /// <summary>"Ultron" dendi mi kontrolü (kuyruksuz; meşgulse boş). İstemci → Host.</summary>
    public const string CheckWakeWord = nameof(CheckWakeWord);

    /// <summary>CheckWakeWord cevabı: kontrol yapılamadı (meşgul/zaman aşımı). Ret DEĞİL; masaüstü adayı sessizce atlar.</summary>
    public const string WakeCheckBusy = "<meşgul>";
    public const string Speak = nameof(Speak);
    public const string ListDocuments = nameof(ListDocuments);
    public const string UploadDocument = nameof(UploadDocument);
    public const string DeleteDocument = nameof(DeleteDocument);
    public const string SetDocumentCollection = nameof(SetDocumentCollection);
    public const string ReprocessDocument = nameof(ReprocessDocument);
    public const string SuggestFollowUps = nameof(SuggestFollowUps);
    public const string SendMessageWithImage = nameof(SendMessageWithImage);

    /// <summary>Host → istemci: hatırlatma zamanı geldi (<see cref="ReminderDueDto"/>).</summary>
    public const string ReminderDue = nameof(ReminderDue);

    /// <summary>Host → istemci: sabah brifingi (<see cref="BriefingDto"/>).</summary>
    public const string Briefing = nameof(Briefing);

    /// <summary>Host → istemci: kendiliğinden öneri (metin).</summary>
    public const string Suggestion = nameof(Suggestion);

    /// <summary>Host → istemci: açılışta başlatılamayan servis uyarısı (metin).</summary>
    public const string ServiceWarning = nameof(ServiceWarning);

    /// <summary>"Ultron" deyince dinleme açık mı (Faz 14). İstemci → Host.</summary>
    public const string GetWakeWord = nameof(GetWakeWord);

    /// <summary>Tepsi menüsünden aç/kapat. İstemci → Host.</summary>
    public const string SetWakeWord = nameof(SetWakeWord);

    /// <summary>Host → istemci: ayar değişti (bool; konuşarak ya da tepsiden).</summary>
    public const string WakeWordChanged = nameof(WakeWordChanged);

    /// <summary>Host → istemci: Ultron yeni bir hafıza kategorisi açtı (ad, örnek bilgi).</summary>
    public const string CategoryCreated = nameof(CategoryCreated);

    /// <summary>İstemci → Host: klavye/fare kaç saniyedir boşta (bilgisayar başında mı?).</summary>
    public const string ReportPresence = nameof(ReportPresence);

    /// <summary>İstemci → Host: "okudum" (tekrar eden hatırlatma susar).</summary>
    public const string AcknowledgeReminder = nameof(AcknowledgeReminder);

    /// <summary>Host → istemci: onay kartı aç (<see cref="ApprovalRequestDto"/>).</summary>
    public const string ApprovalRequested = nameof(ApprovalRequested);

    /// <summary>Host → istemci: onay kartı kapansın (<see cref="ApprovalClosedDto"/>).</summary>
    public const string ApprovalClosed = nameof(ApprovalClosed);

    /// <summary>İstemci → Host: onay kararı (id, onay).</summary>
    public const string ResolveApproval = nameof(ResolveApproval);

    /// <summary>Host → istemci: kodlama işi güncellendi (<see cref="CodingJobDto"/>).</summary>
    public const string CodingUpdate = nameof(CodingUpdate);

    /// <summary>İstemci → Host: Claude'un sorusuna cevap ya da "devam et".</summary>
    public const string ReplyCoding = nameof(ReplyCoding);

    public const string CancelCoding = nameof(CancelCoding);

    /// <summary>İstemci → Host: Öğrenme paneli (<see cref="LearningListDto"/>).</summary>
    public const string ListLearning = nameof(ListLearning);

    /// <summary>İstemci → Host: öneriyi onayla/reddet (id, onay) → bool.</summary>
    public const string ResolveLearning = nameof(ResolveLearning);

    /// <summary>İstemci → Host: öğrenilmiş bir düzeltmeyi geri al (id) → bool.</summary>
    public const string UndoLearning = nameof(UndoLearning);

    /// <summary>İstemci → Host: bekleyen bütün önerileri onayla → sayı.</summary>
    public const string AcceptAllLearning = nameof(AcceptAllLearning);

    /// <summary>İstemci → Host: denetimi şimdi çalıştır (beklemeden) → rapor.</summary>
    public const string RunAuditNow = nameof(RunAuditNow);

    public const string ListStudioProjects = nameof(ListStudioProjects);
    public const string CreateStudioProject = nameof(CreateStudioProject);
    public const string DeleteStudioProject = nameof(DeleteStudioProject);
    public const string OpenStudioProject = nameof(OpenStudioProject);
    public const string SendStudioMessage = nameof(SendStudioMessage);
    public const string FixStudioComponent = nameof(FixStudioComponent);
    public const string ReportStudioCheck = nameof(ReportStudioCheck);
    public const string RateStudioVersion = nameof(RateStudioVersion);
    public const string SelectStudioVersion = nameof(SelectStudioVersion);
    public const string StartClaudeComponent = nameof(StartClaudeComponent);
    public const string GetStudioProfile = nameof(GetStudioProfile);
    public const string AddDesignRule = nameof(AddDesignRule);
    public const string RemoveDesignRule = nameof(RemoveDesignRule);
    public const string AddStudioReference = nameof(AddStudioReference);
    public const string RemoveStudioRecipe = nameof(RemoveStudioRecipe);

    public const string GetGrowth = nameof(GetGrowth);
    public const string GetAnswerTier = nameof(GetAnswerTier);
    public const string SetAnswerTier = nameof(SetAnswerTier);

    /// <summary>Ultron Pro hazır mı (Claude, Codex ya da Gemini bağlı mı); değilse ne yapmalı.</summary>
    public const string GetProStatus = nameof(GetProStatus);

    /// <summary>Kullanıcının istediği hitap (Faz 26; boş = isimle hitap yok). Telefon selamında kullanılır.</summary>
    public const string GetUserAddress = nameof(GetUserAddress);

    /// <summary>Formül ve yazı boyutu. İstemci → Host.</summary>
    public const string GetAppearance = nameof(GetAppearance);

    /// <summary>Host → istemci: görünüm ayarı konuşarak değişti (<see cref="AppearanceDto"/>).</summary>
    public const string AppearanceChanged = nameof(AppearanceChanged);
    public const string ListProAnswers = nameof(ListProAnswers);
    public const string RemoveProAnswer = nameof(RemoveProAnswer);

    /// <summary>Bildiğim siteler (Faz 20). İstemci → Host.</summary>
    public const string ListSites = nameof(ListSites);
    public const string AddSite = nameof(AddSite);
    public const string RemoveSite = nameof(RemoveSite);
    public const string CrawlSite = nameof(CrawlSite);
    public const string ListLessons = nameof(ListLessons);
    public const string CreateLesson = nameof(CreateLesson);
    public const string UpdateLesson = nameof(UpdateLesson);
    public const string DeleteLesson = nameof(DeleteLesson);
    public const string OpenLesson = nameof(OpenLesson);
    public const string AskLesson = nameof(AskLesson);
    public const string AskLessonClaude = nameof(AskLessonClaude);
    public const string ReadLessonPhoto = nameof(ReadLessonPhoto);
    public const string AddLessonText = nameof(AddLessonText);
    public const string RemoveLessonSource = nameof(RemoveLessonSource);
    public const string AddLessonNote = nameof(AddLessonNote);
    public const string RemoveLessonNote = nameof(RemoveLessonNote);
    public const string MakeLessonMaterial = nameof(MakeLessonMaterial);
    public const string SaveLessonMaterial = nameof(SaveLessonMaterial);
    public const string DeleteLessonMaterial = nameof(DeleteLessonMaterial);
    public const string MakeLessonQuiz = nameof(MakeLessonQuiz);
    public const string GradeLessonQuiz = nameof(GradeLessonQuiz);
    public const string MakeLessonPlan = nameof(MakeLessonPlan);

    /// <summary>Host → istemci: Claude Code'un ders sorusu işi (<see cref="LessonUpdateDto"/>).</summary>
    public const string LessonUpdate = nameof(LessonUpdate);

    /// <summary>İstemci → Host: arayüz modu ("ana", "is", "ders"): İş ve Ders modunda ses modelleri ekran kartından boşaltılır.</summary>
    public const string SetMode = nameof(SetMode);

    /// <summary>Host → istemci: Claude Code'un bileşen işi (<see cref="StudioUpdateDto"/>).</summary>
    public const string StudioUpdate = nameof(StudioUpdate);

    /// <summary>Host → istemci: bileşen denetimi bitti (incelenen, öğrenilen tarif, yeni kural).</summary>
    public const string StudioReviewed = nameof(StudioReviewed);

    /// <summary>Host → istemci: gece denetimi bitti (okunan, kendiliğinden öğrenilen, onay bekleyen).</summary>
    public const string LearningReady = nameof(LearningReady);

    // Faz 25 · Mobil (sadece bu bilgisayardan; telefondan çağrılamaz)
    public const string GetMobilePanel = nameof(GetMobilePanel);
    public const string NewMobilePairingCode = nameof(NewMobilePairingCode);
    public const string RemoveMobileDevice = nameof(RemoveMobileDevice);

    // Faz 26 · İlk açılış sihirbazı (sadece bu bilgisayardan)
    public const string GetSetupStatus = nameof(GetSetupStatus);
    public const string InstallOllama = nameof(InstallOllama);
    public const string PullModels = nameof(PullModels);
    public const string DownloadVoiceModels = nameof(DownloadVoiceModels);
    public const string InstallDocker = nameof(InstallDocker);
    public const string SetupSearxng = nameof(SetupSearxng);
    public const string SaveSetupKey = nameof(SaveSetupKey);
    public const string SaveSetupProfile = nameof(SaveSetupProfile);
    public const string CompleteSetup = nameof(CompleteSetup);

    /// <summary>Ana ekranın sağ bilgi paneli (2026-10-31): takvim, hava, döviz, Ultron.</summary>
    public const string GetInfoPanel = nameof(GetInfoPanel);
}

// ---- Mobil (Faz 25) ----

/// <summary>Eşleşmiş telefon kartı.</summary>
public sealed record MobileDeviceDto(string Id, string Name, DateTime PairedAtLocal, DateTime? LastSeenLocal);

/// <summary>Masaüstü "Mobil" bölümü. <paramref name="TailscaleAddress"/>: telefona yazılacak adres; Tailscale yoksa null.</summary>
public sealed record MobilePanelDto(string? TailscaleAddress, IReadOnlyList<MobileDeviceDto> Devices);

public sealed record MobilePairingDto(string Code, string? TailscaleAddress, DateTime ExpiresAtLocal);

// ---- İlk açılış sihirbazı (Faz 26) ----

/// <summary>
/// Kurulumun durumu: yapay zekâ motoru (Ollama + modeller), ses modelleri, web araması, isteğe bağlı anahtarlar, hitap/şehir.
/// <paramref name="Completed"/>: sihirbaz bir kez bitirildi (bir daha kendiliğinden açılmaz).
/// </summary>
public sealed record SetupStatusDto(
    bool Completed,
    bool OllamaInstalled,
    bool OllamaRunning,
    IReadOnlyList<string> MissingModels,
    IReadOnlyList<string> MissingVoiceFiles,
    long VoiceDownloadBytes,
    bool TavilyKey,
    bool SearxngRunning,
    bool DockerInstalled,
    bool GroqKey,
    bool ElevenLabsKey,
    string Address,
    string City)
{
    public bool EngineReady => OllamaRunning && MissingModels.Count == 0;

    public bool VoiceReady => MissingVoiceFiles.Count == 0;

    public bool WebReady => TavilyKey || SearxngRunning;
}

/// <summary>Uzun süren kurulum adımının ilerlemesi. <paramref name="Percent"/> 0-100 (bilinmiyorsa null); <paramref name="Error"/> doluysa adım başarısız.</summary>
public sealed record SetupProgressDto(string Text, double? Percent = null, bool Done = false, string? Error = null);

/// <summary>Kısa form: hepsi isteğe bağlı. Hitap ve şehir ayara, iş/okul ve ilgi alanları hafızaya yazılır.</summary>
public sealed record SetupProfileDto(string Address, string City, string Work, string Interests);

// ---- Ana ekran sağ bilgi paneli (2026-10-31) ----

/// <summary>Dolar, euro, gram altın (TL); <paramref name="ChangePercent"/> önceki kapanışa göre.</summary>
public sealed record InfoRateDto(string Name, double Value, double? ChangePercent);

/// <summary>
/// Sağ panel: BUGÜN (dersler, hatırlatmalar, yaklaşan sınav), HAVA &amp; PİYASA (şehir, hava, kurlar), ULTRON (çalışma süresi,
/// birlikte geçen gün, hafıza ve "seni tanıma" yüzdesi). Kurlar 5 dakikada bir yenilenir.
/// </summary>
public sealed record InfoPanelDto(
    string Date,
    IReadOnlyList<string> Today,
    string? NextExam,
    string City,
    string? Weather,
    string? WeatherDetail,
    IReadOnlyList<InfoRateDto> Rates,
    string? RatesNote,
    double UptimeMinutes,
    int? DaysTogether,
    int Memories,
    int Categories,
    int KnowPercent);

/// <summary>Ultron Pro'nun bağlı hesabı (2026-10-31): <paramref name="Provider"/> Claude, Codex ya da Gemini; hazır değilse <paramref name="Message"/> ne yapılacağını söyler.</summary>
public sealed record ProStatusDto(bool Ready, string Provider, string Message);
