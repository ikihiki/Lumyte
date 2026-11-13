import argparse
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("screen", Path(__file__).resolve().parents[1] / "screen.py")
screen = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(screen)
CASE = screen.CATALOG[0]


def fixture():
    return {
        "HostEnvironmentInfo": {
            "BenchmarkDotNetVersion": "0.15.8", "OsVersion": "fixture OS",
            "ProcessorName": "fixture CPU", "Architecture": "X64", "RuntimeVersion": ".NET 10.0",
            "Configuration": "RELEASE", "DotNetCliVersion": "10.0.403",
        },
        "Benchmarks": [{
            "FullName": CASE["id"], "Parameters": "", "HardwareIntrinsics": "fixture",
            "DisplayInfo": "SignalsScreening-v2(OutlierMode=DontRemove, IterationCount=8, IterationTime=150ms, LaunchCount=1, WarmupCount=3)",
            "Statistics": {"Median": 10, "Mean": 10, "StandardDeviation": 0.1, "N": 8},
            "Memory": {"BytesAllocatedPerOperation": 0},
            "Measurements": [{"IterationStage": "Result", "IterationMode": "Workload",
                              "Nanoseconds": 1000, "Operations": 100} for _ in range(8)],
        }],
    }


class SelectionTests(unittest.TestCase):
    def test_docs_and_empty_skip(self):
        for paths in ([], ["README.md", "docs/performance/foo.md"]):
            self.assertEqual(screen.select(paths)[0], [])

    def test_all_forces_aa(self):
        self.assertEqual(len(screen.select([], True)[0]), 12)

    def test_signals_uses_capped_catalog(self):
        cases, uncovered, omissions = screen.select(["src/Core/Lumyte.Signals/Signal.cs"])
        self.assertEqual(len(cases), 12)
        self.assertFalse(uncovered)
        self.assertTrue(omissions)
        self.assertEqual(len({c['id'] for c in cases}), 12)

    def test_common_dependencies(self):
        for path in ["global.json", "Directory.Build.props", "src/Directory.Build.targets", "NuGet.config", "tools/foo.py", "build/Build.cs"]:
            self.assertEqual(len(screen.select([path])[0]), 12, path)

    def test_unknown_source_is_explicit_sentinel(self):
        cases, uncovered, omissions = screen.select(["src/Graphics/Renderer.cs"])
        self.assertEqual(cases, [CASE])
        self.assertEqual(uncovered, ["src/Graphics/Renderer.cs"])
        self.assertTrue(any("Graphics" in text for text in omissions))

    def test_mixed_known_and_unknown_preserves_uncovered(self):
        cases, uncovered, _ = screen.select(["src/Core/Lumyte.Signals/Signal.cs", "native/foo.cpp"])
        self.assertEqual(len(cases), 12)
        self.assertEqual(uncovered, ["native/foo.cpp"])

    def test_exact_sha_diff_not_merge_base_and_github_outputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "repo"
            repo.mkdir()
            def git(*args):
                return subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()
            git("init", "-q")
            git("config", "user.email", "fixture@example.invalid")
            git("config", "user.name", "fixture")
            (repo / "README.md").write_text("first")
            git("add", ".")
            git("commit", "-qm", "initial")
            base = git("rev-parse", "HEAD")
            git("checkout", "-qb", "other")
            (repo / "unknown.cs").write_text("source")
            git("add", ".")
            git("commit", "-qm", "source")
            head = git("rev-parse", "HEAD")
            output = Path(tmp) / "out"
            args = argparse.Namespace(repo=repo, base=head, head=base, output=output, all=False)
            gh = Path(tmp) / "github-output"
            with patch.dict(os.environ, {"GITHUB_OUTPUT": str(gh)}):
                screen.plan(args)
            data = json.loads((output / "plan.json").read_text())
            self.assertEqual(data['changed_paths'], ["unknown.cs"])
            self.assertIn("selected=true", gh.read_text())
            args.base = base
            screen.plan(args)
            self.assertTrue((output / "summary.md").exists())
            self.assertEqual(json.loads((output / "result.json").read_text())['status'], "skipped")


