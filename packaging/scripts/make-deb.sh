#!/usr/bin/env bash
# Baut das .deb für Debian/Ubuntu/Mint/Pop!_OS aus dem Binärpaket von make-tarball.sh:
#
#   packaging/scripts/make-tarball.sh x86_64 && packaging/scripts/make-deb.sh x86_64    # → amd64
#   packaging/scripts/make-tarball.sh aarch64 && packaging/scripts/make-deb.sh aarch64  # → arm64
#
# Ergebnis: artifacts/dist/rebelsofgaming_<version>_<amd64|arm64>.deb (+ .sha256). Layout:
#   /opt/rebelsofgaming/                     self-contained .NET-App (Laufzeit, SkiaSharp, HarfBuzz)
#   /usr/bin/rebelsofgaming                  → /opt/rebelsofgaming/rebelsofgaming (Symlink)
#   /usr/share/applications|metainfo|icons   Desktop-Datei (rog://-Handler), AppStream, Symbol
#   /usr/share/doc/rebelsofgaming/           copyright (DEP-5), changelog.gz
#   /usr/share/man/man1/rebelsofgaming.1.gz
# Desktop-Datenbank und Symbol-Cache aktualisiert dpkg über die Trigger von desktop-file-utils/hicolor-icon-theme –
# keine Maintainer-Skripte. Abhängigkeiten: aus den NEEDED-Einträgen der .so und den zur Laufzeit geladenen
# Bibliotheken (ICU, OpenSSL, X11/Wayland, fontconfig) abgeleitet – Begründung in packaging/README.md.
set -euo pipefail

arch="${1:-x86_64}"
case "$arch" in
  x86_64) deb_arch=amd64 ;;
  aarch64) deb_arch=arm64 ;;
  *) echo "Architektur unbekannt: $arch (x86_64 oder aarch64)" >&2; exit 2 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
version="$(sed -n 's:.*<RoGLinuxVersion>\(.*\)</RoGLinuxVersion>.*:\1:p' "$root/Directory.Build.props")"
app_id=com.rebelsofgaming.RebelsOfGaming
dist="$root/artifacts/dist"
tarball="$dist/rebelsofgaming-$version-linux-$arch.tar.gz"
[ -f "$tarball" ] || { echo "Paket fehlt: $tarball – zuerst packaging/scripts/make-tarball.sh $arch" >&2; exit 1; }

work="$dist/deb-$deb_arch"
pkg="$work/pkg"
rm -rf "$work"
mkdir -p "$pkg"
tar -xzf "$tarball" -C "$work"
src="$work/rebelsofgaming-$version-linux-$arch"

# Programm nach /opt
mkdir -p "$pkg/opt" "$pkg/usr/bin"
cp -a "$src/rebelsofgaming" "$pkg/opt/rebelsofgaming"
# LTTng-Tracing der .NET-Laufzeit: optional (wird nur geladen, wenn vorhanden) und zöge liblttng-ust nach sich
rm -f "$pkg/opt/rebelsofgaming/libcoreclrtraceptprovider.so"
ln -s /opt/rebelsofgaming/rebelsofgaming "$pkg/usr/bin/rebelsofgaming"

# Desktop-Integration (gleiche Dateien wie im Flatpak)
install -Dm644 "$root/packaging/linux/$app_id.desktop" "$pkg/usr/share/applications/$app_id.desktop"
install -Dm644 "$root/packaging/linux/$app_id.metainfo.xml" "$pkg/usr/share/metainfo/$app_id.metainfo.xml"
install -Dm644 "$src/share/icons/hicolor/512x512/apps/$app_id.png" "$pkg/usr/share/icons/hicolor/512x512/apps/$app_id.png"
install -d "$pkg/usr/share/man/man1"
gzip -9n < "$root/packaging/linux/rebelsofgaming.1" > "$pkg/usr/share/man/man1/rebelsofgaming.1.gz"
install -Dm644 "$root/packaging/linux/rebelsofgaming.lintian-overrides" "$pkg/usr/share/lintian/overrides/rebelsofgaming"

