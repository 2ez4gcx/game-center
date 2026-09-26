; Bộ cài Game Center — Inno Setup 6
; Cách A (mục 5.1): cài theo người dùng, không cần quyền admin.
; Build: ISCC installer\GameCenter.iss   (sau khi chạy build.ps1)

#define AppName "Game Center"
#define AppVersion "0.1.0"
#define PublishDir "..\publish\GameCenter"

[Setup]
AppId={{6C2B7A1E-4F3D-4B8E-9A61-5E2C1D7F0A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Game Center
DefaultDirName={localappdata}\Programs\GameCenter
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DisableProgramGroupPage=yes
DisableDirPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish
OutputBaseFilename=GameCenter-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\GameCenter.exe
LicenseFile={#PublishDir}\Licenses\GameCenter-LICENSE.txt

[Languages]
Name: "vi"; MessagesFile: "Vietnamese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình Desktop"; Flags: checkedonce

[Files]
; Ứng dụng + RetroArch portable + core đã kiểm tra license + Licenses/. Không có DuckStation, ROM, BIOS.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*duckstation*"

[Dirs]
; Thư mục dữ liệu mặc định %USERPROFILE%\GameCenter — KHÔNG bị xóa khi gỡ cài đặt.
Name: "{%USERPROFILE}\GameCenter\Games\NES";       Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Games\SNES";      Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Games\GB";        Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Games\GBA";       Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Games\MegaDrive"; Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Games\PS1";       Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\BIOS";            Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Saves";           Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\States";          Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Covers";          Flags: uninsneveruninstall
Name: "{%USERPROFILE}\GameCenter\Playlists";       Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\GameCenter.exe"
Name: "{group}\Thư mục game"; Filename: "{%USERPROFILE}\GameCenter\Games"
Name: "{group}\Gỡ cài đặt {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\GameCenter.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GameCenter.exe"; Description: "Mở Game Center"; Flags: nowait postinstall skipifsilent

; Gỡ cài đặt chỉ xóa thư mục ứng dụng; Games/Saves/States trong thư mục dữ liệu được giữ nguyên.
