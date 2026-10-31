# Ultron Chrome eklentisi (Faz 22)

Ultron'un kendi Chrome'unda iş yapabilmesi için: YouTube'u kontrol etme (aç, sar, durdur, sonraki, ses, ne çalıyor),
açık sekmeyi okuma, yeni sekme açma. Eklenti **sadece bu bilgisayardaki Ultron'a** (`ws://127.0.0.1:5199/eklenti`) bağlanır
ve eşleştirme kodu olmadan komut kabul etmez. Yorum/mesaj gönderme, form doldurma, satın alma bu eklentide yoktur.

## Kurulum (bir kez)
1. Chrome'da adres çubuğuna `chrome://extensions` yaz.
2. Sağ üstte **Geliştirici modu**nu aç.
3. **Paketlenmemiş öğe yükle** → Ultron'un kurulu olduğu klasördeki `eklenti\chrome` klasörünü seç
   (genelde `C:\Users\<sen>\AppData\Local\Programs\Ultron\eklenti\chrome`).
4. Ultron'a "Chrome eklentisi kodu ne?" diye sor; gelen 8 karakterlik kodu eklentinin simgesine tıklayıp yapıştır → **Kaydet ve bağlan**.
5. Simgede ✕ ya da KOD yazısı kalmadıysa bağlıdır.

Ultron güncellenince eklenti dosyaları da güncellenir; `chrome://extensions` sayfasında eklentinin **yenile** (⟳) düğmesine basmak yeterli.
