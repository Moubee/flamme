#!/usr/bin/env python3
"""Copy a portable Elsa JSON definition to Flamme, using the checked-in catalog.

Closed generic activities such as ForEach<Int32> require a host-specific mapping
to the concrete activity descriptor registered by the Flamme host.
"""
import argparse
import csv
import json
from pathlib import Path

CATALOG = Path(__file__).resolve().parents[2] / "doc/migrations/activity-contracts.csv"

# Paths below identify executable children. Never descend into arbitrary JSON values.
CHILDREN = {
    "ControlFlow.Flowchart": ("activities[]",),
    "ControlFlow.Sequence": ("activities[]",),
    "ControlFlow.Parallel": ("activities[]",),
    "ControlFlow.ForkBranches": ("branches[]",),
    "ControlFlow.If": ("then", "else"),
    "ControlFlow.For": ("body",),
    "ControlFlow.ForEach": ("body",),
    "ControlFlow.ParallelForEach": ("body",),
    "ControlFlow.While": ("body",),
    "ControlFlow.Node": ("body",),
    "ControlFlow.Switch": ("cases[].activity", "default"),
    "ControlFlow.StateMachine": ("states[].entry", "states[].exit", "transitions[].trigger", "transitions[].action"),
    "Http.SendRequest": ("expectedStatusCodes[].activity", "unmatchedStatusCode", "failedToConnect", "timeout"),
    "Workflows.Workflow": ("root",),
}

# These activities require a separate definition, delegate, missing plugin, or host binding.
HOST_BOUND = {
    "Scripting.Inline", "System.Dynamic", "System.HostMethod", "System.NotFound",
    "Workflows.Definition", "Workflows.Dispatch", "Workflows.BulkDispatch", "Workflows.Execute",
}

CLR_TYPES = {
    "Elsa.Workflows.Activities.Flowchart.Models.FlowJoinMode, Elsa.Workflows.Core":
        "Flamme.Workflows.Activities.Flowchart.Models.FlowJoinMode, Flamme.Workflows.Core",
    "Elsa.Http.Models.HttpHeaders, Elsa.Http": "Flamme.Http.HttpHeaders, Flamme.Http",
    "Elsa.Http.HttpHeaders, Elsa.Http": "Flamme.Http.HttpHeaders, Flamme.Http",
    "Elsa.Workflows.Services.WorkflowStorageDriver, Elsa.Workflows.Core":
        "Flamme.Workflows.Services.WorkflowStorageDriver, Flamme.Workflows.Core",
    "Elsa.Workflows.WorkflowInstanceStorageDriver, Elsa.Workflows.Core":
        "Flamme.Workflows.WorkflowInstanceStorageDriver, Flamme.Workflows.Core",
}
for strategy in ("Inherit", "Include", "Exclude", "Configuration"):
    old = f"Elsa.Workflows.LogPersistence.Strategies.{strategy}, Elsa.Workflows.Core"
    CLR_TYPES[old] = old.replace("Elsa.", "Flamme.")


def convert_clr_field(owner, key, path):
    name = owner.get(key)
    if isinstance(name, str) and "Elsa." in name:
        if name not in CLR_TYPES:
            raise ValueError(f"{path}.{key}: map CLR type {name!r} for this installation")
        owner[key] = CLR_TYPES[name]


def convert_declarations(owner, path):
    for key in ("variables", "inputs", "outputs"):
        for i, item in enumerate(owner.get(key) or []):
            for field in ("type", "typeName", "storageDriverType", "storageDriverTypeName"):
                convert_clr_field(item, field, f"{path}.{key}[{i}]")
    custom = owner.get("customProperties") or {}
    if custom.get("Elsa:WorkflowContextProviderTypes"):
        raise ValueError(f"{path}: map the workflow context providers for this installation")
    config = custom.get("logPersistenceConfig") or {}
    config_path = f"{path}.customProperties.logPersistenceConfig"
    for section in ("default", "internalState"):
        convert_clr_field(config.get(section) or {}, "strategyType", f"{config_path}.{section}")
    for section in ("inputs", "outputs"):
        for prop, item in (config.get(section) or {}).items():
            convert_clr_field(item or {}, "strategyType", f"{config_path}.{section}.{prop}")


def child_objects(owner, selector, path):
    first, _, rest = selector.partition(".")
    key = first.removesuffix("[]")
    value = owner.get(key)
    if value is None:
        return
    path += "." + key
    if first.endswith("[]"):
        if not isinstance(value, list):
            raise ValueError(f"{path}: expected an array")
        items = ((item, f"{path}[{i}]") for i, item in enumerate(value))
    else:
        items = ((value, path),)
    for item, location in items:
        if not isinstance(item, dict):
            raise ValueError(f"{location}: expected an object")
        if rest:
            yield from child_objects(item, rest, location)
        else:
            yield item, location


def convert_activity(node, path, contracts):
    old = node.get("type")
    if not isinstance(old, str) or old not in contracts:
        raise ValueError(f"{path}.type: manually bind unknown or specialized activity {old!r}")
    name, version = contracts[old]
    if name in HOST_BOUND:
        raise ValueError(f"{path}.type: {old!r} needs its host binding or dependent workflow")
    requested = node.get("version", version)
    if type(requested) is not int or requested != version:
        raise ValueError(f"{path}.version: no mapping for {old!r} version {requested!r}")
    node.update(type=name, version=version)
    convert_declarations(node, path)
    if isinstance(node.get("variable"), dict):
        for key in ("type", "typeName", "storageDriverType", "storageDriverTypeName"):
            convert_clr_field(node["variable"], key, f"{path}.variable")
    for key, wrapper in node.items():
        if key in {"customProperties", "metadata"} or not isinstance(wrapper, dict):
            continue
        if "expression" in wrapper or "memoryReference" in wrapper:
            convert_clr_field(wrapper, "typeName", f"{path}.{key}")
    for selector in CHILDREN.get(name, ()):
        for child, location in child_objects(node, selector, path):
            convert_activity(child, location, contracts)


def convert(definition):
    if not isinstance(definition, dict) or not isinstance(definition.get("root"), dict):
        raise ValueError("expected one exported JSON workflow definition containing a root object")
    with CATALOG.open(encoding="utf-8") as source:
        contracts = {row["Legacy activity type"]: (row["Published activity type"], int(row["Version"]))
                     for row in csv.DictReader(source)}
    convert_declarations(definition, "$")
    convert_activity(definition["root"], "$.root", contracts)
    return definition


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path, help="new output file")
    args = parser.parse_args()
    try:
        with args.source.open(encoding="utf-8-sig") as source:
            result = convert(json.load(source))
        with args.destination.open("x", encoding="utf-8") as output:
            json.dump(result, output, indent=2, ensure_ascii=False)
            output.write("\n")
    except (OSError, ValueError, TypeError, AttributeError) as error:
        parser.exit(1, f"Conversion failed: {error}\n")


if __name__ == "__main__":
    main()
