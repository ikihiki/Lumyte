"""Read-only integration validation of locally retained real runner artifacts.

These tests never execute runners. Absent ignored evidence is an explicit skip.
"""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import extension_pairs as e

SPEC = importlib.util.spec_from_file_location('core_pairs', Path(__file__).with_name('run-pairs.py'))
CORE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CORE)
ARTIFACTS = e.ROOT / 'artifacts/graphics-benchmarks'


class RetainedEvidenceTests(unittest.TestCase):
    def test_real_text_and_effect_artifact_shapes(self):
        count = 0
        for family, directory in [('Text', 'takeover-checks/final-text'), ('Effects', 'takeover-checks/final-effects'), ('Text', 'adr-completion/firstuse-text'), ('Effects', 'adr-completion/firstuse-effects')]:
            for manifest in sorted((ARTIFACTS / directory).glob('*manifest.json')):
                m = e.read(manifest)
                prefix = manifest.name[:-len('manifest.json')]
                with self.subTest(manifest=str(manifest)), tempfile.TemporaryDirectory() as t:
                    out = Path(t)
                    for p in manifest.parent.glob(prefix + '*'):
                        shutil.copyfile(p, out / p.name)
                    data = e.normalized(out, family, m.get('input', m.get('profile'))['Id'], m.get('method', 'vector'), m['depth'], m.get('frames', m.get('measuredFrames')), m['warmup'], m['source'], allow_smoke=True)
                    self.assertEqual(len(data['groups']), 1)
                    self.assertNotIn('firstUse', data['identity'])
                    self.assertNotIn('deviceInitializationMs', data['identity'])
                    count += 1
        if not count:
            self.skipTest('retained real Text/Effects evidence absent')

    def test_real_ui_completion_joins(self):
        count = 0
        for depth in (1, 3):
            p = ARTIFACTS / f'takeover-checks/final-ui-d{depth}.jsonl'
            if not p.exists():
                continue
            rows = e.lines(p)
            manifest = next(r for r in rows if r.get('scope') == 'manifest')
            for summary in [r for r in rows if r.get('scope') == 'pipeline-summary']:
                key = summary['Key']
                with self.subTest(key=key, depth=depth), tempfile.TemporaryDirectory() as t:
                    out = Path(t)
                    selected = [r for r in rows if 'Key' not in r or r['Key'] == key]
                    (out / 'stdout.log').write_text('\n'.join(json.dumps(r) for r in selected))
                    data = e.normalized(out, 'Ui', '/'.join(key.split('/')[:2]), 'vector', depth, summary['samples'], summary['warmup'], manifest['source'])
                    self.assertEqual(len(data['groups']), 1)
                    count += 1
        if not count:
            self.skipTest('retained real UI evidence absent')

    def test_real_primitive_all_case_groups(self):
        p = ARTIFACTS / 'takeover-checks/final-primitives'
        if not p.exists():
            self.skipTest('retained real Primitives evidence absent')
        m = e.read(p / 'manifest.json')
        for profile in ('G01', 'G02', 'G05', 'G06'):
            with self.subTest(profile=profile), tempfile.TemporaryDirectory() as t:
                out = Path(t)
                shutil.copyfile(p / 'manifest.json', out / 'manifest.json')
                summary = e.read(p / 'summary.json')
                summary['cases'] = [r for r in summary['cases'] if r['Id'] == profile]
                e.dump(out / 'summary.json', summary)
                for kind in ('frames', 'checks'):
                    rows = [r for r in e.lines(p / (kind + '.jsonl')) if 'Id' not in r or r['Id'] == profile]
                    (out / (kind + '.jsonl')).write_text('\n'.join(json.dumps(r) for r in rows))
                data = e.normalized(out, 'Primitives', profile, 'vector', 1, m['frames'], m['warmup'], m['sourceCommit'])
                self.assertEqual(len(data['groups']), len(summary['cases']))

    def test_real_core_warmup_offset_and_stage_distributions(self):
        p = ARTIFACTS / 'formal-ab-one64-d3-duration-qualified/pair-1-A'
        if not p.exists():
            self.skipTest('retained real core evidence absent')
        manifest, summary = e.read(p / 'manifest.json'), e.read(p / 'summary.json')
        result = CORE.validate(p, summary['frames'], manifest['options']['warmup'], manifest['options']['phase'])
        self.assertIn('prepareMs', result['stageDistributions'])
        self.assertIn('p95Ms', result['stageDistributions']['prepareMs'])
