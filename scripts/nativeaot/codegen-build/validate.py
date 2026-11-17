#!/usr/bin/env python3
"""Exercise installed compiler/build packages with the real repository SDK."""

import argparse
import base64
import hashlib
import json
from pathlib import Path
import platform
import re
import runpy
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[3]
FIXTURES = Path(__file__).resolve().parent
G01 = runpy.run_path(str(FIXTURES.parent / "module-consumer/package-fixture.py"))
COMPILER_PACKAGES = ("Elsa.CodeGeneration", "Elsa.CodeGeneration.Analyzers", "Elsa.CodeGeneration.Build")
PACKAGES = (*G01["PACKAGE_IDS"], *COMPILER_PACKAGES)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


class CommandFailure(RuntimeError):
    def __init__(self, status, command):
        super().__init__(f"Command exited {status}: {command}")
        self.status = status


class Validation:
    def __init__(self, options):
        base = Path(options.artifacts_root).resolve()
        base.mkdir(parents=True, exist_ok=True)
        self.root = Path(tempfile.mkdtemp(prefix="run.", dir=base))
        self.feed = self.root / "feed"
        self.feed.mkdir()
        self.version = f"0.0.0-g02.{time.time_ns()}"
        self.options = options
        self.rid = {"Darwin": "osx", "Linux": "linux"}.get(platform.system(), "unsupported") + "-" + {"arm64": "arm64", "aarch64": "arm64", "x86_64": "x64"}.get(platform.machine(), "unsupported")
        require("unsupported" not in self.rid, "Unsupported validation host")
        self.completed = []
        self.expected = []
        self.phase = "prerequisites"

    def run(self, name, command, cwd=REPO, expected=None, allow_failure=False):
        log = self.root / f"{name}.log"
        log.parent.mkdir(parents=True, exist_ok=True)
        command = [str(item) for item in command]
        print(f"Running {name}", flush=True)
        result = subprocess.run(command, cwd=cwd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        log.write_text(result.stdout)
        (log.with_suffix(".command.json")).write_text(json.dumps({"arguments": command, "cwd": str(cwd), "exitCode": result.returncode}, indent=2))
        if expected is not None:
            require(result.returncode != 0 and expected in result.stdout, f"Expected {expected} failure in {name}; see {log}\n{result.stdout[-3000:]}")
        elif result.returncode and not allow_failure:
            print(result.stdout[-6000:], file=sys.stderr)
            raise CommandFailure(result.returncode, command)
        return result

    def pack(self):
        self.phase = "package"
        self.run("dotnet-info", ["dotnet", "--info"])
        if not self.options.skip_native:
            self.run("native-toolchain", ["clang", "--version"])
        for package in PACKAGES:
            area = "test/fixtures" if package == "Elsa.NativeAot.Modules.Producer" else "src/common"
            project = REPO / area / package / f"{package}.csproj"
            self.run(f"pack/{package}", ["dotnet", "pack", project, "-c", "Release", "--artifacts-path", self.root / "package-build", "-p:IsPackable=true", f"-p:Version={self.version}", f"-p:PackageVersion={self.version}", f"-p:PackageOutputPath={self.feed}", f"-bl:{self.root / ('pack-' + package + '.binlog')}"])
        G01["packages"](self.feed, self.version)
        for package in COMPILER_PACKAGES:
            with zipfile.ZipFile(self.feed / f"{package}.{self.version}.nupkg") as archive:
                names = archive.namelist()
                specification = ET.fromstring(archive.read(next(name for name in names if name.endswith(".nuspec"))))
                require(not specification.findall(".//{*}dependency"), f"{package} gained NuGet dependencies")
                require(not any(name.startswith("lib/") for name in names), f"{package} leaked runtime library assets")
                require(not any("Microsoft.CodeAnalysis" in name for name in names), f"{package} ships compiler-host Roslyn assemblies")
                if package == "Elsa.CodeGeneration.Build":
                    for suffix in ("props", "targets"):
                        require(f"buildTransitive/{package}.{suffix}" in names, f"Missing transitive build {suffix}")
                else:
                    binary = f"analyzers/dotnet/cs/{package}.dll"
                    require(binary in names, f"Missing compiler asset {binary}")
                    require(b".NETStandard,Version=v2.0" in archive.read(binary), f"{package} is not a netstandard2.0 compiler assembly")

    def consumer(self, name, bootstrap="Compiler", missing_hook=False, empty_export=False, manual_failure=None, local_mode=None, source_library=False, host_disabled=False, late_mode=None, nullable_entry=False):
        folder = Path(tempfile.mkdtemp(prefix="flamme-codegen-"))
        evidence = self.root / name
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / "consumer-path.txt").write_text(str(folder))
        for filename in ("Directory.Build.props", "Directory.Build.targets"):
            (folder / filename).write_text("<Project />\n")
        if late_mode:
            late = ET.Element("Project")
            ET.SubElement(ET.SubElement(late, "PropertyGroup"), "ElsaExecutionMode").text = late_mode
            ET.ElementTree(late).write(folder / "Directory.Build.targets", encoding="utf-8", xml_declaration=True)
        shutil.copyfile(REPO / "global.json", folder / "global.json")
        packages = ET.parse(REPO / "Directory.Packages.props")
        group = ET.SubElement(packages.getroot(), "ItemGroup")
        for package in PACKAGES:
            ET.SubElement(group, "PackageVersion", Include=package, Version=f"[{self.version}]")
        packages.write(folder / "Directory.Packages.props", encoding="utf-8", xml_declaration=True)
        project = ET.parse(FIXTURES / "Consumer.csproj.in")
        properties = ET.SubElement(project.getroot(), "PropertyGroup")
        if bootstrap != "Compiler":
            ET.SubElement(properties, "ElsaHostBootstrap").text = bootstrap
        if local_mode:
            ET.SubElement(properties, "ElsaExecutionMode").text = local_mode
        if host_disabled:
            ET.SubElement(properties, "ElsaIsHost").text = "false"
        if source_library:
            references = ET.SubElement(project.getroot(), "ItemGroup")
            ET.SubElement(references, "ProjectReference", Include="SourceLibrary/SourceLibrary.csproj")
            library = folder / "SourceLibrary"
            library.mkdir()
            shutil.copyfile(FIXTURES / "SourceLibrary.csproj.in", library / "SourceLibrary.csproj")
            (library / "Marker.cs").write_text("namespace FixtureLibrary; public sealed class Marker;\n")
        project.write(folder / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
        host = (FIXTURES / f"ElsaHost.{bootstrap}.cs.in").read_text()
        if nullable_entry:
            host = host.replace("internal static partial IModule CreateModule", "internal static partial IModule? CreateModule")
        if missing_hook:
            host = re.sub(r"    // EXPORT_HOOK_BEGIN.*?    // EXPORT_HOOK_END\n", "", host, flags=re.S)
        if empty_export:
            host = host.replace("return [ProducerExports.Foundation];", "return [];")
        if manual_failure or host_disabled:
            body = "        return CreateDynamicModule(services, policy);"
            if manual_failure == "discard":
                body = "        _ = GetModuleExports();\n" + body
            elif manual_failure == "unreachable":
                body = "        if (false) return FeatureModuleCatalogBuilder.CreateModule(services, GetModuleExports(), policy);\n" + body
            host = re.sub(r"#if ELSA_GENERATED.*?#endif", body, host, count=1, flags=re.S)
        (folder / "ElsaHost.cs").write_text(host)
        shutil.copyfile(FIXTURES / "Program.cs", folder / "Program.cs")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="fixture", value=str(self.feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(config, "packageSourceMapping")
        ET.SubElement(mapping, "clear")
        local = ET.SubElement(mapping, "packageSource", key="fixture")
        for package in PACKAGES:
            ET.SubElement(local, "package", pattern=package)
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget.org"), "package", pattern="*")
        ET.ElementTree(config).write(folder / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        shutil.copytree(folder, evidence / "source")
        return folder

    def arguments(self, name, framework, settings, rid=False):
        output = self.root / name / "build"
        args = [f"-p:{key}={value}" for key, value in settings.items()]
        if rid:
            args.append(f"-p:RuntimeIdentifier={self.rid}")
        return ["--artifacts-path", output, *args]

    def restore(self, name, folder, framework, settings, rid=False):
        args = self.arguments(name, framework, settings, rid)
        self.run(f"{name}/restore-generate-lock", ["dotnet", "restore", "Consumer.csproj", "--force-evaluate", *args], folder)
        G01["lock"](folder / "packages.lock.json")
        self.run(f"{name}/restore-locked", ["dotnet", "restore", "Consumer.csproj", "--locked-mode", *args], folder)

    def provenance(self, name, source_library=False, omitted=()):
        path = self.root / name / "build/obj/Consumer/project.assets.json"
        data = json.loads(path.read_text())
        for key, value in data["libraries"].items():
            require(value["type"] == "package" or source_library and key.startswith("SourceLibrary/"), f"Unexpected source dependency {key}")
            require(not key.startswith("Microsoft.CodeAnalysis"), "Roslyn entered the consumer dependency graph")
        for package in PACKAGES:
            matches = [(key, value) for key, value in data["libraries"].items() if key.lower() == f"{package}/{self.version}".lower()]
            if package in omitted:
                require(not matches, f"Omitted compiler package {package} still appears")
                continue
            require(len(matches) == 1 and matches[0][1]["type"] == "package", f"Missing exact package {package}/{self.version}")
            digest = base64.b64encode(hashlib.sha512((self.feed / f"{package}.{self.version}.nupkg").read_bytes()).digest()).decode()
            require(matches[0][1]["sha512"] == digest, f"Wrong package bytes for {package}")

    def positive(self, name, framework, expected_provider, settings=None, native=False, bootstrap="Compiler", source_library=False, expected_child=None, local_mode=None, host_disabled=False, late_mode=None):
        settings = dict(settings or {})
        folder = self.consumer(name, bootstrap, source_library=source_library, local_mode=local_mode, host_disabled=host_disabled, late_mode=late_mode)
        self.restore(name, folder, framework, settings, native)
        self.provenance(name, source_library, ("Elsa.CodeGeneration",) if settings.get("FixtureIncludeGenerator") == "false" else ())
        args = self.arguments(name, framework, settings, native)
        # Leave generated paths to the SDK's per-project intermediate roots;
        # a shared global path would let source libraries overwrite host output.
        generated = self.root / name / "build/obj/Consumer"
        args += ["-p:EmitCompilerGeneratedFiles=true"]
        command = ["dotnet", "publish" if native else "build", "Consumer.csproj", "-c", "Release", "--framework", framework, "--no-restore", *args, f"-bl:{self.root / name / 'build.binlog'}"]
        if native:
            command += ["--output", self.root / name / "publish", "-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true", "-p:TrimmerSingleWarn=false", "-p:SuppressTrimAnalysisWarnings=false"]
        result = self.run(f"{name}/build", command, folder)
        require(not re.search(r"(?:warning|error) (?:CS878[45]|CS803[24]|AD0001)", result.stdout), "A compiler asset failed to load or execute")
        if native:
            require(not re.search(r"(?:warning|error) IL\d+", result.stdout), "Unresolved native IL diagnostics")
        self.execute(name, framework, expected_provider, native)
        if settings.get("ElsaGenerateMetadata", "true").lower() == "true" and not host_disabled:
            require(any("ElsaGenerationBuildMarker" in file.read_text() for file in generated.rglob("*.cs")), "No generated build marker was emitted")
        if source_library:
            observed = (folder / "SourceLibrary/observed-mode.txt").read_text()
            require(f"framework={framework}" in observed and "host=false" in observed.lower() and f"mode={expected_child}" in observed, f"Unexpected actual child settings: {observed}")
            require("ELSA_GENERATED" not in observed and "ELSA_DYNAMIC" not in observed, "Host bootstrap constants leaked into a source library")
            (self.root / name / "observed-child.txt").write_text(observed)
        snapshot = ["dotnet", "msbuild", "Consumer.csproj", "-nologo", "-t:ResolveElsaCodeGenerationSettings", f"-p:TargetFramework={framework}", "-p:UseArtifactsOutput=true", f"-p:ArtifactsPath={self.root / name / 'build'}", *[f"-p:{key}={value}" for key, value in settings.items()], "-getProperty:ElsaExecutionMode,ElsaEffectiveExecutionMode,ElsaGenerateMetadata,ElsaIsHost,ElsaHostBootstrap,DefineConstants"]
        if native:
            snapshot.append(f"-p:RuntimeIdentifier={self.rid}")
        self.run(f"{name}/effective-properties", snapshot, folder)
        self.completed.append(name)

    def execute(self, name, framework, provider, native=False):
        output = self.root / name / ("publish" if native else f"build/bin/Consumer/release_{framework}")
        command = [output / "Consumer"] if native else ["dotnet", output / "Consumer.dll"]
        result = self.run(f"{name}/execution", [*command, "--expect-provider", provider, "--expect-native", str(native).lower()])
        lines = result.stdout.splitlines()
        require(lines == [f"probe=codegen-build provider={provider} dynamic-code={str(not native)} reflection-json=False", next((line for line in lines if line.startswith("framework=")), ""), "scenario=producer-lifecycle result=passed", "probe=codegen-build result=passed"], "Incomplete or contradictory executed provider evidence")
        require(any(line.startswith(f"framework=.NET {framework[3:].split('.')[0]}.") for line in lines), "Wrong executed framework")
        require(not any(output.glob("Microsoft.CodeAnalysis*.dll")), "Roslyn assemblies leaked into application output")
        dependencies = output / "Consumer.deps.json"
        if dependencies.exists():
            require("Microsoft.CodeAnalysis" not in dependencies.read_text(), "Roslyn leaked into application runtime dependencies")

    def negative(self, name, framework, settings, diagnostic, bootstrap="Compiler", missing_hook=False, manual_failure=None, nullable_entry=False):
        folder = self.consumer(name, bootstrap, missing_hook=missing_hook, manual_failure=manual_failure, nullable_entry=nullable_entry)
        args = self.arguments(name, framework, settings, settings.get("PublishAot") == "true")
        restore = self.run(f"{name}/restore", ["dotnet", "restore", "Consumer.csproj", "--force-evaluate", *args], folder, allow_failure=True)
        if restore.returncode:
            require(diagnostic in restore.stdout, f"Restore failed without expected {diagnostic}: {restore.stdout[-3000:]}")
        else:
            # SDK validation can run during restore or compile, depending on the
            # availability of NuGet's imported analyzer items.
            command = ["dotnet", "build", "Consumer.csproj", "-c", "Release", "--framework", framework, "--no-restore", *args]
            self.run(f"{name}/build", command, folder, expected=diagnostic)
        self.completed.append(name)

    def empty_export(self, framework, manual=False):
        name = "manual-empty-export-admission" if manual else "empty-export-admission"
        settings = {"ElsaExecutionMode": "Generated", "PublishAot": "false"}
        if manual:
            settings.update(ElsaGenerateMetadata="false", FixtureIncludeGenerator="false")
        folder = self.consumer(name, bootstrap="Manual" if manual else "Compiler", empty_export=True)
        self.restore(name, folder, framework, settings)
        self.run(f"{name}/build", ["dotnet", "build", "Consumer.csproj", "-c", "Release", "--framework", framework, "--no-restore", *self.arguments(name, framework, settings)], folder)
        binary = self.root / name / f"build/bin/Consumer/release_{framework}/Consumer.dll"
        expected = "at least one selected FeatureModuleExport"
        result = self.run(f"{name}/execution", ["dotnet", binary, "--expect-provider", "Generated", "--expect-native", "false"], expected=expected)
        require(result.returncode == 1 and "probe=codegen-build result=failed" in result.stdout, "Uncovered manual export was not rejected by runtime admission")
        self.completed.append(name)

    def transition(self, framework):
        name = "mode-transitions"
        folder = self.consumer(name)
        for index, mode in enumerate(("Dynamic", "Generated", "Dynamic")):
            settings = {"ElsaExecutionMode": mode, "PublishAot": "false"}
            self.restore(name, folder, framework, settings)
            self.run(f"{name}/build-{index}-{mode}", ["dotnet", "build", "Consumer.csproj", "-c", "Release", "--framework", framework, "--no-restore", *self.arguments(name, framework, settings)], folder)
            self.execute(name, framework, mode)
            shutil.copyfile(self.root / name / "execution.log", self.root / name / f"execution-{index}-{mode}.log")
        self.completed.append(name)

    def matrix(self):
        self.phase = "sdk-matrix"
        frameworks = ("net10.0", "net11.0") if self.options.framework == "all" else (self.options.framework,)
        jobs = []
        for framework in frameworks:
            for mode, expected in (("Auto", "Dynamic"), ("Dynamic", "Dynamic"), ("Generated", "Generated")):
                name = f"{framework}-{mode}-managed"
                jobs.append((name, lambda name=name, framework=framework, mode=mode, expected=expected: self.positive(name, framework, expected, {"ElsaExecutionMode": mode, "PublishAot": "false"})))
            for mode in ("Dynamic", "Generated"):
                name = f"{framework}-{mode}-manual-disabled"
                jobs.append((name, lambda name=name, framework=framework, mode=mode: self.positive(name, framework, mode, {"ElsaExecutionMode": mode, "ElsaGenerateMetadata": "false", "FixtureIncludeGenerator": "false", "PublishAot": "false"}, bootstrap="Manual")))
        framework = frameworks[-1]
        positives = [
            ("auto-unset", {}, "Dynamic", {}),
            ("trimmed-auto", {"PublishTrimmed": "true", "PublishAot": "false"}, "Dynamic", {}),
            ("case-insensitive", {"ElsaExecutionMode": "gEnErAtEd", "ElsaGenerateMetadata": "TrUe"}, "Generated", {}),
            ("late-app-setting", {}, "Generated", {"late_mode": "Generated"}),
            ("app-local-setting", {}, "Generated", {"local_mode": "Generated", "source_library": True, "expected_child": "Dynamic"}),
            ("global-setting", {"ElsaExecutionMode": "Generated"}, "Generated", {"source_library": True, "expected_child": "Generated"}),
            ("explicit-nonhost", {}, "Dynamic", {"bootstrap": "Manual", "host_disabled": True}),
        ]
        for name, settings, provider, extra in positives:
            jobs.append((name, lambda name=name, settings=settings, provider=provider, extra=extra: self.positive(name, framework, provider, settings, **extra)))
        negatives = [
            ("invalid-mode", {"ElsaExecutionMode": "invalid"}, "ELSACG001", {}),
            ("invalid-generation", {"ElsaGenerateMetadata": "invalid"}, "ELSACG002", {}),
            ("invalid-host", {"ElsaIsHost": "invalid"}, "ELSACG003", {}),
            ("invalid-bootstrap", {"ElsaHostBootstrap": "invalid"}, "ELSACG004", {}),
            ("dynamic-aot", {"ElsaExecutionMode": "Dynamic", "PublishAot": "true"}, "ELSACG005", {}),
            ("compiler-generation-disabled", {"ElsaGenerateMetadata": "false"}, "ELSACG006", {}),
            ("missing-generator", {"FixtureIncludeGenerator": "false"}, "ELSACG007", {}),
            ("missing-analyzer", {"FixtureIncludeAnalyzer": "false"}, "ELSACG008", {}),
            ("missing-build-imports", {"FixtureIncludeBuild": "false"}, "ELSAGEN001", {"bootstrap": "Manual"}),
            ("analyzers-disabled", {"RunAnalyzersDuringBuild": "false"}, "ELSACG009", {}),
            ("editorconfig-disabled", {"GenerateMSBuildEditorConfigFile": "false"}, "ELSACG010", {}),
            ("missing-export-hook", {"ElsaExecutionMode": "Generated"}, "ELSAGEN003", {"missing_hook": True}),
            ("nullable-host", {"ElsaExecutionMode": "Generated"}, "ELSAGEN002", {"nullable_entry": True}),
            ("manual-wrong-hook", {"ElsaExecutionMode": "Generated"}, "ELSAGEN004", {"bootstrap": "Manual", "manual_failure": "wrong"}),
            ("manual-discard-export", {"ElsaExecutionMode": "Generated"}, "ELSAGEN004", {"bootstrap": "Manual", "manual_failure": "discard"}),
            ("manual-unreachable-export", {"ElsaExecutionMode": "Generated"}, "ELSAGEN004", {"bootstrap": "Manual", "manual_failure": "unreachable"}),
        ]
        for name, settings, diagnostic, extra in negatives:
            jobs.append((name, lambda name=name, settings=settings, diagnostic=diagnostic, extra=extra: self.negative(name, framework, settings, diagnostic, **extra)))
        jobs.append(("mode-transitions", lambda: self.transition(framework)))
        jobs.append(("empty-export-admission", lambda: self.empty_export(framework)))
        jobs.append(("manual-empty-export-admission", lambda: self.empty_export(framework, manual=True)))
        if not self.options.skip_native:
            for native_framework in frameworks:
                for mode in ("Auto", "Generated"):
                    name = f"{native_framework}-{mode}-native"
                    jobs.append((name, lambda name=name, mode=mode, native_framework=native_framework: self.positive(name, native_framework, "Generated", {"ElsaExecutionMode": mode, "PublishAot": "true"}, native=True)))
            jobs.append(("manual-generated-native", lambda: self.positive("manual-generated-native", framework, "Generated", {"ElsaExecutionMode": "Generated", "ElsaGenerateMetadata": "false", "FixtureIncludeGenerator": "false", "PublishAot": "true"}, native=True, bootstrap="Manual")))
        self.expected = [name for name, _ in jobs]
        for name, job in jobs:
            self.phase = name
            job()
        require(self.completed == self.expected, f"Missing SDK matrix result: expected {self.expected}, completed {self.completed}")
        self.phase = "completed"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts-root", default=str(REPO / "artifacts/nativeaot/codegen-build"))
    parser.add_argument("--framework", choices=("all", "net10.0", "net11.0"), default="all")
    parser.add_argument("--skip-native", action="store_true", help="Explicitly omit native lanes for managed-only iteration")
    options = parser.parse_args()
    validation = Validation(options)
    status = 0
    try:
        validation.pack()
        validation.matrix()
    except CommandFailure as exception:
        status = exception.status
        print(exception, file=sys.stderr)
    except Exception as exception:
        status = 65
        print(exception, file=sys.stderr)
    finally:
        (validation.root / "result.json").write_text(json.dumps({"phase": validation.phase, "expected": validation.expected, "completed": validation.completed, "nativeOmitted": options.skip_native, "exitCode": status}, indent=2))
        print(f"G02 evidence: {validation.root} (exit {status}, phase {validation.phase})", flush=True)
    return status


if __name__ == "__main__":
    raise SystemExit(main())
