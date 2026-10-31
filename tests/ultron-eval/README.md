# Ultron değerlendirme seti

Ultron'un **gerçek bileşenleriyle** (sistem mesajı, araç planlayıcı, yönlendirici, hafıza çıkarıcı) tekrarlanabilir ölçüm.
Puanlama tamamen otomatik kontrollerle (sayı, ifade, cümle/kelime/madde sayısı, doğru araç ve parametre, dil); model kendini notlamaz.

| Dosya | Ne |
|---|---|
| `dev.json` | Geliştirme seti: prompt/ayar denemelerinde bakılır |
| `holdout.json` | Kapalı set: denemeler sırasında **bakılmaz, ayar yapılmaz**; sadece önce/sonra karşılaştırması |
| `baseline-dev.json`, `baseline-holdout.json` | Başlangıç ölçümü (2026-10-27, git 6de6761) |
| `sonuclar/` | Her koşu: `<tarih>-<set>-<etiket>.json` (ayrıntı) + `.md` (özet, baseline farkı) |
| `deneyler.md` | Deney günlüğü: her deneyde tek değişken, sonuç, karar |

Kategoriler: türkçe anlama, talimat takibi, mantık, özetleme, soru-cevap, tutarlılık (aynı soru 3 kez), araç kararı, yönlendirme, hafıza çıkarma.
Kişisel veri yok; isimler kurgu. "Şimdi" sabit (27 Ekim 2026 10:00) → tarihe bağlı vakalar tekrarlanabilir.

```
dotnet run tools/ultron-eval/eval.cs -- --set dev --etiket <ad> [--ayar Ollama:Temperature=0.3] [--baseline-yaz]
```
Not: Eğitim (fine-tuning) yapılmadığı için "loss" yok; kaydedilenler puan, süre, ortalama vaka süresi ve GPU belleği.

## Belge sorgulama değerlendirmesi (Faz 10)
Orkestratör düzeyinde (yönlendirici + belge araması + model + kaynak düzeltici): örnek ders notu (`belge/ornek-notlar.json`, kurgu değil genel termodinamik; kullanıcının gerçek notu değil) geçici veritabanına PDF olarak eklenir, `belge/vakalar.json` soruları sorulur.
Kontroller: kaynak kartında doğru sayfa (ve sadece o), cevapta beklenen ifade, "notlarda yok" dürüstlüğü, sayfadaki talimat enjeksiyonuna uymama, yanlış yönlendirme (belgesel, hatırlatma).
```
dotnet run tools/ultron-eval/belge-eval.cs -- --etiket <ad> [--tekrar 2]
```
