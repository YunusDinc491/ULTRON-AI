#:property PublishAot=false
#:package Microsoft.AspNetCore.SignalR.Client@10.0.14
#:project ../../src/Ultron.Contracts/Ultron.Contracts.csproj
// Faz 17 ölçümü: çalışan Ultron'a (hub) bir deneme dersi açar, mühendislik notlarından 6 dosyayı kaynak ekler, kavram ve
// sayısal sorular sorar (sayısalların doğru cevabı burada hesaplı), özet + formül kağıdı + quiz + plan üretir, raporu
// tests/ultron-ders/rapor-<tarih>.md dosyasına yazar ve deneme dersini siler (kullanıcının verisi kirlenmesin).
// Çalıştırma: dotnet run tools/ultron-ders/olcum.cs   (Ultron açıkken)
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

Console.OutputEncoding = Encoding.UTF8;
var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", ".."));
if (!Directory.Exists(Path.Combine(root, "src")))
    root = Directory.GetCurrentDirectory();
var notes = Path.Combine(root, "src", "Ultron.Host", "notlar", "muhendislik");

await using var hub = new HubConnectionBuilder().WithUrl("http://127.0.0.1:5199/hub/chat")
    .WithServerTimeout(TimeSpan.FromMinutes(10)).Build();
await hub.StartAsync();

var session = await hub.InvokeAsync<LessonSessionDto>("CreateLesson", "Termodinamik (ölçüm)", (string?)null);
var report = new StringBuilder($"# Faz 17 ölçümü — {DateTime.Now:yyyy-MM-dd HH:mm}\n\nDeneme dersi #{session.Id}; kaynaklar: mühendislik notları 01, 02, 03, 05, 06, 08, 09.\n\n");
try
{
    foreach (var file in new[] { "01-temel-kavramlar-ve-birimler.md", "02-birinci-yasa-kapali-sistem.md", "03-acik-sistemler-ve-cihazlar.md",
                 "05-ikinci-yasa-carnot-cop.md", "06-entropi-ve-izantropik-verim.md", "08-gaz-guc-cevrimleri.md", "09-buhar-ve-sogutma-cevrimleri.md" })
    {
        var r = await hub.InvokeAsync<DocumentAddResultDto>("AddLessonText", session.Id, Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(Path.Combine(notes, file)), false);
        Console.WriteLine($"kaynak {file}: {r.Status}");
    }

    // ---- Sayısal problemler: doğru cevap burada hesaplı ----
    var problems = new (string Id, string Question, double Expected)[]
    {
        ("p1", "300 K ile 900 K arasında çalışan Carnot ısı makinesinin verimini hesapla.", 1 - 300.0 / 900),
        ("p2", "Hava 300 K'den sıkıştırma oranı 8 ile izantropik sıkıştırılıyor (k=1,4). Son sıcaklığı hesapla.", 300 * Math.Pow(8, 0.4)), // sıkıştırma oranı = hacim oranı → T2 = T1·r^(k-1)
        ("p3", "Sıkıştırma oranı 10 olan ideal Otto çevriminin ısıl verimini hesapla (k=1,4).", 1 - Math.Pow(10, -0.4)),
        ("p4", "Bir buzdolabı 4 °C iç ortam ile 25 °C dış ortam arasında çalışıyor. Carnot soğutma COP'unu hesapla.", 277.15 / 21),
        ("p5", "2 kg hava sabit basınçta 300 K'den 400 K'ye ısıtılıyor, cp=1,005 kJ/kgK. Verilen ısıyı kJ olarak hesapla.", 2 * 1.005 * 100),
        ("p6", "Bir ısı pompası eve 5 kW ısı veriyor ve 1,25 kW elektrik harcıyor. COP değeri kaçtır?", 4),
        ("p7", "Hava 100 kPa ve 300 K'den 800 kPa'ya izantropik sıkıştırılıyor (k=1,4). Çıkış sıcaklığını bul.", 300 * Math.Pow(8, 0.4 / 1.4)),
    };
    report.AppendLine("## Sayısal problemler (hesaplayıcı)\n\n| # | Beklenen | Ultron | Doğru | Süre | Yazan |\n|---|---|---|---|---|---|");
    int right = 0;
    var solutions = new StringBuilder();
    foreach (var (id, question, expected) in problems)
    {
        var (message, seconds) = await AskAsync(session.Id, question);
        var value = ParseResult(message?.Content ?? "");
        var ok = value is { } v && Math.Abs(v - expected) <= Math.Abs(expected) * 0.02;
        if (ok)
            right++;
        report.AppendLine($"| {id} | {expected:0.####} | {(value is { } x ? x.ToString("0.####") : "—")} | {(ok ? "✓" : "✗")} | {seconds:0} sn | {message?.Author}{(message?.Unsure == true ? " · Claude önerildi" : "")} |");
        solutions.AppendLine($"### {id}: {question}\n\n{message?.Content}\n");
        Console.WriteLine($"{id}: {(ok ? "doğru" : "YANLIŞ")} ({seconds:0} sn)");
    }
    report.AppendLine($"\n**Sayısal: {right}/{problems.Length} doğru.**\n");

    // ---- Kavram soruları ----
    var concepts = new[]
    {
        "Entropi nedir, kısaca anlatır mısın?",
        "İzantropik verim nedir, türbin ve kompresör için nasıl tanımlanır?",
        "Kapalı sistem için birinci yasayı anlat.",
        "Rankine çevrimi nedir, dört süreci nelerdir?",
        "Carnot ilkeleri nelerdir?",
        "Kısılma vanasında entalpi neden sabit kalır?",
        "Biot sayısı neyi ifade eder?",
    };
    report.AppendLine("## Kavram soruları\n\n| Soru | Kaynak | Uzunluk | Süre | Claude kartı |\n|---|---|---|---|---|");
    var answers = new StringBuilder();
    foreach (var question in concepts)
    {
        var (message, seconds) = await AskAsync(session.Id, question);
        report.AppendLine($"| {question} | {message?.Sources.Count ?? 0} | {message?.Content.Length ?? 0} | {seconds:0} sn | {(message?.Unsure == true ? "evet" : "hayır")} |");
        answers.AppendLine($"### {question}\n\nKaynaklar: {string.Join("; ", message?.Sources ?? [])}\n\n{message?.Content}\n");
        Console.WriteLine($"kavram: {seconds:0} sn, {message?.Sources.Count} kaynak");
    }

    // ---- Malzemeler ----
    var sw = Stopwatch.StartNew();
    var summary = await Try(() => hub.InvokeAsync<LessonMaterialDto?>("MakeLessonMaterial", session.Id, "ozet", "Entropi ve izantropik verim"));
    var summaryTime = sw.Elapsed.TotalSeconds;
    sw.Restart();
    var formulas = await Try(() => hub.InvokeAsync<LessonMaterialDto?>("MakeLessonMaterial", session.Id, "formul", "Carnot, Otto ve Rankine çevrimleri"));
    var formulaTime = sw.Elapsed.TotalSeconds;
    sw.Restart();
    var quiz = await Try(() => hub.InvokeAsync<LessonMaterialDto?>("MakeLessonQuiz", session.Id, "Termodinamik", 6));
    var quizTime = sw.Elapsed.TotalSeconds;
    QuizGradeDto? grade = null;
    if (quiz?.Quiz is { } qs)
        grade = await Try(() => hub.InvokeAsync<QuizGradeDto?>("GradeLessonQuiz", quiz.Id, qs.Select(q => q.Type == "coktan" ? "A" : q.Type == "sayisal" ? "1" : "Bilmiyorum").ToList()));
    sw.Restart();
    var plan = await Try(() => hub.InvokeAsync<LessonPlanDto>("MakeLessonPlan", session.Id, "Vize", DateTime.Today.AddDays(10), false)) ?? new LessonPlanDto("hata", null);
    var planTime = sw.Elapsed.TotalSeconds;
    Console.WriteLine($"özet {summaryTime:0} sn, formül {formulaTime:0} sn, quiz {quizTime:0} sn ({quiz?.Quiz?.Count ?? 0} soru), plan {planTime:0} sn");

    report.AppendLine($"\n## Malzemeler\n\n- Özet: {summaryTime:0} sn, {summary?.Content.Length ?? 0} karakter\n- Formül kağıdı: {formulaTime:0} sn, {formulas?.Content.Length ?? 0} karakter, {Regex.Matches(formulas?.Content ?? "", @"\$[^$]+\$").Count} formül\n- Quiz: {quizTime:0} sn, {quiz?.Quiz?.Count ?? 0} soru ({string.Join(", ", quiz?.Quiz?.GroupBy(q => q.Type).Select(g => $"{g.Key} {g.Count()}") ?? [])}); hep \"A\"/\"1\" cevabıyla puan {grade?.Correct}/{grade?.Total}, zayıf konular: {string.Join(", ", grade?.WeakTopics ?? [])}\n- Plan: {planTime:0} sn — {plan.Message}\n");
    report.AppendLine("## Çözümler\n").Append(solutions).AppendLine("## Kavram cevapları\n").Append(answers);
    report.AppendLine($"## Özet\n\n{summary?.Content}\n\n## Formül kağıdı\n\n{formulas?.Content}\n\n## Quiz\n");
    if (quiz?.Quiz is { } questions)
        for (var i = 0; i < questions.Count; i++)
            report.AppendLine($"{i + 1}. [{questions[i].Type} · {questions[i].Topic}] {questions[i].Question}{(questions[i].Choices.Count > 0 ? "\n   " + string.Join(" | ", questions[i].Choices.Select((c, k) => $"{(char)('A' + k)}) {c}")) : "")}\n   → {grade?.Results.ElementAtOrDefault(i)?.Feedback}");
    report.AppendLine($"\n## Plan\n\n{plan.Material?.Content}");
}
finally
{
    await hub.InvokeAsync<bool>("DeleteLesson", session.Id);
}

