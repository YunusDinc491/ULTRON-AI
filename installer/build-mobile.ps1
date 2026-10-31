# Ultron · telefon APK'sı (Faz 25). Derler ve bilgisayardaki Ultron'un "rafına" koyar: telefon açılışta görür, "Güncelle" der
# (karar 2026-10-30: güncelleme uygulama içinden). Proje kökünden, Claude içinden çalıştır (Android SDK Claude'un gölge klasöründe):
#   powershell -ExecutionPolicy Bypass -File installer\build-mobile.ps1          # sürüm = rafdaki + 1
#   powershell -ExecutionPolicy Bypass -File installer\build-mobile.ps1 -Surum 5
# Çıktı: %USERPROFILE%\Ultron\Ultron-mobil.apk + Ultron-mobil.json ({"surum":N,"ad":"1.N"})
# İmza bu bilgisayardaki hata ayıklama anahtarı: Android güncellemeyi sadece aynı anahtarla imzalı APK'dan kabul eder.
param(
    [int]$Surum = 0
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$shelf = Join-Path $env:USERPROFILE "Ultron"
$info = Join-Path $shelf "Ultron-mobil.json"

if ($Surum -lt 1) {
    # İlk elle yüklenen APK sürüm 1'di; raf boşsa 2'den başlar.
    $Surum = if (Test-Path $info) { [int](Get-Content $info -Raw | ConvertFrom-Json).surum + 1 } else { 2 }
}
$name = "1.$Surum"

Write-Host "APK $name (sürüm kodu $Surum) derleniyor..."
$project = Join-Path $root "src\Ultron.Mobile\Ultron.Mobile.csproj"
dotnet publish $project -f net10.0-android -c Release -p:AndroidPackageFormat=apk "-p:ApplicationVersion=$Surum" "-p:ApplicationDisplayVersion=$name" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "APK derlenemedi" }

$apk = Get-ChildItem (Join-Path $root "src\Ultron.Mobile\bin\Release\net10.0-android\publish") -Filter "*-Signed.apk" | Select-Object -First 1
if (-not $apk) { throw "İmzalı APK bulunamadı" }
Copy-Item $apk.FullName (Join-Path $shelf "Ultron-mobil.apk") -Force
# Sürüm dosyası en son yazılır: APK kopyalanırken telefon yarım dosyayı "yeni sürüm" sanmasın.
[System.IO.File]::WriteAllText($info, (@{ surum = $Surum; ad = $name } | ConvertTo-Json -Compress))

Write-Host ("Rafta: {0} · sürüm {1} ({2:N1} MB)" -f (Join-Path $shelf "Ultron-mobil.apk"), $name, ($apk.Length / 1MB))