class LayoutTests(unittest.TestCase):
    def test_target_projects_and_relative_references(self):
        import xml.etree.ElementTree as xml
        for target in ('Signals', 'Mathematics'):
            directory = screen.ROOT / 'benchmarks/Core' / f'Lumyte.{target}.Benchmark'
            project = directory / f'Lumyte.{target}.Benchmark.csproj'
            self.assertTrue(project.is_file())
            for reference in xml.parse(project).findall('.//ProjectReference'):
                self.assertTrue(reference.attrib['Include'].startswith('../../../src/'))
                self.assertTrue((directory / reference.attrib['Include']).resolve().is_file())
            self.assertFalse(list((screen.ROOT / f'benchmarks/Lumyte.{target}.Benchmarks').glob('*.csproj')))
        self.assertFalse((screen.ROOT / 'benchmarks/Lumyte.Component.Screening').exists())
        self.assertFalse((screen.ROOT / screen.HARNESS / 'Sources').exists())
        self.assertTrue(all(c['id'].startswith('Lumyte.Signals.Benchmark.') for c in screen.CATALOG))

    def test_old_and_new_paths_select_same_catalog(self):
        for prefix in ('src/Core/Lumyte.Signals/', 'benchmarks/Lumyte.Signals.Benchmarks/',
                       'benchmarks/Core/Lumyte.Signals.Benchmark/'):
            cases, uncovered, _ = screen.select([prefix + 'ScalarBenchmarks.cs'])
            self.assertEqual(cases, screen.CATALOG)
            self.assertFalse(uncovered)
        for prefix in ('src/Core/Lumyte.Mathematics/', 'benchmarks/Lumyte.Mathematics.Benchmarks/',
                       'benchmarks/Core/Lumyte.Mathematics.Benchmark/'):
            path = prefix + 'Program.cs'
            cases, uncovered, _ = screen.select([path])
            self.assertEqual(cases, [CASE])
            self.assertEqual(uncovered, [path])

    def test_explicit_cli_modes_keep_normal_job_and_retention(self):
        program = (screen.ROOT / screen.HARNESS / 'Program.cs').read_text()
        for token in ('--retention', '--screening-smoke', '--screening',
                      'screening ? "SignalsScreening-v2" : "Signals"', 'OutlierMode.DontRemove'):
            self.assertIn(token, program)
        self.assertIn('argument != "--screening"', program)
        source = Path(screen.__file__).read_text()
        self.assertIn('binary, "--screening", "--filter"', source)
        self.assertIn('binary, "--screening-smoke"', source)

    def test_snapshot_overlays_whole_frozen_target(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo = root / 'repo'
            repo.mkdir()
            def git(*args):
                return subprocess.check_output(['git', '-C', str(repo), *args], text=True)
            git('init', '-q')
            git('config', 'user.email', 'fixture@example.invalid')
            git('config', 'user.name', 'fixture')
            product = repo / 'src/Core/Lumyte.Signals'
            product.mkdir(parents=True)
            (product / 'Lumyte.Signals.csproj').write_text('<Project />')
            stale = repo / screen.HARNESS
            stale.mkdir(parents=True)
            (stale / 'stale.cs').write_text('stale harness')
            git('add', '.')
            git('commit', '-qm', 'fixture')
            runner = screen.Runner(root, 10)
            for label in ('base', 'head'):
                project = screen.snapshot(repo, root / label, runner, label)
                self.assertEqual(project.name, 'Lumyte.Signals.Benchmark.csproj')
                self.assertEqual(screen.tree_hash(project.parent), screen.tree_hash(screen.ROOT / screen.HARNESS))
                self.assertFalse((project.parent / 'stale.cs').exists())
                self.assertTrue((project.parent / '../../../src/Core/Lumyte.Signals/Lumyte.Signals.csproj').resolve().exists())
                self.assertTrue((project.parent / 'RetentionProbe.cs').exists())


class DataTests(unittest.TestCase):
    def read_fixture(self, data):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)
            (path / "fixture-full.json").write_text(json.dumps(data))
            return screen.read_bdn(path, [CASE])

    def test_valid_zero_allocations(self):
        self.assertEqual(self.read_fixture(fixture())[CASE['id']]['allocated_bytes'], 0)

    def test_missing_file(self):
        with tempfile.TemporaryDirectory() as tmp, self.assertRaises(screen.Failure) as error:
            screen.read_bdn(Path(tmp), [CASE])
        self.assertEqual(error.exception.status, "missing_data")

    def test_missing_statistics_and_allocations(self):
        for key in ("Statistics", "Memory"):
            data = fixture()
            del data['Benchmarks'][0][key]
            with self.subTest(key=key), self.assertRaises(screen.Failure):
                self.read_fixture(data)

    def test_invalid_nan_and_infinite_values(self):
        for value in (None, float('nan'), float('inf'), -1, "10", True, 0):
            data = fixture()
            data['Benchmarks'][0]['Statistics']['Median'] = value
            with self.subTest(value=value), self.assertRaises(screen.Failure):
                self.read_fixture(data)

    def test_nonfinite_allocation(self):
        data = fixture()
        data['Benchmarks'][0]['Memory']['BytesAllocatedPerOperation'] = float('nan')
        with self.assertRaises(screen.Failure):
            self.read_fixture(data)

    def test_missing_case(self):
        data = fixture()
        data['Benchmarks'] = []
        with self.assertRaises(screen.Failure) as error:
            self.read_fixture(data)
        self.assertEqual(error.exception.status, "missing_data")

    def test_wrong_and_duplicate_identity(self):
        for duplicate in (False, True):
            data = fixture()
            if duplicate:
                data['Benchmarks'].append(copy.deepcopy(data['Benchmarks'][0]))
            else:
                data['Benchmarks'][0]['FullName'] += "Wrong"
            with self.assertRaises(screen.Failure) as error:
                self.read_fixture(data)
            self.assertEqual(error.exception.status, "incomparable")

    def test_missing_host_identity(self):
        data = fixture()
        del data['HostEnvironmentInfo']
        with self.assertRaises(screen.Failure):
            self.read_fixture(data)

    def test_wrong_job_and_parameters(self):
        for field, value in [("DisplayInfo", "Default"), ("DisplayInfo", "ComponentScreening-v1"), ("Parameters", "Depth=1")]:
            data = fixture()
            data['Benchmarks'][0][field] = value
            with self.assertRaises(screen.Failure) as error:
                self.read_fixture(data)
            self.assertEqual(error.exception.status, "incomparable")

    def test_missing_samples_or_outlier_removal(self):
        for field in ("Measurements", "Statistics"):
            data = fixture()
            if field == "Measurements":
                data['Benchmarks'][0][field].pop()
            else:
                data['Benchmarks'][0][field]['N'] = 7
            with self.assertRaises(screen.Failure):
                self.read_fixture(data)

    def test_threshold_requires_percent_and_absolute(self):
        base = self.read_fixture(fixture())
        for median, floor, candidate in [(11, 1, True), (10.9, 0.1, False), (11, 2, False), (9, 1, False)]:
            head = copy.deepcopy(base)
            head[CASE['id']]['median_ns'] = median
            row = screen.compare(base, head, [{**CASE, "min_delta_ns": floor}])[0]
            self.assertEqual(row['latency_candidate'], candidate)

    def test_allocation_increase_from_zero_is_separate(self):
        base = self.read_fixture(fixture())
        head = copy.deepcopy(base)
        head[CASE['id']]['allocated_bytes'] = 8
        row = screen.compare(base, head, [CASE])[0]
        self.assertFalse(row['latency_candidate'])
        self.assertTrue(row['allocation_increase'])
        self.assertIsNone(row['allocation_delta_percent'])

    def test_comparison_mismatched_case_and_runtime(self):
        base = self.read_fixture(fixture())
        with self.assertRaises(screen.Failure):
            screen.compare(base, {}, [CASE])
        head = copy.deepcopy(base)
        head[CASE['id']]['host_identity']['RuntimeVersion'] = 'other runtime'
        with self.assertRaises(screen.Failure) as error:
            screen.compare(base, head, [CASE])
        self.assertEqual(error.exception.status, "incomparable")


