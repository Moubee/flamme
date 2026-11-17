#!/usr/bin/env python3
"""Prepare and verify an external package-only module consumer (stdlib only)."""

import base64
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import xml.etree.ElementTree as ET
import zipfile

PACKAGE_IDS = (
    "Elsa.CodeGeneration.Abstractions",
    "Elsa.Features",
    "Elsa.NativeAot.Modules.Producer",
)


def require(condition: bool, message: object) -> None:
    if not condition:
        raise SystemExit(str(message))


def prepare(repo: Path, consumer: Path, feed: Path, version: str) -> None:
    consumer.mkdir(parents=True, exist_ok=True)
    templates = repo / "scripts/nativeaot/module-consumer"
    shutil.copyfile(templates / "Consumer.csproj.in", consumer / "Consumer.csproj")
    for name in ("Program.cs", "ModuleConsumerScenarios.cs"):
        shutil.copyfile(templates / name, consumer / name)
    shutil.copyfile(repo / "global.json", consumer / "global.json")
    # Neither machine-wide nor ancestor repository build imports may turn this
    # package-only project back into an implicitly source-coupled consumer.
    for name in ("Directory.Build.props", "Directory.Build.targets"):
        (consumer / name).write_text("<Project />\n")
    packages = ET.parse(repo / "Directory.Packages.props")
    group = ET.SubElement(packages.getroot(), "ItemGroup")
    ET.SubElement(group, "PackageVersion", Include=PACKAGE_IDS[-1], Version=f"[{version}]")
    packages.write(consumer / "Directory.Packages.props", encoding="utf-8", xml_declaration=True)

    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="fixture", value=str(feed))
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    mappings = ET.SubElement(config, "packageSourceMapping")
    ET.SubElement(mappings, "clear")
    local = ET.SubElement(mappings, "packageSource", key="fixture")
    for package in PACKAGE_IDS:
        ET.SubElement(local, "package", pattern=package)
    official = ET.SubElement(mappings, "packageSource", key="nuget.org")
    ET.SubElement(official, "package", pattern="*")
    ET.ElementTree(config).write(consumer / "NuGet.Config", encoding="utf-8", xml_declaration=True)


def missing(folder: Path, version: str) -> None:
    """Build an empty-feed control without interpolating paths into XML text."""
    (folder / "empty-feed").mkdir(parents=True, exist_ok=True)
    for name in ("Directory.Build.props", "Directory.Build.targets"):
        ET.ElementTree(ET.Element("Project")).write(folder / name, encoding="utf-8", xml_declaration=True)
    central = ET.Element("Project")
    ET.SubElement(ET.SubElement(central, "PropertyGroup"), "ManagePackageVersionsCentrally").text = "false"
    ET.ElementTree(central).write(folder / "Directory.Packages.props", encoding="utf-8", xml_declaration=True)

    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    ET.SubElement(properties, "TargetFramework").text = "net11.0"
    ET.SubElement(properties, "RestorePackagesPath").text = str(folder / "packages")
    ET.SubElement(ET.SubElement(project, "ItemGroup"), "PackageReference", Include=PACKAGE_IDS[-1], Version=f"[{version}]")
    ET.ElementTree(project).write(folder / "Missing.csproj", encoding="utf-8", xml_declaration=True)

    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="empty-fixture", value=str(folder / "empty-feed"))
    ET.ElementTree(config).write(folder / "NuGet.Config", encoding="utf-8", xml_declaration=True)


def packages(feed: Path, version: str) -> None:
    for package in PACKAGE_IDS:
        archive = feed / f"{package}.{version}.nupkg"
        with zipfile.ZipFile(archive) as contents:
            for framework in ("net10.0", "net11.0"):
                require(f"lib/{framework}/{package}.dll" in contents.namelist(), (package, framework))
            if package == PACKAGE_IDS[0]:
                specification = next(name for name in contents.namelist() if name.endswith(".nuspec"))
                metadata = ET.fromstring(contents.read(specification))
                dependencies = metadata.findall(".//{*}dependency")
                require(not dependencies, "Generation abstractions unexpectedly acquired package dependencies")
    print("Verified dual-target runtime packages and dependency-free abstractions.")


def assets(path: Path, feed: Path, version: str) -> None:
    data = json.loads(path.read_text())
    libraries = data["libraries"]
    require(all(value["type"] == "package" for value in libraries.values()), "Consumer contains source project references")
    for framework in data["project"]["restore"]["frameworks"].values():
        require(not framework.get("projectReferences"), "Consumer restore contains project references")
    for package in PACKAGE_IDS:
        matches = [key for key in libraries if key.lower() == f"{package}/{version}".lower()]
        require(len(matches) == 1, f"Missing or ambiguous package version: {package}/{version}")
        key = matches[0]
        library = libraries[key]
        require(library["type"] == "package", (package, library["type"]))
        archive = feed / f"{package}.{version}.nupkg"
        digest = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
        require(library["sha512"] == digest, f"{package} did not resolve the staged package bytes")
        installed = [Path(folder) / library["path"] / f"{package.lower()}.{version}.nupkg.sha512" for folder in data["packageFolders"]]
        require(any(item.is_file() and item.read_text().strip() == digest for item in installed), f"{package} package cache provenance mismatch")
        print(f"Verified package provenance: {key} sha512={digest}")


def lock(path: Path) -> None:
    data = json.loads(path.read_text())
    groups = data["dependencies"]
    for framework in ("net10.0", "net11.0"):
        require(any(key == framework or key.startswith(framework + "/") for key in groups), f"Lock is missing {framework}")
    print(f"Verified complete two-framework lock: {path}")


def compiler_settings(path: Path, framework: str, async_model: str) -> None:
    properties = json.loads(path.read_text())["Properties"]
    enabled = framework == "net11.0" and async_model == "default"
    async_property = "true" if enabled else "false"
    async_features = [
        feature for feature in re.split(r"[,;\s]+", properties["Features"])
        if feature.partition("=")[0].lower() == "runtime-async"
    ]
    expected_features = ["runtime-async=on"] if enabled else []
    require(
        properties["Configuration"] == "Release"
        and properties["TargetFramework"] == framework
        and properties["UseRuntimeAsync"].lower() == async_property
        and async_features == expected_features,
        f"{path}: expected Release {framework} runtime-async {async_model} "
        f"(UseRuntimeAsync={async_property}, compiler feature {'on' if enabled else 'absent'}); "
        f"observed {properties['Configuration']} {properties['TargetFramework']} "
        f"UseRuntimeAsync={properties['UseRuntimeAsync']}, Features={properties['Features']!r}",
    )


if __name__ == "__main__":
    operation, *arguments = sys.argv[1:]
    if operation == "prepare":
        prepare(*(Path(value) for value in arguments[:3]), arguments[3])
    elif operation == "missing":
        missing(Path(arguments[0]), arguments[1])
    elif operation == "packages":
        packages(Path(arguments[0]), arguments[1])
    elif operation == "assets":
        assets(Path(arguments[0]), Path(arguments[1]), arguments[2])
    elif operation == "lock":
        lock(Path(arguments[0]))
    elif operation == "compiler-settings":
        compiler_settings(Path(arguments[0]), *arguments[1:])
    else:
        raise SystemExit(f"Unknown operation: {operation}")
