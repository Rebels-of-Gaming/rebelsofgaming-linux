# Rebels of Gaming für Linux

Native Linux-App (Avalonia UI, .NET 10) für die Clan-Plattform der **Rebels of Gaming** – gleiche Logik wie die
Windows-App, eigene Oberfläche im RoG-Design, **keine WebView**. Ziel: Linux-Desktop und Steam Deck, Verteilung später
über **Flathub** (Steam ggf. später).

- Arbeitsregeln: [`CLAUDE.md`](CLAUDE.md)
- Stand, Stufen, Annahmen, offene Fragen: [`docs/LINUX-BAUPLAN.md`](docs/LINUX-BAUPLAN.md)
- Funktionsumfang: wie die Windows-App (`external/rog-windows/docs/WINDOWS-PFLICHTENHEFT.md`)

## Aufbau

| Projekt | Inhalt |
|---|---|
| `external/rog-windows` | Git-Submodul des Windows-Repos (fester Commit). Daraus wird **`src/RoG.Core`** gebaut (API-Client, Pipeline, Login, Echtzeit, Parser) – unverändert, nicht kopiert. Außerdem Übersetzungen (`.resw`), Schrift und Logo. |
| `src/RoG.Linux.Platform` | Linux-Verdrahtung ohne Oberfläche: Token und Cache-Schlüssel im Schlüsselbund (Secret Service über D-Bus), Desktop-Benachrichtigungen (Dienst bzw. Portal), Tray (StatusNotifierItem), Autostart (XDG bzw. Portal), Single-Instance, Dateimanager, `xdg-open`, XDG-Ordner, Gerätename, Übersetzungen |
| `src/RoG.Linux.App` | Avalonia-12-Oberfläche (MVVM mit CommunityToolkit.Mvvm): Shell, Start, News, Nachrichten (+ Chat-Fenster), Forum, Squads, Mitglieder, Termine, Server (Minecraft, TeamSpeak, Discord), Matches + Stats, Bestenlisten, Freunde, Mitgliederkarte, Votes, Server-Steuerung (Konsole, Configs, Dateien, Backups, RCON, Bans), App-Sperre, Wiki (offline), Verwaltung + Moderation, Minecraft-Karte, Medien, Uploads, Info, Suche, Glocke, Konto, Einstellungen, Tray |
| `tests/RoG.Linux.Tests` | xUnit: Plattform-Dienste, ViewModels gegen einen Fake-Server, Headless-Screenshots (`artifacts/screenshots/`) |
| `packaging/` | Flatpak-Manifest (Binärpakete), `.desktop`-Datei, AppStream-Metadaten, Store-Screenshots, Skripte – Anleitung in [`packaging/README.md`](packaging/README.md) |

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

## Lizenzen Dritter

- Avalonia UI 12, CommunityToolkit.Mvvm, Tmds.DBus.Protocol, AngleSharp, .NET, xUnit – MIT bzw. Apache 2.0
- Barlow Condensed – SIL Open Font License 1.1 (`external/rog-windows/src/RoG.App/Fonts/OFL.txt`)
- Symbole nach Material Design Icons – Apache License 2.0
- Logo – Rebels of Gaming (aus dem Brand-Kit der Website)
