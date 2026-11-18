"""Parsed required-CI coverage and preparation contracts (no network/tool provisioning)."""
import re
import subprocess
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]


class ConsolidationTests(unittest.TestCase):
    """Expand native matrices and verify retained serial gates and trust boundaries."""

    def test_native_contexts(self):
        jobs = yaml.safe_load((ROOT / ".github/workflows/composition.yml").read_text())["jobs"]
        self.assertEqual(set(jobs), {"linux", "windows", "browser", "aot", "package"})
        self.assertEqual(jobs["package"]["needs"], ["linux", "windows", "browser", "aot"])
        self.assertNotIn("if", jobs["package"])
        self.assertEqual(sum(len(j.get("strategy", {}).get("matrix", {}).get("include", [None])) for j in jobs.values()), 7)
        for host in ("linux", "windows"):
            job = jobs[host]
            self.assertEqual([m["arch"] for m in job["strategy"]["matrix"]["include"]], ["x64", "aarch64"])
            steps = job["steps"]
            compile_steps = [s for s in steps if re.search(r"--target(?:', '|'? +)Compile", s.get("run", ""))]
            self.assertEqual(len(compile_steps), 1)
            self.assertNotIn("--build-lane", compile_steps[0]["run"])
            self.assertIn("--host-only", compile_steps[0]["run"])
            self.assertEqual(sum(s.get("uses", "").endswith("/cache-native") for s in steps), 2)
            self.assertEqual(sum("setup-linux" in s.get("uses", "") or "setup.ps1" in s.get("run", "") for s in steps), 1)
            self.assertFalse(any(s.get("continue-on-error") for s in steps))
            for s in steps:
                if "Quality /" in s.get("name", ""):
                    self.assertEqual(s["if"], "matrix.arch == 'x64'")
                if "--lane" in s.get("run", ""):
                    args = s["run"].split("--target")[-1]
                    self.assertIn("--no-build", args)
                    self.assertIn("--no-restore", args)
                if "test-results" in s.get("with", {}).get("name", ""):
                    self.assertIn("always()", s["if"])
        linux = jobs["linux"]["steps"]
        names = [s.get("name", "") for s in linux]
        for prefix in ("Run CPU", "Native / cold-cache", "Fingerprint software GPU", "Cache ordinary GPU", "GPU backend"):
            step = next(s for s in linux if s.get("name", "").startswith(prefix))
            self.assertEqual(step["if"], "matrix.arch == 'x64'")
            self.assertNotIn("GITHUB_ENV", step.get("run", ""))
        self.assertLess(names.index("Native / cold-cache maximum drawing program"), names.index("Cache ordinary GPU compiled shaders"))
        for host in ("linux", "windows"):
            for s in jobs[host]["steps"]:
                self.assertNotIn("LUMYTE_GRAPHICS_GPU_TESTS", s.get("env", {}))
        self.assertFalse(any("--lane cpu" in s.get("run", "") for s in jobs["windows"]["steps"]))

    def test_publish_contexts_and_shell_syntax(self):
        jobs = yaml.safe_load((ROOT / ".github/workflows/composition.yml").read_text())["jobs"]
        for name in ("browser", "aot"):
            job = jobs[name]
            self.assertNotIn("strategy", job)
            self.assertEqual(sum("setup-linux" in s.get("uses", "") for s in job["steps"]), 1)
        browser = jobs["browser"]["steps"]
        self.assertEqual(sum("setup-chrome" in s.get("uses", "") for s in browser), 1)
        calls = [s["run"] for s in browser if "--platform browser" in s.get("run", "")]
        self.assertEqual(len(calls), 1)
        self.assertNotIn("--browser-case", calls[0])
        self.assertFalse(any("--suite browser-contracts" in s.get("run", "") for s in browser))
        self.assertFalse(any("--aot-group" in s.get("run", "") for s in jobs["aot"]["steps"]))
        for job in jobs.values():
            for step in job["steps"]:
                if "run" in step and step.get("shell", "bash") == "bash":
                    script = re.sub(r"\$\{\{.*?\}\}", "CI_EXPRESSION", step["run"])
                    subprocess.run(["bash", "-n"], input=script, text=True, check=True)


if __name__ == "__main__":
    unittest.main()
