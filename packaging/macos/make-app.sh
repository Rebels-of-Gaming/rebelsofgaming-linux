#!/usr/bin/env bash
# Baut das macOS-App-Bundle (Mac App Store, unsigniert):
#   artifacts/macos/<arch>/Rebels of Gaming.app
# Self-contained Publish von src/RoG.Linux.App (eine Oberfläche für Linux und macOS) für osx-<arch>, Info.plist aus
# Info.plist.in, Symbol (.icns aus AppIcon-1024.png, nur unter macOS mit sips/iconutil).
# Signieren, .pkg und Upload macht sign-app.sh bzw. der Workflow .github/workflows/macos.yml – nicht dieses Skript.
#
#   packaging/macos/make-app.sh [arm64|x64] [build-nummer]
set -euo pipefail

arch="${1:-arm64}"
case "$arch" in
  arm64|x64) rid="osx-$arch" ;;
  *) echo "Architektur unbekannt: $arch (arm64 oder x64)" >&2; exit 2 ;;
esac
build="${2:-${ROG_BUILD_NUMBER:-1}}"
case "$build" in
  ''|*[!0-9.]*) echo "Build-Nummer ungültig: $build" >&2; exit 2 ;;
esac

# .NET 10 unterstützt macOS ab 14 (Sonoma) – kleiner geht nicht
min_macos=14.0

root="$(cd "$(dirname "$0")/../.." && pwd)"
here="$root/packaging/macos"
version="$(sed -n 's:.*<RoGLinuxVersion>\(.*\)</RoGLinuxVersion>.*:\1:p' "$root/Directory.Build.props")"
out="$root/artifacts/macos/$arch"
app="$out/Rebels of Gaming.app"
rm -rf "$out"
mkdir -p "$out"

# ReadyToRun wie unter Linux (schnellerer Start), kein Trimming (Reflexion in Kern und Avalonia)
dotnet publish "$root/src/RoG.Linux.App/RoG.Linux.App.csproj" -c Release -r "$rid" --self-contained true \
  -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishReadyToRun="${ROG_READY_TO_RUN:-true}" -p:DebugType=none \
  -p:PublishDocumentationFiles=false -o "$out/publish"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$out/publish/." "$app/Contents/MacOS/"
# Lizenztext der Schrift gehört zu den Ressourcen, nicht neben den Code
if [ -d "$app/Contents/MacOS/Licenses" ]; then
  mv "$app/Contents/MacOS/Licenses" "$app/Contents/Resources/Licenses"
fi
# createdump ist ein eigenes Programm ohne Sandbox-Recht – der Mac App Store lehnt solche Bundles ab (und in der
# Sandbox könnte es ohnehin nichts schreiben)
rm -f "$app/Contents/MacOS/createdump"
chmod 755 "$app/Contents/MacOS/rebelsofgaming"

sed -e "s/@VERSION@/$version/g" -e "s/@BUILD@/$build/g" -e "s/@MIN_MACOS@/$min_macos/g" \
  "$here/Info.plist.in" > "$app/Contents/Info.plist"
printf 'APPL????' > "$app/Contents/PkgInfo"

if command -v iconutil >/dev/null && command -v sips >/dev/null; then
  iconset="$out/AppIcon.iconset"
  mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$here/AppIcon-1024.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$here/AppIcon-1024.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
  # Vorlage muss 1024 px haben (größte Stufe 512@2x) – nie hochskalieren
  [ "$(sips -g pixelWidth "$here/AppIcon-1024.png" | awk '/pixelWidth/ {print $2}')" = 1024 ] \
    || { echo "AppIcon-1024.png ist nicht 1024 px breit" >&2; exit 1; }
  rm -rf "$iconset"
else
  echo "Hinweis: sips/iconutil fehlen (kein macOS) – Bundle ohne AppIcon.icns" >&2
fi

if command -v plutil >/dev/null; then
  plutil -lint "$app/Contents/Info.plist"
fi

echo "Bundle: $app (Version $version, Build $build, $rid, ab macOS $min_macos)"
