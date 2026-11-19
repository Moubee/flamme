"""Run the package consumer in separate processes against a disposable PostgreSQL database."""

import json
import os
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
from uuid import uuid4


def verify(assembly):
    definition = "mail-" + uuid4().hex

    def run(*args, target=definition):
        process = subprocess.run(
            ["dotnet", assembly, *args],
            env={**os.environ, "FLAMME_SAMPLE_DEFINITION": target},
            text=True, stdout=subprocess.PIPE, check=True,
        )
        return json.loads(process.stdout) if process.stdout.strip() else None

    def start(revision, target=definition):
        wait = run("start", target=target)
        assert wait["revision"] == revision and wait["status"] == "Suspended", wait
        assert wait["incidents"] == 0 and wait["lines"] == ["North opened"], wait
        bookmark, = wait["bookmarks"]
        assert bookmark["name"] == "Signals.Event", bookmark
        return wait

    def resume(wait, release):
        done = run("resume", wait["instance"])
        assert (done["instance"], done["revision"]) == (wait["instance"], wait["revision"]), done
        assert done["status"] == "Finished" and done["incidents"] == 0 and not done["bookmarks"], done
        assert done["lines"] == ["North receipt " + release, "South receipt Elsa.WriteLine"], done
        again = run("resume", wait["instance"])
        assert again["status"] == "Finished" and again["lines"] == [], again

    run("init")
    first = run("publish", "1.0.0")
    old = start(first["revision"])
    second = run("publish", "1.1.0")
    assert (first["version"], second["version"]) == (1, 2) and first["revision"] != second["revision"]
    resume(old, "1.0.0")
    fresh = start(second["revision"])
    assert old["bookmarks"] == fresh["bookmarks"]
    resume(fresh, "1.1.0")

    with TemporaryDirectory() as scratch:
        exported = Path(scratch) / "first.json"
        run("export", "1", str(exported))
        original = json.loads(exported.read_text())
        assert original["id"] == first["revision"]
        assert original["root"]["type"] == "ControlFlow.Sequence"
        activities = original["root"]["activities"]
        primitive_ids = [node["type"] for node in activities]
        assert primitive_ids == ["Console.WriteLine", "Signals.Event", "Console.WriteLine", "Console.WriteLine"]
        manifest = original["customProperties"]["flamme.authoring"]
        pins = {(r["publisher"], r["key"], r["major"], r["release"]) for r in manifest["releases"]}
        assert pins == {("org-north", "north.mail.Send", 1, "1.0.0"), ("org-south", "south.mail.Send", 1, "1.0.0")}
        mapped = [(s["activityId"], s["primitive"], s["version"]) for s in manifest["sourceMap"]]
        assert mapped == [(node["id"], node["type"], node["version"]) for node in activities]
        assert old["bookmarks"][0]["activityId"] == mapped[1][0]
        copy = definition + "-import"
        imported = run("import", str(exported), target=copy)
        run("export", "1", str(exported), target=copy)
        after_import = json.loads(exported.read_text())
        assert original["customProperties"] and original["customProperties"] == after_import["customProperties"]
        resume(start(imported["revision"], target=copy), "1.0.0")

    print(f"Verified PostgreSQL restart, redelivery and import/export for revisions {first['revision']} and {second['revision']}.")


if __name__ == "__main__":
    verify(str(Path(sys.argv[1]).resolve(strict=True)))
