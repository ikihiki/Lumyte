"""Deterministic analyzer compatibility and warmup exclusion regression."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class AnalyzerTests(unittest.TestCase):
    def test_warmup_excluded_and_legacy_measured_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'frames.jsonl'
            base = dict(suite='fixture', workload='case', count=1, scope='stages', hostFrameMs=2)
            rows = [{**base, 'phase': 'warmup', 'hostFrameMs': 999},
                    {**base, 'phase': 'measured'}, base]
            path.write_text('\n'.join(json.dumps(r) for r in rows))
            result = subprocess.run([sys.executable, '-B', str(Path(__file__).with_name('analyze-workloads.py')), str(path)],
                                    check=True, capture_output=True, text=True)
            metric = json.loads(result.stdout)['workloads'][0]['distributions']['hostFrameMs']
            self.assertEqual(metric['n'], 2)
            self.assertEqual(metric['median'], 2)


if __name__ == '__main__':
    unittest.main()
