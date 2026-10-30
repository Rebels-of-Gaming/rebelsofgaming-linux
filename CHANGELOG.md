# Changelog – Rebels of Gaming für Linux

Versionen nach [SemVer](https://semver.org/lang/de/), Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).
Jedes veröffentlichte Paket (Flathub, später ggf. Steam) bekommt einen Git-Tag `vX.Y.Z`. Änderungen bis zur nächsten
Version sammelt `## [Unveröffentlicht]`.

## [Unveröffentlicht]

Stufe 0 – Spike (Version 0.1.0, nicht veröffentlicht).

### Hinzugefügt

- Lösung `RoG.Linux.sln`: Avalonia-App `RoG.Linux.App`, Linux-Dienste `RoG.Linux.Platform`, Tests `RoG.Linux.Tests`
- Geteilter Kern `RoG.Core` der Windows-App als Git-Submodul (`external/rog-windows`, fester Commit) – baut unverändert unter Linux
- RoG-Design für Avalonia (Dunkel, Farben und Barlow Condensed wie Website/Windows), Symbole als Vektorpfade
- Shell mit Seitenleiste wie die Windows-App (aufgeklappt ab 840 px, sonst Symbole), Platzhalter mit „Im Browser öffnen“ für noch fehlende Bereiche
- Login im System-Browser (Discord oder E-Mail) mit PKCE und Rücksprung an `http://127.0.0.1:<Port>/callback`, Gastmodus, Abmelden mit Widerruf
- Geräte-Token im Schlüsselbund (Secret Service über D-Bus), Rückfall „nur diese Sitzung“ mit Hinweis; Gerätename „Linux-App – PC/Steam Deck von …“, Gerätemeldung `platform: linux`
- News: Liste mit Titelbildern, „Mehr laden“, Artikel nativ gezeichnet (Überschriften, Listen, Zitate, Code, Links, Tabellen, Spoiler), Kommentare lesen
- Nachrichten: Unterhaltungen mit Suche und Ungelesen-Markierung, Verlauf lesen („Ältere laden“, Zitate, gelöschte Nachrichten, „aus Minecraft“)
- Konto: Name, Rang, Avatar, Ort des Tokens, Umgebung, Version
- Desktop-Benachrichtigungen (org.freedesktop.Notifications), Links über `xdg-open` (nur http/https/mailto bzw. bekannte Spiele-Links)
- Tastatur: Tab/Pfeile, Strg+1…9 für Bereiche, F5 aktualisiert, Escape schließt das Detail; sichtbarer Fokusrahmen
- Sprachen DE/EN/FR aus den Texten der Windows-App plus Linux-eigene Texte
- Tests (xUnit, Avalonia.Headless) inkl. Screenshots der Hauptseiten in 1280×800 (Steam Deck) und 1440×900
- CI auf den eigenen Linux-Runnern; Entwürfe für Flatpak-Manifest, `.desktop`-Datei (`rog://`-Links) und AppStream-Metadaten
