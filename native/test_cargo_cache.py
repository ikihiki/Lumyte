"""Deterministic Cargo cache key and integration contracts."""
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import cargo_cache


class CargoCacheTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        for name in ("native/Cargo.toml", "native/Cargo.lock", "native/src/lib.rs", "native/build.py", "native/fix.patch", "native/header.h", "native/LICENSE", "build/Targets/Native.targets", "tools/setup/activate.sh", ".cargo/config.toml"):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(name)
        subprocess.run(["git", "add", "."], cwd=self.root, check=True)
        self.environment = {"RUSTFLAGS": "-C opt-level=3", "CC": "cc", "ImageVersion": "stable-image"}
        self.tools = {"rustc": ["rustc", "hash", "rustc 1.99.1"], "cc": "gcc", "sdk": "sdk-version"}

    def tearDown(self):
        self.temporary.cleanup()

    def key(self):
        return cargo_cache.identity(self.root, self.environment, self.tools)

    def test_content_not_mtime_identity(self):
        expected = self.key()
        for path in self.root.rglob("*"):
            if path.is_file() and ".git" not in path.parts:
                os.utime(path, (100, 100))
        self.assertEqual(expected, self.key())

    def test_sources_locks_scripts_headers_patches_licenses_targets_config(self):
        expected = self.key()
        for name, _ in cargo_cache.source_closure(self.root):
            with self.subTest(name=name):
                path = self.root / name
                original = path.read_bytes()
                path.write_bytes(original + b"changed")
                self.assertNotEqual(expected, self.key())
                path.write_bytes(original)

    def test_new_native_source_changes_key_but_ignored_output_does_not(self):
        expected = self.key()
        (self.root / "native/new.rs").write_text("new")
        self.assertNotEqual(expected, self.key())
        expected = self.key()
        (self.root / "artifacts").mkdir()
        (self.root / "artifacts/binary.so").write_bytes(b"output")
        self.assertEqual(expected, self.key())

    def test_missing_tracked_input_fails(self):
        (self.root / "native/header.h").unlink()
        with self.assertRaisesRegex(RuntimeError, "Missing native"):
            self.key()

    def test_compile_flags_toolchain_sdk_and_image_change_key(self):
        expected = self.key()
        for key in ("RUSTFLAGS", "CARGO_TARGET_X86_64_UNKNOWN_LINUX_GNU_LINKER", "BINDGEN_EXTRA_CLANG_ARGS", "WindowsSDKVersion", "ImageVersion"):
            with self.subTest(key=key):
                environment = dict(self.environment, **{key: "changed"})
                self.assertNotEqual(expected, cargo_cache.identity(self.root, environment, self.tools))
        for key in self.tools:
            tools = dict(self.tools, **{key: "changed"})
            self.assertNotEqual(expected, cargo_cache.identity(self.root, self.environment, tools))

    def test_volatile_ci_metadata_and_credentials_not_in_key(self):
        expected = self.key()
        self.environment.update(GITHUB_RUN_ID="new-run", GITHUB_TOKEN="secret", RUNNER_TRACKING_ID="new-job")
        self.assertEqual(expected, self.key())

    def test_os_arch_separate(self):
        expected = self.key()
        with patch.object(cargo_cache.platform, "machine", return_value="other"):
            self.assertNotEqual(expected, self.key())
        with patch.object(cargo_cache.platform, "system", return_value="other"):
            self.assertNotEqual(expected, self.key())

    def test_cli_outputs_valid_github_output(self):
        target = self.root / "github-output"
        with patch("sys.argv", ["cargo_cache.py", "--root", str(self.root), "--github-output", str(target)]), patch.dict(os.environ, self.environment, clear=True), patch.object(cargo_cache, "tool_identities", return_value=self.tools), contextlib.redirect_stdout(io.StringIO()) as stdout:
            cargo_cache.main()
        report = json.loads(stdout.getvalue())
        self.assertEqual(report["key"], self.key())
        self.assertEqual(target.read_text(), "key=" + self.key() + "\n")
        self.assertEqual(report["paths"], list(cargo_cache.TARGETS))

    def test_windows_compiler_identity_never_executes_help(self):
        cargo_home = self.root / "tool-cache"
        bin_dir = cargo_home / "bin"
        bin_dir.mkdir(parents=True)
        suffix = ".exe" if os.name == "nt" else ""
        (bin_dir / ("rustup" + suffix)).write_bytes(b"rustup")
        sysroot = self.root / "rust-sysroot"
        (sysroot / "lib").mkdir(parents=True)
        binaries = {}
        for name in ("cargo", "rustc", "cl", "link"):
            binary = self.root / (name + ".exe")
            binary.write_bytes(name.encode())
            binaries[name] = str(binary)
        calls = []

        def tool_output(arguments, environment):
            calls.append(arguments)
            if arguments[0] in (binaries["cl"], binaries["link"]):
                raise subprocess.CalledProcessError(1100, arguments, output="redirected help failed")
            if "which" in arguments:
                return binaries[arguments[-1]]
            if "--print" in arguments:
                return str(sysroot)
            return "pinned Rust 1.99.1"

        environment = dict(self.environment, CARGO_HOME=str(cargo_home))
        with patch.object(cargo_cache.platform, "system", return_value="Windows"), \
                patch.object(cargo_cache.shutil, "which", side_effect=lambda name, **kwargs: binaries.get(name)), \
                patch.object(cargo_cache, "output", side_effect=tool_output):
            tools = cargo_cache.tool_identities(self.root, environment)
            for name in ("cl", "link"):
                self.assertEqual(tools[name], [str(Path(binaries[name]).resolve()), cargo_cache.digest(Path(binaries[name]))])
            self.assertFalse(any(call[0] in (binaries["cl"], binaries["link"]) for call in calls))
            previous = cargo_cache.identity(self.root, environment, tools)
            Path(binaries["link"]).write_bytes(b"updated linker")
            self.assertNotEqual(previous, cargo_cache.identity(self.root, environment, cargo_cache.tool_identities(self.root, environment)))

    def test_action_only_caches_standard_targets_and_never_skips_compile(self):
        action = Path(__file__).resolve().parents[1] / ".github/actions/cache-native/action.yml"
        content = action.read_text()
        for path in cargo_cache.TARGETS:
            self.assertEqual(content.count("          " + path + "\n"), 2)
        self.assertNotIn("restore-keys:", content)
        self.assertNotIn("cache-hit", content)
        self.assertIn("actions/cache/restore@", content)
        self.assertIn("actions/cache/save@", content)
        self.assertIn("inputs.mode == 'save' && success()", content)
        self.assertNotIn("--target Compile", content)


if __name__ == "__main__":
    unittest.main()
