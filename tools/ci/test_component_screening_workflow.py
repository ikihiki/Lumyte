"""Execute workflow shell blocks against harmless fake commands (no YAML dependency)."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/component-screening.yml"


def block(step):
    text = WORKFLOW.read_text().split("      - name: " + step + "\n", 1)[1]
    text = text.split("        run: |\n", 1)[1]
    lines = []
    for line in text.splitlines():
        if line and not line.startswith("          "):
            break
        lines.append(line[10:])
    return "\n".join(lines)


class WorkflowTests(unittest.TestCase):
    def test_plan_shell_preserves_exact_arguments(self):
        for event in ("pull_request", "workflow_dispatch"):
            with self.subTest(event=event), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                script = root / "tools/benchmarks/component-screening/screen.py"
                script.parent.mkdir(parents=True)
                script.write_text("import json, sys; print(json.dumps(sys.argv[1:]))")
                env = dict(os.environ, BASE_SHA="a" * 40, HEAD_SHA="b" * 40, EVENT_NAME=event)
                result = subprocess.run(["bash", "-e", "-c", block("Select bounded component cases")],
                                        cwd=root, env=env, capture_output=True, text=True, check=True)
                import json
                expected = ["plan"] + (["--all"] if event == "workflow_dispatch" else [])
                expected += ["--base", "a" * 40, "--head", "b" * 40, "--output", "artifacts/component-screening"]
                self.assertEqual(expected, json.loads(result.stdout))

    def test_measure_shell_preserves_paths_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix="workflow contract ") as temporary:
            root = Path(temporary)
            script = root / "tools/benchmarks/component-screening/screen.py"
            script.parent.mkdir(parents=True)
            script.write_text("import json, sys; print(json.dumps(sys.argv[1:]))")
            result = subprocess.run(["bash", "-e", "-c", block("Measure base and head serially")],
                                    cwd=root, env=dict(os.environ, RUNNER_TEMP=str(root)),
                                    capture_output=True, text=True, check=True)
            import json
            self.assertEqual(["run", "--plan", "artifacts/component-screening/plan.json",
                              "--base-dir", str(root / "component-base"),
                              "--head-dir", str(root / "component-head"),
                              "--output", "artifacts/component-screening", "--timeout-seconds", "900"],
                             json.loads(result.stdout))

    def test_summary_shell_writes_actual_summary_file(self):
        with tempfile.TemporaryDirectory(prefix="workflow summary ") as temporary:
            root = Path(temporary)
            summary = root / "artifacts/component-screening/summary.md"
            summary.parent.mkdir(parents=True)
            summary.write_text("screening-result\n")
            target = root / "job summary.md"
            subprocess.run(["bash", "-e", "-c", block("Publish advisory summary")], cwd=root,
                           env=dict(os.environ, GITHUB_STEP_SUMMARY=str(target)), check=True)
            self.assertEqual("screening-result\n", target.read_text())

    def test_notification_keeps_pending_results_and_never_downloads_artifacts(self):
        text = (ROOT / ".github/workflows/component-screening-report.yml").read_text()
        self.assertIn("  cancel-in-progress: false\n  queue: max\n", text)
        self.assertNotIn("download-artifact@", text)
        self.assertIn("github.event.repository.default_branch", text)


if __name__ == "__main__":
    unittest.main()
