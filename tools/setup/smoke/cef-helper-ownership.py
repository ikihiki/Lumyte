#!/usr/bin/env python3
"""Pure ownership regressions; never launches or signals a real process."""
import argparse
import ast
import io
from pathlib import Path
import types
import unittest

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--launcher', type=Path, default=Path(__file__).resolve().parents[1] / 'run-cef-helper-isolated.sh')
args = parser.parse_args()
source = args.launcher.read_text()
inline = source.split("python3 -c '\n", 1)[1].split("\n' \"$self\"", 1)[0]
tree = ast.parse(inline)

class OwnershipTests(unittest.TestCase):
    def test_no_numeric_signaling(self):
        calls = [node.func.attr for node in ast.walk(tree) if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)]
        self.assertNotIn('killpg', calls)
        self.assertNotIn('kill', calls)
        self.assertIn('pidfd_open', calls)
        self.assertIn('pidfd_send_signal', calls)
        self.assertNotIn('kill -TERM "$supervisor"', source)
        self.assertIn('export LUMYTE_ISOLATED_OWNER=$$\n', source)

    def run_signal(self, snapshots, vanished=False):
        sent, closed = [], []
        remaining = iter(snapshots)
        def fake_open(path):
            return io.StringIO(next(remaining))
        def pidfd_open(pid):
            if vanished:
                raise ProcessLookupError()
            return 500 + pid
        namespace = dict(open=fake_open,
            os=types.SimpleNamespace(getpid=lambda: 1, pidfd_open=pidfd_open, close=closed.append),
            signal=types.SimpleNamespace(pidfd_send_signal=lambda fd, sig: sent.append((fd, sig))))
        functions = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in ('owned_children', 'signal_owned')]
        self.assertEqual(2, len(functions))
        exec(compile(ast.Module(body=functions, type_ignores=[]), '<ownership>', 'exec'), namespace)
        namespace['signal_owned'](15)
        return sent, closed

    def test_owned_identity_is_signaled(self):
        self.assertEqual(([(541, 15)], [541]), self.run_signal(['41', '41']))

    def test_reused_foreign_identity_is_not_signaled(self):
        # The numeric PID disappeared/reappeared while pidfd_open ran. It is no
        # longer a direct/adopted child: close the stable handle without signaling.
        self.assertEqual(([], [541]), self.run_signal(['41', '']))

    def test_vanished_child_is_ignored(self):
        self.assertEqual(([], []), self.run_signal(['41'], vanished=True))

if __name__ == '__main__':
    unittest.main(argv=['cef-helper-ownership'])