var folder = Path.Combine(root, "tests", "ultron-ders");
Directory.CreateDirectory(folder);
var path = Path.Combine(folder, $"rapor-{DateTime.Now:yyyyMMdd-HHmm}.md");
await File.WriteAllTextAsync(path, report.ToString());
Console.WriteLine("Rapor: " + path);

async Task<(LessonMessageDto? Message, double Seconds)> AskAsync(long sessionId, string question)
{
    var sw = Stopwatch.StartNew();
    LessonMessageDto? message = null;
    await foreach (var e in hub.StreamAsync<LessonEventDto>("AskLesson", sessionId, question, (byte[]?)null))
        if (e.Type == "completed")
            message = e.Message;
    return (message, sw.Elapsed.TotalSeconds);
}

static double? ParseResult(string content)
{
    var m = Regex.Match(content, @"\*\*Sonuç:\*\*[^=]*=\s*\*\*(-?[\d.,]+(?:E[+-]?\d+)?)");
    if (!m.Success)
        return null;
    var text = m.Groups[1].Value.Replace(".", "").Replace(',', '.');
    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

async Task<T?> Try<T>(Func<Task<T>> call)
{
    try
    {
        return await call();
    }
    catch (Exception ex)
    {
        Console.WriteLine("HATA: " + ex.Message);
        report.AppendLine($"\n> HATA: {ex.Message}\n");
        return default;
    }
}
