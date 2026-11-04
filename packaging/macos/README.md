# macOS-App (Mac App Store)

Native macOS-Version der App – **dieselbe Avalonia-Oberfläche** wie unter Linux (`src/RoG.Linux.App`), die
macOS-Verdrahtung liegt in `src/RoG.Mac.Platform` (Schlüsselbund, Mitteilungen, NSWorkspace, Sandbox-Ordner).
Verteilung **nur über den Mac App Store**, unter **demselben App-Store-Connect-Eintrag wie die iOS-App**
„Rebels of Gaming“ (Apple-ID 6822830271) mit derselben Bundle-ID **`com.rebelsofgaming.app`** („Universal Purchase“:
ein Eintrag, ein Name, eine Bewertung; wer die App auf dem iPhone hat, sieht sie auch im Mac App Store als „gekauft“).
Team-ID **K4KM7V6YK5**.

Gebaut, getestet und hochgeladen wird ausschließlich von GitHub Actions (`.github/workflows/macos.yml`, macOS-Runner –
für das öffentliche Repo kostenlos). Ein Mac ist für die Einrichtung **nicht** nötig (siehe unten: alles mit WSL).

## Was der Workflow macht

| Auslöser | Ergebnis |
|---|---|
| Push auf `develop`, „Run workflow“ (manuell) | Tests (macOS-Plattform + Kern), App-Bundle arm64 + x64, ad-hoc signiert in der Sandbox gestartet (Rauchtest), unsignierte Bundles als Artefakt. **Nie Upload, nie Secrets.** |
| Tag `vX.Y.Z` auf `main` | zusätzlich Job „App Store Connect“ (Umgebung `release`, Freigabe durch dich): mit „Apple Distribution“ signieren, Provisioning-Profil einbetten, `.pkg` mit „Mac Installer Distribution“, prüfen (`altool --validate-app`) und hochladen (`altool --upload-app`). Fehlen Secrets → Hinweis, Schritt übersprungen, Lauf grün. |

- Version = `RoGLinuxVersion` aus `Directory.Build.props` (eine Version für Linux und macOS),
  Build-Nummer (`CFBundleVersion`) = fortlaufende Laufnummer des Workflows (steigt bei jedem Upload).
- **Hochgeladen wird die Apple-silicon-Version (arm64).** Der Mac App Store nimmt reine arm64-Apps an
  („Erfordert einen Mac mit Apple-Chip“). Owner-Entscheidung 04.11.2026: vorerst nur
  Apple-Chip (F16); Intel/Universal siehe „Offen“ unten.
- Lokal (nur auf einem Mac): `packaging/macos/make-app.sh arm64` → `artifacts/macos/arm64/Rebels of Gaming.app`,
  `packaging/macos/sign-app.sh adhoc "artifacts/macos/arm64/Rebels of Gaming.app"`, dann
  `open "artifacts/macos/arm64/Rebels of Gaming.app"`.

## Einmalige Einrichtung (Owner) – Schritt für Schritt

Alles im Browser plus ein paar Befehle in **WSL (Ubuntu)**. Arbeitsordner z. B. `~/apple-mac` – **dieser Ordner enthält
danach private Schlüssel: nie ins Repo, nach dem Eintragen der Secrets sicher aufbewahren (Passwort-Safe) oder löschen.**

### 1. Bundle-ID prüfen

1. <https://developer.apple.com/account> → **Certificates, IDs & Profiles** → **Identifiers**.
2. `com.rebelsofgaming.app` anklicken (existiert schon von der iOS-App/EAS). App-IDs gelten heute für alle Plattformen –
   für macOS muss nichts angehakt werden. **Nicht** „Mac Catalyst“ verwenden (das ist eine andere Technik).
3. Zusätzliche Capabilities braucht die macOS-App nicht (Push ist für iOS; die Mac-App zeigt lokale Mitteilungen).

### 2. Zwei Zertifikatsanfragen (CSR) ohne Mac erzeugen

