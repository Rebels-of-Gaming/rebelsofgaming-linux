#!/usr/bin/env bash
# Erzeugt packaging/flatpak/nuget-sources.json neu – die NuGet-Pakete für den Flatpak-Bau ohne Netz.
# Nach jeder Änderung an Paketen (Directory.Packages.props, *.csproj, Kern-Bump) ausführen und mitcommitten;
# die CI (packaging/scripts/check-packaging.py) schlägt sonst fehl.
#
#   packaging/scripts/update-nuget-sources.sh
#
# Wie flatpak-dotnet-generator (flatpak-builder-tools, MIT) und im selben Format: Restore IM Flatpak-SDK
# (org.freedesktop.Sdk//26.08 + org.freedesktop.Sdk.Extension.dotnet10), damit Laufzeit-, AppHost- und
# Crossgen2-Pakete genau zur .NET-Version der Erweiterung passen – nicht zur lokal installierten. Zusätzlich zum
# Generator: Crossgen2 und AppHost für BEIDE Host-Architekturen (ReadyToRun auf dem aarch64-Runner braucht
# Microsoft.NETCore.App.Crossgen2.linux-arm64, und wegen RuntimeIdentifiers=linux-x64;linux-arm64 will der Restore
# dort Microsoft.NETCore.App.Host.linux-x64 – beides lädt ein Restore auf x86_64 nie, das SDK bringt den eigenen mit).
# Der Restore läuft auf einer Kopie der versionierten Dateien – bin/obj im Arbeitsverzeichnis bleiben unberührt.
# Braucht: flatpak, Netz, die beiden SDK-Refs (flatpak install flathub org.freedesktop.Sdk//26.08
# org.freedesktop.Sdk.Extension.dotnet10//26.08).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
manifest="$root/packaging/flatpak/com.rebelsofgaming.RebelsOfGaming.yml"
out="$root/packaging/flatpak/nuget-sources.json"
fd="$(sed -n "s/^runtime-version: '\(.*\)'/\1/p" "$manifest")"
dotnet_ext="org.freedesktop.Sdk.Extension.dotnet10"

# Benutzer- oder Systeminstallation – je nachdem, wo das SDK liegt
installation=--system
flatpak info --user "org.freedesktop.Sdk//$fd" >/dev/null 2>&1 && installation=--user

work="$(mktemp -d "${TMPDIR:-/tmp}/rog-nuget.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/src" "$work/packages" "$work/extra"
git -C "$root" ls-files -z --recurse-submodules | (cd "$root" && tar --null -T - -cf -) | tar -xf - -C "$work/src"

# shellcheck disable=SC2016 # $PATH/$@ werden absichtlich erst in der Sandbox ausgewertet
sdk() {
  flatpak run "$installation" --env=DOTNET_CLI_TELEMETRY_OPTOUT=true --env=DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    --env=DOTNET_NOLOGO=true --command=sh "--runtime=org.freedesktop.Sdk//$fd" --share=network \
    "--filesystem=$work" "$dotnet_ext//$fd" -c \
    'PATH="$PATH:/usr/lib/sdk/dotnet10/bin" LD_LIBRARY_PATH="$LD_LIBRARY_PATH:/usr/lib/sdk/dotnet10/lib" exec "$@"' -- "$@"
}

runtime_version="$(sdk dotnet --list-runtimes | sed -n 's/^Microsoft\.NETCore\.App \([0-9.]*\) .*/\1/p' | tail -1)"
echo "dotnet in $dotnet_ext//$fd: Laufzeit $runtime_version"

for rid in linux-x64 linux-arm64; do
  sdk dotnet restore "$work/src/src/RoG.Linux.App/RoG.Linux.App.csproj" -r "$rid" -p:PublishReadyToRun=true \
    --packages "$work/packages"
done

# Crossgen2 und AppHost je Host-Architektur (siehe oben)
cat > "$work/extra/extra.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageDownload Include="Microsoft.NETCore.App.Crossgen2.linux-x64" Version="[$runtime_version]" />
    <PackageDownload Include="Microsoft.NETCore.App.Crossgen2.linux-arm64" Version="[$runtime_version]" />
    <PackageDownload Include="Microsoft.NETCore.App.Host.linux-x64" Version="[$runtime_version]" />
    <PackageDownload Include="Microsoft.NETCore.App.Host.linux-arm64" Version="[$runtime_version]" />
  </ItemGroup>
</Project>
EOF
sdk dotnet restore "$work/extra/extra.csproj" --packages "$work/packages"

python3 -I - "$work/packages" "$out" <<'PY'
import base64, binascii, json, pathlib, sys
packages, out = pathlib.Path(sys.argv[1]), sys.argv[2]
sources = []
for sha_file in packages.glob("*/*/*.nupkg.sha512"):
    name, version = sha_file.parent.parent.name, sha_file.parent.name
    filename = f"{name}.{version}.nupkg"
    sources.append({
        "type": "file",
        "url": f"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{filename}",
        "sha512": binascii.hexlify(base64.b64decode(sha_file.read_text())).decode("ascii"),
        "dest": "nuget-sources",
        "dest-filename": filename,
    })
sources.sort(key=lambda s: s["dest-filename"])
with open(out, "w", encoding="utf-8") as fp:
    json.dump(sources, fp, indent=4)
    fp.write("\n")
print(f"{len(sources)} Pakete → {out}")
PY
