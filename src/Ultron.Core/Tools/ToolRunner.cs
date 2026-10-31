using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Ultron.Core.Chat;
using Ultron.Core.Text;

namespace Ultron.Core.Tools;

/// <summary>Bir araç çağrısının özeti (arayüzde "HATIRLATICI KURULDU" gibi gösterilir).</summary>
public sealed record ToolUse(string Name, bool Ok, string Arguments, string Result = "", string? Question = null);

/// <param name="Declined">Model hiçbir araç seçmedi ve soru da sormadı (mesaj bir komut değil, bilgi/sohbet): çağıran normal sohbete dönebilir.</param>
public sealed record ToolRunResult(string Answer, IReadOnlyList<ToolUse> Uses, bool Declined = false);

/// <summary>
/// Araç döngüsü: model araç ister → Ultron izin seviyesine bakıp çalıştırır → sonucu modele "veri" olarak verir →
/// model ya yeni araç ister ya da son cevabı yazar. Mutfaktaki aşçı gibi: tarife (modele) bakar ama ocağı (aracı)
/// sadece izin verilen ölçüde yakar ve her yaptığını deftere (denetim kaydı) yazar.
/// </summary>
public sealed partial class ToolRunner(
    IChatModel chat,
    IEnumerable<ITool> tools,
    IToolAuditLog audit,
    TimeProvider time,
    ILogger<ToolRunner> logger,
    IApprovalService? approvalService = null)
{
    /// <summary>En fazla bu kadar planlama turu (birbirine bağlı işler için; aynı işlem tekrar edilmez).</summary>
    public const int MaxRounds = 4;

    /// <summary>Tarayıcıda çok adımlı işler için (Faz 21).</summary>
    public const int MaxBrowserRounds = 8;

    private readonly IApprovalService approvals = approvalService ?? new NoApprovalService();

    public const string NotDoneAnswer = "Bunu yapamadım; ilgili işlemi gerçekleştiremedim.";

    public const string NudgeMessage = "(Sistem notu: işlemi yazıyla anlatma; isteğimdeki bilgilerle ilgili aracı şimdi çağır. Gerçekten eksik bilgi varsa tek kısa soru sor.)";

    public IReadOnlyList<ToolDefinition> Definitions(IReadOnlyCollection<string>? only = null) =>
        tools.Where(t => only is null || only.Contains(t.Definition.Name)).Select(t => t.Definition).ToList();

    /// <param name="only">Sadece bu araçlar sunulur (ör. hava sorusunda yalnız hava aracı). Null = hepsi.</param>
    /// <param name="expectCall">İstek açıkça bir araç gerektiriyor (kelimelerden grup seçildi): model ilk turda aracı atlarsa bir kez uyarılır.</param>
    public async Task<ToolRunResult> RunAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyCollection<string>? only = null,
        CancellationToken cancellationToken = default, bool expectCall = false)
    {
        IReadOnlyList<ToolDefinition> definitions = Definitions(only);
        var conversation = messages.ToList();
        var uses = new List<ToolUse>();

        var nudged = false;
        // Tarayıcı işleri (aç → oku → tıkla → doldur) daha çok adım ister.
        var maxRounds = only is not null && only.Contains("tarayici") ? MaxBrowserRounds : MaxRounds;
        for (var round = 0; round < maxRounds; round++)
        {
            ToolTurn turn;
            try
            {
                turn = await chat.CompleteWithToolsAsync(conversation, definitions, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Araç kararı alınamadı");
                turn = new ToolTurn("", []);
            }
            logger.LogDebug("Araç turu {Round}: {Calls} çağrı, metin: {Content}", round, turn.Calls.Count, turn.Content);
            // Araç çalıştıktan sonraki tur sonucu söyleme turudur (araç sunulmaz); o turda gelen çağrılar yok sayılır.
            if (definitions.Count == 0)
                return Finish(turn.Content, uses);
            if (turn.Calls.Count == 0)
            {
                // Model aracı çağırmadan "kuruyorum/kurdum" dediyse ya da açık bir hatırlatma isteğini atladıysa bir kez uyarıp tekrar sor.
                if (!nudged && uses.Count == 0 && (expectCall || ClaimsAction(turn.Content) || ToolRouting.ExpectsToolCall(ToolRunContext.UserText)))
                {
                    nudged = true;
                    logger.LogInformation("Araç çağrılmadan işlem iddiası, tekrar soruluyor: {Answer}", turn.Content);
                    conversation.Add(new ChatMessage(Role.Assistant, turn.Content));
                    conversation.Add(new ChatMessage(Role.User, NudgeMessage));
                    continue;
                }
                // Devam turu: başka iş yok. Model bir şey sorduysa (eksik bilgi) o cevaptır; yoksa yapılanların özeti istenir.
                if (uses.Count > 0 && string.IsNullOrWhiteSpace(turn.Content))
                    break;
                return Finish(turn.Content, uses);
            }

            // Aynı işlem (aynı araç + aynı parametreler) iki kez yapılmasın; başarısız bir işlem (ör. eksik numara) devam turunda
            // farklı parametreyle yeniden denenebilir.
            var fresh = turn.Calls.Where(c => !uses.Any(u => u.Name == c.Name && u.Arguments == c.Arguments.GetRawText())).ToList();
            if (fresh.Count == 0)
                break;
            conversation.Add(new ChatMessage(Role.Assistant, turn.Content, fresh));
            foreach (var call in fresh)
            {
                var result = await ExecuteAsync(call, cancellationToken);
                uses.Add(new ToolUse(call.Name, result.Ok, call.Arguments.GetRawText(), result.ForModel, result.Question));
                conversation.Add(new ChatMessage(Role.Tool, result.ForModel, ToolName: call.Name));
            }
            // Sıradaki tur: mesajda başka iş kaldıysa (ör. "listele, sonra yarınkini iptal et" → iptal listeden gelen numarayla) onu yap.
            // kullanıcı: "tek mesajda birden çok şey söylerim, hepsini yapabilsin" (2026-10-27).
            conversation.Add(new ChatMessage(Role.User, ContinueNote));
        }

        // İşler bitti: araçsız son cevap (yapılanların kısa özeti). Devam notları özet turunu şaşırtmasın.
        conversation.RemoveAll(m => m.Role == Role.User && m.Content == ContinueNote);
        var last = await chat.CompleteWithToolsAsync(conversation, [], cancellationToken);
        return Finish(last.Content, uses);
    }

    public const string ContinueNote =
        "(Sistem notu: araç sonuçları yukarıda. Son isteğimde HENÜZ YAPILMAMIŞ bir iş kaldıysa onu yap; yapılanları tekrarlama. Hepsi yapıldıysa işlem seçme ve soru alanını boş bırak.)";

    /// <summary>Tek araç çağrısı (hızlı komut yolu, Faz 23): izin seviyesi, onay kartı ve denetim kaydı aynı kurallarla.</summary>
    public Task<ToolResult> RunOneAsync(ToolCall call, CancellationToken cancellationToken = default) => ExecuteAsync(call, cancellationToken);

    private async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(t => t.Definition.Name == call.Name);
        ToolResult? result = null;
        string? approval = null;
        var level = PermissionLevel.Serbest;
        if (tool is null)
        {
            result = ToolResult.Fail($"'{call.Name}' adında bir araç yok.");
        }
        else
        {
            level = await SafeLevelAsync(tool, call.Arguments, cancellationToken);
            if (level == PermissionLevel.Yasak)
            {
                result = ToolResult.Fail("Bu işlem yasak (sistem ayarları, sistem klasörleri, kalıcı silme, parola, para); kullanıcının kendisi yapmalı.");
            }
            else if (level == PermissionLevel.Onayli)
            {
                // Onay kartı: kullanıcı ekranda (ya da Telegram'da) onaylamadan işlem yapılmaz.
                var decision = await approvals.RequestAsync(call.Name, tool.Describe(call.Arguments), cancellationToken);
                approval = decision switch
                {
                    ApprovalDecision.Approved => "onaylandı",
                    ApprovalDecision.Rejected => "reddedildi",
                    ApprovalDecision.TimedOut => "cevap gelmedi",
                    _ => "onay sorulamadı",
                };
                if (decision != ApprovalDecision.Approved)
                {
                    result = ToolResult.Fail(decision switch
                    {
                        ApprovalDecision.Rejected => "Kullanıcı bu işlemi REDDETTİ; yapılmadı. Bunu kısaca söyle, tekrar deneme.",
                        // Gece sınavı: model bunu "Üzgünüm, daha sonra tekrar dene" diye anlatıyordu; ne olduğunu açıkça söylesin.
                        ApprovalDecision.TimedOut => "Ekrandaki onay kartı süresi içinde onaylanmadı; işlem BAŞLATILMADI. Kullanıcıya tek cümleyle şunu söyle: " +
                            "\"Onay kartını onaylamadığın için başlatmadım; istersen tekrar söyle, kartı onaylayınca başlarım.\" Özür dileme, hata yok deme.",
                        _ => "Bu işlem onay gerektiriyor ama onay soracak ekran yok (masaüstü uygulaması kapalı); işlem yapılmadı.",
                    });
                }
            }
        }

        if (result is null)
        {
            try
            {
                result = await tool!.ExecuteAsync(call.Arguments, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
            {
                logger.LogWarning(ex, "Araç hata verdi: {Tool}", call.Name);
                result = ToolResult.Fail(ex is JsonException or KeyNotFoundException or InvalidOperationException
                    ? "Parametreler geçersiz: " + ex.Message
                    : "Araç çalışırken hata oluştu.");
            }
        }

        logger.LogInformation("Araç {Tool}({Args}) → {Ok}: {Result}", call.Name, call.Arguments.GetRawText(), result.Ok, result.ForModel);
        await audit.AddAsync(new ToolAuditEntry
        {
            Tool = call.Name,
            Arguments = call.Arguments.GetRawText(),
            Result = result.ForModel.Length > 1000 ? result.ForModel[..1000] : result.ForModel,
            Ok = result.Ok,
            Level = level,
            Approval = approval,
            AtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        return result;
    }

    /// <summary>Seviye hesaplanamazsa (bozuk parametre) en güvenlisi: onay iste.</summary>
    private async Task<PermissionLevel> SafeLevelAsync(ITool tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await tool.LevelForAsync(arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "İzin seviyesi hesaplanamadı, onay istenecek: {Tool}", tool.Definition.Name);
            return PermissionLevel.Onayli;
        }
    }

    /// <summary>
    /// Kod seviyesi koruma (Faz 1 dersi): hiçbir araç başarıyla çalışmadıysa model "kurdum, ayarladım" diyemez.
    /// Soru sorması ("Saat kaçta?") ya da bilgi vermesi serbest.
    /// </summary>
    private static ToolRunResult Finish(string answer, List<ToolUse> uses)
    {
        // Araç çalıştı ama model son cevabı boş bıraktı (canlı denemede görüldü): aracın sonucu söylenir.
        if (string.IsNullOrWhiteSpace(answer) && uses.LastOrDefault(u => u.Ok) is { } done)
            answer = done.Result;
        // Araç kullanıcıdan bilgi istedi (ör. hatırlatmanın önemi): model düzgün bir soru sormadıysa aracın sorusu sorulur.
        if (!uses.Any(u => u.Ok) && uses.LastOrDefault(u => u.Question is not null) is { Question: { } question }
            && (!answer.Contains('?') || ClaimsAction(answer)))
            return new ToolRunResult(question, uses);
        if (string.IsNullOrWhiteSpace(answer))
            return new ToolRunResult("Bunu şu an yapamadım; bir kez daha söyler misin?", uses, Declined: uses.Count == 0);
        if (!uses.Any(u => u.Ok) && ClaimsAction(answer))
            answer = uses.Count > 0 ? NotDoneAnswer + " " + "Araç hata verdi; birazdan tekrar deneyebilirsin." : NotDoneAnswer;
        return new ToolRunResult(answer.Trim(), uses);
    }

    public static bool ClaimsAction(string answer) => ActionClaim().IsMatch(" " + TurkishText.Normalize(answer) + " ");

    // Geçmiş ("kurdum") ve şimdiki zaman ("kuruyorum", "kontrol ediyorum") iddiaları: ikisi de araç çağrısı olmadan yalan.
    [GeneratedRegex(@" (kurdum|kuruldu|kuruyorum|ayarladim|ayarlandi|ayarliyorum|olusturdum|olusturuldu|olusturuyorum|ekledim|eklendi|ekliyorum|kaydettim|kaydedildi|kaydediyorum|hatirlatacagim|iptal ettim|iptal edildi|iptal ediyorum|sildim|silindi|siliyorum|actim|aciyorum|gonderdim|gonderiyorum|kontrol ediyorum|bakiyorum|hesapliyorum|yapiyorum|listeliyorum|getiriyorum) ")]
    private static partial Regex ActionClaim();
}
