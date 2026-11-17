#!/usr/bin/env python3
"""Regression coverage for fixture preparation and evidence validation."""

import json
from pathlib import Path
import runpy
import tempfile
import unittest
import xml.etree.ElementTree as ET

HELPERS = runpy.run_path(str(Path(__file__).with_name("package-fixture.py")))
REPO = Path(__file__).resolve().parents[3]


class FixtureXmlTests(unittest.TestCase):
    def test_missing_package_xml_preserves_special_character_paths(self) -> None:
        with tempfile.TemporaryDirectory(prefix="flamme-fixture-xml-") as temporary:
            folder = Path(temporary) / 'missing & "double" \'single\' <xml>'
            HELPERS["missing"](folder, "0.0.0-test")
            project = ET.parse(folder / "Missing.csproj").getroot()
            self.assertEqual(project.findtext("PropertyGroup/RestorePackagesPath"), str(folder / "packages"))
            reference = project.find("ItemGroup/PackageReference")
            self.assertEqual(reference.attrib, {"Include": "Elsa.NativeAot.Modules.Producer", "Version": "[0.0.0-test]"})
            source = ET.parse(folder / "NuGet.Config").find("packageSources/add")
            self.assertEqual(source.attrib["value"], str(folder / "empty-feed"))
            self.assertTrue((folder / "empty-feed").is_dir())
            for name in ("Directory.Build.props", "Directory.Build.targets"):
                self.assertEqual(list(ET.parse(folder / name).getroot()), [])
            self.assertEqual(ET.parse(folder / "Directory.Packages.props").findtext("PropertyGroup/ManagePackageVersionsCentrally"), "false")

    def test_package_source_mapping_preserves_special_character_feed(self) -> None:
        with tempfile.TemporaryDirectory(prefix="flamme-fixture-xml-") as temporary:
            root = Path(temporary)
            consumer = root / 'consumer & "quotes"'
            feed = root / 'feed & "double" \'single\' <xml>'
            HELPERS["prepare"](REPO, consumer, feed, "0.0.0-test")
            config = ET.parse(consumer / "NuGet.Config")
            self.assertEqual(config.find("packageSources/add[@key='fixture']").attrib["value"], str(feed))
            packages = config.findall("packageSourceMapping/packageSource[@key='fixture']/package")
            self.assertEqual([package.attrib["pattern"] for package in packages], list(HELPERS["PACKAGE_IDS"]))


class CompilerSettingsTests(unittest.TestCase):
    def test_requires_runtime_async_for_default_net11(self) -> None:
        with tempfile.TemporaryDirectory(prefix="flamme-fixture-properties-") as temporary:
            evidence = Path(temporary) / "properties.json"
            properties = {
                "Configuration": "Release",
                "TargetFramework": "net11.0",
                "UseRuntimeAsync": "false",
                "Features": "",
            }
            evidence.write_text(json.dumps({"Properties": properties}))
            HELPERS["compiler_settings"](evidence, "net11.0", "disabled")
            with self.assertRaisesRegex(SystemExit, "expected Release net11.0 runtime-async default"):
                HELPERS["compiler_settings"](evidence, "net11.0", "default")

            properties.update(UseRuntimeAsync="true", Features=";runtime-async=on;other=on")
            evidence.write_text(json.dumps({"Properties": properties}))
            HELPERS["compiler_settings"](evidence, "net11.0", "default")
            with self.assertRaisesRegex(SystemExit, "expected Release net11.0 runtime-async disabled"):
                HELPERS["compiler_settings"](evidence, "net11.0", "disabled")

    def test_rejects_missing_compiler_feature_even_when_opted_in(self) -> None:
        with tempfile.TemporaryDirectory(prefix="flamme-fixture-properties-") as temporary:
            evidence = Path(temporary) / "properties.json"
            evidence.write_text(json.dumps({"Properties": {
                "Configuration": "Release",
                "TargetFramework": "net11.0",
                "UseRuntimeAsync": "true",
                "Features": "unrelated-runtime-async=on",
            }}))
            with self.assertRaisesRegex(SystemExit, "compiler feature on"):
                HELPERS["compiler_settings"](evidence, "net11.0", "default")


if __name__ == "__main__":
    unittest.main()
