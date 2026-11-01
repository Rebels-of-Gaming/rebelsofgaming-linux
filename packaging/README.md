# Pakete und eigenes Flatpak-Repo – Rebels of Gaming für Linux

Stand 31.10.2026. **Owner-Entscheidung F15:** kein Flathub (Flathub nimmt keine KI-erstellten Manifeste/PRs an),
sondern

1. ein **eigenes, GPG-signiertes Flatpak-Repo** unter `https://cdn.rogclan.de/apps/linux/flatpak/`
   (Bucket `rog-public`, Präfix `apps/linux/`), aus dem Quelltext gebaut, x86_64 + aarch64, Branch `stable`;
2. ein **`.deb`** für Debian/Ubuntu/Mint/Pop!_OS (amd64 + arm64);
3. ein **Tarball** je Architektur (self-contained, zum Entpacken).

Grundlage außerdem F2 (App-ID `com.rebelsofgaming.RebelsOfGaming`), F3 (Avalonia 12, natives Wayland), F10
(GPL-3.0-or-later, Quelltext öffentlich) in `docs/LINUX-BAUPLAN.md`. **Veröffentlicht ist noch nichts** – Tags,
`main` und Releases nur durch den Owner.

## Für Nutzer: so wird installiert

**Flatpak (empfohlen, auch Steam Deck):**

- Ein Klick: `https://cdn.rogclan.de/apps/linux/com.rebelsofgaming.RebelsOfGaming.flatpakref` öffnen – GNOME Software,
  KDE Discover bzw. Discover im Desktopmodus des Steam Deck fragen nach und installieren. Die Laufzeit
  (freedesktop 26.08) kommt dabei von Flathub (`RuntimeRepo=`), die App aus unserem Repo; **Updates kommen automatisch**
  über das Repo (Software-Center bzw. `flatpak update`).
- Kommandozeile:
  ```bash
  flatpak install --user --from https://cdn.rogclan.de/apps/linux/com.rebelsofgaming.RebelsOfGaming.flatpakref
  flatpak run com.rebelsofgaming.RebelsOfGaming
  ```
- Nur das Repo hinzufügen (danach im Software-Center suchen):
  ```bash
  flatpak remote-add --user --if-not-exists rebelsofgaming https://cdn.rogclan.de/apps/linux/rebelsofgaming.flatpakrepo
  flatpak install --user rebelsofgaming com.rebelsofgaming.RebelsOfGaming
  ```
- Offline: `rebelsofgaming-<version>-<x86_64|aarch64>.flatpak` (GitHub-Release bzw. `…/apps/linux/<version>/`) mit
  `flatpak install --user ./rebelsofgaming-<version>-x86_64.flatpak` – legt das Repo für Updates gleich mit an.

**Debian/Ubuntu/Mint/Pop!_OS (.deb):**

```bash
wget https://cdn.rogclan.de/apps/linux/rebelsofgaming_amd64.deb      # immer die neueste Version (arm64: _arm64)
sudo apt install ./rebelsofgaming_amd64.deb
```

Versionierte Dateien mit Prüfsumme: `https://cdn.rogclan.de/apps/linux/<version>/` und am GitHub-Release; Übersicht
für die Website: `https://cdn.rogclan.de/apps/linux/latest.json`. Das `.deb` aktualisiert sich **nicht** selbst
(kein apt-Repo) – neue Version wieder herunterladen und installieren.

## Dateien

