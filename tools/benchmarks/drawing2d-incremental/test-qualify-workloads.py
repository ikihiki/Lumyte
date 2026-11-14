"""Run: python3 -B tools/benchmarks/drawing2d-incremental/test-qualify-workloads.py"""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('qualify', Path(__file__).with_name('qualify-workloads.py'))
q = importlib.util.module_from_spec(spec)
spec.loader.exec_module(q)


def frames(n, phase='warmup', host=1., gpu=.5):
    return [dict(workload='case', phase=phase, iteration=i, scope='stages',
                 hostFrameMs=host, gpuRecordedSpanMs=gpu, frameCpuWallMs=5.) for i in range(n)]


def run(i=0, measured=20):
    return {'path': '/tmp/process-' + str(i), 'sha256': str(i),
            'identity': dict(check='active-device', count=200, device='actual GPU; driver=x',
                             targetWidth=100, targetHeight=100, dpr=1, format='RGBA',
                             depth=1, scope='stages', source='commit-A', traceHash='trace',
                             profile='default', runId=str(i)),
            'groups': {'case': {'warmup': frames(200), 'measured': frames(measured, 'measured')}}}


class QualificationTests(unittest.TestCase):
    def setUp(self):
        self.plan = q.policy(200)
        self.absolute = dict(zip(q.METRICS, (.1, .1)))

    def aa(self, runs):
        return q.aa_report(runs, self.plan, .05, self.absolute)

    def test_stable_and_exact_boundary(self):
        rows = frames(200)
        for row in rows[-50:]:
            row['hostFrameMs'] = 1.05
        self.assertEqual(q.stability(rows, self.plan)['status'], 'stable')
        rows[-1]['hostFrameMs'] = 2  # one outlier is retained but median does not change
        self.assertEqual(q.stability(rows, self.plan)['status'], 'stable')

    def test_earliest_never_overrides_final(self):
        rows = frames(250)
        for r in rows[-50:]:
            r['gpuRecordedSpanMs'] = 1
        result = q.stability(rows, q.policy(250))
        self.assertEqual(result['earliestStableFrames'], 200)
        self.assertEqual(result['status'], 'unstable')

    def test_missing_gpu_not_cpu_substitute(self):
        rows = frames(200, gpu=None)
        result = q.stability(rows, self.plan)
        self.assertEqual(result['status'], 'unmeasured')
        self.assertEqual(result['final']['metrics']['hostFrameMs'], 'stable')
        self.assertIsNone(result['final']['ratios']['gpuRecordedSpanMs'])

    def test_aligned_cycles(self):
        rows = frames(400)
        for i, row in enumerate(rows):
            row['hostFrameMs'] = 1 if i % 100 < 50 else 3
        self.assertEqual(q.stability(rows, q.policy(400))['status'], 'unstable')
        self.assertEqual(q.stability(rows, q.policy(400, 100))['status'], 'stable')

    def test_bounded_plan_and_exact_count(self):
        for n, cycle in ((150, 50), (2050, 50), (201, 50), (200, 75), (200, 100)):
            with self.subTest(n=n, cycle=cycle), self.assertRaises(ValueError):
                q.policy(n, cycle)
        with self.assertRaises(ValueError):
            q.stability(frames(250), self.plan)
        self.assertEqual(q.stability(frames(2000), q.policy(2000))['status'], 'stable')

    def test_aa_independent_files_and_dispersion(self):
        runs = [run(i) for i in range(5)]
        report = self.aa(runs)
        self.assertEqual(report['status'], 'qualified')
        self.assertEqual(report['cases']['case']['metrics']['hostFrameMs']['dispersion']['n'], 5)
        with self.assertRaises(ValueError):
            self.aa(runs[:4])
        with self.assertRaises(ValueError):
            self.aa([runs[0]] * 5)

    def test_aa_noisy_and_missing_and_unstable(self):
        for mode in ('noisy', 'missing', 'unstable'):
            runs = [run(i) for i in range(5)]
            if mode == 'noisy':
                for row in runs[-1]['groups']['case']['measured']:
                    row['hostFrameMs'] = 1.2
            elif mode == 'missing':
                runs[-1]['groups']['case']['measured'][0]['gpuRecordedSpanMs'] = None
            else:
                for row in runs[-1]['groups']['case']['warmup'][-50:]:
                    row['hostFrameMs'] = 2
            self.assertEqual(self.aa(runs)['status'], 'not-qualified', mode)

    def test_aa_absolute_and_relative_both_required(self):
        runs = [run(i) for i in range(5)]
        for row in runs[-1]['groups']['case']['measured']:
            row['hostFrameMs'] = 1.04
        report = q.aa_report(runs, self.plan, .05, dict(zip(q.METRICS, (.01, .1))))
        self.assertEqual(report['status'], 'not-qualified')

    def test_incompatible_trace_source_target_and_missing_metadata(self):
        for key in ('traceHash', 'source', 'targetWidth', 'profile', 'device', 'depth'):
            runs = [run(i) for i in range(5)]
            runs[-1]['identity'][key] = 'different'
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.aa(runs)
        runs = [run(i) for i in range(5)]
        del runs[-1]['identity']['traceHash']
        with self.assertRaises(ValueError):
            self.aa(runs)

    def parse(self, rows):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'run.jsonl'
            path.write_text('\n'.join(json.dumps(r) for r in rows))
            return q.load_run(path)

    def valid_rows(self):
        return [run()['identity']] + frames(200) + frames(20, 'measured')

    def test_parser_valid(self):
        result = self.parse(self.valid_rows())
        self.assertEqual(len(result['groups']['case']['warmup']), 200)
        self.assertEqual(len(result['sha256']), 64)

    def test_parser_invalid_zero_nonfinite_boolean(self):
        for metric in q.METRICS:
            for value in (0, -1, float('nan'), float('inf'), True, '1'):
                rows = self.valid_rows()
                rows[1][metric] = value
                with self.subTest(metric=metric, value=value), self.assertRaises(ValueError):
                    self.parse(rows)

    def test_parser_missing_phase_duplicate_iteration_failed_check(self):
        for mode in ('phase', 'iteration', 'count', 'invalid-query', 'failed-check', 'array'):
            rows = self.valid_rows()
            if mode == 'phase':
                del rows[1]['phase']
            elif mode == 'iteration':
                rows[2]['iteration'] = 0
            elif mode == 'count':
                rows[1]['count'] = 3
            elif mode == 'invalid-query':
                rows[1]['gpuQueryValid'] = False
            elif mode == 'array':
                rows.append([])
            else:
                rows.append(dict(check='correctness', status='failed'))
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                self.parse(rows)

    def test_duplicate_json_keys(self):
        with self.assertRaisesRegex(ValueError, 'duplicate JSON key'):
            json.loads('{"phase":"warmup","phase":"measured"}', object_pairs_hook=q.unique_object)

    def test_parser_does_not_discard_log_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'bad.jsonl'
            path.write_text('log line\n{}\n')
            with self.assertRaisesRegex(ValueError, ':1:'):
                q.load_run(path)

    def formal_inputs(self):
        labels = list('ABBAABBAAB')
        runs = [run(i + 10, 2000) for i in range(10)]
        for r, label in zip(runs, labels):
            r['identity']['source'] = 'commit-' + label
        return runs, labels, [run(i, 2000) for i in range(5)]

    def formal(self, runs, labels, aa):
        return q.formal_report(runs, labels, aa, self.plan, .05, self.absolute)

    def test_formal_validated_not_target_claim(self):
        report = self.formal(*self.formal_inputs())
        self.assertEqual(report['status'], 'evidence-qualified')
        self.assertEqual(len(report['pairs']), 5)
        self.assertEqual(report['pairs'][0]['case']['hostFrameMs']['ratioBoverA'], 1.)

    def test_formal_missing_provenance_order_repetitions(self):
        runs, labels, aa = self.formal_inputs()
        for args in ((runs[:8], labels[:8], aa), (runs, ['A', 'B'] * 5, aa), (runs, labels, [])):
            with self.assertRaises(ValueError):
                self.formal(*args)
        aa[0]['identity']['source'] = 'unrelated'
        with self.assertRaises(ValueError):
            self.formal(runs, labels, aa)

    def test_formal_duration_and_count_and_missing_gpu(self):
        for mode in ('duration', 'missing-duration', 'count', 'gpu'):
            runs, labels, aa = self.formal_inputs()
            rows = runs[0]['groups']['case']['measured']
            if mode == 'count':
                rows.pop()
            elif mode == 'gpu':
                rows[0]['gpuRecordedSpanMs'] = None
            elif mode == 'missing-duration':
                del rows[0]['frameCpuWallMs']
            else:
                for row in rows:
                    row['frameCpuWallMs'] = 1.
            self.assertEqual(self.formal(runs, labels, aa)['status'], 'not-qualified', mode)

    def test_formal_distinct_binaries_and_consistent_variants(self):
        labels = ['A', 'B', 'B', 'A', 'A', 'B', 'B', 'A', 'A', 'B']
        aa = [run(100 + i, 2000) for i in range(5)]
        runs = [run(i, 2000) for i in range(10)]
        for r in aa:
            r['identity']['binaryHash'] = 'binary-A'
        for r, label in zip(runs, labels):
            r['identity']['source'] = 'commit-' + label
            r['identity']['binaryHash'] = 'binary-' + label
        self.assertEqual(self.formal(runs, labels, aa)['status'], 'evidence-qualified')
        runs[3]['identity']['binaryHash'] = 'changed-within-A'
        with self.assertRaisesRegex(ValueError, 'one binaryHash'):
            self.formal(runs, labels, aa)
        runs[3]['identity']['binaryHash'] = 'binary-A'
        for r in aa:
            r['identity']['binaryHash'] = 'not-formal-A'
        with self.assertRaisesRegex(ValueError, 'A/A binaryHash'):
            self.formal(runs, labels, aa)

    def test_declared_counts_and_summary_must_match(self):
        for extra in ({'samples': 2001}, {'warmup': 201}):
            rows = self.valid_rows()
            rows[0].update(extra)
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / 'truncated.jsonl'
                path.write_text('\n'.join(json.dumps(r) for r in rows))
                with self.assertRaisesRegex(ValueError, 'count differs'):
                    q.load_run(path)
        rows = self.valid_rows()
        rows.append(dict(check='stages-summary', workload='case', frames=999))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'summary.jsonl'
            path.write_text('\n'.join(json.dumps(r) for r in rows))
            with self.assertRaisesRegex(ValueError, 'summary frame count'):
                q.load_run(path)

    def test_placeholder_source_rejected(self):
        runs = [run(i) for i in range(5)]
        for r in runs:
            r['identity']['source'] = 'unrecorded'
        with self.assertRaisesRegex(ValueError, 'provenance'):
            q.compatible(runs)

    def test_cli_invalid_json_report_and_pilot(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'run.jsonl'
            path.write_text('\n'.join(json.dumps(r) for r in self.valid_rows()))
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = q.main(['pilot', '--warmup-frames', '200', str(path)])
            self.assertEqual(code, 0)
            self.assertEqual(json.loads(output.getvalue())['status'], 'stable')
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(q.main(['pilot', '--warmup-frames', '150', str(path)]), 2)


if __name__ == '__main__':
    unittest.main()
