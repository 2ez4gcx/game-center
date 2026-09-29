#!/usr/bin/env bash
# Đóng gói Game Center cho macOS thành file .dmg.
# Chạy trên macOS (hoặc GitHub Actions macos). Cần: dotnet 8 SDK, curl, unzip, hdiutil, codesign.
#
#   tools/package-macos.sh <arm64|x86_64> [phiên-bản-RetroArch]
#
# arm64: Mac chip Apple (M1–M4); x86_64: Mac chip Intel.
# Kết quả: publish/macos/GameCenter-<phiên bản>-macos-<arch>.dmg
# App chỉ ký ad-hoc: lần đầu mở cần chuột phải → Mở (chưa có tài khoản Apple Developer để công chứng).
# Dữ liệu người dùng (Games, Saves...) nằm ở ~/GameCenter.
set -euo pipefail

ARCH="${1:?Cần chọn arm64 hoặc x86_64}"
RA_VERSION="${2:-1.22.2}"
SDL_VERSION="2.30.11"
case "$ARCH" in
    arm64)  RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) echo "Kiến trúc không hợp lệ: $ARCH" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/GameCenter.Desktop/GameCenter.Desktop.csproj")"
OUT="$ROOT/publish/macos"
APP="$OUT/Game Center.app"
BIN="$APP/Contents/MacOS"
EMU="$BIN/Emulators"
WORK="$OUT/work-$ARCH"
CORES=(mednafen_psx_hw snes9x mgba genesis_plus_gx fceumm gambatte)

rm -rf "$APP" "$WORK"
mkdir -p "$BIN" "$APP/Contents/Resources" "$WORK"

echo "==> Build Game Center ($RID, kèm .NET 8)"
dotnet publish "$ROOT/src/GameCenter.Desktop" -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -o "$BIN"

echo "==> SDL2 $SDL_VERSION (bản universal, đọc tay cầm)"
curl -fL "https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL2-$SDL_VERSION.dmg" -o "$WORK/SDL2.dmg"
MNT="$(hdiutil attach -nobrowse -readonly "$WORK/SDL2.dmg" | tail -n1 | cut -f3)"
cp "$MNT/SDL2.framework/Versions/A/SDL2" "$BIN/libSDL2.dylib"
hdiutil detach "$MNT" -quiet
install_name_tool -id "@rpath/libSDL2.dylib" "$BIN/libSDL2.dylib"

echo "==> RetroArch $RA_VERSION (universal)"
curl -fL "https://buildbot.libretro.com/stable/$RA_VERSION/apple/osx/universal/RetroArch_Metal.dmg" -o "$WORK/RetroArch.dmg"
MNT="$(hdiutil attach -nobrowse -readonly "$WORK/RetroArch.dmg" | tail -n1 | cut -f3)"
mkdir -p "$EMU"
cp -R "$MNT/RetroArch.app" "$EMU/"
hdiutil detach "$MNT" -quiet

echo "==> Core ($ARCH)"
# Core để ngoài RetroArch.app (Emulators/cores) để không làm hỏng chữ ký của RetroArch
mkdir -p "$EMU/cores"
for c in "${CORES[@]}"; do
    curl -fL "https://buildbot.libretro.com/nightly/apple/osx/$ARCH/latest/${c}_libretro.dylib.zip" -o "$WORK/$c.zip"
    unzip -o -q "$WORK/$c.zip" -d "$EMU/cores"
done

echo "==> Giấy phép"
curl -fL "https://raw.githubusercontent.com/libretro/RetroArch/v$RA_VERSION/COPYING" -o "$BIN/Licenses/RetroArch-LICENSE.txt"

echo "==> Info.plist"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Game Center</string>
    <key>CFBundleDisplayName</key><string>Game Center</string>
    <key>CFBundleIdentifier</key><string>com.khuongdoan.gamecenter</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleExecutable</key><string>GameCenter</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.games</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>© 2026 Khuong Doan — khuongdoan.com</string>
</dict>
</plist>
EOF

if find "$APP" -iname '*duckstation*' | grep -q .; then
    echo "Phát hiện DuckStation — không được đóng gói" >&2; exit 1
fi

echo "==> Ký ad-hoc"
codesign --force --deep --sign - "$APP"

echo "==> DMG"
DMG="$OUT/GameCenter-$VERSION-macos-$ARCH.dmg"
STAGE="$WORK/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Game Center" -srcfolder "$STAGE" -ov -format UDZO "$DMG" > /dev/null
rm -rf "$WORK" "$APP"
echo "Xong: $DMG"
