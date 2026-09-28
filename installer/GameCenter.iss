; Bộ cài Game Center — Inno Setup 7
; Cài theo người dùng, không cần quyền admin. Mặc định C:\GameCenter (dễ tìm), cho chọn ổ khác (ví dụ D:\GameCenter).
; Games, Saves, BIOS... nằm ngay trong thư mục cài đặt; gỡ cài đặt KHÔNG xóa các thư mục này.
; Build: ISCC installer\GameCenter.iss   (sau khi chạy build.ps1)

#define AppName "Game Center"
#define AppVersion "0.2.0"
#define PublishDir "..\publish\GameCenter"
; Cho phép build bản thử với AppId khác: ISCC /DTestBuild installer\GameCenter.iss
#ifdef TestBuild
  #define AppIdValue "{{6C2B7A1E-4F3D-4B8E-9A61-5E2C1D7F0A99}"
  #define OutName "GameCenter-TEST"
#else
  #define AppIdValue "{{6C2B7A1E-4F3D-4B8E-9A61-5E2C1D7F0A11}"
  #define OutName "GameCenter-Setup-" + AppVersion
#endif

[Setup]
AppId={#AppIdValue}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Khuong Doan
AppPublisherURL=https://khuongdoan.com/
DefaultDirName={sd}\GameCenter
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish
OutputBaseFilename={#OutName}
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
; Thư mục dữ liệu nằm trong thư mục cài đặt — KHÔNG bị xóa khi gỡ cài đặt.
Name: "{app}\Games\NES";       Flags: uninsneveruninstall
Name: "{app}\Games\SNES";      Flags: uninsneveruninstall
Name: "{app}\Games\GB";        Flags: uninsneveruninstall
Name: "{app}\Games\GBA";       Flags: uninsneveruninstall
Name: "{app}\Games\MegaDrive"; Flags: uninsneveruninstall
Name: "{app}\Games\PS1";       Flags: uninsneveruninstall
Name: "{app}\BIOS";            Flags: uninsneveruninstall
Name: "{app}\Saves";           Flags: uninsneveruninstall
Name: "{app}\States";          Flags: uninsneveruninstall
Name: "{app}\Covers";          Flags: uninsneveruninstall
Name: "{app}\Playlists";       Flags: uninsneveruninstall
Name: "{app}\Config";          Flags: uninsneveruninstall
Name: "{app}\Database";        Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\GameCenter.exe"
Name: "{group}\Thư mục game"; Filename: "{app}\Games"
Name: "{group}\Gỡ cài đặt {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\GameCenter.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GameCenter.exe"; Description: "Mở Game Center"; Flags: nowait postinstall skipifsilent

; Gỡ cài đặt chỉ xóa thư mục ứng dụng; Games/Saves/States trong thư mục dữ liệu được giữ nguyên.