| Datei | Zweck |
|---|---|
| `flatpak/com.rebelsofgaming.RebelsOfGaming.yml` | Flatpak-Manifest: freedesktop 26.08 + `org.freedesktop.Sdk.Extension.dotnet10`, Bau **aus dem Quelltext ohne Netz**, Rechte mit Begründung je Zeile |
| `flatpak/nuget-sources.json` | Alle NuGet-Pakete (linux-x64 + linux-arm64, Laufzeit-/AppHost-/Crossgen2-Pakete) im Format von flatpak-dotnet-generator |
| `flatpak/rebelsofgaming.sh` | Starter `/app/bin/rebelsofgaming` im Flatpak |
| `flatpak/com.rebelsofgaming.RebelsOfGaming.flatpakref` | Ein-Klick-Installation (App + Repo + Schlüssel, `RuntimeRepo` = Flathub) |
| `flatpak/rebelsofgaming.flatpakrepo` | Repo hinzufügen (Software-Center/`flatpak remote-add`) |
| `flatpak/rebelsofgaming.gpg` / `.asc` | **Öffentlicher** Signierschlüssel des Repos (binär / ASCII); als `GPGKey=` (base64) in beiden Dateien oben |
| `linux/com.rebelsofgaming.RebelsOfGaming.desktop` | Starter: Übersetzungen DE/FR, `x-scheme-handler/rog`, `SingleMainWindow`, Schnellzugriffe (`rog://…`) |
| `linux/com.rebelsofgaming.RebelsOfGaming.metainfo.xml` | AppStream (Software-Center): Texte EN/DE/FR, OARS, Farben, Screenshots, Releases |
| `linux/rebelsofgaming.1` | Handbuchseite (im `.deb`) |
| `linux/rebelsofgaming.lintian-overrides` | Bewusste lintian-Abweichungen des `.deb` (siehe unten) |
| `screenshots/*.png` | Bildschirmfotos 1280×800 für AppStream, `<szene>-<de/en>.png` (lädt die Release-Pipeline hoch) |
| `scripts/make-tarball.sh` | Tarball je Architektur → `artifacts/dist/rebelsofgaming-<version>-linux-<arch>.tar.gz` + `.sha256` |
| `scripts/make-deb.sh` | `.deb` aus dem Tarball → `artifacts/dist/rebelsofgaming_<version>_<amd64/arm64>.deb` + `.sha256` |
| `scripts/flatpak-build.sh` | Flatpak einer Architektur in ein (unsigniertes) Repo bauen, optional installieren |
| `scripts/flatpak-repo.sh` | Builds ins Veröffentlichungs-Repo übernehmen, **signieren**, Summary/AppStream/statische Deltas erneuern |
| `scripts/update-nuget-sources.sh` | `nuget-sources.json` neu erzeugen (Restore im Flatpak-SDK) |
| `scripts/check-packaging.py` | CI-Prüfung: NuGet-Quellen vollständig, Schlüssel/URLs in `.flatpakref`/`.flatpakrepo` stimmen |
| `../.github/workflows/release.yml` | Release-Pipeline (Tag `v*` auf `main`, Probelauf per `workflow_dispatch`) |

## Aufbau in rog-public (ausgeliefert über cdn.rogclan.de)

```
apps/linux/
├── com.rebelsofgaming.RebelsOfGaming.flatpakref   Ein-Klick-Installation        Cache 60 s
├── rebelsofgaming.flatpakrepo                     Repo hinzufügen               Cache 60 s
├── rebelsofgaming.gpg                             öffentlicher Repo-Schlüssel   Cache 60 s
├── latest.json                                    Version, URLs, SHA-256, Größe Cache 60 s
├── rebelsofgaming_amd64.deb / _arm64.deb          neueste .deb (fester Name)    Cache 60 s
├── icon-512.png, screenshots/*.png                Symbol + AppStream-Bilder     Cache 1 Tag
├── <version>/                                     .deb, Tarballs, .flatpak, *.sha256   unveränderlich
└── flatpak/                                       OSTree-Repo (archive-z2)
    ├── objects/, deltas/                          nach Prüfsumme benannt        Cache 1 Jahr, immutable
    └── summary(.sig), summary.idx(.sig), summaries/, refs/, delta-indexes/, config      Cache 60 s
```

Content-Types: Repo-Dateien `application/octet-stream`, `.flatpakref` `application/vnd.flatpak.ref`, `.flatpakrepo`
`application/vnd.flatpak.repo` (damit Browser sie an das Software-Center geben), Bundles `application/vnd.flatpak`, `.deb`
`application/vnd.debian.binary-package`. Hochgeladen wird in der Reihenfolge Objekte → Deltas → Refs → Summary, gelöscht
(Prune, die letzten 3 Commits je Ref bleiben) erst danach – kein Client sieht eine Summary mit fehlenden Objekten.

