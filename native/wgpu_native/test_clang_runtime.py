"""Private SDK checksum/cache/discovery contracts (no network or GPU)."""
import hashlib
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import clang_runtime as runtime


class ClangRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("runtimes/linux-x64/native/libclang.so", b"native-runtime")
            archive.writestr("LICENSE.TXT", b"license")
            archive.writestr("../not-extracted", b"ignored")
        self.data = stream.getvalue()
        self.spec = {"linux-x64": {"version": "test", "sha256": hashlib.sha256(self.data).hexdigest()}}

    def tearDown(self):
        self.temporary.cleanup()

    def resolve(self):
        with patch.dict("os.environ", {}, clear=True), patch.object(runtime.json, "loads", return_value=self.spec), patch.object(runtime.urllib.request, "urlopen", return_value=io.BytesIO(self.data)) as request:
            directory = runtime.resolve_library(self.root, "linux-x64")
        return directory, request

    def test_download_and_exact_member_extraction(self):
        directory, request = self.resolve()
        request.assert_called_once()
        self.assertEqual((directory / "libclang.so").read_bytes(), b"native-runtime")
        self.assertEqual((directory / "LICENSE.TXT").read_bytes(), b"license")
        self.assertFalse((directory.parent / "not-extracted").exists())

    def test_cache_is_offline_and_binary_tamper_is_repaired(self):
        directory, _ = self.resolve()
        (directory / "libclang.so").write_bytes(b"tampered")
        _, request = self.resolve()
        request.assert_not_called()
        self.assertEqual((directory / "libclang.so").read_bytes(), b"native-runtime")

    def test_archive_tamper_fails_closed(self):
        directory, _ = self.resolve()
        (directory / "runtime.nupkg").write_bytes(b"tampered")
        with self.assertRaisesRegex(RuntimeError, "SHA-256"):
            self.resolve()

    def test_download_hash_mismatch_fails_before_cache_write(self):
        self.spec["linux-x64"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(RuntimeError, "SHA-256"):
            self.resolve()
        self.assertFalse(list(self.root.rglob("runtime.nupkg")))

    def test_explicit_library_override(self):
        with patch.dict("os.environ", {"LIBCLANG_PATH": str(self.root)}, clear=True):
            self.assertEqual(runtime.resolve_library(self.root, "unsupported"), self.root)

    def test_linux_uses_compiler_standard_headers(self):
        (self.root / "stddef.h").write_text("// SDK")
        with patch.object(runtime.subprocess, "check_output", return_value=str(self.root)) as process:
            self.assertEqual(runtime.host_header_args("linux-arm64"), ["-isystem", str(self.root)])
        self.assertIn("-print-file-name=include", process.call_args.args[0])

    def test_linux_missing_sdk_fails_early(self):
        with patch.object(runtime.subprocess, "check_output", return_value=str(self.root)):
            with self.assertRaisesRegex(RuntimeError, "Host C SDK"):
                runtime.host_header_args("linux-x64")

    def test_prepare_preserves_quoted_sdk_paths_and_native_target(self):
        environment = {"BINDGEN_EXTRA_CLANG_ARGS": '-DUSER_FLAG=1'}
        includes = ["-isystem", "C:/Program Files/SDK/include"]
        with patch.object(runtime, "resolve_library", return_value=self.root), patch.object(runtime, "host_header_args", return_value=includes), patch.object(runtime, "probe_headers") as probe:
            runtime.prepare(self.root, "win-arm64", self.root, environment)
        self.assertEqual(environment["LIBCLANG_PATH"], str(self.root))
        self.assertEqual(runtime.shlex.split(environment["BINDGEN_EXTRA_CLANG_ARGS"]), ["--target=aarch64-pc-windows-msvc", *includes, "-DUSER_FLAG=1"])
        self.assertEqual(probe.call_args.args[2][1:], runtime.shlex.split(environment["BINDGEN_EXTRA_CLANG_ARGS"]))

    def test_windows_arm64_discovers_correct_vs_component_and_ucrt(self):
        vs = self.root / "VS"
        msvc = vs / "VC/Tools/MSVC/14.50/include"
        ucrt = self.root / "Windows Kits/10/Include/10.0.26100.0/ucrt"
        msvc.mkdir(parents=True)
        ucrt.mkdir(parents=True)
        with patch.dict("os.environ", {"ProgramFiles(x86)": str(self.root)}, clear=True), patch.object(runtime.subprocess, "check_output", return_value=str(vs)) as process:
            self.assertEqual(runtime.host_header_args("win-arm64"), ["-isystem", str(msvc), "-isystem", str(ucrt)])
        self.assertIn("Microsoft.VisualStudio.Component.VC.Tools.ARM64", process.call_args.args[0])


if __name__ == "__main__":
    unittest.main()
