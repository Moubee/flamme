"""Build the reference host in two modes and run paired fresh-process samples."""

import argparse
import json
import os
from pathlib import Path
import random
import shutil
import statistics
import sys
import subprocess
import tempfile


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
WEB = Path("src/apps/Elsa.Server.Web")
PROJECT = WEB / "Elsa.Server.Web.csproj"
MODES = ("Dynamic", "Generated")
GRAPHS = ("small", "web")


def prepare(source):
    # Include tracked working-tree edits while excluding every previous build output.
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).split(b"\0")
    for name in filter(None, tracked):
        path = Path(os.fsdecode(name))
        if (ROOT / path).is_file():
            (source / path).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / path, source / path)
    shutil.copy2(HERE / "CompositionProbe.cs", source / WEB)
    program = source / WEB / "Program.cs"
    original = program.read_text()
    builder = "var builder = WebApplication.CreateBuilder(args);"
    registration = "services\n    .AddElsa(elsa =>"
    after_apply = "// Obfuscate HTTP request headers."
    if any(original.count(anchor) != 1 for anchor in (builder, registration, after_apply)):
        raise ValueError("Locate the reference host's AddElsa call before measuring its composition.")
    prefix = original.split(after_apply)[0].replace(builder, """if (Environment.GetEnvironmentVariable("ELSA_PROBE_GRAPH") == "small")
{
    await CompositionProbe.RunSmall();
    return;
}
""" + builder).replace(registration, """var start = CompositionProbe.Start();
var module = services
    .AddElsa(elsa =>""")
    program.write_text(prefix + "var startup = CompositionProbe.Stop(start);\nawait CompositionProbe.Measure(module, startup);\npublic partial class Program;\n")


def build(source, output):
    subprocess.run(["dotnet", "restore", PROJECT, "--locked-mode", "-m:4"], cwd=source, check=True)
    for mode in MODES:
        subprocess.run(["dotnet", "publish", PROJECT, "-c", "Release", "--no-restore", "--no-self-contained", "-m:4",
                        f"-p:ElsaExecutionMode={mode}", "-o", output / mode], cwd=source, check=True)


def measure(output, samples):
    randomizer = random.Random(63)
    pairs = [(graph, mode) for graph in GRAPHS for mode in MODES]
    rows = []
    for sample in range(1, samples + 1):
        randomizer.shuffle(pairs)
        for graph, mode in pairs:
            env = dict(os.environ, ELSA_PROBE_GRAPH=graph, ASPNETCORE_ENVIRONMENT="Development",
                       DOTNET_TieredCompilation="0", Logging__LogLevel__Default="None")
            process = subprocess.run(["dotnet", "Elsa.Server.Web.dll"], cwd=output / mode,
                                     env=env, stdout=subprocess.PIPE, text=True)
            if process.returncode != 0:
                sys.stderr.write(process.stdout)
                process.check_returncode()
            row = json.loads(process.stdout.splitlines()[-1])
            if row["provider"] != mode:
                raise ValueError(f"{mode} host used {row['provider']}")
            row.update(graph=graph, sample=sample)
            print(json.dumps(row), flush=True)
            rows.append(row)
    if len({(r["runtime"], r["architecture"], r["serverGc"]) for r in rows}) != 1:
        raise ValueError("Runtime, architecture or GC mode differed between processes.")
    for graph in GRAPHS:
        group = [r for r in rows if r["graph"] == graph]
        if len({tuple(r["features"]) for r in group}) != 1:
            raise ValueError(f"{graph} modes installed different features")
    print("Median [P25, P75]")
    print("Graph Mode Features Startup_ms Startup_bytes Lookup_ns Workflows_per_second")
    for graph in GRAPHS:
        for mode in MODES:
            group = [r for r in rows if r["graph"] == graph and r["provider"] == mode]
            cells = []
            for metric in ("startupMs", "startupBytes", "lookupNs", "workflowsPerSecond"):
                values = [r[metric] for r in group]
                q1, _, q3 = statistics.quantiles(values, method="inclusive")
                cells.append(f"{statistics.median(values):.3f} [{q1:.3f},{q3:.3f}]")
            print(graph, mode, len(group[0]["features"]), *cells)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--samples", type=int, default=10, help="fresh processes per mode and graph")
    args = parser.parse_args()
    if args.samples < 2:
        parser.error("--samples must be at least two for quartiles")
    with tempfile.TemporaryDirectory(prefix="elsa-composition-") as directory:
        root = Path(directory)
        prepare(root / "source")
        build(root / "source", root / "output")
        measure(root / "output", args.samples)


if __name__ == "__main__":
    main()