## Wie ein Release abläuft (`.github/workflows/release.yml`)

Auslöser: Tag `vX.Y.Z` auf einem Commit, der in `main` liegt (nur der Owner). `workflow_dispatch` (Actions → Release →
Run workflow) ist ein **Probelauf**: alles wird gebaut und geprüft, aber nichts hochgeladen und kein Release angelegt.

1. **Version prüfen:** Commit liegt auf `main`; Tag = `RoGLinuxVersion` (`Directory.Build.props`); die AppStream-Datei
   hat `<release version="X.Y.Z" … type="stable">`.
2. **Tarball + .deb** (x86_64 und aarch64 per Cross-Publish auf `ubuntu-24.04`): Build + alle Tests (x86_64),
   `make-tarball.sh`, `make-deb.sh`; für amd64 zusätzlich `lintian --fail-on error,warning`, Installation mit `apt` ohne
   Recommends und Start unter Xvfb.
3. **Flatpak** je Architektur **nativ** (`ubuntu-24.04` bzw. `ubuntu-24.04-arm`, für öffentliche Repos kostenlos):
   `flatpak-builder` aus dem Quelltext, NuGet ohne Netz aus `nuget-sources.json`, Repo als Artefakt.
4. **Veröffentlichen:** privaten Signierschlüssel aus dem Secret laden (muss zu `rebelsofgaming.gpg` passen), mit
   S3-Zugang das bestehende Repo aus `rog-public` laden (neue Commits hängen am Vorgänger → Deltas), beide Architekturen
   übernehmen und signieren (`flatpak-repo.sh`: `build-commit-from --gpg-sign`, `build-update-repo
   --generate-static-deltas --gpg-sign`), **aus dem signierten Repo über eine `.flatpakref` installieren und starten**
   (prüft Signatur und Schlüssel Ende-zu-Ende), je Architektur ein **Flatpak-Bundle** (`flatpak build-bundle` mit
   Schlüssel, `--repo-url` auf das eigene Repo und Flathub als Laufzeit-Repo – Offline-Installation, Updates danach
   über das Repo), `latest.json` erzeugen, alles als Artefakt (90 Tage).
5. **Upload** nach `rog-public/apps/linux/` (siehe oben), danach Prüfsummen über cdn.rogclan.de und
   `flatpak remote-ls` gegen das echte Repo.
6. **GitHub-Release** mit `.deb` (amd64/arm64), Tarballs, Flatpak-Bundles (`.flatpak`, x86_64/aarch64), `.sha256`,
   `.flatpakref`, `.flatpakrepo`.

Fehlen die S3-Secrets, überspringt die Pipeline Schritt 5 mit Hinweis und legt das GitHub-Release nur als **Entwurf** an.

### Checkliste vor einem Tag

- [ ] `RoGLinuxVersion` in `Directory.Build.props` erhöht
- [ ] AppStream: neuer `<release version="X.Y.Z" date="<Tag-Datum>" type="stable">` **oben** (für 0.1.0 vorbereitet,
      Datum 2026-10-31 – am Release-Tag anpassen)
- [ ] `CHANGELOG.md`: Abschnitt `[X.Y.Z] – Datum` statt `[Unveröffentlicht]`
- [ ] Pakete geändert? → `packaging/scripts/update-nuget-sources.sh` (CI prüft es)
- [ ] Probelauf `workflow_dispatch` auf `develop` grün, dann Fast-Forward `main` + Tag (Owner)

## Owner-Schritte (einmalig)

