// Ultron · Faz 6 · Referans ses kaydı (Chatterbox'ın sesi taklit etmesi için)
// Ekrandaki metni okuyun; Enter'a basınca kayıt biter. Çıktı: %USERPROFILE%\Ultron\voices\<ad>.wav (24 kHz mono).
// Çalıştırma (proje kökünden): dotnet run tools/voice-samples/record.cs -- ses
// Sadece kendi sesinizi ya da kaydına izin veren birinin sesini kullanın.
#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:package NAudio@2.2.1

using System.Text;
using NAudio.Wave;

Console.OutputEncoding = Encoding.UTF8;

var name = args.Length > 0 ? args[0] : "ses";
var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "voices");
Directory.CreateDirectory(folder);
var path = Path.Combine(folder, name + ".wav");

Console.WriteLine("""
    Sakin bir odada, laptopa 20-30 cm yakın, normalden biraz YÜKSEK sesle, DOĞAL ve TONLAMALI okuyun.
    Cümleler arasında uzun duraklamayın; aşağıdaki çubuk konuşurken en az yarıya ulaşmalı.
    10-20 saniye yeterli. Hazır olunca Enter'a basın, okuyun, bitince tekrar Enter'a basın.

      "Sistemler çevrimiçi. Bugün hava biraz serin, yanına bir ceket almanı öneririm.
       Akşamki toplantıyı unutma; saat yedide başlıyor. Bu arada, dün sorduğun konuyu araştırdım,
       sonuç sandığından çok daha ilginç çıktı. Hazır olduğunda anlatırım."
    """);
Console.ReadLine();

using (var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(24000, 16, 1), BufferMilliseconds = 50 })
using (var writer = new WaveFileWriter(path, waveIn.WaveFormat))
{
    var peak = 0f;
    waveIn.DataAvailable += (_, e) =>
    {
        writer.Write(e.Buffer, 0, e.BytesRecorded);
        var now = 0f;
        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            now = Math.Max(now, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
        peak = Math.Max(peak, now);
        // Canlı seviye göstergesi: konuşurken çubuk yeşil bölgeye (en az yarısına) ulaşmalı.
        var bar = (int)Math.Min(40, now * 40);
        Console.Write($"\r  [{new string('█', bar)}{new string('·', 40 - bar)}] {(now < 0.25f ? "kısık, yaklaş/yüksek sesle" : now > 0.95f ? "patlıyor, uzaklaş" : "iyi              ")}");
    };
    waveIn.StartRecording();
    Console.WriteLine("● Kaydediliyor... (bitince Enter)");
    Console.ReadLine();
    waveIn.StopRecording();
    Thread.Sleep(200);
    Console.WriteLine();
    Console.WriteLine($"Süre: {writer.TotalTime.TotalSeconds:F1} sn · en yüksek seviye: {peak:P0}" +
                      (peak < 0.3f ? "  (kısık: yaklaşıp tekrar kaydetmeniz önerilir)" : peak > 0.98f ? "  (patlıyor, biraz uzaklaşın)" : "  (iyi)"));
}

Console.WriteLine($"Kaydedildi: {path}");
