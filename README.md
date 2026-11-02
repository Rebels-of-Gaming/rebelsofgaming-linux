# Rebels of Gaming für Linux

Native Linux-App (Avalonia UI, .NET 10) für die Clan-Plattform der **Rebels of Gaming** – gleiche Logik wie die
Windows-App, eigene Oberfläche im RoG-Design, **keine WebView**. Ziel: Linux-Desktop und Steam Deck, Verteilung über
ein **eigenes Flatpak-Repo** (`cdn.rogclan.de`, Ein-Klick-Installation per `.flatpakref`) und ein **`.deb`** – siehe
[`packaging/README.md`](packaging/README.md).

- Arbeitsregeln: [`CLAUDE.md`](CLAUDE.md)
- Stand, Stufen, Annahmen, offene Fragen: [`docs/LINUX-BAUPLAN.md`](docs/LINUX-BAUPLAN.md)
- Funktionsumfang: wie die Windows-App (`external/rog-windows/docs/WINDOWS-PFLICHTENHEFT.md`)

## Installation

> Noch nicht veröffentlicht – die Adressen gelten ab dem ersten Release (`v0.1.0`).

**Flatpak (empfohlen, auch Steam Deck)** – eigenes, GPG-signiertes Repo, Updates kommen automatisch:

- Ein Klick: [`com.rebelsofgaming.RebelsOfGaming.flatpakref`](https://cdn.rogclan.de/apps/linux/com.rebelsofgaming.RebelsOfGaming.flatpakref)
  öffnen (GNOME Software, KDE Discover, Discover im Desktopmodus des Steam Deck). Die Laufzeit (freedesktop 26.08)
  kommt von Flathub, die App aus unserem Repo.
- Kommandozeile:
  ```bash
  flatpak install --user --from https://cdn.rogclan.de/apps/linux/com.rebelsofgaming.RebelsOfGaming.flatpakref
  # oder erst das Repo hinzufügen, dann installieren
  flatpak remote-add --user --if-not-exists rebelsofgaming https://cdn.rogclan.de/apps/linux/rebelsofgaming.flatpakrepo
  flatpak install --user rebelsofgaming com.rebelsofgaming.RebelsOfGaming
  ```
- Offline-Datei: `rebelsofgaming-<version>-<x86_64|aarch64>.flatpak` am GitHub-Release
  (`flatpak install --user ./rebelsofgaming-….flatpak` – richtet das Repo für Updates mit ein).

**Debian, Ubuntu, Mint, Pop!_OS (`.deb`, amd64/arm64)** – Programm unter `/opt/rebelsofgaming`, Befehl `rebelsofgaming`:

```bash
wget https://cdn.rogclan.de/apps/linux/rebelsofgaming_amd64.deb     # arm64: rebelsofgaming_arm64.deb
sudo apt install ./rebelsofgaming_amd64.deb
```

Das `.deb` aktualisiert sich nicht selbst (kein apt-Repo) – neue Versionen wieder herunterladen.

**Tarball (andere Distributionen)** – self-contained, ohne Installation:

```bash
tar -xzf rebelsofgaming-<version>-linux-x86_64.tar.gz
rebelsofgaming-<version>-linux-x86_64/rebelsofgaming/rebelsofgaming
```

Versionierte Dateien mit SHA-256: `https://cdn.rogclan.de/apps/linux/<version>/` und am
[GitHub-Release](https://github.com/Rebels-of-Gaming/rebelsofgaming-linux/releases). Repo-Schlüssel:
`0270 92E9 4A7C 7F09 1DAE 3E5C 285E 5FD5 A7C1 1AEC` (`packaging/flatpak/rebelsofgaming.asc`). Details und
Bau-Anleitung: [`packaging/README.md`](packaging/README.md).

## Aufbau

| Projekt | Inhalt |
|---|---|
| `external/rog-windows` | Git-Submodul des Windows-Repos (fester Commit). Daraus wird **`src/RoG.Core`** gebaut (API-Client, Pipeline, Login, Echtzeit, Parser) – unverändert, nicht kopiert. Außerdem Übersetzungen (`.resw`), Schrift und Logo. |
| `src/RoG.Linux.Platform` | Linux-Verdrahtung ohne Oberfläche: Token und Cache-Schlüssel im Schlüsselbund (Secret Service über D-Bus), Desktop-Benachrichtigungen (Dienst bzw. Portal), Tray (StatusNotifierItem), Autostart (XDG bzw. Portal), Single-Instance, Dateimanager, `xdg-open`, XDG-Ordner, Gerätename, Übersetzungen |
| `src/RoG.Linux.App` | Avalonia-12-Oberfläche (MVVM mit CommunityToolkit.Mvvm): Shell, Start, News, Nachrichten (+ Chat-Fenster), Forum, Squads, Mitglieder, Termine, Server (Minecraft, TeamSpeak, Discord), Matches + Stats, Bestenlisten, Freunde, Mitgliederkarte, Votes, Server-Steuerung (Konsole, Configs, Dateien, Backups, RCON, Bans), App-Sperre, Wiki (offline), Verwaltung + Moderation, Minecraft-Karte, Medien, Uploads, Info, Suche, Glocke, Konto, Einstellungen, Tray |
| `tests/RoG.Linux.Tests` | xUnit: Plattform-Dienste, ViewModels gegen einen Fake-Server, Headless-Screenshots (`artifacts/screenshots/`) |
| `packaging/` | Flatpak-Manifest (Bau aus dem Quelltext), eigenes Repo (`.flatpakref`, öffentlicher Schlüssel), `.deb`, `.desktop`-Datei, AppStream-Metadaten, Store-Screenshots, Skripte – Anleitung in [`packaging/README.md`](packaging/README.md) |

## Entwicklung

```bash
git clone --recurse-submodules https://github.com/Rebels-of-Gaming/rebelsofgaming-linux.git
# schon geklont: git submodule update --init

# .NET SDK ohne root in ~/.dotnet (einmalig)
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build RoG.Linux.sln
dotnet test RoG.Linux.sln                              # inkl. Screenshots nach artifacts/screenshots/
dotnet run --project src/RoG.Linux.App                 # Produktion
dotnet run --project src/RoG.Linux.App -- --environment=staging   # bzw. ROG_ENVIRONMENT=staging
```

Ohne Bildschirm: `xvfb-run -a dotnet run --project src/RoG.Linux.App`. Fenstersystem erzwingen: `-- --x11` bzw.
`-- --wayland` (oder `ROG_DISPLAY=x11`). Läuft die App schon, holt ein zweiter Start nur das Fenster nach vorn
(`rog://…`-Links werden weitergereicht).

Laufzeit-Voraussetzungen: Wayland (nativ, Avalonia 12.1) oder X11/XWayland, `libfontconfig1`, ein Secret-Service-Dienst (GNOME Keyring, KWallet,
KeePassXC) für dauerhaftes Anmelden – ohne ihn gilt die Anmeldung nur bis zum Beenden (Hinweis in der App).

## Lizenz

GPL-3.0-or-later (`LICENSE`). Name „Rebels of Gaming“, Logo und Grafiken sind ausgenommen – siehe `TRADEMARKS.md`.

## Lizenzen Dritter

- Avalonia UI 12, CommunityToolkit.Mvvm, Tmds.DBus.Protocol, AngleSharp, .NET, xUnit – MIT bzw. Apache 2.0
- Barlow Condensed – SIL Open Font License 1.1 (`external/rog-windows/src/RoG.App/Fonts/OFL.txt`)
- Symbole nach Material Design Icons – Apache License 2.0
- Logo – Rebels of Gaming (aus dem Brand-Kit der Website)