1. **S3-Zugang nur für `rog-public/apps/linux/`** in SeaweedFS anlegen (eigene Identität statt Admin-Schlüssel), in
   `weed shell` auf dem SeaweedFS-Server:
   ```
   s3.configure -user=rog-linux-ci -access_key=<neu> -secret_key=<neu> \
     -actions=Read:rog-public/apps/linux/*,Write:rog-public/apps/linux/*,Tagging:rog-public/apps/linux/*,List:rog-public
   ```
   Ohne `-apply` zeigt der Befehl nur die geplante Konfiguration – prüfen, dann mit `-apply` wiederholen.
   SeaweedFS prüft Objektrechte als `Aktion:bucket/schlüssel` und erlaubt `*` am Ende → Lesen/Schreiben/Löschen/Tags
   nur unter `apps/linux/`. `List` lässt sich nur je Bucket vergeben (`List:rog-public`, zeigt nur Dateinamen im ohnehin
   öffentlichen Bucket). Alternative bei älteren SeaweedFS-Versionen: `-buckets=rog-public -actions=Read,Write,List,Tagging`
   (dann ganzer Bucket). Kurz testen, z. B. mit
   `aws --endpoint-url https://s3.wahke.lu s3 cp test.txt s3://rog-public/apps/linux/test.txt` (geht) und
   `… s3://rog-public/test.txt` (muss `AccessDenied` liefern).
   Danach im Repo unter *Settings → Secrets and variables → Actions* die Secrets **`ROG_PUBLIC_APPS_KEY_ID`** (Access
   Key) und **`ROG_PUBLIC_APPS_SECRET`** (Secret Key) setzen (`gh secret set ROG_PUBLIC_APPS_KEY_ID -R
   Rebels-of-Gaming/rebelsofgaming-linux`). Ein Probelauf (`workflow_dispatch`) prüft den Zugang dann lesend.
   cdn.rogclan.de muss `apps/linux/` wie die übrigen Dateien aus `rog-public` ausliefern.
2. **Signierschlüssel** – erledigt (31.10.2026): Secret `ROG_FLATPAK_GPG_PRIVATE_KEY`, Variable
   `ROG_FLATPAK_GPG_KEY_ID` = `027092E94A7C7F091DAE3E5C285E5FD5A7C11AEC` (RSA 4096, nur Signieren, ohne Ablauf,
   „Rebels of Gaming Flatpak Repository &lt;info@wahke.lu&gt;“). Der private Schlüssel existiert **nur** als GitHub-Secret
   (lokal sicher gelöscht, keine Sicherung). Geht er verloren: neuen Schlüssel erzeugen, `rebelsofgaming.gpg`/`.asc` und
   `GPGKey=` ersetzen; bestehende Installationen müssen dann einmal die neue `.flatpakrepo`/`.flatpakref` öffnen bzw.
   `flatpak remote-modify --gpg-import=rebelsofgaming.gpg rebelsofgaming` ausführen.
3. Website: Installationsseite mit `.flatpakref`-Link und `.deb` (Daten aus `latest.json`), Linux-Feld im Store-Block,
   Datenschutzerklärung (Downloads/Updates von cdn.rogclan.de, Laufzeit von Flathub).

## Lokal bauen und prüfen

```bash
export PATH="$HOME/.dotnet:$PATH"
# Tarball + .deb (beide Architekturen gehen auf x86_64)
packaging/scripts/make-tarball.sh x86_64 && packaging/scripts/make-deb.sh x86_64
dpkg-deb -I artifacts/dist/rebelsofgaming_0.1.0_amd64.deb; dpkg-deb -c artifacts/dist/rebelsofgaming_0.1.0_amd64.deb
lintian --pedantic artifacts/dist/rebelsofgaming_0.1.0_amd64.deb

# Flatpak aus dem Quelltext (nur versionierte Dateien!) in ein lokales Repo, signieren, über .flatpakref installieren
flatpak --user remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak --user install flathub org.freedesktop.Platform//26.08 org.freedesktop.Sdk//26.08 org.freedesktop.Sdk.Extension.dotnet10//26.08
packaging/scripts/flatpak-build.sh x86_64                                   # → artifacts/flatpak/repo-x86_64
GNUPGHOME=<test-schlüssel> packaging/scripts/flatpak-repo.sh artifacts/flatpak/repo <key-id> artifacts/flatpak/repo-x86_64
sed "s#^Url=.*#Url=file://$PWD/artifacts/flatpak/repo#" packaging/flatpak/com.rebelsofgaming.RebelsOfGaming.flatpakref > /tmp/local.flatpakref
flatpak --user install /tmp/local.flatpakref     # mit Test-Schlüssel zusätzlich GPGKey= ersetzen
flatpak run com.rebelsofgaming.RebelsOfGaming

# Paketprüfungen wie in der CI
python3 -I packaging/scripts/check-packaging.py
desktop-file-validate packaging/linux/com.rebelsofgaming.RebelsOfGaming.desktop
appstreamcli validate --pedantic --explain packaging/linux/com.rebelsofgaming.RebelsOfGaming.metainfo.xml
```

