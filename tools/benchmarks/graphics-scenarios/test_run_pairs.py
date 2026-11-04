import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('pairs', Path(__file__).with_name('run-pairs.py'))
PAIRS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PAIRS)


class PairDriverTests(unittest.TestCase):
    def invoke(self, mutate=None, exit_code=0):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        output = Path(temporary.name) / 'run'

        def fake_run(command, **kwargs):
            summary = {'status': 'passed', 'frames': 2, 'finalDrainIncluded': True, 'elapsedMs': 10}
            rows = [{'sample': dict(frame=i, inputId=str(i), gpuSpanMs=1, hostFrameMs=1, completionLatencyMs=1, retirementMs=0)} for i in range(1, 3)]
            manifest = {k: 'fixed' for k in ('sourceCommit', 'dirtyDiffSha256', 'scenarioVersion', 'seed', 'target', 'runtime', 'os', 'architecture', 'cpu', 'serverGc', 'environment', 'cache', 'contentProvenance')}
            manifest.update(schema='graphics-benchmarks-v1', timestampSupported=True, options={}, device='Radeon 8060S; vendor=0x1002; device=0x1586; type=IntegratedGpu; driver=radv')
            (output / 'manifest.json').write_text(json.dumps(manifest))
            (output / 'checks.jsonl').write_text(json.dumps({'check': dict(status='passed', exactRgba8=True, sha256='hash', inputId='0')}))
            if mutate:
                mutate(summary, rows)
            (output / 'summary.json').write_text(json.dumps(summary))
            (output / 'frames.jsonl').write_text('\n'.join(map(json.dumps, rows)))
            class Result:
                returncode = exit_code
            return Result()

        with patch.object(PAIRS.subprocess, 'run', side_effect=fake_run):
            return PAIRS.run(output, 'smoke', 'one', 'dispersed', 64, 1, 'canvas', 2, 0)

    def test_complete_frames(self):
        self.assertEqual(self.invoke()['frames'], 2)

    def test_missing_frames_are_not_success(self):
        with self.assertRaisesRegex(RuntimeError, 'Incomplete'):
            self.invoke(lambda summary, rows: rows.pop())

    def test_duplicate_frame_ids_are_not_success(self):
        with self.assertRaisesRegex(RuntimeError, 'Duplicate'):
            self.invoke(lambda summary, rows: rows[1]['sample'].update(frame=1))

    def test_failed_exit_is_not_success(self):
        with self.assertRaisesRegex(RuntimeError, 'failed'):
            self.invoke(exit_code=1)

    def test_failed_summary_is_not_success(self):
        with self.assertRaisesRegex(RuntimeError, 'failed'):
            self.invoke(lambda summary, rows: summary.update(status='failed'))

    def test_cross_variant_input_ids_rejected(self):
        a = dict(_compatibility={}, _inputIds=['a'])
        b = dict(_compatibility={}, _inputIds=['b'])
        with self.assertRaisesRegex(RuntimeError, 'logical input'):
            PAIRS.compatible(a, b)

    def test_cross_variant_provenance_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'Incompatible'):
            PAIRS.compatible(dict(_compatibility={'source': 1}), dict(_compatibility={'source': 2}))

    def test_missing_gpu_queries_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'Invalid raw'):
            self.invoke(lambda summary, rows: rows[0]['sample'].update(gpuSpanMs=None))

    def test_existing_output_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaises(FileExistsError):
                PAIRS.run(Path(temporary), 'smoke', 'one', 'dispersed', 64, 1, 'canvas', 2, 0)


    def test_burst_phase_spike_is_not_diluted_by_idle_medians(self):
        values = [100.0 if i % 240 in (29, 149) else 1.0 for i in range(960)]
        self.assertTrue(PAIRS.burst_stability(values, values)['stable'])
        changed = values[:]
        changed[29 + 3 * 240] = 106
        self.assertFalse(PAIRS.burst_stability(changed, values)['stable'])
        self.assertFalse(PAIRS.burst_stability(values, [None] * 960)['stable'])
        self.assertFalse(PAIRS.burst_stability(values[:-1], values[:-1])['stable'])


if __name__ == '__main__':
    unittest.main()
