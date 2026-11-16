"""Deterministic provenance contract tests; no GPU or memory/time budgets."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import os
import sys

import build


class ProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        root = Path(self.temporary.name)
        self.binary = root / "native.so"
        self.binary.write_bytes(b"test-native-artifact")
        self.provenance = root / "provenance.json"
        self.proof = {"upstreamCommit": build.COMMIT, "archiveSha256": build.ARCHIVE_SHA,
                      "originalSourceSha256": build.ORIGINAL_SHA, "patchedSourceSha256": build.PATCHED_SHA,
                      "hostRid": "linux-x64", "binarySha256": hashlib.sha256(self.binary.read_bytes()).hexdigest()}
        self.save()

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        self.provenance.write_text(json.dumps(self.proof))

    def test_matching_artifact_is_accepted(self):
        with contextlib.redirect_stdout(io.StringIO()):
            build.verify(self.binary, self.provenance, "linux-x64")

    def test_wrong_runtime_is_rejected(self):
        with self.assertRaisesRegex(RuntimeError, "hostRid"):
            build.verify(self.binary, self.provenance, "win-arm64")

    def test_changed_binary_is_rejected(self):
        self.binary.write_bytes(b"changed")
        with self.assertRaisesRegex(RuntimeError, "SHA-256"):
            build.verify(self.binary, self.provenance, "linux-x64")

    def test_unpatched_source_provenance_is_rejected(self):
        self.proof["patchedSourceSha256"] = build.ORIGINAL_SHA
        self.save()
        with self.assertRaisesRegex(RuntimeError, "patchedSourceSha256"):
            build.verify(self.binary, self.provenance, "linux-x64")


class ToolEncodingTests(unittest.TestCase):
    def test_utf8_metadata_ignores_windows_default_code_page(self):
        expected = {"packages": [{"name": "ā_日本語", "manifest_path": "C:/利用者/ā/Cargo.toml"}]}
        payload = json.dumps(expected, ensure_ascii=False).encode("utf-8")
        command = [sys.executable, "-c", "import sys; sys.stdout.buffer.write(" + repr(payload) + ")"]
        with patch.object(build.subprocess, "_text_encoding", return_value="cp1252"):
            actual = json.loads(build.tool_output(command, os.environ.copy()))
        self.assertEqual(expected, actual)

    def test_invalid_utf8_is_rejected_not_replaced(self):
        command = [sys.executable, "-c", "import sys; sys.stdout.buffer.write(bytes([255]))"]
        with self.assertRaises(UnicodeDecodeError):
            build.tool_output(command, os.environ.copy())


if __name__ == "__main__":
    unittest.main()
