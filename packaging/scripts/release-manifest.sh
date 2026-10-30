#!/usr/bin/env bash
# Erzeugt das Flathub-Manifest für eine Version aus dem Entwurf: URLs unter cdn.rogclan.de/apps/linux/<version>/ und
# die echten Prüfsummen aus den *.tar.gz.sha256 von make-tarball.sh (Owner-Entscheidung F1).
#
#   packaging/scripts/release-manifest.sh <version> <ordner mit *.tar.gz.sha256> [ziel.yml]
#
# Ergebnis (Standard: artifacts/release/<App-ID>.yml) ist genau die Datei, die später per PR ins Flathub-Repo der App
# geht. Fehlt die Prüfsumme einer Architektur, bricht das Skript ab – halbe Releases gibt es nicht.
set -euo pipefail

version="${1:?Version fehlt (z. B. 0.2.0)}"
dist="${2:?Ordner mit den .sha256-Dateien fehlt}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
app_id=com.rebelsofgaming.RebelsOfGaming
target="${3:-$root/artifacts/release/$app_id.yml}"
mkdir -p "$(dirname "$target")"

python3 - "$root/packaging/flatpak/$app_id.yml" "$target" "$version" "$dist" <<'PY'
import pathlib, re, sys
src, dst, version, dist = sys.argv[1:]
text = pathlib.Path(src).read_text(encoding="utf-8")
for arch in ("x86_64", "aarch64"):
    name = f"rebelsofgaming-{version}-linux-{arch}.tar.gz"
    sha_file = pathlib.Path(dist) / f"{name}.sha256"
    if not sha_file.is_file():
        sys.exit(f"Prüfsumme fehlt: {sha_file}")
    sha = sha_file.read_text(encoding="utf-8").split()[0]
    if not re.fullmatch(r"[0-9a-f]{64}", sha):
        sys.exit(f"Prüfsumme ungültig: {sha_file}")
    pattern = re.compile(
        r"(only-arches: \[" + arch + r"\]\n(\s+))url: \S+\n(?:\s+#[^\n]*\n)?\s+sha256: '[0-9a-f]{64}'")
    url = f"https://cdn.rogclan.de/apps/linux/{version}/{name}"
    text, n = pattern.subn(lambda m: f"{m.group(1)}url: {url}\n{m.group(2)}sha256: '{sha}'", text)
    if n != 1:
        sys.exit(f"Quelle für {arch} im Manifest nicht gefunden")
pathlib.Path(dst).write_text(text, encoding="utf-8")
PY
echo "Manifest: $target"
