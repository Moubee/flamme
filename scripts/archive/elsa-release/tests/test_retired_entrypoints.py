from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class RetiredEntrypointTests(unittest.TestCase):
    def test_publisher_commands_refuse_execution_before_processing_inputs(self):
        archive = Path(__file__).resolve().parents[2]
        scripts = (
            archive / "elsa-release/scripts/release.py",
            archive / "elsa-release/scripts/release_train.py",
            archive / "elsa-release-announcements/scripts/post_discord.py",
        )
        with tempfile.TemporaryDirectory() as directory:
            for script in scripts:
                with self.subTest(script=script.name):
                    result = subprocess.run(
                        [sys.executable, str(script), "--execute"],
                        cwd=directory,
                        capture_output=True,
                        text=True,
                        timeout=5,
                    )
                    self.assertNotEqual(result.returncode, 0)
                    self.assertEqual(result.stdout, "")
                    self.assertIn("Retired upstream automation", result.stderr)
                    self.assertEqual(list(Path(directory).iterdir()), [])


if __name__ == "__main__":
    unittest.main()