```bash
mkdir -p ~/apple-mac && cd ~/apple-mac && chmod 700 .
# 1) für „Apple Distribution“ (signiert die App)
openssl genrsa -out mac_dist.key 2048
openssl req -new -key mac_dist.key -out mac_dist.csr -subj "/emailAddress=<deine Apple-ID-E-Mail>/CN=Rebels of Gaming/C=LU"
# 2) für „Mac Installer Distribution“ (signiert das .pkg)
openssl genrsa -out mac_installer.key 2048
openssl req -new -key mac_installer.key -out mac_installer.csr -subj "/emailAddress=<deine Apple-ID-E-Mail>/CN=Rebels of Gaming/C=LU"
```

### 3. Zertifikate anlegen

1. **Certificates** → **+**.
2. **Apple Distribution** wählen → Continue → `mac_dist.csr` hochladen → Continue → **Download** (`distribution.cer`)
   nach `~/apple-mac` (unter Windows liegt WSL unter `\\wsl$\Ubuntu\home\<name>\apple-mac`).
   *Hinweis:* EAS hat für iOS schon ein „Apple Distribution“-Zertifikat angelegt; dessen privater Schlüssel liegt bei
   Expo. Ein **eigenes** Zertifikat für den Mac-Workflow ist sauberer (EAS bleibt unberührt). Apple erlaubt mehrere
   Distribution-Zertifikate je Team; ist das Limit erreicht, ein ungenutztes widerrufen – **nicht** das von EAS.
3. Wieder **+** → **Mac Installer Distribution** → Continue → `mac_installer.csr` → **Download** (`mac_installer.cer`).

### 4. In `.p12` umwandeln (Passwort selbst wählen, je Datei eins)

```bash
cd ~/apple-mac
openssl x509 -inform DER -in distribution.cer -out distribution.pem
openssl pkcs12 -export -inkey mac_dist.key -in distribution.pem -out mac_dist.p12 -name "Apple Distribution" \
  -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1
openssl x509 -inform DER -in mac_installer.cer -out mac_installer.pem
openssl pkcs12 -export -inkey mac_installer.key -in mac_installer.pem -out mac_installer.p12 -name "Mac Installer Distribution" \
  -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1
```

(Die `-keypbe/-certpbe/-macalg`-Angaben sorgen dafür, dass der Schlüsselbund von macOS die Datei lesen kann.)

### 5. Provisioning-Profil „Mac App Store“

1. **Profiles** → **+** → unter **Distribution**: **Mac App Store Connect** → Continue.
2. Profiltyp **Mac**, App ID **com.rebelsofgaming.app** → Continue.
3. Zertifikat: das **neue** „Apple Distribution“ aus Schritt 3 → Continue.
4. Name: `RoG macOS App Store` → **Generate** → **Download** (`RoG_macOS_App_Store.provisionprofile`) nach `~/apple-mac`.
5. Läuft ein Zertifikat ab oder wird widerrufen: neues Zertifikat + neues Profil, beide Secrets ersetzen.

### 6. macOS im bestehenden App-Store-Connect-Eintrag hinzufügen

1. <https://appstoreconnect.apple.com> → **Apps** → **Rebels of Gaming**.
2. Links in der Seitenleiste neben „iOS-App“ auf **+** („Plattform hinzufügen“) → **macOS**.
3. Es entsteht eine macOS-Version (Vorgabe „1.0“) → auf die Versionsnummer der Mac-App ändern (z. B. **0.2.0**, muss
   zur hochgeladenen Version passen).
4. Gleich mit ausfüllen (oder nach dem ersten Build): Mac-Bildschirmfotos (16:10, z. B. 1280 × 800 – passende
   Aufnahmen liegen in `packaging/screenshots/`, Format stimmt), Beschreibung/Schlagwörter (können die von iOS sein),
   Kategorie ist schon „Soziale Netze“. Datenschutz-Angaben gelten für den ganzen Eintrag (wie iOS – nichts Neues).
5. Hinweis: Ist die iOS-App heute auf Macs mit Apple-Chip als „iPad-App“ erhältlich, ersetzt die native Mac-App sie
   dort, sobald sie veröffentlicht ist.

