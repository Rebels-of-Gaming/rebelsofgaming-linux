#!/usr/bin/env bash
# Baut das Flatpak aus dem Quelltext in ein (unsigniertes) OSTree-Repo – Branch „stable“, eine Architektur:
#
#   packaging/scripts/flatpak-build.sh [arch] [repo-ordner] [--install]
#     arch         x86_64 (Standard) oder aarch64 – nur nativ (aarch64 auf einem aarch64-Rechner/-Runner)
#     repo-ordner  Standard artifacts/flatpak/repo-<arch>
#     --install    zusätzlich für den Benutzer installieren (flatpak run com.rebelsofgaming.RebelsOfGaming)
#
# Gebaut wird aus einem Abzug der VERSIONIERTEN Dateien (git ls-files, inkl. Submodul) unter artifacts/flatpak/src –
# lokale bin/, obj/ und artifacts/ gelangen nie ins Paket. Unversionierte neue Dateien fehlen also (erst „git add“).
# Signiert wird erst beim Zusammenführen ins Veröffentlichungs-Repo (packaging/scripts/flatpak-repo.sh).
# Braucht flatpak-builder sowie org.freedesktop.Sdk//26.08 + org.freedesktop.Sdk.Extension.dotnet10//26.08 +
# org.freedesktop.Platform//26.08 (ROG_FLATPAK_DEPS_FROM=flathub installiert fehlende selbst).
set -euo pipefail

arch="${1:-x86_64}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
repo="${2:-$root/artifacts/flatpak/repo-$arch}"
install=false
[ "${3:-}" = "--install" ] && install=true
app_id=com.rebelsofgaming.RebelsOfGaming
out="$root/artifacts/flatpak"
stage="$out/src"

case "$arch" in x86_64|aarch64) ;; *) echo "Architektur unbekannt: $arch" >&2; exit 2 ;; esac
host="$(uname -m)"
if [ "$arch" != "$host" ]; then
  echo "Hinweis: $arch auf $host braucht qemu-user (binfmt) – in der CI baut ein nativer $arch-Runner." >&2
fi

rm -rf "$stage"
mkdir -p "$stage"
git -C "$root" ls-files -z --recurse-submodules | (cd "$root" && tar --null -T - -cf -) | tar -xf - -C "$stage"

args=(--user --force-clean --disable-rofiles-fuse --arch="$arch" --default-branch=stable
      --state-dir="$out/.flatpak-builder" --repo="$repo")
[ -n "${ROG_FLATPAK_DEPS_FROM:-}" ] && args+=(--install-deps-from="$ROG_FLATPAK_DEPS_FROM")
$install && args+=(--install)
flatpak-builder "${args[@]}" "$out/build-dir-$arch" "$stage/packaging/flatpak/$app_id.yml"
echo "Repo: $repo (app/$app_id/$arch/stable)"
$install && echo "Starten: flatpak run $app_id"
exit 0
