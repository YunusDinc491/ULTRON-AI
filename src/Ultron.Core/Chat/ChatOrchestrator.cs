using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Conversations;
using Ultron.Core.Documents;
using Ultron.Core.Memories;
using Ultron.Core.Research;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Core.Web;

namespace Ultron.Core.Chat;

/// <summary>
/// Bir kullanıcı mesajının baştan sona yolculuğu. Orkestra şefi gibi: kendisi çalgı çalmaz,
/// hafızayı, router'ı, web'i ve modeli doğru sırada devreye sokar. Terminal, masaüstü ve ileride
/// Telegram aynı akışı kullanır; aradaki tek fark olayları nasıl gösterdikleri.
/// Durumsuzdur: geçmiş her mesajda veritabanından okunur.
/// </summary>
public sealed class ChatOrchestrator(
    IChatModel chat,
    IConversationStore conversations,
    MemoryService memory,
    IMemoryQueue memoryQueue,
    IntentRouter router,
    WebAnswerService webAnswer,
    FollowUpService followUps,
    ToolRunner tools,
    DocumentService documents,
    DocumentReader reader,
    ResearchService research,
    Library.LibraryService library,
    IOptions<ChatOptions> options,
    TimeProvider time,
    ILogger<ChatOrchestrator> logger,
    ICodeChatModel? codeChat = null,
    Learning.LearningService? learning = null,
    Settings.UltronSettings? settings = null,
    DeepWebService? deepWeb = null,
    Pro.ProService? pro = null,
    Self.SelfRequests? self = null,
    Sites.SiteService? sites = null)
{
    public IAsyncEnumerable<ChatEvent> SendAsync(Guid conversationId, string userText, CancellationToken cancellationToken = default) =>
        SendAsync(conversationId, userText, image: null, cancellationToken);

    /// <param name="image">Soruya eklenmiş görsel (fotoğraf, ekran görüntüsü): cevabı gemma4 görsele bakarak verir (Faz 11).</param>
    public async IAsyncEnumerable<ChatEvent> SendAsync(
        Guid conversationId,
        string userText,
        ImageInput? image,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1) Kaydet ve bilgi çıkarmayı kuyruğa at (cevabı bekletmez).
        var history = (await conversations.GetRecentMessagesAsync(conversationId, options.Value.HistoryWindow, cancellationToken)).ToList();
        var previousAssistant = history.LastOrDefault(m => m.Role == Role.Assistant)?.Content;
        // Görsel kaydedilmez (gizlilik); konuşma kaydında yerinde bir işaret kalır.
        var stored = image is null ? userText : $"[{(image.FromScreen ? "Ekran görüntüsü" : "Görsel")}] {userText}".TrimEnd();
        var userMessage = await conversations.AddMessageAsync(conversationId, Role.User, stored, cancellationToken);
        history.Add(userMessage);
        if (userText.Length > 0)
            memoryQueue.Enqueue(new MemoryJob(userText, userMessage.Id, previousAssistant));

        // Görselli soru: görsel + soru doğrudan modele (kişilik, hafıza ve konuşma geçmişiyle birlikte).
        if (image is not null)
        {
            yield return new ImageAttached(image.FromScreen);
            var imageRecall = await memory.RecallAsync(userText.Length > 0 ? userText : "görsel", cancellationToken);
            var question = userText.Length > 0 ? userText : ImagePrompts.DefaultQuestion;
            var imageNotes = new List<string> { ImagePrompts.Note(image.FromScreen) };
            imageNotes.AddRange(AnswerStyle.NotesFor(question));
            var messages = ChatContext.Build(SystemPrompt.Build(time.GetLocalNow().DateTime, imageRecall, imageNotes), history, options.Value.HistoryWindow);
            messages[^1] = new ChatMessage(Role.User, question, Images: [image.Bytes]);

            var seen = new StringBuilder();
            await foreach (var part in chat.StreamAsync(messages, cancellationToken))
            {
                seen.Append(part);
                yield return new AnswerChunk(part);
            }

            var savedImageAnswer = await conversations.AddMessageAsync(conversationId, Role.Assistant, seen.ToString(), cancellationToken);
            yield return new AnswerCompleted(savedImageAnswer.Id, savedImageAnswer.Content);
            yield break;
        }

        // Ultron'un kendisiyle ilgili istek ("formülleri büyüt", "arayüze tema ekle"): kod yapar ya da dürüstçe "yapamıyorum" der.
        if (self is not null && await self.HandleAsync(userText, "sohbet", previousAssistant, cancellationToken) is { } selfReply)
        {
            yield return new IntentDetected(MessageIntent.Komut);
            yield return new AnswerChunk(selfReply);
            var savedSelf = await conversations.AddMessageAsync(conversationId, Role.Assistant, selfReply, cancellationToken);
            yield return new AnswerCompleted(savedSelf.Id, savedSelf.Content);
            yield break;
        }

        // Hızlı komut yolu (Faz 23): "YouTube'da X aç", "sesi %30 yap", "durdur", "10 dakika ileri sar"… modele sorulmadan araç
        // çalışır; sesli cevap tek kelimelik onay ("Açıyorum."), ayrıntı ekranda.
        if (Voice.VoiceCommands.Parse(userText) is { } quick)
        {
            yield return new IntentDetected(MessageIntent.Komut);
            var result = await tools.RunOneAsync(new ToolCall(quick.Tool, quick.Arguments), cancellationToken);
            yield return new ToolsUsed([new ToolUse(quick.Tool, result.Ok, quick.Arguments.GetRawText(), result.ForModel, result.Question)]);
            var spoken = result.Ok ? quick.Confirmation : result.Question ?? result.ForModel.Replace("HATA: ", "");
            yield return new AnswerChunk(spoken);
            var full = result.Ok ? $"{quick.Confirmation}\n\n{result.ForModel}" : spoken;
            var savedQuick = await conversations.AddMessageAsync(conversationId, Role.Assistant, full, cancellationToken);
            logger.LogInformation("Hızlı komut: {Tool} {Args} → {Ok}", quick.Tool, quick.Arguments.GetRawText(), result.Ok);
            yield return new AnswerCompleted(savedQuick.Id, savedQuick.Content);
            yield break;
        }

        // Araştırma modu ("X'i araştır"): çok adımlı arama → kaynaklı rapor → bilgi tabanına kayıt. Hatırlatma vb. araç isteği değilse.
        if (ResearchService.IsRequest(userText) && ToolRouting.Select(MessageIntent.Sohbet, userText) is null)
        {
            var report = new StringBuilder();
            await foreach (var e in research.RunAsync(userText, cancellationToken))
            {
                if (e is AnswerChunk chunk)
                    report.Append(chunk.Text);
                yield return e;
            }

            var savedReport = await conversations.AddMessageAsync(conversationId, Role.Assistant, report.ToString().Trim(), cancellationToken);
            memoryQueue.Enqueue(new MemoryJob(userText, savedReport.Id, LearnedAnswer: savedReport.Content)); // araştırmadan öğrenilenler hafızaya
            yield return new AnswerCompleted(savedReport.Id, savedReport.Content);
            yield break;
        }

        // 2) Sınıflandırma ve hatırlama birbirinden bağımsız: aynı anda.
        var intentTask = router.ClassifyAsync(userText, previousAssistant, cancellationToken);
        var recallTask = memory.RecallAsync(userText, cancellationToken);
        await Task.WhenAll(intentTask, recallTask);
        var (intent, recall) = (intentTask.Result, recallTask.Result);
        // Yazılım cevapları kod ve madde içerir: sesli okunmaz, ekrana yazılır (masaüstü "Ekrana yazıyorum" der).
        yield return new IntentDetected(intent, intent == MessageIntent.Yazilim || AnswerStyle.IsDetailRequested(userText) || AnswerStyle.RequestedCount(userText) is not null);
        yield return new MemoriesRecalled(recall);

        // 3) Güncel sorularda web (önbellek ya da arama).
        var notes = new List<string>();
        notes.AddRange(AnswerStyle.NotesFor(userText)); // "detaylı anlat", "tam 4 madde" ya da karşılaştırma/neden/kod sorusu
        if (intent == MessageIntent.Yazilim && notes.Count == 0)
            notes.Add(AnswerStyle.ReasoningNote); // yazılım cevabı ekrana yazılır (sesli okunmaz): yapı ve kod serbest
        WebContext? web = null;
        // Araç gereken mesajlar (komut, hatırlatma, hava): hava aracı varsa web araması yapılmaz.
        var toolSet = ToolRouting.Select(intent, userText, previousAssistant);

        // Belgeler: router "Belge" dediyse ya da mesaj belgeye işaret ediyorsa ("notlarımda", "PDF'te").
        // "Termo II ders notlarımda…" içindeki "ders" kelimesi ders programı aracını seçmesin.
        DocumentContext? docs = null;
        // Açık hatırlatma isteği ("notlarımı 9'da çalışmamı hatırlat") her zaman araca gider.
        var documentQuestion = intent == MessageIntent.Belge || DocumentQuery.Mentions(userText);
        if (documentQuestion && intent != MessageIntent.Komut && !ToolRouting.ExpectsToolCall(userText))
        {
            docs = await TryPrepareDocumentsAsync(userText, cancellationToken);
            // "Belgesel öner" gibi yanlış alarmda (Belge sınıfı değil, parça da yok) not eklenmez: normal akış (araçlar dahil).
            // Bilgi tabanında parça yoksa ve bilgisayar araçları seçildiyse ("x.txt dosyasında ne yazıyor?") iş araca kalır.
            var computerTools = toolSet is { Count: > 0 } && toolSet.Contains("dosya_oku");
            if (docs is not null && (docs.Sources.Count > 0 || docs.Whole || (intent == MessageIntent.Belge && !computerTools)))
            {
                toolSet = null;
                notes.Add(DocumentQuery.Note(docs));
                yield return new DocumentsSearched(docs);
            }
            else
            {
                docs = null;
            }
        }

        // Yazılım soruları: resmi dokümantasyondan ilgili bölümler (Faz 15 b). Güncel ("Next.js'in son sürümü ne?") web'e gider.
        IReadOnlyList<WebSource>? librarySources = null;
        var softwareQuestion = intent == MessageIntent.Yazilim
            || (intent is MessageIntent.GenelBilgi or MessageIntent.Sohbet && Library.LibraryAnswer.Mentions(userText));
        if (softwareQuestion && toolSet is null && docs is null)
        {
            var (hits, lastSync) = await TrySearchLibraryAsync(userText, previousAssistant, cancellationToken);
            if (hits.Count > 0)
            {
                notes.Add(Library.LibraryAnswer.Note(hits, lastSync));
                if (Library.LibraryAnswer.UnknownApis(userText, hits).Concat(Library.LibraryAnswer.UnknownNamedConcepts(userText, hits)).ToList() is { Count: > 0 } unknown)
                {
                    logger.LogInformation("Soruda kaynaklarda geçmeyen API adı: {Names}", string.Join(", ", unknown));
                    notes.Add(Library.LibraryAnswer.UnknownApiNote(unknown));
                }
                librarySources = Library.LibraryAnswer.Sources(hits);
                yield return new LibrarySearched(hits.Count);
            }

            // Gece denetiminden geçip kullanıcının onayladığı düzeltmeler (Faz 15 d): benzer soruda eski hata tekrarlanmasın.
            if (intent == MessageIntent.Yazilim && learning is not null && await learning.NoteForAsync(userText, cancellationToken) is { } corrections)
            {
                notes.Add(corrections);
                logger.LogInformation("Onaylı düzeltme bağlama girdi: {Question}", userText);
            }
        }

        // Takvim hesabı ("10 gün sonra hangi gün?") kodla: "bugün" kelimesi Güncel'e (web) götürüyor, model yanlış sayıyordu.
        var dateAnswer = toolSet is null && docs is null ? DateAnswer.TryAnswer(userText, time.GetLocalNow().DateTime) : null;

        // Cevap paketi (Faz 19). Bilgi soruları (genel bilgi, güncel, yazılım) pakete göre; kişisel sorular, araçlar ve
        // belgeler her pakette yerelde. Önce PRO'nun öğrendikleri (hangi paket seçili olursa olsun Claude'a gitmeden).
        var tier = settings is null ? AnswerTier.Medium : await AnswerTiers.GetAsync(settings, cancellationToken);
        var knowledge = toolSet is null && docs is null && dateAnswer is null && userText.Length > 0
            && intent is MessageIntent.GenelBilgi or MessageIntent.Guncel or MessageIntent.Yazilim
            && IntentNotes.DirectAnswer(intent, toolsAvailable: true) is null && !AnswerStyle.IsAboutUltron(userText);

        // Bildiğim siteler (Faz 20): takip edilen sitenin gece kopyasında güçlü bir eşleşme varsa internete çıkmadan oradan.
        // "Okulumun akademik takvimi" kişisel sanılabilir; soru siteyi işaret ediyorsa yine bakılır.
        IReadOnlyList<Sites.SiteHit> siteHits = [];
        if (sites is not null && toolSet is null && docs is null && dateAnswer is null && userText.Length > 0)
        {
            var mentioned = false;
            try
            {
                var known = await sites.ListAsync(cancellationToken);
                mentioned = known.Count > 0 && Sites.SiteService.Mentions(userText, known);
                if (known.Count > 0 && (knowledge || mentioned))
                    siteHits = await sites.SearchAsync(userText, 8, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Takip edilen sitelere bakılamadı");
            }
            // Soru siteyi açıkça işaret ediyorsa ("okulumun takvimi") daha düşük eşik yeter; işaret etmeyen bilgi sorusunda sıkı.
            var threshold = mentioned ? Sites.SiteService.MentionedScore : Sites.SiteService.StrongScore;
            if (siteHits.Count > 0)
                logger.LogInformation("Site eşleşmesi {Score:0.00} (eşik {Threshold:0.00}, {Page}): {Question}", siteHits[0].Score, threshold, siteHits[0].Page.Title, userText);
            if (siteHits.Count > 0 && siteHits[0].Score >= threshold)
            {
                logger.LogInformation("Site kopyasından ({Score:0.00}): {Question}", siteHits[0].Score, userText);
                notes.Add(Sites.SiteService.Note(siteHits));
                yield return new TierProgress("Bildiğim siteler", siteHits[0].Site.Name);
                knowledge = false; // PRO/Medium/web'e gidilmez
            }
            else
                siteHits = [];
        }

        if (knowledge && pro is not null)
        {
            Pro.ProHit? hit = null;
            try
            {
                hit = await pro.FindAsync(userText, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "PRO hafızasına bakılamadı");
            }
            string? proText = null;
            if (hit is not null)
            {
                yield return hit.Answer.Origin == "medium"
                    ? new TierProgress("Web hafızası", "Daha önce internetten öğrenilmiş cevap")
                    : new TierProgress("PRO hafızası", "Daha önce araştırılmış cevap");
                proText = Pro.ProService.Display(hit.Answer.Answer, hit.Answer.Sources);
            }
            else if (tier == AnswerTier.Pro)
            {
                var box = new Box<Pro.ProResult>();
                await foreach (var e in WithProgressAsync(AnswerTiers.Name(tier), step => pro.ResearchAsync(userText, step, cancellationToken), box, cancellationToken))
                    yield return e;
                if (box.Value is { Ok: true } result)
                    proText = Pro.ProService.Display(result.Answer, result.Sources);
                else
                {
                    yield return new TierProgress(AnswerTiers.Name(AnswerTier.Medium), (box.Value?.Error ?? "Pro cevap veremedi") + " Ultron ile cevaplıyorum.");
                    tier = AnswerTier.Medium;
                }
            }
            if (proText is not null)
            {
                yield return new AnswerChunk(proText);
                var savedPro = await conversations.AddMessageAsync(conversationId, Role.Assistant, proText, cancellationToken);
                yield return new AnswerCompleted(savedPro.Id, savedPro.Content);
                yield break;
            }
        }
        if (tier == AnswerTier.Pro)
            tier = AnswerTier.Medium; // PRO'da yerel kalan sorular (kişisel, araç, belge) kapsamlı üslupla

        // Medium: derin web (akıllı okuma + TR/EN + gerekirse 2. tur). Yazılımda kütüphane notunun yanına eklenir.
        var deepDone = false;
        if (tier == AnswerTier.Medium && knowledge && deepWeb is not null)
        {
            var box = new Box<WebContext>();
            await foreach (var e in WithProgressAsync(AnswerTiers.Name(tier), step => deepWeb.PrepareAsync(userText, previousAssistant, step, cancellationToken), box, cancellationToken))
                yield return e;
            if (box.Value is { } deep && (deep.FromCache || deep.Sources.Count > 0))
            {
                web = deep;
                deepDone = true;
                notes.Add(webAnswer.BuildNote(deep));
                yield return new WebPrepared(deep);
            }
        }

        if (intent == MessageIntent.Guncel && toolSet is null && docs is null && dateAnswer is null && !deepDone)
        {
            var (context, error) = await TryPrepareWebAsync(userText, previousAssistant, cancellationToken);
            if (context is not null)
            {
                web = context;
                notes.Add(webAnswer.BuildNote(context));
                yield return new WebPrepared(context);
            }
            else
            {
                notes.AddRange(IntentNotes.For(intent, webAvailable: false));
                yield return new WebUnavailable(error!);
            }
        }

        // 4) Cevap: araç döngüsü, kod seviyesi korumadan sabit cevap ya da modelden akış.
        var answer = new StringBuilder();
        var direct = IntentNotes.DirectAnswer(intent, toolsAvailable: true)
            ?? (toolSet is null && AnswerStyle.IsAboutUltron(userText) ? AnswerStyle.CapabilitiesAnswer : null)
            ?? dateAnswer;
        var usedTools = false;
        if (toolSet is not null)
        {
            var now = time.GetLocalNow().DateTime;
            // Araç turlarında kısa, işe odaklı sistem mesajı (bkz. ToolRouting.SystemPrompt); geçmiş yine verilir ("onu iptal et").
            var messages = ChatContext.Build(ToolRouting.SystemPrompt(now, tools: toolSet), history, options.Value.HistoryWindow);
            ToolRunContext.UserText = userText;
            // Kelimelerden seçilen araç grubu varsa model ilk turda aracı atlarsa bir kez uyarılır.
            var run = await tools.RunAsync(messages, toolSet.Count == 0 ? null : toolSet, cancellationToken, expectCall: toolSet.Count > 0);
            // Kelime yanılttıysa ("ders" geçen ama sadece bilgi veren mesaj): araç seçilmedi → normal sohbet cevabı.
            usedTools = !(run.Declined && intent != MessageIntent.Komut && !ToolRouting.ExpectsToolCall(userText));
            if (usedTools)
            {
                if (run.Uses.Count > 0)
                    yield return new ToolsUsed(run.Uses);
                answer.Append(run.Answer);
                yield return new AnswerChunk(run.Answer);
            }
        }

        if (usedTools)
        {
        }
        else if (direct is not null)
        {
            answer.Append(direct);
            yield return new AnswerChunk(direct);
        }
        else
        {
            var now = time.GetLocalNow().DateTime;
            var address = settings is null ? null : await settings.GetAsync(Settings.UltronSettings.UserAddress, cancellationToken);
            var messages = ChatContext.Build(SystemPrompt.Build(now, recall, notes, tier, address), history, options.Value.HistoryWindow);
            var guard = new RepetitionGuard();
            var codeAnswer = intent == MessageIntent.Yazilim && codeChat is not null && toolSet is null && docs is null;
            if (codeAnswer)
                logger.LogInformation("Yazılım cevabını {Model} yazıyor", codeChat!.Name);
            await foreach (var part in codeAnswer ? codeChat!.StreamAsync(messages, cancellationToken) : chat.StreamAsync(messages, cancellationToken))
            {
                answer.Append(part);
                yield return new AnswerChunk(part);
                if (guard.Append(part))
                {
                    logger.LogWarning("Model tekrar döngüsüne girdi, cevap kesildi: {Question}", userText);
                    var trimmed = RepetitionGuard.Trim(answer.ToString());
                    answer.Clear().Append(trimmed);
                    break; // arayüz son metni AnswerCompleted'tan alır
                }
            }
        }

        // 5) Kaynaklar, önbellek ve kayıt.
        if (web is { Sources.Count: > 0 })
            yield return new SourcesListed(web.Sources);
        if (siteHits.Count > 0)
            yield return new SourcesListed(Sites.SiteService.Sources(siteHits));
        if (docs is { Sources.Count: > 0 })
        {
            // Numara yanlış parçayı gösteriyorsa düzelt (kart doğru sayfayı açsın); arayüz son metni Completed'tan alır.
            var repaired = DocumentCitations.Repair(answer.ToString(), docs.Sources);
            answer.Clear().Append(repaired);
            if (DocumentCitations.Cited(docs.Sources, repaired) is { Count: > 0 } cited)
                yield return new DocumentSourcesListed(cited);
        }
        var citedLibrary = "";
        if (librarySources is not null && Library.LibraryAnswer.Cited(librarySources, answer.ToString()) is { Count: > 0 } libraryCited)
        {
            citedLibrary = string.Join("\n", libraryCited.Select(s => $"[{s.Number}] {s.Title} — {s.Url}"));
            yield return new SourcesListed(libraryCited);
        }
        if (web is not null && !Pro.ProService.LooksUnsure(answer.ToString())) // "bulamadım" cevabı önbelleğe girmez: soru tekrar gelince yeniden aranır
            await webAnswer.SaveAsync(web, answer.ToString(), cancellationToken);


        // Takip soruları kartta sorulacaksa modelin cevap sonuna eklediği soru cümlesi atılır (arayüz son metni Completed'tan alır).
        var followUpsApply = options.Value.FollowUps && direct is null && !usedTools && docs is null && intent is MessageIntent.Kisisel or MessageIntent.Sohbet or MessageIntent.GenelBilgi;
        // Modelden gelen cevaplarda sondaki teklif/soru atılır ("…ister misin?"); araç ve sabit cevaplara dokunulmaz.
        var finalText = !usedTools && direct is null ? AnswerCleanup.Tidy(answer.ToString()) : answer.ToString();

        var saved = await conversations.AddMessageAsync(conversationId, Role.Assistant, finalText, cancellationToken);
        if (web is { Sources.Count: > 0 } && finalText.Length > 0 && !Pro.ProService.LooksUnsure(finalText)) // "veremiyorum" cevabı bilgi değil (2026-10-30, #144)
            memoryQueue.Enqueue(new MemoryJob(userText, saved.Id, LearnedAnswer: finalText)); // web'den öğrenilenler hafızaya (kur, hava gibi anlık bilgiler elenir)
        yield return new AnswerCompleted(saved.Id, saved.Content);

        // Medium kendi bulduğu iyi cevabı hafızaya alır (Faz 20): benzer soru bir dahakine internete çıkmadan cevaplanır.
        // Cevap gösterildikten sonra (kullanıcı beklemez); önbellekten gelenler ve kaynağı az olanlar alınmaz.
        if (deepDone && web is { FromCache: false, Sources.Count: >= 2 } && pro is not null && !usedTools && direct is null)
        {
            try
            {
                await pro.LearnFromWebAsync(userText, finalText, web.Sources, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Medium cevabı hafızaya alınamadı");
            }
        }

        // Yazılım cevabı gece denetimine (Faz 15 d; tasarım kararı: sadece yazılım soruları). Araç ve sabit cevaplar hariç.
        if (learning is not null && intent == MessageIntent.Yazilim && direct is null && !usedTools && docs is null && finalText.Length > 0)
            await learning.RecordAsync(userText, finalText, citedLibrary, cancellationToken);

        // 6) Zahmetsiz takip soruları: cevabı kişiselleştirecek eksik bilgiler için (komut ve güncel sorularda değil).
        if (followUpsApply)
        {
            var questions = await followUps.SuggestAsync(userText, saved.Content, recall, cancellationToken);
            if (questions.Count > 0)
                yield return new FollowUpsSuggested(questions);
        }
    }

    private sealed class Box<T>
    {
        public T? Value { get; set; }
    }

    /// <summary>
    /// Uzun süren işi (Medium derin web, PRO Claude araştırması) çalıştırırken adımlarını akışa <see cref="TierProgress"/> olarak
    /// verir; sonuç <paramref name="box"/>'a. İş hata verirse sonuç boş kalır (çağıran bir alt pakete düşer).
    /// </summary>
    private async IAsyncEnumerable<ChatEvent> WithProgressAsync<T>(string tier, Func<Action<string>, Task<T>> work, Box<T> box,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var task = Task.Run(async () =>
        {
            try
            {
                return await work(step => channel.Writer.TryWrite(step));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Tier} işi yarıda kaldı", tier);
                return default;
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);
        await foreach (var step in channel.Reader.ReadAllAsync(cancellationToken))
            yield return new TierProgress(tier, step.Length > 120 ? step[..120] + "…" : step);
        box.Value = await task;
    }

    private async Task<DocumentContext?> TryPrepareDocumentsAsync(string userText, CancellationToken cancellationToken)
    {
        try
        {
            // "Belgeyi özetle / incele": arama değil, belgenin tamamı.
            return DocumentReader.IsWholeRequest(userText)
                ? await reader.PrepareAsync(userText, cancellationToken)
                : await documents.PrepareAsync(userText, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Belge araması yapılamadı");
            return null;
        }
    }

    private async Task<(IReadOnlyList<Library.LibraryHit> Hits, DateTime? LastSyncLocal)> TrySearchLibraryAsync(string userText, string? previousAssistant, CancellationToken cancellationToken)
    {
        try
        {
            // Türkçe soru + İngilizce arama ifadesi (çevrilemezse sadece soru).
            var context = previousAssistant is null ? userText : $"Önceki asistan mesajı: {(previousAssistant.Length > 400 ? previousAssistant[..400] : previousAssistant)}\nSoru: {userText}";
            var english = Library.LibraryAnswer.ParseQuery(await chat.CompleteJsonAsync(
                [new(Role.System, Library.LibraryAnswer.QueryPrompt), new(Role.User, context)], Library.LibraryAnswer.QuerySchema, cancellationToken));
            logger.LogInformation("Kütüphane araması: '{Query}' + '{English}'", userText, english);
            var hits = await library.SearchAsync(english is null ? [userText] : [userText, english], 5, cancellationToken);
            if (hits.Count == 0)
                return ([], null);
            var last = (await library.StatsAsync(cancellationToken)).LastSyncUtc;
            return (hits, last is { } t ? TimeZoneInfo.ConvertTimeFromUtc(t, time.LocalTimeZone) : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Kütüphane araması yapılamadı");
            return ([], null);
        }
    }

    // yield, catch bloğu olan try içinde kullanılamaz; hata yakalama ayrı metotta.
    private async Task<(WebContext? Context, string? Error)> TryPrepareWebAsync(string userText, string? previousAssistant, CancellationToken cancellationToken)
    {
        try
        {
            return (await webAnswer.PrepareAsync(userText, previousAssistant, cancellationToken), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Web kullanılamadı");
            return (null, ex.Message);
        }
    }
}
