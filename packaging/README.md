# Paket und Flathub – Rebels of Gaming für Linux

Stand Stufe 10 (30.10.2026). **Nichts davon ist veröffentlicht oder bei Flathub eingereicht.** Einreichung, Uploads
nach `rog-public`/cdn.rogclan.de, Tags und Secrets nur auf Auftrag des Owners.

Grundlage: Owner-Entscheidungen F1 (Flathub mit Binärpaketen, Quelltext privat), F2 (App-ID
`com.rebelsofgaming.RebelsOfGaming`), F3 (Avalonia 12, natives Wayland) in `docs/LINUX-BAUPLAN.md`.

## Dateien

| Datei | Zweck |
|---|---|
| `linux/com.rebelsofgaming.RebelsOfGaming.desktop` | Starter: Übersetzungen DE/FR, `x-scheme-handler/rog`, `SingleMainWindow`, Schnellzugriffe Nachrichten/Benachrichtigungen/Server/Termine (`rog://…`) |
| `linux/com.rebelsofgaming.RebelsOfGaming.metainfo.xml` | AppStream (Software-Center, Flathub): Texte EN/DE/FR, OARS, Farben, Screenshots, Releases |
| `flatpak/com.rebelsofgaming.RebelsOfGaming.yml` | Flatpak-Manifest (freedesktop 26.08), bindet die Binärpakete von cdn.rogclan.de ein; jedes Recht mit Begründung |
| `screenshots/*.png` | Bildschirmfotos für AppStream, 1280×800, `<szene>-<de/en>.png` (noch **nicht** hochgeladen) |
| `scripts/make-tarball.sh` | Binärpaket je Architektur → `artifacts/dist/rebelsofgaming-<version>-linux-<arch>.tar.gz` + `.sha256` |
| `scripts/flatpak-local.sh` | Lokaler Flatpak-Probebau aus dem eigenen Paket (ohne Upload) |
| `scripts/release-manifest.sh` | Manifest einer Version mit echten URLs und Prüfsummen (für den Flathub-PR) |
| `../.github/workflows/release.yml` | Release-Pipeline (Entwurf): nur bei Tag `v*` auf `main` |

## Paket bauen

```bash
export PATH="$HOME/.dotnet:$PATH"
packaging/scripts/make-tarball.sh x86_64     # self-contained, linux-x64
packaging/scripts/make-tarball.sh aarch64    # Cross-Publish linux-arm64 – geht auf einem x86_64-Rechner
```

Inhalt: `rebelsofgaming-<version>-linux-<arch>/rebelsofgaming/` (Programm) und `…/share/` (Desktop-Datei, AppStream,
Symbol 512 px, Lizenz der Schrift) – genau das Layout, das das Manifest nach `/app` kopiert. Version aus
`Directory.Build.props` (`RoGLinuxVersion`). Zeitstempel, Reihenfolge und Besitzer im Archiv sind fest.

## Prüfen

```bash
desktop-file-validate packaging/linux/com.rebelsofgaming.RebelsOfGaming.desktop
appstreamcli validate --pedantic --explain packaging/linux/com.rebelsofgaming.RebelsOfGaming.metainfo.xml
flatpak-builder-lint manifest packaging/flatpak/com.rebelsofgaming.RebelsOfGaming.yml
flatpak-builder-lint appstream packaging/linux/com.rebelsofgaming.RebelsOfGaming.metainfo.xml
flatpak-builder-lint builddir artifacts/flatpak/build-dir     # nach dem Probebau
```

Erwartet (Stand 30.10.2026):

- `desktop-file-validate`: ohne Meldung.
- `appstreamcli validate --pedantic`: erfolgreich, nur der Hinweis `cid-contains-uppercase-letter` (die App-ID
  hat Großbuchstaben – ist mit F2 so entschieden und bei Flathub erlaubt).
- `flatpak-builder-lint manifest`: ohne Befund.
- `flatpak-builder-lint appstream`/`builddir`: nur `screenshot-image-not-found`/`appstream-missing-screenshots`
  (Bilder noch nicht hochgeladen).

