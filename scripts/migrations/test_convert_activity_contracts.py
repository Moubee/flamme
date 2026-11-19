import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


class ConversionTests(unittest.TestCase):
    tool = Path(__file__).with_name("convert-activity-contracts.py")

    def test_converts_only_executable_nodes_and_typed_configuration(self):
        literal = {"type": "Elsa.FlowJoin", "activities": [{"type": "Elsa.WriteLine"}]}
        root = {"id": "Elsa.Sequence", "type": "Elsa.Sequence", "name": "Elsa.Flowchart",
                "metadata": literal, "activities": [
                    {"id": "one", "type": "Elsa.WriteLine",
                     "text": {"typeName": "String", "expression": {"type": "Literal", "value": literal}}},
                    {"id": "two", "type": "Elsa.FlowJoin", "version": 1, "mode": {
                        "typeName": "Elsa.Workflows.Activities.Flowchart.Models.FlowJoinMode, Elsa.Workflows.Core",
                        "expression": {"type": "Literal", "value": "WaitAny"}}}]}
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory, "old.json"), Path(directory, "new.json")
            source.write_text(json.dumps({"root": root}))
            subprocess.run([sys.executable, self.tool, source, output], check=True)
            new = json.loads(output.read_text())["root"]
            self.assertEqual(json.loads(source.read_text())["root"], root)
            self.assertEqual((new["type"], new["version"]), ("ControlFlow.Sequence", 1))
            self.assertEqual(new["id"], "Elsa.Sequence")
            self.assertEqual(new["name"], "Elsa.Flowchart")
            self.assertEqual(new["metadata"], literal)
            first, join = new["activities"]
            self.assertEqual((first["type"], first["version"]), ("Console.WriteLine", 1))
            self.assertEqual(first["text"]["expression"]["value"], literal)
            self.assertEqual(join["type"], "ControlFlow.Join")
            self.assertEqual(join["mode"]["typeName"], "Flamme.Workflows.Activities.Flowchart.Models.FlowJoinMode, Flamme.Workflows.Core")

    def test_converts_log_strategies_without_changing_expressions_or_property_names(self):
        def strategy(prefix, name):
            return f"{prefix}.Workflows.LogPersistence.Strategies.{name}, {prefix}.Workflows.Core"

        literal = {"strategyType": strategy("Elsa", "Inherit")}

        def config(prefix):
            return {
                "default": {"strategyType": strategy(prefix, "Include")},
                "internalState": {"strategyType": strategy(prefix, "Inherit")},
                "inputs": {"secret": {"strategyType": strategy(prefix, "Exclude")}},
                "outputs": {"strategyType": {"strategyType": strategy(prefix, "Configuration"),
                                             "expression": {"type": "Literal", "value": literal}}},
            }

        old = {"customProperties": {"logPersistenceConfig": config("Elsa")},
               "root": {"type": "Elsa.WriteLine", "customProperties": {"logPersistenceConfig": config("Elsa")}}}
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory, "old.json"), Path(directory, "new.json")
            source.write_text(json.dumps(old))
            subprocess.run([sys.executable, self.tool, source, output], check=True)
            new = json.loads(output.read_text())
            for owner in (new, new["root"]):
                self.assertEqual(owner["customProperties"]["logPersistenceConfig"], config("Flamme"))

    def test_rejects_unmapped_per_property_log_strategies_before_writing_output(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory, "old.json"), Path(directory, "new.json")
            for section in ("inputs", "outputs"):
                with self.subTest(section=section):
                    config = {section: {"secret": {"strategyType": "Elsa.Custom.Strategy, Elsa.Custom"}}}
                    root = {"type": "Elsa.WriteLine", "customProperties": {"logPersistenceConfig": config}}
                    source.write_text(json.dumps({"root": root}))
                    result = subprocess.run([sys.executable, self.tool, source, output], capture_output=True, text=True)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn(f"$.root.customProperties.logPersistenceConfig.{section}.secret.strategyType", result.stderr)
                    self.assertFalse(output.exists())

    def test_rejects_unresolved_activities_and_versions_before_writing_output(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory, "old.json"), Path(directory, "new.json")
            for root in ({"type": "Someone.Send"}, {"type": "Elsa.WriteLine", "version": 2},
                         {"type": "Elsa.Workflows.Activities.ForEach<Int32>"}, {"type": "Elsa.ParallelForEach<String>"}):
                with self.subTest(root=root):
                    source.write_text(json.dumps({"root": root}))
                    result = subprocess.run([sys.executable, self.tool, source, output], capture_output=True, text=True)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("$.root", result.stderr)
                    self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
