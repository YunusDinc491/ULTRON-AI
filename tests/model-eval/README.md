# Faz 1 · Model değerlendirme

Aday modelleri aynı "sınava" sokup karşılaştırmak için kullanılan araçlar.

## Dosyalar
| Dosya | Ne işe yarar |
|---|---|
| `sorular.json` | Test seti, sistem prompt'ları, araç tanımları, embedding testi. **Kod değiştirmeden** buradan düzenlenir. |
| `eval.cs` | Değerlendirme betiği (.NET 10 file-based app, proje dosyası gerekmez) |
| `gorseller/` | Görsel testleri için `g1.jpg`, `g2.jpg`, `g3.jpg` (kullanıcı koyar) |
| `sonuclar/<tarih-saat>/` | Her çalıştırmanın `rapor.md` ve `ham.json` çıktısı |

## Çalıştırma
Bu klasörde, Ollama açıkken ve **laptop fişteyken**:
```
dotnet run eval.cs
```
Sadece bazı modeller: `dotnet run eval.cs -- qwen3-vl:8b`
Sadece embedding testi: `dotnet run eval.cs -- --sadece-embedding`
Sadece bir kategori (ör. görseller): `dotnet run eval.cs -- --kategori gorsel`

## Ne ölçülüyor?
| Bölüm | Nasıl puanlanır |
|---|---|
| `sohbet` (5) | 📝 Elle: Türkçe doğallığı, doğruluk, kişiliğe uyum (ciddi, kısa, isimle hitap yok) |
| `cikarma` (6) | ✅/❌ Otomatik: JSON şemaya uygun mu, kategoriler doğru mu, gereksiz bilgi çıkarmıyor mu |
| `router` (7) | ✅/❌ Otomatik: doğru sınıf mı |
| `arac` (5) | ✅/❌ Otomatik: doğru araç ve gerekli argümanlar var mı; gerekmediğinde araç çağırmıyor mu |
| `gorsel` (3) | `anahtar_kelimeler` doluysa otomatik, boşsa 📝 elle |
| VRAM | `num_ctx` 4096/8192/16384 için modelin ne kadarı GPU'da (`/api/ps`) |
| Hız | tok/s (`eval_count / eval_duration`), yükleme süresi |
| Embedding | 10 hafıza kaydı + 10 soru: doğru kayıt 1. sırada mı (kosinüs benzerliği); GPU ve CPU süreleri |

## Görsel testi nasıl eklenir?
1. İçinde Türkçe yazı olan görseli `gorseller/g1.jpg` olarak kaydet (ders notu fotoğrafı, ekran görüntüsü, tabela…).
2. İstersen `sorular.json` içinde `g1` maddesinin `anahtar_kelimeler` listesine görselde kesin geçen 2-3 kelime yaz. Böylece otomatik puanlanır.
