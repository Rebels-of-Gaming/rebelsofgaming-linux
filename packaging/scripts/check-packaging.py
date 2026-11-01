#!/usr/bin/env python3
"""Schnelle Prüfungen der Paket-Dateien (CI, ohne Flatpak-Bau):

1. packaging/flatpak/nuget-sources.json enthält jedes Paket, das App, Plattform und Kern referenzieren, in der
   zentral festgelegten Version – sonst scheitert der Flatpak-Bau ohne Netz. Abhilfe:
   packaging/scripts/update-nuget-sources.sh
2. .flatpakref und .flatpakrepo betten genau den öffentlichen Schlüssel packaging/flatpak/rebelsofgaming.gpg ein
   und zeigen auf das eigene Repo (Branch stable, Laufzeit von Flathub).

    python3 -I packaging/scripts/check-packaging.py
"""

import base64
import configparser
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
FLATPAK = ROOT / "packaging" / "flatpak"
REPO_URL = "https://cdn.rogclan.de/apps/linux/flatpak/"
errors: list[str] = []


def central_versions(props: pathlib.Path) -> dict[str, str]:
    tree = ET.parse(props)
    return {e.get("Include").lower(): e.get("Version") for e in tree.iter("PackageVersion")}


def references(project: pathlib.Path) -> list[str]:
    return [e.get("Include").lower() for e in ET.parse(project).iter("PackageReference")]


# 1. NuGet-Quellen
sources = json.loads((FLATPAK / "nuget-sources.json").read_text(encoding="utf-8"))
available = {s["dest-filename"] for s in sources}
projects = {
    ROOT / "src" / "RoG.Linux.App" / "RoG.Linux.App.csproj": ROOT / "Directory.Packages.props",
    ROOT / "src" / "RoG.Linux.Platform" / "RoG.Linux.Platform.csproj": ROOT / "Directory.Packages.props",
    ROOT / "external" / "rog-windows" / "src" / "RoG.Core" / "RoG.Core.csproj":
        ROOT / "external" / "rog-windows" / "Directory.Packages.props",
}
for project, props in projects.items():
    versions = central_versions(props)
    for package in references(project):
        version = versions.get(package)
        if version is None:
            errors.append(f"{project.name}: {package} ohne Version in {props.relative_to(ROOT)}")
        elif f"{package}.{version.lower()}.nupkg" not in available:
            errors.append(f"nuget-sources.json: {package} {version} fehlt ({project.name}) – "
                          "packaging/scripts/update-nuget-sources.sh ausführen")
for prefix in ("microsoft.netcore.app.runtime.linux-x64.", "microsoft.netcore.app.runtime.linux-arm64.",
               "microsoft.netcore.app.crossgen2.linux-x64.", "microsoft.netcore.app.crossgen2.linux-arm64.",
               "microsoft.netcore.app.host.linux-arm64."):
    if not any(name.startswith(prefix) for name in available):
        errors.append(f"nuget-sources.json: {prefix}* fehlt")
for s in sources:
    if s.get("dest") != "nuget-sources" or not s["url"].startswith("https://api.nuget.org/"):
        errors.append(f"nuget-sources.json: unerwarteter Eintrag {s.get('dest-filename')}")

# 2. Einstiegsdateien des eigenen Repos
key = base64.b64encode((FLATPAK / "rebelsofgaming.gpg").read_bytes()).decode("ascii")
for name, section in (("com.rebelsofgaming.RebelsOfGaming.flatpakref", "Flatpak Ref"),
                      ("rebelsofgaming.flatpakrepo", "Flatpak Repo")):
    parser = configparser.ConfigParser(interpolation=None)
    parser.optionxform = str
    parser.read(FLATPAK / name, encoding="utf-8")
    entry = parser[section]
    if entry.get("GPGKey") != key:
        errors.append(f"{name}: GPGKey passt nicht zu rebelsofgaming.gpg")
    if entry.get("Url") != REPO_URL:
        errors.append(f"{name}: Url muss {REPO_URL} sein")
    branch = entry.get("Branch") or entry.get("DefaultBranch")
    if branch != "stable":
        errors.append(f"{name}: Branch/DefaultBranch muss stable sein")
    if section == "Flatpak Ref":
        if entry.get("Name") != "com.rebelsofgaming.RebelsOfGaming":
            errors.append(f"{name}: Name muss die App-ID sein")
        if entry.get("RuntimeRepo") != "https://dl.flathub.org/repo/flathub.flatpakrepo":
            errors.append(f"{name}: RuntimeRepo muss Flathub sein (Laufzeit + .NET-Erweiterung)")

if errors:
    print("\n".join(f"::error::{e}" for e in errors))
    sys.exit(1)
print(f"Paket-Dateien ok ({len(sources)} NuGet-Pakete, Schlüssel und Repo-Dateien stimmen)")