Nach Änderungen an Paketen (`Directory.Packages.props`, `*.csproj`, Kern-Bump): `packaging/scripts/update-nuget-sources.sh`
(Restore im Flatpak-SDK, damit Laufzeit-Pakete zur .NET-Version der Erweiterung passen; zusätzlich Crossgen2 für beide
Host-Architekturen, sonst scheitert ReadyToRun auf dem aarch64-Runner).

Werkzeuge ohne root (so lief die Prüfung 30./31.10.2026): `apt-get download appstream libappstream5 libxmlb2
libstemmer0d appstream-compose libappstream-compose0 gir1.2-appstream-1.0 gir1.2-ostree-1.0 libostree-1-1
libavahi-glib1 flatpak libflatpak0 libmalcontent-0-0 flatpak-builder xdg-dbus-proxy ostree`, je `dpkg-deb -x` in einen
Ordner, `PATH`/`LD_LIBRARY_PATH`/`GI_TYPELIB_PATH` darauf, `FLATPAK_USER_DIR` auf einen eigenen Ordner; lintian im
Container (`docker run debian:trixie`, `apt install lintian`).

### Prüfergebnisse (31.10.2026, x86_64)

- **Flatpak aus dem Quelltext:** Bau ohne Netz ok (NuGet aus `nuget-sources.json`, 36 Pakete, .NET 10.0.12 der
  Erweiterung), Repo mit dem echten Schlüssel signiert (Summary + Commits, statische Deltas, `appstream`/`appstream2`),
  **Installation über eine lokale `.flatpakref`** (Remote `rebelsofgaming`, `gpg-verify=true`,
  `gpg-verify-summary=true`) ok, App startet unter Xvfb bis zum Anmeldebildschirm. Download 63 MB, installiert 156 MB.
- **`.deb` amd64:** 45,9 MB (arm64 40,5 MB). lintian (Debian 13, `--pedantic`): ohne Befund außer den bewusst
  übersteuerten Tags; `apt install` ohne Recommends und Start unter Xvfb ok auf **Debian 12, Ubuntu 22.04 und 24.04**;
  Installation auf Debian 13 ok.
- `desktop-file-validate` ohne Meldung; `appstreamcli validate --pedantic` erfolgreich, nur `cid-contains-uppercase-letter`
  (App-ID mit Großbuchstaben, F2).
- aarch64-Flatpak lokal nicht gebaut (kein arm64-Rechner) – baut nativ auf `ubuntu-24.04-arm` in der Pipeline.

Nicht geprüft (braucht eine echte Sitzung): Wayland, Schlüsselbund, Portale (Benachrichtigung, Autostart,
Dateiauswahl), Tray, fprintd, Login im Browser, Ein-Klick-Installation im Software-Center. Bitte auf einem Desktop (gern
Steam Deck) einmal durchklicken: Anmelden im Browser → Token im Schlüsselbund; Benachrichtigung testen, Autostart an/aus,
Datei hochladen; Download + „Im Ordner zeigen“; Tray und `rog://messages` aus dem Browser; X11-Rückfall mit
`flatpak run --env=ROG_DISPLAY=x11 com.rebelsofgaming.RebelsOfGaming`.

## `.deb` – Abhängigkeiten und lintian