### 7. App-Store-Connect-API-Schlüssel

1. App Store Connect → **Benutzer und Zugriff** → **Integrationen** → **App Store Connect API** → **Teamschlüssel** → **+**.
2. Name `GitHub macOS`, Zugriff **App-Manager** → **Generieren**.
3. **API-Schlüssel herunterladen** (`AuthKey_<KEYID>.p8` – geht nur **einmal**) nach `~/apple-mac`.
4. **Schlüssel-ID** (Spalte „Schlüssel-ID“) und **Aussteller-ID** (oben über der Liste, „Issuer ID“) notieren.

### 8. GitHub-Secrets anlegen

In der **Umgebung `release`** (nur Tag-Läufe sehen sie): GitHub → Repo **rebelsofgaming-linux** → **Settings** →
**Environments** → **release** → **Environment secrets** → **Add environment secret**. Am schnellsten mit `gh` aus WSL:

```bash
cd ~/apple-mac
R=Rebels-of-Gaming/rebelsofgaming-linux
base64 -w0 mac_dist.p12                      | gh secret set MAC_DIST_CERT_P12_BASE64        --env release -R "$R"
gh secret set MAC_DIST_CERT_PASSWORD        --env release -R "$R"      # fragt nach dem Passwort aus Schritt 4
base64 -w0 mac_installer.p12                 | gh secret set MAC_INSTALLER_CERT_P12_BASE64   --env release -R "$R"
gh secret set MAC_INSTALLER_CERT_PASSWORD   --env release -R "$R"
base64 -w0 RoG_macOS_App_Store.provisionprofile | gh secret set MAC_PROVISIONING_PROFILE_BASE64 --env release -R "$R"
gh secret set ASC_API_KEY_ID                --env release -R "$R"      # Schlüssel-ID, z. B. ABC123DEFG
gh secret set ASC_API_ISSUER_ID             --env release -R "$R"      # Aussteller-ID (UUID)
base64 -w0 AuthKey_<KEYID>.p8                | gh secret set ASC_API_KEY_P8_BASE64           --env release -R "$R"
```

| Secret | Inhalt |
|---|---|
| `MAC_DIST_CERT_P12_BASE64` | `mac_dist.p12` (Apple Distribution + privater Schlüssel), Base64 |
| `MAC_DIST_CERT_PASSWORD` | Passwort von `mac_dist.p12` |
| `MAC_INSTALLER_CERT_P12_BASE64` | `mac_installer.p12` (Mac Installer Distribution), Base64 |
| `MAC_INSTALLER_CERT_PASSWORD` | Passwort von `mac_installer.p12` |
| `MAC_PROVISIONING_PROFILE_BASE64` | `RoG_macOS_App_Store.provisionprofile`, Base64 |
| `ASC_API_KEY_ID` | Schlüssel-ID des API-Schlüssels |
| `ASC_API_ISSUER_ID` | Aussteller-ID (Issuer ID) |
| `ASC_API_KEY_P8_BASE64` | `AuthKey_<KEYID>.p8`, Base64 |

Base64 ohne WSL: **macOS** `base64 -i datei | pbcopy` · **Windows PowerShell**
`[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\pfad\datei")) | Set-Clipboard` – dann im Browser einfügen.

## Veröffentlichen (wie bei Linux, nur mit deinem „Freigabe“)

1. Version in `Directory.Build.props` (+ CHANGELOG) – gilt für Linux und macOS gemeinsam.
2. `main` per Fast-Forward, Tag `vX.Y.Z` → Workflow **macOS** → Job „App Store Connect“ wartet auf deine Freigabe
   (Umgebung `release`) → signiert, prüft, lädt hoch.
3. App Store Connect → **TestFlight** (macOS): Build erscheint nach 10–30 Min. („Verarbeitung“) → interne Tester
   (TestFlight-App auf dem Mac) → testen.
4. macOS-Version → Build auswählen → **Zur Prüfung einreichen**.

