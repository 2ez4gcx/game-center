#!/usr/bin/env bash
# Đóng gói Game Center cho Linux thành một file AppImage (x86_64).
# Chạy trên Linux (hoặc GitHub Actions ubuntu). Cần: dotnet 8 SDK, curl, 7z (p7zip-full), unzip.
#
#   tools/package-linux.sh [phiên-bản-RetroArch]
#
# Kết quả: publish/linux/GameCenter-<phiên bản>-x86_64.AppImage
# Dữ liệu người dùng (Games, Saves...) nằm ở ~/GameCenter vì AppImage chỉ đọc.
set -euo pipefail

RA_VERSION="${1:-1.22.2}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/GameCenter.Desktop/GameCenter.Desktop.csproj")"
OUT="$ROOT/publish/linux"
APPDIR="$OUT/GameCenter.AppDir"
APP="$APPDIR/usr/lib/gamecenter"
EMU="$APP/Emulators/RetroArch"
WORK="$OUT/work"
CORES=(mednafen_psx_hw snes9x mgba genesis_plus_gx fceumm gambatte)

rm -rf "$OUT"
mkdir -p "$APP" "$WORK"

echo "==> Build Game Center (linux-x64, kèm .NET 8)"
dotnet publish "$ROOT/src/GameCenter.Desktop" -c Release -r linux-x64 --self-contained true \
    -p:DebugType=none -o "$APP"

echo "==> RetroArch $RA_VERSION"
curl -fL "https://buildbot.libretro.com/stable/$RA_VERSION/linux/x86_64/RetroArch.7z" -o "$WORK/RetroArch.7z"
7z x -y -o"$WORK" "$WORK/RetroArch.7z" > /dev/null
RA_IMG="$(find "$WORK" -maxdepth 2 -name 'RetroArch-Linux-*.AppImage' | head -n1)"
chmod +x "$RA_IMG"
# Giải nén sẵn AppImage của RetroArch: tránh chạy AppImage lồng trong AppImage (cần FUSE)
(cd "$WORK" && "$RA_IMG" --appimage-extract > /dev/null)
mkdir -p "$EMU"
cp -a "$WORK/squashfs-root/." "$EMU/"
# assets / info / autoconfig đi kèm bản RetroArch
cp -a "$RA_IMG.home/.config/retroarch/." "$EMU/"
cat > "$EMU/retroarch" <<'EOF'
#!/bin/sh
# Gọi RetroArch đã giải nén; bỏ biến môi trường của AppImage Game Center để RetroArch dùng đúng thư mục của nó
HERE="$(dirname "$(readlink -f "$0")")"
unset APPDIR APPIMAGE ARGV0 OWD
exec "$HERE/AppRun" "$@"
EOF
chmod +x "$EMU/retroarch"

echo "==> Core"
mkdir -p "$EMU/cores"
for c in "${CORES[@]}"; do
    curl -fL "https://buildbot.libretro.com/nightly/linux/x86_64/latest/${c}_libretro.so.zip" -o "$WORK/$c.zip"
    unzip -o -q "$WORK/$c.zip" -d "$EMU/cores"
done

echo "==> Giấy phép"
curl -fL "https://raw.githubusercontent.com/libretro/RetroArch/v$RA_VERSION/COPYING" -o "$APP/Licenses/RetroArch-LICENSE.txt"

echo "==> AppDir"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/gamecenter/GameCenter" "$@"
EOF
chmod +x "$APPDIR/AppRun" "$APP/GameCenter"
cp "$ROOT/assets/gamecenter.svg" "$APPDIR/gamecenter.svg"
cat > "$APPDIR/gamecenter.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Game Center
Comment=Launcher game retro miễn phí
Exec=GameCenter
Icon=gamecenter
Categories=Game;Emulator;
Terminal=false
EOF

echo "==> AppImage"
curl -fL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" -o "$WORK/appimagetool"
chmod +x "$WORK/appimagetool"
ARCH=x86_64 "$WORK/appimagetool" --appimage-extract-and-run "$APPDIR" "$OUT/GameCenter-$VERSION-x86_64.AppImage"

# Chỉ chặn chương trình DuckStation; RetroArch tự kèm vài file mô tả có chữ "duckstation" (.info, .params)
if find "$APPDIR" -type f \( -iname '*duckstation*.so' -o -iname '*duckstation*.appimage' -o -iname 'duckstation-*' \) | grep -q .; then
    echo "Phát hiện DuckStation — không được đóng gói" >&2; exit 1
fi
rm -rf "$WORK"
echo "Xong: $OUT/GameCenter-$VERSION-x86_64.AppImage"