# Dokumentation: copyright (DEP-5) + Debian-Changelog
doc="$pkg/usr/share/doc/rebelsofgaming"
install -d "$doc"
cat > "$doc/copyright" <<'EOF'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: rebelsofgaming
Upstream-Contact: Rebels of Gaming <info@wahke.lu>
Source: https://github.com/Rebels-of-Gaming/rebelsofgaming-linux
Comment: The name "Rebels of Gaming", the fist logo and graphics are not covered by the
 license (see TRADEMARKS.md in the source). The package bundles the .NET runtime
 (MIT), Avalonia (MIT), SkiaSharp/HarfBuzzSharp (MIT), Tmds.DBus.Protocol (MIT),
 CommunityToolkit.Mvvm (MIT) and AngleSharp (MIT).

Files: *
Copyright: 2020-2026 Rebels of Gaming, wahke (https://wahke.lu)
License: GPL-3.0-or-later
 This program is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.
 .
 On Debian systems, the full text of the GNU General Public License
 version 3 can be found in the file '/usr/share/common-licenses/GPL-3'.

Files: opt/rebelsofgaming/Licenses/OFL-BarlowCondensed.txt
Copyright: 2017 The Barlow Project Authors (https://github.com/jpt/barlow)
License: OFL-1.1
 This Font Software is licensed under the SIL Open Font License, Version 1.1,
 included in the file named above.
EOF
epoch="$(git -C "$root" log -1 --format=%ct)"
date_rfc="$(LC_ALL=C date -u -R -d "@$epoch")"
{
  echo "rebelsofgaming ($version) stable; urgency=medium"
  echo
  echo "  * Release $version - changes: CHANGELOG.md in the source repository"
  echo
  echo " -- Rebels of Gaming <info@wahke.lu>  $date_rfc"
} | gzip -9n > "$doc/changelog.gz"

# Rechte wie von Debian erwartet: Ordner 755, Dateien 644, nur Programme ausführbar
find "$pkg" -type d -exec chmod 755 {} +
find "$pkg" -type f -exec chmod 644 {} +
chmod 755 "$pkg/opt/rebelsofgaming/rebelsofgaming"
[ -f "$pkg/opt/rebelsofgaming/createdump" ] && chmod 755 "$pkg/opt/rebelsofgaming/createdump"

installed_size="$(du -sk --apparent-size "$pkg" | cut -f1)"
mkdir -p "$pkg/DEBIAN"
# ICU/OpenSSL lädt .NET zur Laufzeit (dlopen) – Paketnamen je Distribution (Debian 11–13, Ubuntu 20.04–26.04)
cat > "$pkg/DEBIAN/control" <<EOF
Package: rebelsofgaming
Version: $version
Architecture: $deb_arch
Maintainer: Rebels of Gaming <info@wahke.lu>
Installed-Size: $installed_size
Depends: libc6 (>= 2.27), libgcc-s1, libstdc++6, libssl3t64 | libssl3 | libssl1.1, libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70 | libicu67, libfontconfig1, libx11-6, libxext6, libxi6, libxrandr2, libxcursor1, libxfixes3, libice6, libsm6, libwayland-client0, libwayland-cursor0, libwayland-egl1, libxkbcommon0
Recommends: libegl1, libgl1, libgbm1, libdrm2, libdbus-1-3, xdg-desktop-portal, xdg-utils, gnome-keyring | kwalletmanager | keepassxc, libsdl3-0 | libsdl2-2.0-0, fonts-dejavu-core | fonts-noto-core
Suggests: fprintd
Section: net
Priority: optional
Homepage: https://rebelsofgaming.com/
Description: native app of the Rebels of Gaming clan platform
 The official app of the Rebels of Gaming, a multigaming clan since 2020:
 news, forum, wiki with offline reading, direct messages in real time,
 squads, members, calendar, matches, game servers with live status, gallery,
 downloads and uploads - natively on the Linux desktop and the Steam Deck,
 in German, English and French.
 .
 Sign-in happens in the browser; the app keeps only a device token in the
 system keyring. No tracking, no advertising.
EOF

out="$dist/rebelsofgaming_${version}_${deb_arch}.deb"
SOURCE_DATE_EPOCH="$epoch" dpkg-deb --root-owner-group -Zxz --build "$pkg" "$out" >/dev/null
(cd "$dist" && sha256sum "$(basename "$out")" > "$(basename "$out").sha256")
rm -rf "$work"
cat "$out.sha256"