class ExecutionTests(unittest.TestCase):
    def test_timeout_kills_child_and_retains_log(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp)
            runner = screen.Runner(output, 0.1)
            with self.assertRaises(screen.Failure) as error:
                runner.call([sys.executable, '-c', 'import time; print("partial", flush=True); time.sleep(10)'], output, 'timeout')
            self.assertEqual(error.exception.status, 'timeout')
            self.assertIn('partial', (output / 'timeout.log').read_text())

    def test_failed_command_not_green(self):
        with tempfile.TemporaryDirectory() as tmp:
            runner = screen.Runner(Path(tmp), 5)
            with self.assertRaises(screen.Failure) as error:
                runner.call([sys.executable, '-c', 'raise SystemExit(4)'], tmp, 'failed')
            self.assertEqual(error.exception.status, 'execution_error')

    def test_finally_reports_invalid_plan_and_timeout(self):
        for status in ('incomparable', 'timeout'):
            with tempfile.TemporaryDirectory() as tmp:
                output = Path(tmp)
                plan = output / 'plan.json'
                screen.dump(plan, {'case_version': 2, 'harness_version': screen.VERSION, 'harness_sha256': screen.tree_hash(screen.ROOT / screen.HARNESS),
                                   'selected': True, 'cases': [CASE], 'base_sha': 'expected', 'head_sha': 'expected'})
                args = argparse.Namespace(output=output, plan=plan, base_dir=output, head_dir=output, timeout_seconds=5)
                with patch.object(screen.Runner, 'call', side_effect=screen.Failure(status, 'fixture failure')):
                    self.assertEqual(screen.run(args), 1)
                self.assertEqual(json.loads((output / 'result.json').read_text())['status'], status)
                self.assertIn(status, (output / 'summary.md').read_text())

    def test_legacy_case_version_is_incomparable_before_execution(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp)
            plan = output / 'plan.json'
            screen.dump(plan, {'harness_version': screen.VERSION, 'case_version': 1,
                               'harness_sha256': screen.tree_hash(screen.ROOT / screen.HARNESS)})
            args = argparse.Namespace(output=output, plan=plan, base_dir=output,
                                      head_dir=output, timeout_seconds=5)
            with patch.object(screen.Runner, 'call') as call:
                self.assertEqual(screen.run(args), 1)
                call.assert_not_called()
            self.assertEqual(json.loads((output / 'result.json').read_text())['status'], 'incomparable')

    def test_deadline_capped(self):
        import time
        runner = screen.Runner(Path('.'), 5000)
        self.assertLessEqual(runner.deadline - time.monotonic(), 900)


if __name__ == '__main__':
    unittest.main()
