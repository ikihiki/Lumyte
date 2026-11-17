#!/usr/bin/env python3
"""Development-only source guards plus real std-only native lifecycle tests."""
import os
from pathlib import Path
import subprocess
import unittest

REPO = Path(__file__).resolve().parents[3]
SOURCE = REPO / 'src/UI/Lumyte.UI.WebView.Cef.Supervisor'

class OwnershipTests(unittest.TestCase):
    def test_no_python_production_launcher(self):
        source = (REPO / 'tools/setup/run-cef-helper-isolated.sh').read_text()
        self.assertNotIn('python', source.lower())
        self.assertIn('--portal-session --owner "$$"', source)
        self.assertNotIn('kill -TERM "$supervisor"', source)

    def test_native_ownership_guards(self):
        source = (SOURCE / 'src/main.rs').read_text()
        self.assertNotIn('killpg', source)
        self.assertNotIn('read_to_end', source)
        self.assertIn('children()?.contains(&pid)', source)
        self.assertIn('close(0)', source)
        self.assertIn('close(1)', source)
        self.assertIn('getppid()', source)
        self.assertIn('dead(&owner)', source)

    def test_real_native_lifecycle(self):
        env = dict(os.environ)
        env.setdefault('CARGO_HOME', str(REPO/'artifacts/build/tools/cargo'))
        env.setdefault('RUSTUP_HOME', str(REPO/'artifacts/build/tools/rustup'))
        cargo = Path(env['CARGO_HOME'])/'bin/cargo'
        subprocess.run([str(cargo), '+1.99.1', 'test', '--locked', '--offline', '--release', '--manifest-path', str(SOURCE/'Cargo.toml'), '--target-dir', str(REPO/'artifacts/cef-supervisor/cargo'), '--', '--test-threads=1'], cwd=REPO, env=env, check=True, timeout=40)

if __name__ == '__main__':
    unittest.main()
