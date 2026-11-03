#!/usr/bin/env bash
# Signiert das App-Bundle (Hardened Runtime) und baut auf Wunsch das Installationspaket für den Mac App Store.
#
#   packaging/macos/sign-app.sh adhoc  <App>                    # CI-Rauchtest/lokal: Ad-hoc, Sandbox ohne Profil
#   packaging/macos/sign-app.sh store  <App> <Profil> <pkg>     # Mac App Store: „Apple Distribution“ + Profil,
#                                                               # .pkg mit „Mac Installer Distribution“
# Für „store“ müssen die Zertifikate im Schlüsselbund liegen (Workflow: temporärer Schlüsselbund), die Namen kommen aus
# MAC_APP_IDENTITY (Standard „Apple Distribution“) und MAC_INSTALLER_IDENTITY (Standard
# „3rd Party Mac Developer Installer“ – so heißt „Mac Installer Distribution“ im Schlüsselbund).
set -euo pipefail

mode="${1:?adhoc oder store}"
app="${2:?Pfad zum .app-Bundle}"
here="$(cd "$(dirname "$0")" && pwd)"
exe="$app/Contents/MacOS/rebelsofgaming"

case "$mode" in
  adhoc)
    identity="-"
    entitlements="$here/RebelsOfGaming.dev.entitlements"
    ;;
  store)
    profile="${3:?Pfad zum Mac-App-Store-Provisioning-Profil}"
    pkg="${4:?Ziel des .pkg}"
    identity="${MAC_APP_IDENTITY:-Apple Distribution}"
    installer="${MAC_INSTALLER_IDENTITY:-3rd Party Mac Developer Installer}"
    entitlements="$here/RebelsOfGaming.entitlements"
    cp "$profile" "$app/Contents/embedded.provisionprofile"
    ;;
  *) echo "Modus unbekannt: $mode" >&2; exit 2 ;;
esac

# Von innen nach außen: erst jede native Bibliothek, dann das Programm (mit Rechten), zuletzt das Bundle
while IFS= read -r -d '' file; do
  if [ "$file" != "$exe" ] && file -b "$file" | grep -q 'Mach-O'; then
    codesign --force --options runtime --sign "$identity" "$file"
  fi
done < <(find "$app/Contents" -type f -print0)
codesign --force --options runtime --entitlements "$entitlements" --sign "$identity" "$exe"
codesign --force --options runtime --entitlements "$entitlements" --sign "$identity" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
codesign --display --entitlements - "$app" >/dev/null

if [ "$mode" = store ]; then
  productbuild --component "$app" /Applications --sign "$installer" "$pkg"
  pkgutil --check-signature "$pkg"
  echo "Paket: $pkg"
fi