Werkzeuge ohne root (so lief die Prüfung in Stufe 10): `apt-get download appstream libappstream5 libxmlb2
libstemmer0d appstream-compose libappstream-compose0 gir1.2-appstream-1.0 gir1.2-ostree-1.0 libostree-1-1
libavahi-glib1 flatpak libflatpak0 libmalcontent-0-0 flatpak-builder xdg-dbus-proxy`, je `dpkg-deb -x` in einen
Ordner, `PATH`/`LD_LIBRARY_PATH`/`GI_TYPELIB_PATH` darauf; `flatpak-builder-lint` per
`pip install --no-deps git+https://github.com/flathub-infra/flatpak-builder-lint.git` in ein venv mit
`--system-site-packages` (PyGObject vom System). Mit root einfacher: `apt install appstream flatpak flatpak-builder`
und `flatpak install flathub org.flatpak.Builder` (enthält `flatpak-builder-lint`).

## Lokal als Flatpak testen

```bash
flatpak --user remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak --user install flathub org.freedesktop.Platform//26.08 org.freedesktop.Sdk//26.08
packaging/scripts/make-tarball.sh x86_64
packaging/scripts/flatpak-local.sh x86_64 --install
flatpak run com.rebelsofgaming.RebelsOfGaming            # Wayland; erzwungen X11: … --x11
```

`flatpak-local.sh` ersetzt in einer Kopie des Manifests (`artifacts/flatpak/…local.yml`) die cdn-URL durch den Pfad
zum eigenen Paket und setzt die echte Prüfsumme; gebaut wird nach `artifacts/flatpak/build-dir`.

Probebau 30.10.2026 (x86_64, freedesktop 26.08, ohne root): Bau, `appstreamcli compose` und Installation ok; die App
startet unter Xvfb (X11-Rückfall, private D-Bus-Sitzung ohne Schlüsselbund) bis zum Anmeldebildschirm mit dem
Hinweis „Kein Schlüsselbund erreichbar“ – Schriften, ICU, Skia und Symbol laufen in der Laufzeitumgebung. Nicht
geprüft (braucht eine echte Sitzung): Wayland, Schlüsselbund, Portale (Benachrichtigung, Autostart, Dateiauswahl),
Tray, fprintd, Login im Browser. Bitte auf einem Desktop (gern Steam Deck) einmal durchklicken:

1. Anmelden im Browser → Rücksprung in die App, Token im Schlüsselbund (`seahorse`/KWallet: Einträge „Rebels of Gaming …“)
2. Benachrichtigung testen (Einstellungen), Autostart an/aus (Portal fragt einmal), Datei hochladen (Dateiauswahl)
3. Download in den Downloads-Ordner, „Im Ordner zeigen“
4. Tray-Symbol (KDE/Steam Deck), `rog://messages` aus dem Browser, Rechtsklick auf das Symbol → Schnellzugriffe
5. `flatpak run --env=ROG_DISPLAY=x11 com.rebelsofgaming.RebelsOfGaming` (X11-Rückfall)

## Screenshots erneuern

```bash
dotnet test RoG.Linux.sln --filter "FullyQualifiedName~StoreScreenshotTests"
for s in home messages servers calendar news; do for l in de en; do
  cp artifacts/screenshots/store_${s}_${l}_1280x800.png packaging/screenshots/$s-$l.png; done; done
```

Englisch ist Standard, Deutsch Sprachvariante (`xml:lang="de"`); Inhalte stammen aus dem Fake-Server der Tests
(deutsche Beispieldaten). Forum ist bewusst nicht dabei: Emoji-Reaktionen rendern headless als Kästchen.
Die Bilder müssen vor der Einreichung öffentlich unter
`https://cdn.rogclan.de/apps/linux/screenshots/<datei>` liegen (Platzhalter-URLs in der AppStream-Datei);
Flathub spiegelt sie beim Bau auf eigene Server – Besucher von Flathub laden sie also nicht von cdn.rogclan.de.

## Release-Pipeline (`.github/workflows/release.yml`)

Läuft **nur** bei einem Tag `vX.Y.Z` auf einem Commit, der in `main` liegt (Pushes auf develop/main und PRs lösen sie
nicht aus). Ablauf heute:

1. **Tag prüfen:** Commit liegt auf `main`; Tag = `RoGLinuxVersion`; die AppStream-Datei hat
   `<release version="X.Y.Z" date="…" type="stable">`.
2. **Paket x86_64 + aarch64:** Kern als öffentliches Submodul, Build + Tests (x86_64), `make-tarball.sh`; Tarball + `.sha256`
   als Workflow-Artefakt (90 Tage).
