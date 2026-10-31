; Ultron kurulum dosyası (Inno Setup 6). installer\build.ps1 derler; elle: ISCC /DAppVersion=1.0.0 /DStageDir=... ultron.iss
; Kararlar (2026-10-27): gerçek Setup.exe, .NET dahil, %LOCALAPPDATA%\Programs\Ultron (yönetici yetkisi gerekmez),
; Başlat menüsü + masaüstü kısayolu, "Windows açılışında başlat" seçeneği, kaldırma kişisel veriyi sormadan silmez.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef StageDir
  #define StageDir "cikti\paket"
#endif

[Setup]
AppId={{4F6FD6DF-5319-41B3-AFBC-E96FA9C1CEE8}
AppName=Ultron
AppVersion={#AppVersion}
AppVerName=Ultron {#AppVersion}
AppPublisher=Yunus Dinç
DefaultDirName={localappdata}\Programs\Ultron
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=Ultron-Setup-{#AppVersion}
SetupIconFile=..\src\Ultron.Desktop\ultron.ico
UninstallDisplayIcon={app}\Ultron.exe
UninstallDisplayName=Ultron
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol koy"
Name: "autostart"; Description: "Windows açılışında tepside başlat"

[Files]
Source: "{#StageDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 1.0.0'daki sherpa-onnx wake word kütüphaneleri (1.0.1'de ses örnekleriyle eşleştirmeye geçildi).
Type: files; Name: "{app}\sherpa-onnx*.dll"
Type: files; Name: "{app}\onnxruntime*.dll"
Type: filesandordirs; Name: "{%USERPROFILE}\Ultron\models\kws"

[Icons]
Name: "{autoprograms}\Ultron"; Filename: "{app}\Ultron.exe"
Name: "{autodesktop}\Ultron"; Filename: "{app}\Ultron.exe"; Tasks: desktopicon

[Registry]
; Açılış kaydı (Ultron her açılışta da kendisi günceller; tepsiden kapatılabilir).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Ultron"; \
  ValueData: """{app}\Ultron.exe"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Ultron.exe"; Description: "Ultron'u şimdi başlat"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM Ultron.exe"; Flags: runhidden; RunOnceId: "KapatMasaustu"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM Ultron.Host.exe"; Flags: runhidden; RunOnceId: "KapatHost"

[Code]
const
  SettingsRel = '\Ultron\desktop-settings.json';

{ Çalışan Ultron (geliştirme sürümü dahil) kapatılır: tek kopya kuralı yüzünden kurulan sürüm açılamazdı. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM Ultron.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM Ultron.Host.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

{ "Windows açılışında başlat" seçimi Ultron'un kendi ayar dosyasına da yazılır (yoksa Ultron ilk açılışta kaydı geri ekler). }
procedure SaveAutoStartChoice;
var
  Path, Text, OnText, OffText: String;
  Json: AnsiString;
begin
  Path := ExpandConstant('{%USERPROFILE}') + SettingsRel;
  if WizardIsTaskSelected('autostart') then begin OnText := 'true'; OffText := 'false'; end
  else begin OnText := 'false'; OffText := 'true'; end;
  if LoadStringFromFile(Path, Json) then
  begin
    Text := String(Json);
    StringChangeEx(Text, '"AutoStart":' + OffText, '"AutoStart":' + OnText, True);
    SaveStringToFile(Path, AnsiString(Text), False);
  end
  else
  begin
    ForceDirectories(ExtractFileDir(Path));
    SaveStringToFile(Path, '{"AutoStart":' + OnText + '}', False);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveAutoStartChoice;
end;

{ Bitiş sayfası: Ultron'un dayandığı ama kurulumla gelmeyen parçalar (bu bilgisayarda eksik olanlar). }
function Missing: String;
var
  Home, Local: String;
begin
  Home := ExpandConstant('{%USERPROFILE}');
  Local := ExpandConstant('{localappdata}');
  Result := '';
  if not FileExists(Local + '\Programs\Ollama\ollama.exe') then
    Result := Result + #13#10 + '• Ollama (dil modeli) — ollama.com, sonra: ollama pull gemma4:e4b-it-qat ve ollama pull qwen3-embedding:0.6b';
  if not (FileExists(Local + '\Programs\DockerDesktop\Docker Desktop.exe') or FileExists(ExpandConstant('{commonpf}') + '\Docker\Docker\Docker Desktop.exe')) then
    Result := Result + #13#10 + '• Docker Desktop (web araması için SearXNG)';
  if not FileExists(Home + '\Ultron\models\whisper\ggml-large-v3-turbo-q5_0.bin') then
    Result := Result + #13#10 + '• Whisper ses tanıma modeli (%USERPROFILE%\Ultron\models\whisper)';
  if not FileExists(Home + '\Ultron\tts\.venv\Scripts\python.exe') then
    Result := Result + #13#10 + '• Chatterbox ses ortamı (yoksa Ultron Piper sesiyle konuşur)';
  if not FileExists(Home + '\.local\bin\claude.exe') then
    Result := Result + #13#10 + '• Claude Code (kodlama ajanı, isteğe bağlı)';
end;

procedure CurPageChanged(CurPageID: Integer);
var
  M: String;
begin
  if CurPageID = wpFinished then
  begin
    M := Missing;
    if M <> '' then
      WizardForm.FinishedLabel.Caption := 'Ultron kuruldu. Bu bilgisayarda şunlar eksik; Ultron bunlar olmadan kısmen çalışır:' + M
    else
      WizardForm.FinishedLabel.Caption := 'Ultron kuruldu. Gereken bütün parçalar bu bilgisayarda mevcut.';
  end;
end;

{ Kaldırma: kişisel veriler (hafıza, konuşmalar, belgeler, YEDEKLER, modeller) varsayılan olarak SİLİNMEZ; sadece açıkça "Evet" denirse. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Ultron kaldırıldı.' + #13#10#13#10 +
              'Kişisel verilerin de silinsin mi? (hafıza, konuşmalar, hatırlatmalar, belgeler, YEDEKLER, ses ve dil modelleri)' + #13#10 +
              ExpandConstant('{localappdata}') + '\Ultron' + #13#10 + ExpandConstant('{%USERPROFILE}') + '\Ultron' + #13#10#13#10 +
              'Hayır dersen her şey yerinde kalır; Ultron''u yeniden kurunca kaldığın yerden devam eder.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}') + '\Ultron', True, True, True);
      DelTree(ExpandConstant('{%USERPROFILE}') + '\Ultron', True, True, True);
    end;
end;
