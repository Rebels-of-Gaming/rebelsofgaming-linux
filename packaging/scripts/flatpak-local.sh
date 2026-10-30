#!/usr/bin/env bash
# Lokaler Flatpak-Probebau aus dem selbst gebauten Paket (ohne Upload, ohne Flathub):
#   1. packaging/scripts/make-tarball.sh <arch>         → artifacts/dist/rebelsofgaming-<version>-linux-<arch>.tar.gz
#   2. packaging/scripts/flatpak-local.sh [arch] [--install]
# Erzeugt artifacts/flatpak/<App-ID>.local.yml (Manifest mit path: + echter Prüfsumme statt cdn.rogclan.de-URL) und
# baut damit nach artifacts/flatpak/build-dir. Mit --install wird die App für den Benutzer installiert
# (flatpak run com.rebelsofgaming.RebelsOfGaming). Braucht flatpak-builder sowie
# org.freedesktop.Platform//<runtime-version> und org.freedesktop.Sdk//<runtime-version> (flatpak --user install flathub …).
set -euo pipefail

arch="${1:-x86_64}"
install=false
[ "${2:-}" = "--install" ] && install=true

root="$(cd "$(dirname "$0")/../.." && pwd)"
app_id=com.rebelsofgaming.RebelsOfGaming
version="$(sed -n 's:.*<RoGLinuxVersion>\(.*\)</RoGLinuxVersion>.*:\1:p' "$root/Directory.Build.props")"
tarball="$root/artifacts/dist/rebelsofgaming-$version-linux-$arch.tar.gz"
manifest="$root/packaging/flatpak/$app_id.yml"
out="$root/artifacts/flatpak"

if [ ! -f "$tarball" ]; then
  echo "Paket fehlt: $tarball – zuerst packaging/scripts/make-tarball.sh $arch" >&2
  exit 1
fi

sha="$(sha256sum "$tarball" | cut -d' ' -f1)"
mkdir -p "$out"

# Nur die Quelle der gewünschten Architektur ersetzen: url → path, Platzhalter-Prüfsumme → echte
python3 - "$manifest" "$out/$app_id.local.yml" "$arch" "$tarball" "$sha" <<'PY'
import re, sys
src, dst, arch, tarball, sha = sys.argv[1:]
text = open(src, encoding="utf-8").read()
pattern = re.compile(
    r"(only-arches: \[" + re.escape(arch) + r"\]\n\s+)url: \S+\n(\s+#[^\n]*\n)?(\s+)sha256: '0{64}'")
text, n = pattern.subn(lambda m: f"{m.group(1)}path: {tarball}\n{m.group(3)}sha256: '{sha}'", text)
if n != 1:
    sys.exit(f"Quelle für {arch} im Manifest nicht gefunden")
open(dst, "w", encoding="utf-8").write(text)
PY

args=(--user --force-clean --arch="$arch" --state-dir="$out/.flatpak-builder")
$install && args+=(--install)
flatpak-builder "${args[@]}" "$out/build-dir" "$out/$app_id.local.yml"
echo "Gebaut: $out/build-dir (Manifest $out/$app_id.local.yml)"
$install && echo "Starten: flatpak run $app_id"
