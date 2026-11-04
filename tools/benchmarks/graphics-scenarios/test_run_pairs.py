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
            summary = {'status': 'passed', 'frames': 2}
            rows = [{'sample': {'frame': 1}}, {'sample': {'frame': 2}}]
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

    def test_existing_output_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaises(FileExistsError):
                PAIRS.run(Path(temporary), 'smoke', 'one', 'dispersed', 64, 1, 'canvas', 2, 0)


if __name__ == '__main__':
    unittest.main()
