"""Run after Release build under the activated SDK; invalid ICD proves parsing is pre-device."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
DLL = Path(__file__).with_name('bin') / 'Release/net10.0/Lumyte.Graphics.Incremental.Benchmarks.dll'
ENV = dict(os.environ, VK_DRIVER_FILES='/nonexistent/cli-test-no-gpu.json')


class CliTests(unittest.TestCase):
    def test_catalog_does_not_initialize_device(self):
        p = subprocess.run(['dotnet', str(DLL), '--workload-list'], env=ENV, capture_output=True, text=True, timeout=30)
        self.assertEqual(p.returncode, 0, p.stderr)
        rows = [json.loads(line) for line in p.stdout.splitlines()]
        self.assertEqual(len(rows), 12)
        self.assertEqual(len({r['name'] for r in rows}), 12)

    def test_rejections_precede_device_and_artifact_creation(self):
        cases = [(['--workload-hardware', '--count', '0'], '--count'),
                 (['--workload-hardware', '--samples', '0'], '--samples'),
                 (['--workload-hardware', '--target-width', '0'], '--target-width'),
                 (['--workload-hardware', '--warmup', '2001'], '--warmup'),
                 (['--workload-hardware', '--case', 'missing'], 'No workload matches'),
                 (['--workload-hardware', '--case', ''], 'Missing'),
                 (['--workload-hardware', '--count', '1', '--count', '2'], 'duplicate'),
                 (['--workload-hardware', '--quick'], '--quick requires'),
                 (['--workload-cpu', '--samples', '2'], 'Hardware options'),
                 (['--workload-hardware', '--mode', 'stages', '--frames-in-flight', '3'], 'one frame'),
                 (['--workload-cpu', '--quick', '--quick'], 'Duplicate'),
                 (['--workload-cpu', '--count'], 'Missing')]
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory) / 'must-not-exist'
            for args, message in cases:
                with self.subTest(args=args):
                    if args[0] == '--workload-cpu':
                        args = [*args, '--artifacts', str(out)]
                    p = subprocess.run(['dotnet', str(DLL), *args], env=ENV, capture_output=True, text=True, timeout=30)
                    self.assertNotEqual(p.returncode, 0)
                    self.assertIn(message.lower(), (p.stdout + p.stderr).lower())
                    for forbidden in ('VulkanDevice.Create', 'vkCreateInstance', 'ERROR_INCOMPATIBLE_DRIVER'):
                        self.assertNotIn(forbidden, p.stdout + p.stderr)
                    self.assertFalse(out.exists())


if __name__ == '__main__':
    unittest.main()
