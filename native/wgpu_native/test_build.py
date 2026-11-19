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


class PreparationTests(unittest.TestCase):
    def test_preparation_fetches_but_never_builds_or_stages_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "output"
            source = output / ("wgpu-native-" + build.COMMIT)
            (source / "src").mkdir(parents=True)
            (source / "ffi/webgpu-headers").mkdir(parents=True)
            (source / "src/lib.rs").write_bytes(b"patched")
            (source / "Cargo.lock").write_bytes(b"locked")
            (root / "source-info.json").write_text(json.dumps({
                "archiveSha256": build.ARCHIVE_SHA, "headerSha256": build.HEADER_SHA,
                "cargoLockSha256": hashlib.sha256(b"locked").hexdigest()}))
            (output / "provenance.json").write_bytes(b"must not stage")
            arguments = ["build.py", "--output", str(output), "--prepare-only", "--cargo", "pinned-cargo"]
            def version(arguments, environment):
                return "cargo 1.99.1 (pinned)" if arguments[0] == "pinned-cargo" else "rustc 1.99.1 (pinned)"
            with patch("sys.argv", arguments), patch.object(build, "__file__", str(root / "build.py")), \
                    patch.object(build, "PATCHED_SHA", hashlib.sha256(b"patched").hexdigest()), \
                    patch.object(build, "fetch", return_value=b"header"), \
                    patch.object(build, "tool_output", side_effect=version), \
                    patch.object(build.clang_runtime, "prepare") as prepare, \
                    patch.object(build.subprocess, "run") as run, contextlib.redirect_stdout(io.StringIO()):
                build.main()
            prepare.assert_called_once()
            run.assert_called_once()
            self.assertEqual(["pinned-cargo", "fetch", "--locked", "--manifest-path", str(source / "Cargo.toml")], run.call_args.args[0])
            self.assertEqual(b"must not stage", (output / "provenance.json").read_bytes())
            self.assertFalse((output / "target").exists())


class HeaderFreshnessTests(unittest.TestCase):
    def test_identical_header_does_not_write(self):
        with tempfile.TemporaryDirectory() as directory:
            header = Path(directory) / "header.h"
            header.write_bytes(b"pinned header")
            with patch.object(Path, "write_bytes", side_effect=AssertionError("unexpected header rewrite")):
                build.write_if_changed(header, b"pinned header")

    def test_missing_or_changed_header_is_written(self):
        with tempfile.TemporaryDirectory() as directory:
            header = Path(directory) / "header.h"
            build.write_if_changed(header, b"pinned header")
            self.assertEqual(b"pinned header", header.read_bytes())
            build.write_if_changed(header, b"updated header")
            self.assertEqual(b"updated header", header.read_bytes())


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
