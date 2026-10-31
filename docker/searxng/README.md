# SearXNG (yerel arama motoru)

Ultron'un web araması için. Google, Bing, DuckDuckGo gibi motorlara sorguyu SearXNG gönderir;
Ultron sonuçları `http://127.0.0.1:8888/search?q=...&format=json` adresinden JSON olarak okur.
Sadece bu bilgisayardan erişilebilir (127.0.0.1).

## İlk kurulum
1. Docker Desktop açık olmalı.
2. Bu klasörde gizli anahtar dosyası oluştur (bir kez, git'e girmez):
   ```powershell
   $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
   "SEARXNG_SECRET=" + (($b | ForEach-Object { $_.ToString("x2") }) -join "") | Out-File .env -Encoding ascii
   ```
3. `docker compose up -d`

## Kontrol
```powershell
Invoke-RestMethod "http://127.0.0.1:8888/search?q=dolar+kuru&format=json" | Select-Object -ExpandProperty results | Select-Object -First 3 title, url
```

## Durdurma / güncelleme
- `docker compose down`
- `docker compose pull; docker compose up -d`

`restart: unless-stopped` sayesinde Docker Desktop açıldığında SearXNG da kendiliğinden başlar.