- **Depends** aus den `NEEDED`-Einträgen der mitgelieferten `.so` und den zur Laufzeit geladenen Bibliotheken:
  `libc6 (>= 2.27)` (höchste Symbolversion), `libgcc-s1`, `libstdc++6`, OpenSSL (`libssl3t64 | libssl3 | libssl1.1`)
  und ICU (`libicu78 | … | libicu67`) – beide lädt .NET per `dlopen`, Paketnamen je Distribution –, `libfontconfig1`
  (SkiaSharp), X11-Rückfall (`libx11-6`, `libxext6`, `libxi6`, `libxrandr2`, `libxcursor1`, `libxfixes3`, `libice6`,
  `libsm6`) und Wayland (`libwayland-client0`, `libwayland-cursor0`, `libwayland-egl1`, `libxkbcommon0`).
- **Recommends:** GPU (`libegl1`, `libgl1`, `libgbm1`, `libdrm2` – ohne zeichnet Skia in Software), `libdbus-1-3`,
  Portale/`xdg-utils`, ein Schlüsselbund (`gnome-keyring | kwalletmanager | keepassxc`), SDL für Controller
  (`libsdl3-0 | libsdl2-2.0-0`), eine Schrift. **Suggests:** `fprintd`. Kein libsecret nötig (Secret Service direkt
  über D-Bus).
- `libcoreclrtraceptprovider.so` (LTTng-Tracing, optional) fehlt im `.deb` – sonst hinge es an `liblttng-ust`.
- **lintian-Overrides** (`linux/rebelsofgaming.lintian-overrides`, im Paket unter `/usr/share/lintian/overrides/`):
  `dir-or-file-in-opt` (Vorgabe `/opt/rebelsofgaming`), `embedded-library` (FreeType/libjpeg/libpng in
  `libSkiaSharp.so`, zlib-ng in .NET), `unstripped-binary-or-object` (fertige NuGet-Bibliotheken unverändert).
- Keine Maintainer-Skripte: Desktop-Datenbank und Symbol-Cache aktualisiert dpkg über Trigger.
- Neue Distributionsversion mit neuer ICU (z. B. `libicu80`)? → in `make-deb.sh` vorn ergänzen.

## Lizenz (Owner-Entscheidung F10, 30.10.2026)

**GPL-3.0-or-later** (`LICENSE`, `project_license` in der AppStream-Datei, DEP-5-`copyright` im `.deb`). **Name „Rebels
of Gaming“, Faust-Logo und Grafiken sind nicht Teil der Lizenz** (`TRADEMARKS.md`). Schriftlizenz (Barlow Condensed,
OFL) liegt im Flatpak unter `/app/share/licenses/com.rebelsofgaming.RebelsOfGaming/`, im `.deb` unter
`/opt/rebelsofgaming/Licenses/`.

## Rechte im Flatpak (Kurzfassung)

Netz · Wayland + X11-Rückfall · IPC (nur X11) · DRI · `/dev/input` (Controller) · `org.freedesktop.secrets`
(Schlüsselbund) · `org.kde.StatusNotifierWatcher` (Tray) · System-Bus `net.reactivated.Fprint` (Fingerabdruck) ·
`xdg-download`. Ohne Recht über Portale: Benachrichtigungen, Autostart, Dateiauswahl, Bildschirmfoto, Farbschema,
Links. Begründung je Zeile im Manifest.

## Screenshots erneuern

```bash
dotnet test RoG.Linux.sln --filter "FullyQualifiedName~StoreScreenshotTests"
for s in home messages servers calendar news; do for l in de en; do
  cp artifacts/screenshots/store_${s}_${l}_1280x800.png packaging/screenshots/$s-$l.png; done; done
```

Englisch ist Standard, Deutsch Sprachvariante (`xml:lang="de"`); Inhalte stammen aus dem Fake-Server der Tests.
Die Release-Pipeline lädt sie nach `https://cdn.rogclan.de/apps/linux/screenshots/`; Software-Center laden sie von dort.