3. **Manifest:** Prüfsummen kontrollieren, `release-manifest.sh` → fertiges Flathub-Manifest als Artefakt.
4. **Deaktiviert (TODO):** Upload nach `rog-public/apps/linux/<version>/` (nie überschreiben, danach Prüfsumme über
   cdn.rogclan.de kontrollieren) und PR ins Flathub-Repo der App.

### Release-Pipeline freischalten (Owner)

- Zugang für `rog-public` **nur mit Schreibrecht unter `apps/linux/`** anlegen (SeaweedFS auf s3.wahke.lu) und als
  Secrets `ROG_PUBLIC_APPS_KEY_ID`/`ROG_PUBLIC_APPS_SECRET` ins Repo.
- Nach Annahme bei Flathub: Token mit Schreibrecht nur auf `flathub/com.rebelsofgaming.RebelsOfGaming` als
  `FLATHUB_APP_REPO_TOKEN`.
- Danach die beiden auskommentierten Schritte einkommentieren; `aws` muss auf den Runnern vorhanden sein (oder
  `rclone`/`s5cmd` – Schritt anpassen).

## Checkliste Flathub-Einreichung

- [x] **Lizenz** (`project_license`) entschieden: GPL-3.0-or-later (F10, siehe unten).
- [ ] **Domain-Nachweis:** Flathub gibt nach der Annahme im Entwickler-Portal einen Token aus; er muss unter
      `https://rebelsofgaming.com/.well-known/org.flathub.VerifiedApps.txt` stehen (Datei in der Website, eigene
      Route oder `public/.well-known/`). Erst dann erscheint die App als „verifiziert“.
- [ ] **Screenshots** hochladen nach `https://cdn.rogclan.de/apps/linux/screenshots/` (10 Dateien aus
      `packaging/screenshots/`), danach `flatpak-builder-lint appstream` ohne `screenshot-image-not-found`.
- [ ] **Release-Eintrag** in der AppStream-Datei: Version, Datum, `type="stable"` (oben einfügen).
- [ ] **Pakete** x86_64 + aarch64 über die Release-Pipeline bauen und nach `rog-public/apps/linux/<version>/`
      hochladen; Manifest aus dem Artefakt `flathub-manifest-<version>` nehmen.
- [ ] **Controller:** `<control>gamepad</control>` nur lassen, wenn die Controller-Steuerung (Stufe 10) fertig ist.
- [ ] **Probe auf echtem Desktop** (Liste oben unter „Lokal als Flatpak testen“), auch aarch64 falls Gerät vorhanden.
- [ ] **Einreichen:** Fork von `flathub/flathub`, Zweig von `new-pr`, nur das Manifest (+ ggf. `flathub.json`)
      hinzufügen, PR gegen `new-pr`; Prüfung durch Flathub (Rechte begründen: Kommentare im Manifest).
- [ ] Datenschutzerklärung der Website: Abschnitt zur Linux-App/Flathub ergänzen (Flathub ist ein eigener Anbieter,
      der Download läuft über Flathub, Updates ebenfalls).

## Lizenz (Owner-Entscheidung F10, 30.10.2026)

**GPL-3.0-or-later** (`LICENSE` im Repo-Wurzelverzeichnis, `project_license` in der AppStream-Datei). Der Quelltext wird
öffentlich; ab dann baut Flathub aus dem Quelltext statt aus Binärpaketen (F1 entfällt, ebenso der Upload-Zugang F11).
Vorher nötig: `RoG.Core` (Submodul aus `rebelsofgaming-windows`) ebenfalls öffentlich unter einer GPL-verträglichen
Lizenz, Git-Historie beider Repos auf Geheimnisse geprüft. **Name „Rebels of Gaming“, Faust-Logo und Grafiken sind
nicht Teil der Lizenz** (`TRADEMARKS.md`).

Schriftlizenz (Barlow Condensed, OFL) liegt im Paket unter `share/licenses/com.rebelsofgaming.RebelsOfGaming/`.

## Rechte im Flatpak (Kurzfassung)

Netz · Wayland + X11-Rückfall · IPC (nur X11) · DRI · `org.freedesktop.secrets` (Schlüsselbund) ·
`org.kde.StatusNotifierWatcher` (Tray) · System-Bus `net.reactivated.Fprint` (Fingerabdruck) · `xdg-download`.
Ohne Recht über Portale: Benachrichtigungen, Autostart, Dateiauswahl, Bildschirmfoto, Farbschema, Links.
Begründung je Zeile im Manifest.