## Hinweise für die App-Prüfung (App Review Information → Notes)

Demo-Konto wie bei iOS: **demo@rebelsofgaming.com** (Passwort wie bei iOS – nicht im Repo).
Textvorschlag (EN):

> Rebels of Gaming is the companion app of a gaming community (rebelsofgaming.com). Sign-in happens in the user's
> default browser (OAuth 2.0 with PKCE, RFC 8252); the browser returns to the app via a one-time loopback address
> (http://127.0.0.1:&lt;random port&gt;/callback) – this is why the app has the network.server entitlement (listening on
> localhost only, for a few minutes during sign-in). Demo account (email and password on the website's
> sign-in page, same as for the iOS app): demo@rebelsofgaming.com / &lt;password&gt;. The app is built with .NET; the runtime compiles code at run
> time, which requires com.apple.security.cs.allow-jit. Downloads go to the user's Downloads folder
> (files.downloads.read-write), uploads use the system file picker (files.user-selected.read-write). No tracking,
> no third-party analytics; all content is loaded from the operator's own servers.

## Symbol

- Vorlage: `rog-icon-square-1024.png` – Kopie von `resources/brand/icon/rog-icon-square-1024.png` aus dem
  Website-Repo (Brand-Kit, einzige Quelle aller Logos; Stand 04.11.2026). Ändert sich das Brand-Kit, Datei neu kopieren.
- `AppIcon-1024.png` = Mac-Symbolraster nach Apple (1024er Leinwand, Körper 824 × 824 mit 100 px Rand, Radius 185,
  Schatten), erzeugt mit `dotnet run packaging/macos/make-icon.cs -- packaging/macos/rog-icon-square-1024.png
  packaging/macos/AppIcon-1024.png`. Nur verkleinert, nie vergrößert.
- `make-app.sh` baut daraus `AppIcon.icns` mit allen Größen 16–512 jeweils @1x und @2x (bis 1024 px).

## Rechte der App (Sandbox)

`RebelsOfGaming.entitlements` (Store) – Begründung je Recht steht in der Datei. `RebelsOfGaming.dev.entitlements` ist
die gleiche Sandbox **ohne** die profilgebundenen Rechte (Team-/App-ID, Schlüsselbund-Gruppe) für den ad-hoc-signierten
Rauchtest in der CI; dort fällt der Schlüsselbund automatisch auf den Anmelde-Schlüsselbund zurück.
Hardened Runtime ist an (`codesign --options runtime`), JIT nur über `cs.allow-jit` (CoreCLR; `allow-unsigned-executable-memory`
und `disable-library-validation` braucht .NET 10 nicht – alle Bibliotheken tragen unsere Signatur).

## Offen / später

- ~~Start in der Sandbox~~ **gelöst (Bauplan M9):** nativer Starter `launcher.c` (linkt AppKit, startet .NET über
  hostfxr). Manuelle Prüfung mit echter Developer-ID-Signatur + Notarisierung: Workflow „macOS“ → Run workflow →
  `devid_sandbox_test` (Secrets `MAC_DEVID_CERT_P12_BASE64`/`MAC_DEVID_CERT_PASSWORD`, Umgebung `release`).
- **Universal (Intel + Apple-Chip)** – vorerst nicht gewünscht (F16): .NET-Rahmenbibliotheken sind je Architektur vorkompiliert – ein einfaches
  `lipo` reicht nicht. Plan: kleiner nativer Starter (universal) unter `Contents/MacOS`, der je Architektur die passende
  .NET-Laufzeit lädt. Der Workflow baut x64 schon mit und startet es unter Rosetta (Rauchtest).
- Klick auf eine Mitteilung holt die App nach vorn, springt aber noch nicht zum Ziel (eigener
  `UNUserNotificationCenter`-Delegate), „Beim Anmelden starten“ (`SMAppService`), Touch ID für die App-Sperre,
  Bildschirmfoto hochladen.
- Echte Tests auf einem Mac (Login, Schlüsselbund mit Profil, Mitteilungen, Deep Links) – siehe Bauplan.
