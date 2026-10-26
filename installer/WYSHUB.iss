[Setup]
AppName=WYSHUB
AppVersion=1.0.0
DefaultDirName={autopf}\WYSHUB
DisableDirPage=no
DefaultGroupName=WYSHUB
OutputBaseFilename=WYSHUB_Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\WYSHUB_executavel\icons\Light.ico
OutputDir=..\instaladores
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianPortuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Run]
Filename: "{cmd}"; Parameters: "/C \"\"{app}\install_dependencies.bat\"\""; WorkingDir: "{app}"; Flags: runhidden skipifsilent

[Files]
Source: "install_dependencies.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\requirements.txt"; DestDir: "{app}"; Flags: ignoreversion

Source: "..\WYSHUB_executavel\bin\Release\net8.0-windows\win-x64\publish\WYSHUB.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\WYSHUB_executavel\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\setup-wizard.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dependencias_offline\*"; DestDir: "{app}\dependencias_offline"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WYSHUB"; Filename: "{app}\WYSHUB.exe"; WorkingDir: "{app}"; IconFilename: "{app}\WYSHUB.exe"
Name: "{commondesktop}\WYSHUB"; Filename: "{app}\WYSHUB.exe"; WorkingDir: "{app}"; IconFilename: "{app}\WYSHUB.exe"

[Code]
var
  RunSetupWizardCheckBox: TNewCheckBox;
  ResultCode: Integer;

function InitializeWizard(): Integer;
var
  Page: TWizardPage;
begin
  Page := CreateCustomPage(wpSelectDir, 'Assistente de instalação', 'Execute o assistente de instalação para instalar pré-requisitos e recursos necessários.');
  RunSetupWizardCheckBox := TNewCheckBox.Create(Page);
  RunSetupWizardCheckBox.Parent := Page.Surface;
  RunSetupWizardCheckBox.Left := ScaleX(0);
  RunSetupWizardCheckBox.Top := ScaleY(0);
  RunSetupWizardCheckBox.Width := Page.SurfaceWidth;
  RunSetupWizardCheckBox.Caption := 'Executar assistente de instalação (recomendada para .NET, Visual C++ e recursos da Microsoft)';
  RunSetupWizardCheckBox.Checked := True;
  Result := 0;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and RunSetupWizardCheckBox.Checked then
  begin
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup-wizard.ps1') + '" -SourcePath "' + ExpandConstant('{app}') + '"', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;
