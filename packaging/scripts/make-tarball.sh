#!/usr/bin/env bash
# Baut das Binärpaket für Flathub (Owner-Entscheidung F1, 30.10.2026): self-contained Publish + Desktop-Dateien als
#   artifacts/dist/rebelsofgaming-<version>-linux-<arch>.tar.gz  (+ .sha256)
# Layout im Archiv: rebelsofgaming-<version>-linux-<arch>/{rebelsofgaming,share}/ – flatpak-builder streift den
# obersten Ordner ab, das Manifest kopiert dist/rebelsofgaming nach /app/lib und dist/share nach /app/share.
# Die Release-Pipeline (.github/workflows/release.yml) baut beide Architekturen, erzeugt mit release-manifest.sh das
# Manifest mit URL + sha256 und lädt später (nach Owner-Freigabe) nach https://cdn.rogclan.de/apps/linux/<version>/
# (Bucket rog-public) hoch. Der Upload ist bewusst NICHT Teil dieses Skripts.
#
#   packaging/scripts/make-tarball.sh [x86_64|aarch64]
set -euo pipefail

arch="${1:-x86_64}"
case "$arch" in
  x86_64) rid=linux-x64 ;;
  aarch64) rid=linux-arm64 ;;
  *) echo "Architektur unbekannt: $arch (x86_64 oder aarch64)" >&2; exit 2 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
version="$(sed -n 's:.*<RoGLinuxVersion>\(.*\)</RoGLinuxVersion>.*:\1:p' "$root/Directory.Build.props")"
app_id=com.rebelsofgaming.RebelsOfGaming
name="rebelsofgaming-$version-linux-$arch"
work="$root/artifacts/dist/$name"
rm -rf "$work"
mkdir -p "$work"

# ReadyToRun (vorkompiliert, Stufe 10): erstes Fenster unter Xvfb ~0,9 s statt ~1,5 s, Paket ~25 % größer.
# Kein Trimming: RoG.Core (API-Client, System.Text.Json) und Avalonia nutzen Reflexion – das Risiko stiller Fehler zur
# Laufzeit ist größer als der Gewinn an Paketgröße. ROG_READY_TO_RUN=false baut ohne Vorkompilierung.
ready_to_run="${ROG_READY_TO_RUN:-true}"
dotnet publish "$root/src/RoG.Linux.App/RoG.Linux.App.csproj" -c Release -r "$rid" --self-contained true \
  -p:PublishTrimmed=false -p:PublishReadyToRun="$ready_to_run" -p:DebugType=none -o "$work/rebelsofgaming"

# Layout wie unter /app im Flatpak (das Manifest kopiert es nur noch)
install -Dm644 "$root/packaging/linux/$app_id.desktop" "$work/share/applications/$app_id.desktop"
install -Dm644 "$root/packaging/linux/$app_id.metainfo.xml" "$work/share/metainfo/$app_id.metainfo.xml"
install -Dm644 "$root/external/rog-windows/src/RoG.App/Assets/rog-badge-512.png" "$work/share/icons/hicolor/512x512/apps/$app_id.png"
install -Dm644 "$root/external/rog-windows/src/RoG.App/Fonts/OFL.txt" "$work/share/licenses/$app_id/OFL-BarlowCondensed.txt"

# Reproduzierbar: feste Zeitstempel und Reihenfolge, keine Benutzerkennungen
epoch="$(git -C "$root" log -1 --format=%ct)"
tar --sort=name --mtime="@$epoch" --owner=0 --group=0 --numeric-owner -C "$root/artifacts/dist" -czf "$work.tar.gz" "$name"
(cd "$root/artifacts/dist" && sha256sum "$name.tar.gz" > "$name.tar.gz.sha256")
rm -rf "$work"
cat "$work.tar.gz.sha256"
