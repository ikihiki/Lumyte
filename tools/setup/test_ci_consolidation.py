"""Required CI graph: immutable build artifacts feed independent test jobs."""
import re
import subprocess
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]


class ConsolidationTests(unittest.TestCase):
    def setUp(self):
        self.jobs = yaml.safe_load((ROOT / ".github/workflows/composition.yml").read_text())["jobs"]

    def test_native_build_once_matching_dependencies(self):
        jobs = self.jobs
        producers = {key for key in jobs if key.startswith("native-build-")}
        self.assertEqual(len(producers), 4)
        consumers = set()
        for key in producers:
            steps = jobs[key]["steps"]
            commands = "\n".join(step.get("run", "") for step in steps)
            self.assertEqual(commands.count("--target Compile") + commands.count("'--target', 'Compile'"), 1)
            self.assertNotIn("--build-lane", commands)
            self.assertNotIn("--lane", commands)
            self.assertIn("--host-only", commands)
            suffix = key.removeprefix("native-build-")
            for lane in ("integration", "quality", "cpu", "gpu"):
                consumer = lane + "-" + suffix
                if consumer not in jobs:
                    continue
                consumers.add(consumer)
                self.assertEqual(jobs[consumer]["needs"], [key])
                self.assertEqual(jobs[consumer]["runs-on"], jobs[key]["runs-on"])
                self.assertEqual(jobs[consumer]["env"]["LUMYTE_PREBUILT_CONSUMER"], "1")
        self.assertEqual(len(consumers), 8)
        self.assertIn("cpu-linux-x64", consumers)
        self.assertIn("gpu-linux-x64", consumers)
        self.assertNotIn("cpu-windows-x64", consumers)

    def test_consumers_cannot_rebuild_or_depend_on_output_cache(self):
        for key, job in self.jobs.items():
            if "needs" not in job or key == "package":
                continue
            steps = job["steps"]
            commands = "\n".join(step.get("run", "") for step in steps)
            self.assertNotIn("--target Compile", commands)
            self.assertNotIn("'--target', 'Compile'", commands)
            self.assertNotRegex(commands, r"dotnet (?:build|restore|publish)")
            self.assertTrue(any("actions/download-artifact@" in step.get("uses", "") for step in steps))
            self.assertFalse(any("setup-linux" in step.get("uses", "") or "cache-native" in step.get("uses", "") for step in steps))
            self.assertIn("prebuilt.py extract", commands)
            self.assertIn("LUMYTE_SETUP_TOOLS_ONLY", commands)
            self.assertFalse(any(step.get("continue-on-error") for step in steps))

    def test_browser_aot_and_final_gate(self):
        jobs = self.jobs
        browser = {"graphics", "text", "ui-application", "ui-navigation", "ui-pages", "ui-transitions", "audio"}
        for case in browser:
            job = jobs["browser-" + case]
            self.assertEqual(job["needs"], ["browser-build"])
            commands = "\n".join(step.get("run", "") for step in job["steps"])
            self.assertIn("--browser-case " + case + " --no-build --no-restore", commands)
            self.assertNotIn("--suite browser-contracts", commands)
        for group in ("balanced-1", "balanced-2"):
            job = jobs["aot-" + group]
            self.assertEqual(job["needs"], ["aot-build"])
            self.assertIn("--aot-group " + group, str(job))
        expected = {key for key in jobs if "needs" in jobs[key] and key != "package"}
        self.assertEqual(set(jobs["package"]["needs"]), expected)
        self.assertNotIn("if", jobs["package"])
        gpu = jobs["gpu-linux-x64"]["steps"]
        names = [step.get("name") for step in gpu]
        self.assertLess(names.index("Native / cold-cache maximum drawing program"), names.index("Cache ordinary GPU compiled shaders"))
        self.assertIn("--pipeline-validation", str(gpu))

    def test_bash_syntax(self):
        for job in self.jobs.values():
            for step in job["steps"]:
                if "run" in step and step.get("shell", "bash") == "bash":
                    script = re.sub(r"\$\{\{.*?\}\}", "CI_EXPRESSION", step["run"])
                    subprocess.run(["bash", "-n"], input=script, text=True, check=True)


if __name__ == "__main__":
    unittest.main()
