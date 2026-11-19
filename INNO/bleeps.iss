; Bleeps Inno Setup installer
; Detects the .NET 10 Desktop Runtime; downloads and installs it when it is missing.
; Hamlib travels INSIDE the program folder ({app}\hamlib) - HamlibNative looks there first.

#define MyAppName      "Bleeps"
#define MyAppPublisher "PA5DJ"
#define MyAppURL       "https://shackassistant.com/guides/bleeps.html"
#define MyAppExeName   "Bleeps.exe"
#define PublishDir     SourcePath + "\build"

; The version comes out of the built EXE (FileVersion in Bleeps.csproj)
#define ExeVersion     GetVersionNumbersString(PublishDir + "\" + MyAppExeName)
#define VerShort       Copy(ExeVersion, 1, RPos(".", ExeVersion) - 1)
#define MyAppVersion   "v" + VerShort

[Setup]
AppId={{0C62B44A-312B-4035-AAAC-87D924EFB312}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#SourcePath}
OutputBaseFilename=BleepsSetup_{#MyAppVersion}
SetupIconFile={#SourcePath}\..\src\Bleeps\Bleeps.ico
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; the exe, its DLLs, the Twemoji licence and hamlib\ (the four Hamlib DLLs)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DotNetPage: TDownloadWizardPage;

// .NET always installs version folders under Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\.
function IsDotNet10Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    FindClose(FindRec);
    Result := True;
  end;
end;

procedure InitializeWizard;
begin
  DotNetPage := CreateDownloadPage(
    '.NET 10 Desktop Runtime',
    'Downloading Microsoft .NET 10 Desktop Runtime - required by Bleeps...',
    nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not IsDotNet10Installed then
  begin
    DotNetPage.Clear;
    DotNetPage.Add('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', 'dotnet-runtime.exe', '');
    DotNetPage.Show;
    try
      try
        DotNetPage.Download;
      except
        if DotNetPage.AbortedByUser then
          SuppressibleMsgBox('Download cancelled. Bleeps requires the .NET 10 Desktop Runtime.', mbError, MB_OK, IDOK)
        else
          SuppressibleMsgBox('Download failed: ' + GetExceptionMessage + #13#10 +
            'Install .NET 10 manually from https://dotnet.microsoft.com/en-us/download/dotnet/10.0', mbError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DotNetPage.Hide;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if IsDotNet10Installed then Exit;
  if not Exec(ExpandConstant('{tmp}\dotnet-runtime.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Could not start the .NET 10 installer.';
    Exit;
  end;
  if ResultCode <> 0 then
    Result := '.NET 10 installation failed (exit code ' + IntToStr(ResultCode) + '). ' +
              'Please install it manually from https://dotnet.microsoft.com/en-us/download/dotnet/10.0';
end;
