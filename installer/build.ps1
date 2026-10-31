# Ultron · Setup.exe üretir (Faz 14). Taşınma kamyonu benzetmesi: önce eşyalar kutulara konur (dotnet publish),
# sonra kamyona yüklenir (Inno Setup). Proje kökünden çalıştır:
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1            # sürüm 1.0.0
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.1.0
# Çıktı: installer\cikti\Ultron-Setup-<sürüm>.exe  (git'e girmez)
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot "cikti"
$stage = Join-Path $out "paket"
$app = Join-Path $stage "app"

$iscc = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup bulunamadı: winget install JRSoftware.InnoSetup" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

# .NET dahil (self-contained, tasarım kararı): .NET kurulu olmayan bilgisayarda da çalışır.
# Masaüstü ana klasöre, Host kendi klasörüne (iki ayrı .NET çalışma zamanı; ortak klasörde sürüm çakışmasın).
$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:DebugType=none", "--nologo", "-v", "q")
Write-Host "1/4 Masaüstü yayımlanıyor..."
dotnet publish (Join-Path $root "src\Ultron.Desktop\Ultron.Desktop.csproj") @common -o $app
if ($LASTEXITCODE -ne 0) { throw "Masaüstü yayımlanamadı" }
Write-Host "2/4 Host yayımlanıyor..."
dotnet publish (Join-Path $root "src\Ultron.Host\Ultron.Host.csproj") @common -o (Join-Path $app "host")
if ($LASTEXITCODE -ne 0) { throw "Host yayımlanamadı" }

# Ses sunucusu betiği: Host, kendi klasöründen yukarı çıkıp tools\tts-server\server.py arar ({app}\tools\tts-server).
Write-Host "3/4 Betikler..."
$tts = Join-Path $app "tools\tts-server"
New-Item -ItemType Directory -Force $tts | Out-Null
Copy-Item (Join-Path $root "tools\tts-server\*.py") $tts
# Wake word modele değil, kullanıcının ses örneklerine dayanır (%USERPROFILE%\Ultron\voices\wake, tepsiden kaydedilir): pakete bir şey girmez.
# Chrome eklentisi (Faz 22): kullanıcı chrome://extensions → Paketlenmemiş öğe yükle → {app}\eklenti\chrome.
$ext = Join-Path $app "eklenti\chrome"
New-Item -ItemType Directory -Force $ext | Out-Null
Copy-Item (Join-Path $root "extension\chrome\*") $ext -Recurse
# Web araması (Faz 26): kurulum sihirbazı SearXNG kapsayıcısını bu ayar dosyasıyla kurar (gizli anahtarı kendisi üretir).
$sx = Join-Path $app "searxng"
New-Item -ItemType Directory -Force $sx | Out-Null
Copy-Item (Join-Path $root "docker\searxng\settings.yml") $sx

Write-Host "4/4 Setup.exe derleniyor..."
& $iscc "/Q" "/DAppVersion=$Version" "/DStageDir=$stage" "/O$out" (Join-Path $PSScriptRoot "ultron.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup derlemesi başarısız" }

$setup = Join-Path $out "Ultron-Setup-$Version.exe"
Write-Host ("Hazır: {0} ({1:N0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
