#!/usr/bin/env bash
# Führt die Builds je Architektur im Veröffentlichungs-Repo zusammen, signiert sie und erneuert Summary,
# AppStream-Zweige und statische Deltas – das Ergebnis wird 1:1 nach rog-public/apps/linux/flatpak/ gespiegelt.
#
#   packaging/scripts/flatpak-repo.sh <ziel-repo> <gpg-schlüssel-id> <quell-repo>...
#
# GNUPGHOME muss den privaten Signierschlüssel enthalten (CI: Secret ROG_FLATPAK_GPG_PRIVATE_KEY, nie auf der Platte
# des Owners). Existiert das Ziel-Repo (vorher aus S3 geladen), werden die neuen Commits als Nachfolger angehängt –
# Clients bekommen dann Deltas vom Vorgänger; sonst wird es neu angelegt. Ältere Commits als die letzten
# ROG_FLATPAK_PRUNE_DEPTH (Standard 3) je Ref werden entfernt. ROG_FLATPAK_PUBLIC_KEY: öffentlicher Schlüssel, der
# ins Repo eingetragen wird (Standard packaging/flatpak/rebelsofgaming.gpg; Probelauf: Wegwerf-Schlüssel).
set -euo pipefail

dest="${1:?Ziel-Repo fehlt}"
key="${2:?GPG-Schlüssel-ID fehlt}"
shift 2
[ "$#" -ge 1 ] || { echo "Mindestens ein Quell-Repo angeben" >&2; exit 2; }
root="$(cd "$(dirname "$0")/../.." && pwd)"
app_id=com.rebelsofgaming.RebelsOfGaming
gpg_home="${GNUPGHOME:-$HOME/.gnupg}"

if [ ! -f "$dest/config" ]; then
  ostree init --mode=archive-z2 --repo="$dest"
fi

for src in "$@"; do
  found=false
  for ref_file in "$src"/refs/heads/app/"$app_id"/*/stable; do
    [ -f "$ref_file" ] || continue
    ref="${ref_file#"$src"/refs/heads/}"
    echo "Übernehme $ref aus $src"
    flatpak build-commit-from --src-repo="$src" --gpg-sign="$key" --gpg-homedir="$gpg_home" --no-update-summary \
      "$dest" "$ref"
    found=true
  done
  $found || { echo "Kein app/$app_id/*/stable in $src" >&2; exit 1; }
done

flatpak build-update-repo --gpg-sign="$key" --gpg-homedir="$gpg_home" \
  --title="Rebels of Gaming" --comment="Apps of the Rebels of Gaming clan" \
  --homepage=https://rebelsofgaming.com/ --icon=https://cdn.rogclan.de/apps/linux/icon-512.png \
  --default-branch=stable --gpg-import="${ROG_FLATPAK_PUBLIC_KEY:-$root/packaging/flatpak/rebelsofgaming.gpg}" \
  --generate-static-deltas --prune --prune-depth="${ROG_FLATPAK_PRUNE_DEPTH:-3}" "$dest"
echo "Repo fertig: $dest"
