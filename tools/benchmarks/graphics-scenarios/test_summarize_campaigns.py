import importlib.util
from pathlib import Path
import tempfile
import unittest

import extension_pairs as e

SPEC = importlib.util.spec_from_file_location('inventory', Path(__file__).with_name('summarize-campaigns.py'))
INVENTORY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INVENTORY)


class InventoryTests(unittest.TestCase):
    def fixture(self, path, duration=11000, frames=2000, qualified=True, aa=False):
        e.dump(path / 'plan.json', dict(paths={'A': 'canvas', 'B': 'canvas' if aa else 'scene'},
               profile={'update': 'one'}, aaNoiseControl=aa, warmup=200))
        sample = dict(elapsedMs=duration, frames=frames, stageDistributions={'hostFrameMs': {'medianMs': 1}})
        pairs = [dict(pair=i + 1, order=['A', 'B'] if i % 2 == 0 else ['B', 'A'], summaries={'A': sample, 'B': sample}) for i in range(5)]
        e.dump(path / 'comparison.json', dict(status='measured' if qualified else 'unstable-or-duration-shortfall',
               minimumDurationMet=duration >= 10000, processPairs=pairs, medianHostRatioBoverA=1,
               medianThroughputRatioBoverA=1, scope='original scope retained'))
        pilot = path / 'pilot-A'
        pilot.mkdir()
        e.dump(pilot / 'manifest.json', {'sourceCommit': 'frozen-source'})

    def test_archive_bytes_and_aa_identity_are_recomputed(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            self.fixture(path, aa=True)
            e.archive(path)
            data = INVENTORY.campaign(path)
            self.assertEqual(data['kind'], 'A/A noise control')
            self.assertTrue(data['qualified'])
            self.assertEqual(data['formalProcessCount'], 10)
            self.assertEqual(data['archive']['compressedBytes'], (path / 'evidence.tar.gz').stat().st_size)
            self.assertEqual(data['archive']['sha256'], INVENTORY.digest(path / 'evidence.tar.gz'))
            self.assertEqual(data['archive']['uncompressedMemberBytes'], sum(data['archive']['rawFileBytes'].values()))

    def test_false_duration_and_frame_qualification_rejected(self):
        for duration, frames, expected in [(9999, 2000, 'duration'), (11000, 1999, 'budget')]:
            with tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary)
                self.fixture(path, duration, frames)
                with self.assertRaisesRegex(ValueError, expected):
                    INVENTORY.campaign(path)

    def test_shortfall_remains_visible_without_archive(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            self.fixture(path, duration=7000, qualified=False)
            data = INVENTORY.campaign(path)
            self.assertFalse(data['qualified'])
            self.assertFalse(data['minimumDurationMet'])
            self.assertIsNone(data['archive'])


if __name__ == '__main__':
    unittest.main()
