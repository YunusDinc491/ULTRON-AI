# ULTRON-AI

> Windows için **yerel çalışan** kişisel yapay zekâ asistanı ve ona bağlanan Android uygulaması.
> *A **local-first** personal AI assistant for Windows, with an Android companion app.* — [English below ↓](#english)

---

## 🇹🇷 Türkçe

### İçindekiler
1. [Proje nedir?](#proje-nedir)
2. [Neden yapıldı?](#neden-yapıldı)
3. [Ultron ile neler yapılabilir?](#ultron-ile-neler-yapılabilir)
4. [Kurulum](#kurulum)
5. [Mimari ve teknolojiler](#mimari-ve-teknolojiler)
6. [Gizlilik ve güvenlik](#gizlilik-ve-güvenlik)
7. [Kaynaktan derleme](#kaynaktan-derleme)
8. [Lisans ve not](#lisans-ve-not)

---

### Proje nedir?

**Ultron**, Windows bilgisayarında çalışan, Türkçe konuşan kişisel bir yapay zekâ asistanıdır. Dil modelini buluttaki bir sunucuda değil, **kendi bilgisayarında** ([Ollama](https://ollama.com) ile) çalıştırır. Bu yüzden sohbetlerin, hafızan ve belgelerin bilgisayarından çıkmaz.

Ultron üç parçadan oluşur:

| Parça | Ne yapar |
|---|---|
| **Masaüstü uygulaması** | Bilim-kurgu "HUD" görünümlü ana ekran; sohbet, ses, çalışma alanları ve paneller |
| **Arka plan servisi (Host)** | Yapay zekâ motoru, hafıza, araçlar, hatırlatıcılar ve telefon bağlantısının çalıştığı yer |
| **Android uygulaması** | Telefondan yazarak ya da konuşarak Ultron'a ulaşmak (Tailscale ile, özel ağ üzerinden) |

Varsayılan modeller: sohbet için **`gemma4:e4b-it-qat`**, hafıza araması için **`qwen3-embedding:0.6b`**.

---

### Neden yapıldı?

- **Yerel LLM'leri gerçekten anlamak için.** Büyük dil modellerinin kendi bilgisayarında nasıl çalıştığını, hangi sınırlara takıldığını ve bu sınırların nasıl aşılabileceğini (hafıza, kaynak bulma, araç kullanma, ses) uçtan uca deneyimlemek.
- **Verinin bende kalması için.** Kişisel bilgiler, ders notları ve sohbetler bir şirketin sunucusuna gitmeden işlenebilsin.
- **Günlük hayatta işe yaraması için.** Okulda ders çalışmak, kod ve arayüz yazmak, hatırlatma kurmak, bilgisayarı sesle yönetmek gibi gerçek ihtiyaçlar.
- **Küçük bir modelden en iyi sonucu almak için.** Yaklaşık 4 milyar parametrelik bir yerel modelin; doğru bağlam, kaynak gösterme, hesap makinesi, web okuma ve kendi kendini denetleme ile ne kadar ileri gidebileceğini görmek.

---

### Ultron ile neler yapılabilir?

#### 1. Sohbet ve kalıcı hafıza
- Anlattığın kalıcı bilgileri (alerjin, okulun, tercihlerin…) **kendiliğinden hatırlar**, konu başlıklarına ayırır.
- Çelişen bilgiyi fark eder: "Artık İzmir'de yaşıyorum" dersen eskisini günceller.
- Hatırlarken **anlam araması + anahtar kelime araması** birlikte kullanılır (embedding + SQLite FTS5).
- Hafıza panelinden her bilgiyi görebilir, düzeltebilir ya da silebilirsin.
- Nasıl hitap edileceğini sen seçersin (adın, "patron" ya da hiçbiri).

#### 2. Ses
| Özellik | Açıklama |
|---|---|
| Çağrı kelimesi | **"Ultron"** diye seslenince dinlemeye başlar (15 ses örneğinle senin sesini tanır) |
| Bas-konuş | Düğmeye basıp konuşursun, susunca biter |
| Konuşma tanıma | Whisper, ekran kartında (Vulkan) yerel olarak; istersen Groq ile bulutta |
| Seslendirme | Piper ile ücretsiz yerel ses ya da ElevenLabs ile doğal ses |
| Söz kesme | Ultron konuşurken araya girebilirsin |

#### 3. Üç çalışma alanı
Üstteki sabit çubuktan **[İŞ] · [ULTRON] · [OKUL]** arasında geçilir; ekran piksel piksel değişir.

| Alan | Ne işe yarar |
|---|---|
| **ULTRON** (ana ekran) | Sohbet, canlı sistem telemetrisi (CPU, RAM, ekran kartı…) ve günün özeti: takvim, hava durumu, döviz ve altın, Ultron'un seni ne kadar tanıdığı |
| **İŞ** | İstediğin arayüz bileşenini tarif edersin; Ultron React + Tailwind kodunu yazar, **canlı önizler**. Masaüstü, tablet ve telefon çerçeveleri (iPhone 17 Pro, Galaxy S25 Ultra, Pixel 10 Pro…). Oturumlar konu başlığıyla kaydedilir, sürümler saklanır |
| **OKUL** | Kendi ders dosyalarına (PDF, Word, PowerPoint, defter fotoğrafı) dayanan çalışma: konu anlatımı, **ortalı ve büyük formüller**, adım adım problem çözümü (sayıları hesap makinesi bulur), quiz, formül kağıdı, sınav planı, ders notları |

#### 4. Web ve araştırma
- Güncel sorulara **kaynaklı** cevap ([1], [2] gibi atıflarla).
- Sayfaları ve PDF'leri derinlemesine okur, gerekirse bağlantıları takip eder.
- Arama motoru olarak kendi bilgisayarında **SearXNG** (sınırsız) ya da **Tavily** (ayda 1000 ücretsiz arama).
- "Bildiğim siteler": okulun sitesi gibi takip ettiğin siteleri her gece tarar; yeni duyuru ve sınav tarihlerini bildirir.

#### 5. Belgeler
- PDF, Word, metin ve taranmış (resimli) sayfalarına soru sorabilirsin.
- Cevap hangi belgenin hangi sayfasından geldiğini gösterir.

#### 6. Araçlar ve otomasyon
| Araç | Örnek |
|---|---|
| Hatırlatıcılar | "Yarın 9'da ilacımı hatırlat" (önem derecesi, tekrar, kaçırılanlar) |
| Sabah brifingi | O günün dersleri, hava, hatırlatmalar |
| Öneriler | Yarın yağmur ve sabah dersin varsa akşamdan şemsiye hatırlatması |
| Bilgisayar kontrolü | Pencereler, ses ve medya, dosya düzenleme; riskli işler **onay kartıyla** |
| Chrome eklentisi | YouTube'da şarkı açma, sarma; açık sekmeyi okuma |
| Hesap makinesi, hava durumu, ders programı | Konuşarak ayarlanır |

#### 7. Cevap paketleri
| Paket | Nasıl çalışır |
|---|---|
| **Ultron** | Tamamen yerel model; akıllı okuma, Türkçe ve İngilizce arama, kapsamlı cevap |
| **Ultron Pro** | Soruyu **kendi** Claude Code, OpenAI Codex CLI ya da Google Gemini CLI hesabın araştırır; iyi cevap Ultron'un hafızasına girer, bir dahaki sefere internete gitmeden kullanılır |

#### 8. Kodlama ajanı
- Büyük kodlama işlerini Claude Code'a devreder; ilerlemeyi gösterir, soruları sana iletir.
- Claude Code'un proje notlarına ulaşabilmesi için bir **MCP sunucusu** açar (anahtarla korunur).

#### 9. Telefon
- Android uygulamasından yazarak ya da sesli soru sorarak Ultron'a ulaşırsın; cevap bilgisayardaki Ultron'dan gelir.
- Bağlantı [Tailscale](https://tailscale.com) ile özel ağ üzerinden kurulur, internete açılmaz.
- Güncellemeler uygulama içinden gelir.

#### 10. Kendini geliştirme
- Gece denetimi: gün içindeki cevapları resmi belgelerle karşılaştırır, hatalıları düzeltip öğrenir.
- "Gelişim" ekranı: haftalık grafikler, gece raporları, zayıf konular.

---

### Kurulum

**Gereksinimler:** Windows 10/11 (64 bit). Konuşma tanıma için güncel bir ekran kartı önerilir. Modeller ve sesler için yaklaşık 8 GB boş alan.

1. [Releases](../../releases) sayfasından **`Ultron-Setup-x.y.z.exe`** dosyasını indirip çalıştır (yönetici izni gerekmez).
2. Ultron'u aç. **İlk açılış sihirbazı** seni adım adım götürür:

| Adım | Ne olur |
|---|---|
| Yapay zekâ motoru | Ollama yoksa tek tıkla kurulur, modeller indirilir (~6,8 GB) |
| Ses | Whisper ve Piper modelleri indirilir (~0,8 GB) |
| Web araması | Tavily anahtarı **ya da** Docker + SearXNG; artı ve eksileri yazar, sen seçersin |
| İsteğe bağlı | Groq (konuşma tanıma) ve ElevenLabs (doğal ses) anahtarları |
| Seni tanıyayım | Hitap, şehir, iş/okul, ilgi alanları (hepsi isteğe bağlı) |
| "Ultron" de | Çağrı kelimesi için 15 ses örneği |

**Telefon (isteğe bağlı):**
1. Bilgisayara ve telefona aynı hesapla [Tailscale](https://tailscale.com) kur.
2. Releases'tan **`Ultron-mobil.apk`**'yı telefona yükle (mağaza dışı olduğu için Play Protect uyarabilir: *Daha fazla ayrıntı → Yine de yükle*).
3. Masaüstünde logo menüsü → **[MOBİL]** → *Kod al*; telefonda bilgisayarın Tailscale adresini ve 6 haneli kodu gir.

---

### Mimari ve teknolojiler

| Proje | Görevi |
|---|---|
| `Ultron.Desktop` | WPF + Blazor Hybrid masaüstü uygulaması (HUD, İş, Okul, paneller, çağrı kelimesi, tepsi) |
| `Ultron.Host` | ASP.NET Core servis: SignalR hub, MCP sunucusu, telefon erişimi, Telegram |
| `Ultron.Core` | İş mantığı: sohbet akışı, hafıza, yönlendirme, araçlar, dersler, İş ekranı, ayarlar |
| `Ultron.Infrastructure` | Ollama, EF Core + SQLite, Whisper.net, seslendirme, web araması, belge okuyucular, kurulum |
| `Ultron.Contracts` | Servis ile istemciler arasındaki veri tipleri |
| `Ultron.Mobile` | .NET MAUI Blazor Hybrid Android uygulaması |
| `extension/chrome` | Manifest V3 Chrome eklentisi (sadece `127.0.0.1` ile konuşur) |

**Teknolojiler:** .NET 10 · C# · Blazor Hybrid (WPF, MAUI) · ASP.NET Core SignalR · Entity Framework Core + SQLite (FTS5) · Ollama · Whisper.net (Vulkan) · Piper · NAudio · PdfPig · Open XML SDK · Model Context Protocol · Inno Setup · Docker (SearXNG, isteğe bağlı).

---

### Gizlilik ve güvenlik

- **Veriler bilgisayarında:** dil modeli, hafıza, sohbetler ve belgeler yerelde işlenir.
- **Depoda sır yok:** API anahtarları kodda tutulmaz; kullanıcının kendi klasöründe (`%USERPROFILE%\Ultron`) ya da .NET user-secrets'ta durur ve git'e girmez.
- **Dışarıya kapalı servis:** Host yalnızca `127.0.0.1`'i dinler; Tailscale açıksa onun özel adresini de dinler ve oradan gelen her istek cihaza özel anahtar ister.
- **Web sitelerine karşı koruma:** Tarayıcıdan gelen istekler (Origin başlığı taşıyanlar) reddedilir; açık bir web sitesi Ultron'a bağlanamaz. Tek istisna eşleştirme kodlu Chrome eklentisi.
- **Onay kartı:** Dosya taşıma, pencere kapatma, gönderme gibi riskli bilgisayar işlemleri senin onayın olmadan yapılmaz; web sayfasına gizlenmiş bir talimat yetki açamaz.
- **Eşleştirme kodları:** 10 dakika geçerli, tek kullanımlık; 5 yanlış denemede geçersiz olur.
- **Bulut hizmetleri isteğe bağlı:** Tavily, Groq, ElevenLabs ve Pro hesapları yalnızca sen eklersen kullanılır; sihirbaz her birinin neyi dışarı gönderdiğini yazar.

Bir güvenlik açığı bulursan lütfen herkese açık issue yerine depo sahibine doğrudan yaz.

---

### Kaynaktan derleme

**Gereksinimler:** .NET 10 SDK, Windows. Kurulum dosyası için [Inno Setup 6](https://jrsoftware.org/isinfo.php); telefon uygulaması için .NET MAUI Android iş yükü.

```powershell
dotnet build Ultron.slnx                                                        # Android uygulaması hariç her şey
dotnet test tests\Ultron.Tests\Ultron.Tests.csproj                              # birim testleri
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.0.0     # Setup.exe → installer\cikti
powershell -ExecutionPolicy Bypass -File installer\build-mobile.ps1             # Android APK
```

---

### Lisans ve not

[MIT](LICENSE) © 2026 Yunus Dinç

> Bu proje, Anthropic'in yapay zekâ asistanı **Claude**'un (Claude Code) yardımıyla geliştirilmiştir. Amacı, **yerel büyük dil modelleri (local LLM) hakkında bilgi sahibi olmak ve bunları gerçek bir uygulamada uçtan uca tecrübe etmektir.**

---
---

## English

### Contents
1. [What is it?](#what-is-it)
2. [Why was it built?](#why-was-it-built)
3. [What can you do with Ultron?](#what-can-you-do-with-ultron)
4. [Installation](#installation)
5. [Architecture and technologies](#architecture-and-technologies)
6. [Privacy and security](#privacy-and-security)
7. [Building from source](#building-from-source)
8. [License and note](#license-and-note)

---

### What is it?

**Ultron** is a Turkish-speaking personal AI assistant that runs on your Windows PC. It runs its language model **on your own computer** (through [Ollama](https://ollama.com)), not on a cloud server, so your conversations, memories and documents never leave your machine.

Ultron has three parts:

| Part | What it does |
|---|---|
| **Desktop app** | A sci-fi "HUD" style main screen: chat, voice, workspaces and panels |
| **Background service (Host)** | Runs the AI engine, memory, tools, reminders and the phone connection |
| **Android app** | Reach Ultron from your phone by typing or talking (privately, over Tailscale) |

Default models: **`gemma4:e4b-it-qat`** for chat and **`qwen3-embedding:0.6b`** for memory search.

---

### Why was it built?

- **To really understand local LLMs.** To experience end to end how large language models run on a personal computer, where they hit their limits, and how those limits can be pushed (memory, retrieval, tool use, voice).
- **To keep data with the user.** Personal information, course notes and conversations can be processed without going to a company's server.
- **To be useful every day.** Real needs such as studying, writing code and UI components, setting reminders and controlling the PC by voice.
- **To get the most out of a small model.** To see how far a ~4-billion-parameter local model can go with the right context, cited sources, a calculator, web reading and self-review.

---

### What can you do with Ultron?

#### 1. Chat and long-term memory
- **Automatically remembers** lasting facts you share (an allergy, your school, your preferences…) and sorts them into topics.
- Notices contradictions: if you say "I live in İzmir now", it updates the old fact.
- Recall combines **semantic search and keyword search** (embeddings + SQLite FTS5).
- The memory panel lets you see, edit or delete every fact.
- You choose how it addresses you (your name, "boss", or nothing).

#### 2. Voice
| Feature | Description |
|---|---|
| Wake word | Starts listening when you say **"Ultron"** (learns your voice from 15 samples) |
| Push-to-talk | Hold the button, speak, it stops when you go quiet |
| Speech recognition | Whisper locally on the GPU (Vulkan), or Groq in the cloud if you prefer |
| Text-to-speech | Free local voice with Piper, or a natural voice with ElevenLabs |
| Barge-in | You can interrupt Ultron while it is speaking |

#### 3. Three workspaces
Switch between **[İŞ] · [ULTRON] · [OKUL]** (Work · Ultron · School) from the fixed top bar; the screen changes with a pixel-by-pixel transition.

| Workspace | Purpose |
|---|---|
| **ULTRON** (main screen) | Chat, live system telemetry (CPU, RAM, GPU…) and a daily summary: calendar, weather, exchange rates and gold, how well Ultron knows you |
| **İŞ (Work)** | Describe the UI component you want; Ultron writes React + Tailwind code with a **live preview** in desktop, tablet and phone frames (iPhone 17 Pro, Galaxy S25 Ultra, Pixel 10 Pro…). Sessions are named by topic and versions are kept |
| **OKUL (School)** | Study grounded in your own course files (PDF, Word, PowerPoint, photos of notes): explanations, **large centred formulas**, step-by-step problem solving (numbers come from a calculator), quizzes, formula sheets, exam plans, lesson notes |

#### 4. Web and research
- Answers current questions **with sources** (cited as [1], [2]).
- Reads pages and PDFs in depth and follows links when needed.
- Search engine: self-hosted **SearXNG** (unlimited) or **Tavily** (1,000 free searches per month).
- "Known sites": sites you follow, such as your school's, are crawled every night; new announcements and exam dates are reported.

#### 5. Documents
- Ask questions about your PDFs, Word files, text and scanned (image) pages.
- Answers show which page of which document they came from.

#### 6. Tools and automation
| Tool | Example |
|---|---|
| Reminders | "Remind me to take my medicine at 9 tomorrow" (priority, repeats, missed reminders) |
| Morning briefing | The day's classes, weather and reminders |
| Suggestions | If it will rain and you have a morning class, it reminds you about an umbrella the evening before |
| Computer control | Windows, audio and media, file organising; risky actions need an **approval card** |
| Chrome extension | Play and seek songs on YouTube, read the open tab |
| Calculator, weather, class schedule | Configured by talking |

#### 7. Answer tiers
| Tier | How it works |
|---|---|
| **Ultron** | Fully local model; smart reading, Turkish and English search, thorough answers |
| **Ultron Pro** | Your **own** Claude Code, OpenAI Codex CLI or Google Gemini CLI account researches the question; good answers go into Ultron's memory and are reused next time without going online |

#### 8. Coding agent
- Hands larger coding jobs to Claude Code, shows progress and relays its questions to you.
- Exposes a token-protected **MCP server** so Claude Code can read project notes.

#### 9. Phone
- Type or ask by voice from the Android app; answers come from Ultron on your PC.
- Connected over your private network with [Tailscale](https://tailscale.com); nothing is exposed to the internet.
- Updates arrive inside the app.

#### 10. Self-improvement
- Nightly review: compares the day's answers with official documentation, fixes and learns from mistakes.
- "Growth" screen: weekly charts, nightly reports, weak topics.

---

### Installation

**Requirements:** Windows 10/11 (64-bit). A recent GPU is recommended for speech recognition. About 8 GB of free space for models and voices.

1. Download **`Ultron-Setup-x.y.z.exe`** from [Releases](../../releases) and run it (no admin rights needed).
2. Open Ultron. The **first-run wizard** guides you step by step:

| Step | What happens |
|---|---|
| AI engine | Installs Ollama with one click if missing and downloads the models (~6.8 GB) |
| Voice | Downloads the Whisper and Piper models (~0.8 GB) |
| Web search | A Tavily key **or** Docker + SearXNG; it lists the pros and cons, you choose |
| Optional | Groq (speech recognition) and ElevenLabs (natural voice) keys |
| Getting to know you | How to address you, city, work/school, interests (all optional) |
| Say "Ultron" | 15 voice samples for the wake word |

**Phone (optional):**
1. Install [Tailscale](https://tailscale.com) on the PC and the phone with the same account.
2. Install **`Ultron-mobil.apk`** from Releases (Play Protect may warn because it is not from the store: *More details → Install anyway*).
3. On the desktop: logo menu → **[MOBİL]** → *Kod al* (get code); enter the PC's Tailscale address and the 6-digit code in the app.

---

### Architecture and technologies

| Project | Role |
|---|---|
| `Ultron.Desktop` | WPF + Blazor Hybrid desktop app (HUD, Work, School, panels, wake word, tray) |
| `Ultron.Host` | ASP.NET Core service: SignalR hub, MCP server, phone access, Telegram |
| `Ultron.Core` | Domain logic: chat flow, memory, routing, tools, lessons, Work screen, settings |
| `Ultron.Infrastructure` | Ollama, EF Core + SQLite, Whisper.net, text-to-speech, web search, document readers, setup |
| `Ultron.Contracts` | Data types shared between the service and its clients |
| `Ultron.Mobile` | .NET MAUI Blazor Hybrid Android app |
| `extension/chrome` | Manifest V3 Chrome extension (talks only to `127.0.0.1`) |

**Technologies:** .NET 10 · C# · Blazor Hybrid (WPF, MAUI) · ASP.NET Core SignalR · Entity Framework Core + SQLite (FTS5) · Ollama · Whisper.net (Vulkan) · Piper · NAudio · PdfPig · Open XML SDK · Model Context Protocol · Inno Setup · Docker (SearXNG, optional).

---

### Privacy and security

- **Data stays on your PC:** the language model, memory, conversations and documents are processed locally.
- **No secrets in the repository:** API keys are never stored in code; they live in the user's own folder (`%USERPROFILE%\Ultron`) or .NET user-secrets and are not committed.
- **Service closed to the outside:** the Host listens only on `127.0.0.1`; if Tailscale is on, it also listens on its private address, and every request from there requires a per-device key.
- **Protection against websites:** requests coming from a browser (carrying an Origin header) are rejected, so an open website cannot connect to Ultron. The only exception is the Chrome extension, which needs a pairing code.
- **Approval card:** risky computer actions such as moving files, closing windows or sending are never done without your approval; instructions hidden in a web page cannot grant permissions.
- **Pairing codes:** valid for 10 minutes, single use, invalidated after 5 wrong attempts.
- **Cloud services are optional:** Tavily, Groq, ElevenLabs and Pro accounts are used only if you add them; the wizard explains what each one sends out.

If you find a security issue, please contact the repository owner directly instead of opening a public issue.

---

### Building from source

**Requirements:** .NET 10 SDK, Windows. [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer; the .NET MAUI Android workload for the phone app.

```powershell
dotnet build Ultron.slnx                                                        # everything except the Android app
dotnet test tests\Ultron.Tests\Ultron.Tests.csproj                              # unit tests
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.0.0     # Setup.exe → installer\cikti
powershell -ExecutionPolicy Bypass -File installer\build-mobile.ps1             # Android APK
```

---

### License and note

[MIT](LICENSE) © 2026 Yunus Dinç

> This project was developed with the help of **Claude** (Claude Code), Anthropic's AI assistant. Its purpose is **to gain knowledge about local large language models (local LLMs) and to experience them end to end in a real application.**
