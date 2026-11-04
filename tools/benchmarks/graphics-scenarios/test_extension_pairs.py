import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import extension_pairs as e

DEVICE = 'AMD Radeon 8060S; vendor=0x1002; device=0x1586; type=IntegratedGpu; driver=radv'


def jsonl(path, rows):
    path.write_text(''.join(json.dumps(r) + '\n' for r in rows))


class ExtensionTests(unittest.TestCase):
    def test_commands_actual_clis(self):
        for family in e.DEFAULTS:
            cmd = e.command(family, e.DEFAULTS[family], 'vector', 1, Path('runner.dll'), 'sha', Path('out'), 2000, 200)
            self.assertIn('--warmup', cmd)
            self.assertEqual('--output' in cmd, family != 'Ui')
            self.assertEqual('--depth' in cmd, family != 'Primitives')
        with self.assertRaises(ValueError):
            e.command('ImageTransfer', '', '', 1, Path('x'), '', Path('out'), 2000, 200)

    def test_phase_matched_stability(self):
        self.assertEqual(e.stability([1] * 200, [2] * 200)['warmup'], 200)
        self.assertFalse(e.stability([1] * 200, [None] * 200)['stable'])
        self.assertFalse(e.stability(list(range(1, 201)), [2] * 200)['stable'])
        self.assertFalse(e.stability([1] * 200, [1] * 200, 60)['stable'])
        self.assertEqual(e.stability([1] * 230, [1] * 230, 60)['warmup'], 230)

    def test_hardware_manifest_not_icd(self):
        self.assertEqual(e.hardware(DEVICE), DEVICE)
        for device in ['radeon_icd.json', DEVICE.replace('IntegratedGpu', 'Cpu'), DEVICE.replace('8060S', 'llvmpipe')]:
            with self.assertRaises(ValueError):
                e.hardware(device)

    def test_stats_duration_null_invalid(self):
        d = e.analyze(dict(groups=[('p', [1, 2, 3, 4], [None] * 4, 9999)]), 2)['p']
        self.assertFalse(d['minimumTenSeconds'])
        self.assertIsNone(d['gpu'])
        self.assertEqual(d['host']['budgetExceedances'], 2)
        self.assertEqual(d['host']['p95Ms'], 4)
        for value in [-1, float('nan'), float('inf'), True]:
            with self.assertRaises(ValueError):
                e.number(value)

    def effect(self, path):
        e.dump(path / 'case-manifest.json', dict(version=1, source='sha', depth=1, warmup=0,
               measuredFrames=200, profile={'Id': 'opaque-1080p'}, device=DEVICE, smoke=False))
        e.dump(path / 'case-summary.json', dict(status='passed', count=200,
               elapsedIncludingFinalDrainAndRetirementMs=1000))
        raw = [dict(FrameId=i, HostIncludingSlotWaitMs=1, GpuSpanMs=2,
                    SubmitToCompletionObservationMs=3, RetirementMs=0) for i in range(200)]
        jsonl(path / 'case-frames.jsonl', raw)
        jsonl(path / 'case-checks.jsonl', [dict(phase=p, slot=0, status='passed', hash='a',
              analyticMaximumError=0, tolerance=.1) for p in ('before', 'after')])
        return raw

    def test_effect_raw_validation(self):
        with tempfile.TemporaryDirectory() as t:
            p = Path(t)
            raw = self.effect(p)
            result = e.normalized(p, 'Effects', 'opaque-1080p', 'vector', 1, 200, 0, 'sha')
            self.assertEqual(len(result['groups'][0][1]), 200)
            raw[-1]['FrameId'] = 0
            jsonl(p / 'case-frames.jsonl', raw)
            with self.assertRaisesRegex(ValueError, 'frame IDs'):
                e.normalized(p, 'Effects', 'opaque-1080p', 'vector', 1, 200, 0, 'sha')
            self.effect(p)
            with self.assertRaisesRegex(ValueError, 'source'):
                e.normalized(p, 'Effects', 'opaque-1080p', 'vector', 1, 200, 0, 'other')
            jsonl(p / 'case-checks.jsonl', [])
            with self.assertRaises(ValueError):
                e.normalized(p, 'Effects', 'opaque-1080p', 'vector', 1, 200, 0, 'sha')

    def test_ui_completion_join_and_invalid_query(self):
        with tempfile.TemporaryDirectory() as t:
            p = Path(t)
            key = 'U01/hud-60/60hz/20rows'
            rows = [dict(scope='manifest', schemaVersion=1, scenarioVersion='ui-v2', source='sha', depth=1, actualDeviceAndDriver=DEVICE, deviceInitializationMs=1),
                    dict(scope='ui-preflight', Key=key, FontHash='font', NativeFontInitializationMs=1, InitialMountAndLayoutMs=2, firstTextLayoutMs=3),
                    dict(scope='pipeline-summary', Key=key, samples=2, warmup=0, includesFinalDrain=True, includesRetirement=True, elapsedMs=100)]
            for i in range(2):
                rows += [dict(scope='frame', Key=key, frame=i, inputId=str(i), hostFrameMs=1, slotWaitAndRetireMs=2),
                         dict(scope='completion', Key=key, Frame=i, InputId=str(i), CompletionMs=3, RetireMs=1, gpuQueryStatus='valid', gpuSpanMs=2)]
            rows += [dict(check='ui-independent-exact-pixels', Key=key, status='passed', analyticSentinel=True, sha256='a') for _ in range(7)]
            jsonl(p / 'stdout.log', rows)
            self.assertEqual(e.normalized(p, 'Ui', 'U01/hud-60', 'vector', 1, 2, 0, 'sha')['groups'][0][1], [3, 3])
            first = e.normalized(p, 'Ui', 'U01/hud-60', 'vector', 1, 2, 0, 'sha')['identity']
            rows[0]['deviceInitializationMs'] = 99
            rows[1]['NativeFontInitializationMs'] = 99
            jsonl(p / 'stdout.log', rows)
            self.assertEqual(first, e.normalized(p, 'Ui', 'U01/hud-60', 'vector', 1, 2, 0, 'sha')['identity'])
            rows[4]['InputId'] = 'wrong'
            jsonl(p / 'stdout.log', rows)
            with self.assertRaisesRegex(ValueError, 'input mismatch'):
                e.normalized(p, 'Ui', 'U01/hud-60', 'vector', 1, 2, 0, 'sha')
            rows[4]['InputId'] = '0'
            rows[4]['gpuQueryStatus'] = 'invalid'
            jsonl(p / 'stdout.log', rows)
            with self.assertRaisesRegex(ValueError, 'GPU query'):
                e.normalized(p, 'Ui', 'U01/hud-60', 'vector', 1, 2, 0, 'sha')

    def test_text_quality_methods_cannot_be_equivalent(self):
        with tempfile.TemporaryDirectory() as t:
            p = Path(t)
            self.effect(p)
            m = e.read(p / 'case-manifest.json')
            m.update(frames=200, input={'Id': 'fixed'}, method='msdf', fonts=['font'])
            e.dump(p / 'case-manifest.json', m)
            checks = [dict(phase=phase, state=state, slot=0, status='passed', sha256='a', quality={'status': 'passed'}) for phase in ('before', 'after') for state in (0, 1, 0)]
            jsonl(p / 'case-checks.jsonl', checks)
            e.dump(p / 'case-layout-check.json', {'status': 'passed'})
            with self.assertRaisesRegex(ValueError, 'profile/font'):
                e.normalized(p, 'Text', 'fixed', 'vector', 1, 200, 0, 'sha')

    def test_native_primitive_timestamp_status_and_capability(self):
        with tempfile.TemporaryDirectory() as t:
            p = Path(t)
            device = dict(vendor=0x1002, device=0x1586, type='IntegratedGpu', timestampInstrumented=True)
            manifest = dict(schema='graphics-primitives/1', scenarioVersion='v1', status='passed', sourceCommit='sha', frames=2, warmup=0,
                            actualDevice=device, measurementVersion='primitive-queue-timestamps-v2', dirtyDiffSha256='d', inputSha256='i', target={}, sourceHashes={})
            e.dump(p / 'manifest.json', manifest)
            e.dump(p / 'summary.json', dict(status='passed', cases=[dict(Id='G01', Name='draw', depth=1, count=2, elapsedIncludingFinalDrainMs=10)]))
            rows = [dict(Name='draw', depth=1, frame=i, inputId='draw:' + str(i), shaderSha256='s', completionLatencyMs=1, retirementMs=0,
                         outerHostMs=1, gpuSpanMs=2, gpuQueryStatus='valid') for i in range(2)]
            jsonl(p / 'frames.jsonl', rows)
            jsonl(p / 'checks.jsonl', [dict(check='pipelined-final-pixels', Name='draw', depth=1, status='passed'),
                                      dict(check='device-loss-recovery', status='not-measured')])
            data = e.normalized(p, 'Primitives', 'G01', 'vector', 1, 2, 0, 'sha')
            self.assertEqual(data['groups'][0][2], [2, 2])
            for invalid in ('pending', 'invalid', 'unsupported'):
                rows[0]['gpuQueryStatus'] = invalid
                jsonl(p / 'frames.jsonl', rows)
                with self.assertRaises(ValueError):
                    e.normalized(p, 'Primitives', 'G01', 'vector', 1, 2, 0, 'sha')

    def test_execute_failure_timeout_and_provenance(self):
        for result, prov in [(subprocess.CompletedProcess([], 1), [{}, {}]),
                             (subprocess.CompletedProcess([], 0), [{'source': 1}, {'source': 2}]),
                             (subprocess.TimeoutExpired([], 1), [{}, {}])]:
            with tempfile.TemporaryDirectory() as t, patch.object(e, 'provenance', side_effect=prov), patch.object(e.subprocess, 'run') as run:
                if isinstance(result, Exception):
                    run.side_effect = result
                else:
                    run.return_value = result
                p = Path(t) / 'process'
                with self.assertRaises(ValueError):
                    e.execute(p, ['fake'], {}, 1, Path(t), Path('fake'))
                self.assertTrue((p / 'execution.json').exists())
                self.assertTrue((p / 'provenance-after.json').exists())

    def test_archive_reproducible_and_hash_verified(self):
        with tempfile.TemporaryDirectory() as t:
            p = Path(t)
            (p / 'raw').write_text('evidence')
            e.archive(p)
            first = (p / 'evidence.tar.gz').read_bytes()
            e.archive(p)
            self.assertEqual(first, (p / 'evidence.tar.gz').read_bytes())
            self.assertEqual(e.verify_archive(p / 'evidence.tar.gz')['files']['raw'], e.sha(p / 'raw'))

    def test_stages_and_process_dispersion(self):
        stages = e.frame_stages([dict(prepareMs=1, CompletionMs=2), dict(prepareMs=3, CompletionMs=4)])
        data = dict(groups=[('p', [1, 3], [2, 4], 10000)], stageGroups={'p': stages})
        result = e.analyze(data, 2)['p']
        self.assertEqual(result['stages']['prepareMs']['medianMs'], 2)
        self.assertEqual(result['stages']['CompletionMs']['budgetExceedances'], 1)
        self.assertEqual(e.dispersion([1, 2, 3, 4, 5])['medianAbsoluteDeviation'], 1)

    def test_unqualified_retains_report_archive_and_exits_failure(self):
        for unstable, expected_calls in [(True, 2), (False, 12)]:
            with self.subTest(unstable=unstable), tempfile.TemporaryDirectory() as t:
                args = argparse.Namespace(output=Path(t) / 'campaign', family='Effects', profile=None,
                           method='vector', depth=1, pilot_frames=200, timeout=1, max_frames=100000,
                           pilot_only=False, budget_ms=16)
                calls = []
                def execute(path, cmd, *unused):
                    path.mkdir()
                    calls.append(cmd)
                    return {'source': 'same'}
                def normalize(path, *unused):
                    n = int(calls[-1][calls[-1].index('--frames') + 1])
                    return dict(identity={}, groups=[('p', [1] * n, [None if unstable else 2] * n, 1000)])
                with patch.object(e.subprocess, 'check_output', return_value=b'sha'), patch.object(e, 'execute', side_effect=execute), patch.object(e, 'normalized', side_effect=normalize):
                    with self.assertRaisesRegex(ValueError, 'not qualified'):
                        e.campaign(args)
                self.assertEqual(len(calls), expected_calls)
                self.assertEqual(e.read(args.output / 'comparison.json')['status'], 'not-qualified')
                self.assertTrue((args.output / 'campaign-failure.json').exists())
                e.verify_archive(args.output / 'evidence.tar.gz')

    def test_campaign_five_alternating_pairs_fixed_plan(self):
        with tempfile.TemporaryDirectory() as t:
            args = argparse.Namespace(output=Path(t) / 'campaign', family='Effects', profile=None,
                       method='vector', depth=1, pilot_frames=200, timeout=1, max_frames=100000,
                       pilot_only=False, budget_ms=16)
            calls = []
            def execute(path, cmd, *unused):
                path.mkdir()
                calls.append(cmd)
                if len(calls) > 2:
                    self.assertEqual(len(e.read(args.output / 'plan.json')['commands']), 12)
                return {'source': 'same'}
            def normalize(path, *unused):
                n = int(calls[-1][calls[-1].index('--frames') + 1])
                return dict(identity={'profile': 'same'}, groups=[('p', [1] * n, [2] * n, 1000 if n == 200 else 11000)])
            with patch.object(e.subprocess, 'check_output', return_value=b'sha\n'), patch.object(e, 'execute', side_effect=execute), patch.object(e, 'normalized', side_effect=normalize):
                e.campaign(args)
            self.assertEqual(len(calls), 12)
            plan = e.read(args.output / 'plan.json')
            self.assertEqual([x['label'] for x in plan['commands'][2:6]], ['pair-1-A', 'pair-1-B', 'pair-2-B', 'pair-2-A'])
            report = e.read(args.output / 'comparison.json')
            self.assertEqual(report['status'], 'measured')
            self.assertEqual(report['absoluteMedianDeltasBminusA'][0]['p'], {'host': 0, 'gpu': 0})
            self.assertEqual(report['processPairDispersion']['p']['hostRatio']['count'], 5)
            self.assertEqual(report['processPairDispersion']['p']['hostRatio']['sampleStdev'], 0)
            self.assertEqual(len({x['frames'] for x in plan['commands'][2:]}), 1)


if __name__ == '__main__':
    unittest.main()
