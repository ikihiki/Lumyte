"""Required CI graph: immutable build artifacts feed independent test jobs."""
import re
import subprocess
import unittest
import xml.etree.ElementTree as ET
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
                self.assertEqual(jobs[consumer]["env"]["LUMYTE_PREBUILT_ROOT"], "$" + "{{ github.workspace }}")
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

    def test_retry_uploads_replace_instead_of_merging_attempts(self):
        for job in self.jobs.values():
            for step in job['steps']:
                if 'actions/upload-artifact@' in step.get('uses', ''):
                    self.assertIs(step['with']['overwrite'], True)
        self.assertEqual(self.jobs['package']['steps'][2]['with']['pattern'], 'parley-native-*')

    def test_runtime_validation_follows_tools_only_provisioning(self):
        for key, job in self.jobs.items():
            if 'needs' not in job or key == 'package':
                continue
            commands = '\n'.join(step.get('run', '') for step in job['steps'])
            self.assertIn('--check-runtime', commands)
            self.assertLess(commands.index('LUMYTE_SETUP_TOOLS_ONLY'), commands.index('--check-runtime'))
            extract = next(step['run'] for step in job['steps'] if 'prebuilt.py extract' in step.get('run', ''))
            self.assertNotIn('--check-runtime', extract)

    def test_streamed_gzip_transport_paths(self):
        for job in self.jobs.values():
            for step in job['steps']:
                if 'prebuilt.py' in step.get('run', ''):
                    self.assertIn('.tar.gz', step['run'])
                if 'actions/upload-artifact@' in step.get('uses', '') and step['with'].get('name', '').startswith('prebuilt-'):
                    self.assertTrue(step['with']['path'].endswith('.tar.gz'))
                    self.assertEqual(step['with']['compression-level'], 0)

    def test_runtime_roles_use_signed_helper_before_reverification(self):
        for key, job in self.jobs.items():
            if 'needs' not in job or key == 'package' or job['runs-on'].startswith('windows'):
                continue
            step = next(step for step in job['steps'] if step.get('name') == 'Provision tools and runtime only (no product compilation)')
            kind = 'browser' if key.startswith('browser-') else 'aot' if key.startswith('aot-') else 'native'
            self.assertIn('bash tools/setup/install-ci-runtime.sh ' + kind, step['run'])
            self.assertLess(step['run'].index('install-ci-runtime.sh'), step['run'].index('--check-runtime'))
            self.assertNotIn('sudo apt-get install', step['run'])

    def test_metadata_analysis_skips_compilers_and_keeps_actual_build_guards(self):
        props = ET.parse(ROOT / 'Directory.Build.props').getroot()
        compile_guard = next(t for t in props.findall('Target') if t.get('Name') == 'RejectPrebuiltProductCompilation')
        self.assertEqual(compile_guard.get('BeforeTargets'), 'CoreCompile')
        self.assertIn("'$(DesignTimeBuild)' != 'true' or '$(SkipCompilerExecution)' != 'true'", compile_guard.get('Condition'))
        preparation = next(t for t in props.findall('Target') if t.get('Name') == 'RejectPrebuiltProductPreparation')
        self.assertIn('Restore', preparation.get('BeforeTargets').split(';'))
        self.assertIn('CompileSlangShaders', preparation.get('BeforeTargets').split(';'))
        self.assertIn('Lumyte.ShaderCompiler', preparation.get('Condition'))
        slang = ET.parse(ROOT / 'tools/shaders/Slang.targets').getroot()
        target = next(t for t in slang.findall('Target') if t.get('Name') == 'CompileSlangShaders')
        self.assertIn("'$(DesignTimeBuild)' != 'true'", target.get('Condition'))
        self.assertIn('LUMYTE_PREBUILT_ROOT', ET.tostring(slang, encoding='unicode'))

    def test_windows_text_input_package_keeps_a_host_appropriate_owner(self):
        step = next(s for s in self.jobs['integration-windows-x64']['steps'] if s.get('name') == 'Distribution / Windows packages')
        self.assertIn('src/Platform/Lumyte.Platform.Windows.TextInput', step['run'])
        self.assertIn("'--no-build', '--no-restore'", step['run'])

    def test_bash_syntax(self):
        for job in self.jobs.values():
            for step in job["steps"]:
                if "run" in step and step.get("shell", "bash") == "bash":
                    script = re.sub(r"\$\{\{.*?\}\}", "CI_EXPRESSION", step["run"])
                    subprocess.run(["bash", "-n"], input=script, text=True, check=True)


if __name__ == "__main__":
    unittest.main()
