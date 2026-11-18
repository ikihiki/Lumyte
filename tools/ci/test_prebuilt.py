"""Run: python3 -m unittest discover -s tools/ci -p 'test_prebuilt.py' -v."""
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import prebuilt as p


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.root = self.base / 'repo'
        self.root.mkdir()
        self.nuget = self.base / 'packages'
        self.archive = self.base / 'prebuilt.tar'
        self.env = patch.dict(os.environ, GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='2')
        self.env.start()
        self.addCleanup(self.env.stop)
        self.git('init', '-q')
        self.git('config', 'user.email', 'test@example.invalid')
        self.git('config', 'user.name', 'Test')
        self.put('src/project.cs', 'source')
        self.put('global.json', '{"sdk":{"version":"10.0.403"}}')
        self.git('add', '.')
        self.git('commit', '-qm', 'fixture')
        self.put('src/P/bin/Release/app', 'binary', 0o755)
        assets = {'packageFolders': {str(self.nuget) + os.sep: {}},
                  'libraries': {'Example/1.0': {'type': 'package', 'path': 'example/1.0'}}}
        self.put('src/P/obj/project.assets.json', json.dumps(assets))
        package = self.nuget / 'example/1.0/build/Example.targets'
        package.parent.mkdir(parents=True)
        package.write_text('import')
        self.put('artifacts/wgpu-native/liblumyte_wgpu_native.so', 'native')
        self.put('artifacts/dev-env/dxc/lib/libdxcompiler.so', 'dxc')
        self.expected = self.identity()

    def test_published_runtime_and_generator_closure(self):
        for name in ('artifacts/browser/graphics/wwwroot/_framework/main.wasm',
                     'artifacts/audio-browser/wwwroot/index.html',
                     'artifacts/execution-aot/Lumyte.Execution.Sample',
                     'artifacts/dev-env/mesa-26.2.4/x64/vulkan_lvp.dll',
                     'artifacts/svg-usvg/native/liblumyte_svg_usvg.so',
                     'src/P/obj/Release/generated/Generator.cs',
                     'benchmarks/Core/Benchmark/obj/project.assets.json.copy'):
            self.put(name, 'required', 0o755 if '-aot/' in name else 0o644)
        self.put('artifacts/text-parley/native/cargo/release/cache', 'excluded')
        paths = p.collect(self.root, self.nuget)
        self.assertIn('workspace/artifacts/execution-aot/Lumyte.Execution.Sample', paths)
        self.assertIn('workspace/artifacts/browser/graphics/wwwroot/_framework/main.wasm', paths)
        self.assertIn('workspace/artifacts/dev-env/mesa-26.2.4/x64/vulkan_lvp.dll', paths)
        self.assertFalse(any('/cargo/' in name for name in paths))
        self.create()
        self.fresh()
        self.verify(extract=True)
        self.assertTrue((self.root / 'artifacts/execution-aot/Lumyte.Execution.Sample').stat().st_mode & 0o111)

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.root), *args], stderr=subprocess.DEVNULL)

    def put(self, name, content, mode=0o644):
        dest = self.root / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(content)
        dest.chmod(mode)
        return dest

    def identity(self):
        return p.identity(self.root, self.nuget, 'linux-x64', 'Release', '10.0.403')

    def create(self):
        p.create(self.archive, self.root, self.nuget, self.expected)

    def fresh(self):
        for name in ('src/P', 'artifacts'):
            shutil.rmtree(self.root / name)
        shutil.rmtree(self.nuget)

    def verify(self, expected=None, extract=False):
        return p.verify(self.archive, self.root, self.nuget,
                        self.expected if expected is None else expected, extract)

    def rewrite(self, mutate):
        with tarfile.open(self.archive) as tf:
            records = [(m, tf.extractfile(m).read() if m.isfile() else None) for m in tf]
        manifest = json.loads(records[0][1])
        mutate(manifest, records)
        records[0] = (records[0][0], p.canonical(manifest))
        with tarfile.open(self.archive, 'w') as tf:
            for info, data in records:
                if data is not None:
                    info.size = len(data)
                tf.addfile(info, io.BytesIO(data) if data is not None else None)

    def test_fresh_extract_modes_symlinks_and_package_imports(self):
        os.symlink('app', self.root / 'src/P/bin/Release/app-link')
        self.create()
        self.fresh()
        self.verify(extract=True)
        self.assertEqual((self.root / 'src/P/bin/Release/app').stat().st_mode & 0o777, 0o755)
        self.assertEqual((self.root / 'src/P/bin/Release/app-link').read_text(), 'binary')
        self.assertEqual((self.nuget / 'example/1.0/build/Example.targets').read_text(), 'import')
        self.verify(extract=True)  # identical destinations are idempotent

    def test_missing_archive(self):
        with self.assertRaises(FileNotFoundError):
            self.verify(extract=True)

    def test_missing_outputs_or_package_fail(self):
        shutil.rmtree(self.nuget)
        with self.assertRaises(p.Invalid):
            self.create()
        self.assertFalse(self.archive.exists())

    def test_all_identity_mismatches_before_writes(self):
        self.create()
        self.fresh()
        for field in self.expected:
            with self.subTest(field=field):
                wrong = dict(self.expected, **{field: 'wrong'})
                with self.assertRaisesRegex(p.Invalid, 'identity mismatch'):
                    self.verify(wrong, extract=True)
        self.assertFalse((self.root / 'src/P').exists())

    def test_dirty_source_and_actual_head(self):
        before = self.identity()
        self.put('src/project.cs', 'dirty')
        self.assertNotEqual(before['source_sha256'], self.identity()['source_sha256'])
        self.assertEqual(before['head'], self.identity()['head'])
        self.git('add', '.')
        self.git('commit', '-qm', 'changed')
        self.assertNotEqual(before['head'], self.identity()['head'])

    def test_consumer_retry_accepts_same_run_original_producer(self):
        with patch.dict(os.environ, GITHUB_RUN_ATTEMPT='1'):
            self.create()
        self.fresh()
        with patch.dict(os.environ, GITHUB_RUN_ATTEMPT='2'):
            manifest = self.verify(self.identity(), extract=True)
        self.assertEqual(manifest['producer_attempt'], '1')
        self.assertEqual((self.root / 'src/P/bin/Release/app').read_text(), 'binary')

    def test_rerun_producer_replaces_same_named_transport(self):
        self.create()
        # upload-artifact overwrite replaces the old named artifact, never merges it.
        self.archive.unlink()
        self.put('src/P/bin/Release/app', 'rerun binary', 0o755)
        with patch.dict(os.environ, GITHUB_RUN_ATTEMPT='2'):
            self.create()
        self.fresh()
        with patch.dict(os.environ, GITHUB_RUN_ATTEMPT='3'):
            manifest = self.verify(self.identity(), extract=True)
        self.assertEqual(manifest['producer_attempt'], '2')
        self.assertEqual((self.root / 'src/P/bin/Release/app').read_text(), 'rerun binary')

    def test_external_icd_dependency_checked_after_runtime_provisioning(self):
        driver = self.base / 'fresh-system/usr/lib/libvulkan_lvp.so'
        self.put('artifacts/dev-env/lavapipe.json',
                 json.dumps({'ICD': {'library_path': str(driver)}}))
        self.create()
        self.fresh()
        # Archive integrity and extraction do not assume producer OS packages.
        self.verify(extract=True)
        with self.assertRaisesRegex(p.Invalid, 'missing system lavapipe'):
            p.verify(self.archive, self.root, self.nuget, self.expected, check_runtime=True)
        driver.parent.mkdir(parents=True)
        driver.write_text('runtime installed by package manager')
        p.verify(self.archive, self.root, self.nuget, self.expected, check_runtime=True)

    def test_missing_run_identity(self):
        with patch.dict(os.environ, GITHUB_RUN_ID=''):
            with self.assertRaises(p.Invalid):
                self.identity()

    def test_corrupt_hash_fails_before_any_extraction(self):
        self.create()
        self.fresh()
        def corrupt(manifest, records):
            records[-1] = (records[-1][0], b'corrupt')
        self.rewrite(corrupt)
        with self.assertRaises(p.Invalid):
            self.verify(extract=True)
        self.assertFalse((self.root / 'src/P').exists())

    def test_unknown_existing_file_never_overwritten(self):
        self.create()
        self.put('src/P/bin/Release/app', 'unknown')
        with self.assertRaisesRegex(p.Invalid, 'overwrite'):
            self.verify(extract=True)
        self.assertEqual((self.root / 'src/P/bin/Release/app').read_text(), 'unknown')

    def test_source_payload_never_allowed(self):
        self.create()
        def change(manifest, records):
            old = records[1][0].name
            new = 'workspace/src/project.cs'
            manifest['entries'][new] = manifest['entries'].pop(old)
            records[1][0].name = new
        self.rewrite(change)
        with self.assertRaises(p.Invalid):
            self.verify(extract=True)
        self.assertEqual((self.root / 'src/project.cs').read_text(), 'source')

    def test_tracked_file_inside_bin_never_allowed(self):
        self.git('add', 'src/P/bin/Release/app')
        with self.assertRaisesRegex(p.Invalid, 'tracked payload'):
            self.create()

    def test_traversal_absolute_and_unknown_names(self):
        for name in ('../escape', '/tmp/escape', 'workspace/src/P/bin/../escape',
                     'workspace/artifacts/build/tools/secret', 'workspace/.git/config',
                     'nuget/example/1.0/../../escape', 'workspace/C:/file',
                     'workspace/src/P/bin/CON', 'workspace/src/P/bin/trailing.',
                     'workspace/src/P/bin/control' + chr(10)):
            with self.subTest(name=name):
                with self.assertRaises(p.Invalid):
                    p.target(name, self.root, self.nuget)

    def test_escaping_dangling_and_cyclic_symlinks(self):
        link = self.root / 'src/P/bin/Release/link'
        for value in ('/etc/passwd', '../../../../../../escape', 'missing', 'link'):
            with self.subTest(value=value):
                link.symlink_to(value)
                with self.assertRaises(p.Invalid):
                    self.create()
                link.unlink()

    def test_existing_symlink_ancestor(self):
        self.create()
        self.fresh()
        outside = self.base / 'outside'
        outside.mkdir()
        (self.root / 'src/P').symlink_to(outside, target_is_directory=True)
        with self.assertRaises(p.Invalid):
            self.verify(extract=True)
        self.assertEqual(list(outside.iterdir()), [])

    def test_special_modes_rejected(self):
        (self.root / 'src/P/bin/Release/app').chmod(0o4755)
        with self.assertRaises(p.Invalid):
            self.create()

    def test_duplicate_extra_missing_and_hardlink_members(self):
        self.create()
        original = self.archive.read_bytes()
        for kind in ('duplicate', 'extra', 'missing', 'hardlink'):
            self.archive.write_bytes(original)
            def change(manifest, records):
                if kind == 'missing':
                    records.pop()
                elif kind == 'duplicate':
                    records.append(records[-1])
                elif kind == 'extra':
                    info = tarfile.TarInfo('extra')
                    records.append((info, b''))
                else:
                    records[-1][0].type = tarfile.LNKTYPE
                    records[-1][0].linkname = records[1][0].name
                    records[-1] = (records[-1][0], None)
            self.rewrite(change)
            with self.subTest(kind=kind), self.assertRaises(p.Invalid):
                self.verify()

    def test_toolcaches_profiles_and_cargo_targets_excluded(self):
        for path in ('artifacts/build/tools/cargo/credentials.toml',
                     'artifacts/dev-env/dotnet-home/.nuget/NuGet.Config',
                     'artifacts/wgpu-native/target/debug/cache',
                     'artifacts/wgpu-native/source.tar.gz',
                     'artifacts/dev-env/mise/cache/http:dotnet/state',
                     'artifacts/dev-env/mise/cache/http:dotnet/toolchain:metadata',
                     'artifacts/dev-env/nuget-packages/unused/1.0/private:state'):
            self.put(path, 'secret')
        self.create()
        entries = self.verify()['entries']
        self.assertFalse(any('secret' in n or 'credentials' in n or 'target/debug' in n
                             or 'dotnet-home' in n or 'source.tar' in n for n in entries))

    def test_mise_private_cache_is_not_a_transport_candidate(self):
        # The previous broad artifacts/dev-env traversal passed this private
        # backend state path through portable payload validation and failed.
        self.put('artifacts/dev-env/mise/cache/http:dotnet/backend:state', 'private cache')
        paths = p.collect(self.root, self.nuget)
        self.assertFalse(any('/mise/' in name for name in paths))
        self.assertTrue(any(name.startswith('nuget/example/1.0/') for name in paths))

    def test_unsafe_real_output_fails_with_escaped_filename(self):
        bad = 'src/P/bin/Release/invalid:name' + chr(10)
        self.put(bad, 'payload')
        with self.assertRaises(p.Invalid) as caught:
            p.collect(self.root, self.nuget)
        self.assertIn(repr('workspace/' + bad), str(caught.exception))
        self.assertNotIn(chr(10), str(caught.exception))

    def test_wrong_nuget_root_in_assets(self):
        self.put('src/P/obj/project.assets.json', json.dumps({'packageFolders': {'/wrong/': {}}}))
        with self.assertRaisesRegex(p.Invalid, 'packageFolders'):
            self.create()


if __name__ == '__main__':
    unittest.main()
