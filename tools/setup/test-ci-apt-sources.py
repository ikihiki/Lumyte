"""Offline only: never invokes system apt/sudo or changes live config."""
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import sys
import subprocess
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('ci_apt', HERE / 'ci-apt-sources.py')
APT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(APT)
AWS = 'http://us-east-1.ec2.archive.ubuntu.com/ubuntu/'
CANONICAL = 'https://archive.ubuntu.com/ubuntu/'
PACKAGES = 'build-essential pkg-config curl ca-certificates git unzip zip tar python3 fontconfig fonts-noto-cjk libicu-dev libssl-dev zlib1g-dev libvulkan-dev mesa-vulkan-drivers vulkan-tools'.split()


class SourceTests(unittest.TestCase):
    def test_legacy_only_uri_changes(self):
        source = '# ' + AWS + '\ndeb [arch=amd64 signed-by=/keys/ubuntu.gpg by-hash=yes] ' + AWS + ' noble main universe # keep\ndeb-src ' + AWS + ' noble-updates main\n'
        expected = source.replace('] ' + AWS, '] ' + CANONICAL).replace('deb-src ' + AWS, 'deb-src ' + CANONICAL)
        self.assertEqual(expected, APT.rewrite(source))

    def test_deb822_preserves_multiline_keys_options_disabled_and_other_uris(self):
        source = 'Types: deb deb-src\nURIs: ' + AWS + '\n https://name:password@private.example/pkg\nSuites: noble noble-updates\nComponents: main restricted universe multiverse\nArchitectures: amd64\nSigned-By:\n -----BEGIN PGP PUBLIC KEY BLOCK-----\n .\n abc\n -----END PGP PUBLIC KEY BLOCK-----\n\nEnabled: no\nURIs: ' + AWS + '\nTypes: deb\n'
        self.assertEqual(source.replace(AWS, CANONICAL, 1), APT.rewrite(source, True))

    def test_unrecognized_and_security_ports_azure_query_credentials_stay_exact(self):
        uris = ['https://security.ubuntu.com/ubuntu', 'https://ports.ubuntu.com/ubuntu-ports',
                'http://azure.archive.ubuntu.com/ubuntu', 'https://archive.ubuntu.com/ubuntu',
                'https://user:secret@us-east-1.ec2.archive.ubuntu.com/ubuntu',
                'http://us-east-1.ec2.archive.ubuntu.com:80/ubuntu', AWS + '?token=secret',
                'http://us-east-1.ec2.archive.ubuntu.com.attacker.example/ubuntu', 'file:/private/repo']
        for uri in uris:
            self.assertEqual(uri, APT.canonical(uri))

    def test_duplicate_or_malformed_deb822_fails_without_dump(self):
        for text in ['URIs: x\nURIs: y\n', 'not a field\n']:
            with self.assertRaises(ValueError):
                APT.rewrite(text, True)

    def test_native_config_resolution_and_fail_closed_unsupported_overrides(self):
        seen = []
        def run(command, **kwargs):
            seen.append(command)
            return subprocess.CompletedProcess(command, 0, "SourceList='/private/a list'\nSourceParts='/private/parts/'\n")
        self.assertEqual([Path('/private/a list'), Path('/private/parts')], APT.source_paths(run))
        self.assertIn('Dir::Etc::sourcelist/f', seen[0])
        for extra in ["Root='/sysroot'\n", "BinaryParts='custom'\n"]:
            with self.assertRaises(ValueError):
                APT.source_paths(lambda *a, **k: subprocess.CompletedProcess([], 0, "SourceList='/a' SourceParts='/b' " + extra))

    def test_binary_specific_dir_is_queried_and_rejected_without_guessing(self):
        def run(command, **kwargs):
            output = "SourceList='/ordinary/list' SourceParts='/ordinary/parts'"
            # Native apt-config reports only requested values. The old helper
            # never requested this root, therefore silently used ordinary sources.
            if 'Binary::apt-get::Dir' in command:
                output += " BinaryDir='/alternate'"
            return subprocess.CompletedProcess(command, 0, output)
        with self.assertRaises(ValueError):
            APT.source_paths(run)

    def test_missing_denied_dangling_source_does_not_silently_drop_repository(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            main = root / 'host.list'
            parts = root / 'host.d'
            parts.mkdir()
            destination = root / 'copy'
            destination.mkdir()
            with self.assertRaises(ValueError):
                APT.prepare(main, parts, destination)
            main.write_text('deb ' + AWS + ' noble main\n')
            (root / 'denied').mkdir()
            with patch.object(Path, 'read_bytes', side_effect=PermissionError('private secret')):
                with self.assertRaises(PermissionError):
                    APT.prepare(main, parts, root / 'denied')
            (parts / 'broken.list').symlink_to(root / 'does-not-exist')
            target = root / 'broken'
            target.mkdir()
            with self.assertRaises(FileNotFoundError):
                APT.prepare(main, parts, target)

    def run_setup(self, github='true', distro='ubuntu', arch='x86_64', legacy=True, rootless=False, root_mode=False, runner='github-hosted', missing_python=False, privileged_missing=False, old_python=False, fail_update=False, trace=False, no_rewrite=False, secret_path=False, baseline=False, audit=False, diagnostic_io_failure=False, ci_apt=False, spaced_root=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / 'tools'
            tools.mkdir()
            source = (HERE / 'setup-os.sh').read_text()
            if baseline:
                source = subprocess.check_output(['git', 'show', 'bf0bf3f7:tools/setup/setup-os.sh'], cwd=HERE, text=True)
            if os.environ.get('LUMYTE_TEST_ROUTE_BASELINE') == '1':
                source = subprocess.check_output(['git', 'show', 'bf0bf3f7:tools/setup/setup-os.sh'], cwd=HERE, text=True)
            if os.environ.get('LUMYTE_TEST_BOOTSTRAP_BASELINE') == '1':
                source = subprocess.check_output(['git', 'show', 'bbc7e5bc:tools/setup/setup-os.sh'], cwd=HERE, text=True)
            if os.environ.get('LUMYTE_TEST_BASELINE') == '1':
                source = subprocess.check_output(['git', 'show', '969a47dc:tools/setup/setup-os.sh'], cwd=HERE, text=True)
            release = root / 'os-release'
            release.write_text('ID=' + distro + '\nVERSION_CODENAME=noble\n')
            keyring = root / 'archive-keyring.gpg'
            keyring.touch()
            source = source.replace('/etc/os-release', str(release)).replace('/usr/bin/apt-get', str(root / 'bin/apt-get'))
            source = source.replace('/usr/share/keyrings/ubuntu-archive-keyring.gpg', str(keyring))
            source = source.replace('/usr/share/keyrings/debian-archive-keyring.gpg', str(keyring))
            source = source.replace('"$EUID" == 0', '"$FIXTURE_EUID" == 0')
            (tools / 'setup-os.sh').write_text(source)
            env_name = 'env with spaces' if spaced_root else 'env'
            env_root = root / env_name
            (tools / 'environment.sh').write_text('export LUMYTE_ENV_ROOT=' + shlex.quote(str(env_root)) + '\n')
            (tools / 'ci-apt.sh').write_text((HERE / 'ci-apt.sh').read_text())
            (tools / 'ci-apt-sources.py').write_text((HERE / 'ci-apt-sources.py').read_text())
            main = root / 'sources.list'
            parts = root / 'parts'
            parts.mkdir()
            content = ('deb [signed-by=/unchanged/key.gpg arch=amd64] ' + AWS + ' noble main universe\n') if legacy else ('Types: deb\nURIs: ' + AWS + '\nSuites: noble noble-updates\nComponents: main universe\nSigned-By: /unchanged/key.gpg\n')
            selected = main if legacy else parts / 'ubuntu.sources'
            if no_rewrite:
                content = content.replace(AWS, CANONICAL)
            selected.write_text(content)
            (parts / 'thirdparty.list').write_text('deb https://user:password@private.example/pkg stable main\n')
            binaries = root / ('CANARY_SECRET_PATH' if secret_path else 'bin')
            binaries.mkdir()
            scripts = {
                'uname': '#!/bin/sh\nif [ "$1" = -s ]; then echo Linux; else echo "$FIXTURE_ARCH"; fi\n',
                'sudo': '#!/bin/sh\nif [ -n "$FIXTURE_PRIV_PATH" ]; then PATH="$FIXTURE_PRIV_PATH"; export PATH; fi\nwhile [ "$1" = -n ] || [ "' + chr(36) + '{1#--preserve-env=}" != "$1" ]; do shift; done\nexec "$@"\n',
                'apt-config': '#!/usr/bin/env python3\nimport os,shlex,sys\nvalues={"Dir::Etc::sourcelist":os.environ["FIXTURE_MAIN"],"Dir::Etc::sourceparts":os.environ["FIXTURE_PARTS"]}\nfor arg in sys.argv[1:]:\n if "=" in arg:\n  k,v=arg.split("=",1);values[k]=v\nprint("SourceList="+shlex.quote(values["Dir::Etc::sourcelist"]))\nprint("SourceParts="+shlex.quote(values["Dir::Etc::sourceparts"] if values["Dir::Etc::sourceparts"] != "-" else os.environ["FIXTURE_ROOT"]+"/disabled-parts/"))\n',
                'apt-get': '''#!/usr/bin/env python3
import os,sys,json
from pathlib import Path
archives=None
args=sys.argv[1:];main=Path(os.environ['FIXTURE_MAIN']);parts=Path(os.environ['FIXTURE_PARTS'])
if os.environ.get('APT_CONFIG'):
 import re
 config=Path(os.environ['APT_CONFIG']).read_text()
 archives=Path(re.search(r'Dir::Cache::archives "([^"]+)"',config)[1])
 main=Path(re.search(r'Dir::Etc::sourcelist "([^"]+)"',config)[1])
 parts=Path(re.search(r'Dir::Etc::sourceparts "([^"]+)"',config)[1])
for value in args:
 if value.startswith('Dir::Cache::archives='):archives=Path(value.split('=',1)[1])
 if value.startswith('Dir::Etc::sourcelist='):main=Path(value.split('=',1)[1])
 if value.startswith('Dir::Etc::sourceparts='):parts=Path(value.split('=',1)[1])
sources=([main.read_text()] if main.exists() else [])+([p.read_text() for p in sorted(parts.iterdir())] if parts != Path('-') else [])
with open(os.environ['FIXTURE_LOG'],'a') as f:f.write(json.dumps({'args':args,'sources':sources,'proxy':os.environ.get('HTTPS_PROXY'),'frontend':os.environ.get('DEBIAN_FRONTEND'),'main':str(main),'parts':str(parts)})+'\\n')
if os.environ.get('FIXTURE_FAIL_UPDATE')=='1' and 'update' in args:sys.exit(100)
if '--download-only' in args:
 archives.mkdir(parents=True,exist_ok=True)
 (archives/'sentinel package.deb').write_text('downloaded archive sentinel')
''',
                'dpkg-deb': '''#!/usr/bin/env python3
import sys
from pathlib import Path
assert sys.argv[1]=='--extract'
assert Path(sys.argv[2]).read_text()=='downloaded archive sentinel'
(Path(sys.argv[3])/'extracted-sentinel').write_text(sys.argv[2])
'''}
            for name, script in scripts.items():
                path = binaries / name
                path.write_text(script)
                path.chmod(0o755)
            # Fake apt/config use an absolute interpreter solely to stay runnable
            # while the target bootstrap PATH genuinely has no named python3.
            for name in ['apt-get', 'apt-config', 'dpkg-deb']:
                path = binaries / name
                path.write_text(path.read_text().replace('#!/usr/bin/env python3', '#!' + sys.executable, 1))
            for name in ['bash', 'dirname', 'mkdir', 'cat', 'env', 'true']:
                (binaries / name).symlink_to(shutil.which(name))
            if not missing_python:
                path = binaries / 'python3'
                path.write_text('#!' + sys.executable + '\nimport os,sys,json\nwith open(os.environ["FIXTURE_PY_LOG"],"a") as f:f.write(json.dumps(sys.argv[1:])+"\\n")\n' + ('sys.exit(1)\n' if old_python else 'os.execv(' + repr(sys.executable) + ', [' + repr(sys.executable) + ', *sys.argv[1:]])\n'))
                path.chmod(0o755)
            private_path = ''
            if privileged_missing:
                private = root / 'privileged-bin'
                private.mkdir()
                for path in binaries.iterdir():
                    if path.name != 'python3':
                        (private / path.name).symlink_to(path)
                private_path = str(private)
            log = root / 'calls.jsonl'
            env = {**os.environ, 'PATH': str(binaries) if missing_python else str(binaries) + ':' + os.environ['PATH'], 'FIXTURE_ROOT': str(root),
                   'FIXTURE_PY_LOG': str(root / 'python-calls.jsonl'), 'FIXTURE_FAIL_UPDATE': '1' if fail_update else '0', 'FIXTURE_MAIN': str(main), 'FIXTURE_PARTS': str(parts), 'FIXTURE_LOG': str(log), 'FIXTURE_ARCH': arch,
                   'GITHUB_ACTIONS': github, 'RUNNER_ENVIRONMENT': runner, 'FIXTURE_PRIV_PATH': private_path, 'HTTPS_PROXY': 'https://proxy.example:1234', 'LUMYTE_CI_APT': '1' if ci_apt else '0', 'LUMYTE_SETUP_ROOTLESS': '1' if rootless else '0', 'FIXTURE_EUID': '0' if root_mode else '1000'}
            with open('/dev/full', 'w') if diagnostic_io_failure else open(os.devnull, 'w') as failed_sink:
                result = subprocess.run(['bash', str(tools / 'setup-os.sh')], env=env, stdout=subprocess.PIPE,
                                        stderr=failed_sink if diagnostic_io_failure else subprocess.PIPE, text=True)
            if diagnostic_io_failure:
                result.stderr = ''
            if fail_update:
                self.assertNotEqual(0, result.returncode)
                calls = [json.loads(line) for line in log.read_text().splitlines()]
                self.assertEqual(1, len(calls))
                self.assertEqual('update', calls[0]['args'][0])
                self.assertEqual(str(main), calls[0]['main'])
                self.assertEqual(content, selected.read_text())
                return (calls, str(main), result.stdout + result.stderr) if trace else (calls, str(main))
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertNotIn('password', result.stdout + result.stderr)
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            self.assertEqual(2, len(calls))
            self.assertEqual(content, selected.read_text())
            self.assertIn('APT::Update::Error-Mode=any', calls[0]['args'])
            self.assertEqual(PACKAGES, calls[1]['args'][-len(PACKAGES):])
            self.assertEqual(None if rootless else 'noninteractive', calls[1]['frontend'])
            self.assertEqual(env['HTTPS_PROXY'], calls[1]['proxy'])
            if not rootless and not ci_apt:
                for call in calls:
                    self.assertIn('https://user:password@private.example/pkg', ''.join(call['sources']))
            if rootless:
                extracted = env_root / 'sysroot/extracted-sentinel'
                self.assertTrue(extracted.is_file(), 'downloaded .deb was not extracted into sysroot')
                archive_args = [arg.split('=', 1)[1] for arg in calls[1]['args'] if arg.startswith('Dir::Cache::archives=')]
                archives = Path(archive_args[-1]) if archive_args else env_root / 'apt/archives'
                self.assertEqual(str(archives / 'sentinel package.deb'), extracted.read_text())
            if audit:
                def normalize(call):
                    copied = json.loads(json.dumps(call))
                    for index, arg in enumerate(copied['args']):
                        if arg.startswith('Dir::Etc::sourcelist='):
                            copied['args'][index] = 'Dir::Etc::sourcelist=<private-main>'
                        elif arg.startswith('Dir::Etc::sourceparts='):
                            copied['args'][index] = 'Dir::Etc::sourceparts=<private-parts>'
                    if copied['main'] != str(main) and not rootless:
                        copied['main'] = '<private-main>'
                        copied['parts'] = '<private-parts>'
                    return json.dumps(copied, sort_keys=True).replace(str(root), '<fixture>')
                py_log = root / 'python-calls.jsonl'
                probes = [line for line in py_log.read_text().splitlines() if json.loads(line)[0] == '-c'] if py_log.exists() else []
                return [normalize(call) for call in calls], probes
            return (calls, str(main), result.stdout + result.stderr) if trace else (calls, str(main))

    def test_forwarded_options_survive_private_rewrite_and_discovery(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            main = root / 'selected.list'
            parts = root / 'selected.d'
            parts.mkdir()
            main.write_text('deb ' + AWS + ' noble main\n')
            options = ['-o', 'Dir::Cache::archives=' + str(root / 'archives with spaces'),
                       '-o', 'Binary::apt-get::APT::Keep-Downloaded-Packages=true',
                       '-o', 'Dir::Etc::sourcelist=' + str(main),
                       '-o', 'Dir::Etc::sourceparts=' + str(parts)]
            calls = []
            def run(command, **kwargs):
                calls.append(command)
                return subprocess.CompletedProcess(command, 0, 'SourceList=' + shlex.quote(str(main)) + ' SourceParts=' + shlex.quote(str(parts)))
            APT.install(PACKAGES, run, apt_options=options)
            self.assertEqual(['apt-config', *options, 'shell'], calls[0][:len(options) + 2])
            self.assertEqual(3, len(calls))
            for command in calls[1:]:
                for option in options[1::2]:
                    self.assertIn(option, command)
                selected = [arg.split('=', 1)[1] for arg in command if arg.startswith('Dir::Etc::sourcelist=')]
                self.assertEqual(str(main), selected[0])
                self.assertNotEqual(str(main), selected[-1])
                self.assertFalse(Path(selected[-1]).exists())
            self.assertEqual('deb ' + AWS + ' noble main\n', main.read_text())

    def test_ci_apt_options_reach_discovery_update_and_install(self):
        for root_mode, missing_python in [(True, False), (False, False), (True, True)]:
            with self.subTest(root_mode=root_mode, missing_python=missing_python):
                calls, original = self.run_setup(ci_apt=True, root_mode=root_mode, missing_python=missing_python)
                for call in calls:
                    self.assertNotEqual(original, call['main'])
                    self.assertTrue(call['main'].endswith('/env/ci-apt/sources.list'))
                    self.assertEqual('-', call['parts'])
                    self.assertIn(CANONICAL.rstrip('/'), ''.join(call['sources']))
                    self.assertNotIn(AWS, ''.join(call['sources']))
                    self.assertIn('Binary::apt-get::APT::Keep-Downloaded-Packages=true', call['args'])
                    self.assertTrue(any(arg.startswith('Dir::Cache::archives=') and arg.endswith('/env/ci-apt/archives') for arg in call['args']))

    def test_ci_legacy_fail_first_then_private_effective_sources_selected(self):
        calls, original = self.run_setup()
        for call in calls:
            self.assertIn(CANONICAL, ''.join(call['sources']))
            self.assertNotIn(AWS, ''.join(call['sources']))
            self.assertNotEqual(original, call['main'])
        self.assertEqual(calls[0]['main'], calls[1]['main'])

    def test_ci_deb822_fail_first_then_private_effective_sources_selected(self):
        calls, _ = self.run_setup(legacy=False)
        self.assertIn(CANONICAL, ''.join(calls[1]['sources']))
        self.assertIn('Signed-By: /unchanged/key.gpg', ''.join(calls[1]['sources']))

    def test_local_debian_arm64_no_source_selection_override(self):
        for args in [{'github': ''}, {'distro': 'debian'}, {'arch': 'aarch64'}]:
            calls, main = self.run_setup(**args)
            self.assertEqual(main, calls[0]['main'])
            self.assertIn(AWS, ''.join(calls[0]['sources']))

    def test_rootless_keeps_original_signed_private_sysroot_download_route(self):
        calls, _ = self.run_setup(rootless=True)
        self.assertIn('--download-only', calls[1]['args'])
        self.assertIn('https://archive.ubuntu.com/ubuntu', ''.join(calls[0]['sources']))
        self.assertIn('https://security.ubuntu.com/ubuntu', ''.join(calls[0]['sources']))
        self.assertFalse(any(value.startswith('Dir::Etc::') for value in calls[0]['args']))

    def test_rootless_ci_downloaded_archive_is_actually_extracted(self):
        for arch in ['x86_64', 'aarch64']:
            for ci_apt in [False, True]:
                with self.subTest(arch=arch, ci_apt=ci_apt):
                    calls, _ = self.run_setup(rootless=True, ci_apt=ci_apt, arch=arch, spaced_root=True)
                    self.assertIn('--download-only', calls[1]['args'])
                    if ci_apt:
                        for call in calls:
                            self.assertEqual('-', call['parts'])
                            self.assertTrue(call['main'].endswith('/env with spaces/ci-apt/sources.list'))
                            self.assertIn('Binary::apt-get::APT::Keep-Downloaded-Packages=true', call['args'])
                            archive_args = [arg for arg in call['args'] if arg.startswith('Dir::Cache::archives=')]
                            self.assertEqual(2, len(archive_args))
                            self.assertTrue(archive_args[0].endswith('/ci-apt/archives'))
                            self.assertTrue(archive_args[-1].endswith('/apt/archives'))
                    self.assertTrue(all('signed-by=' in text for text in calls[1]['sources']))
                    self.assertFalse(any('trusted=yes' in text for text in calls[1]['sources']))

    def test_root_normal_ci_uses_same_private_selection_without_sudo(self):
        calls, main = self.run_setup(root_mode=True)
        self.assertNotEqual(main, calls[0]['main'])
        self.assertIn(CANONICAL, ''.join(calls[1]['sources']))

    def test_missing_python_hosted_container_keeps_all_required_native_packages(self):
        for root_mode in [True, False]:
            with self.subTest(root_mode=root_mode):
                calls, main = self.run_setup(missing_python=True, root_mode=root_mode)
                self.assertEqual(main, calls[0]['main'])
                self.assertIn(AWS, ''.join(calls[0]['sources']))
                self.assertEqual(PACKAGES, calls[1]['args'][-len(PACKAGES):])
                self.assertIn('python3', calls[1]['args'])

    def test_sudo_guard_checks_privileged_path_not_caller_interpreter(self):
        calls, main = self.run_setup(privileged_missing=True)
        self.assertEqual(main, calls[0]['main'])
        self.assertIn(AWS, ''.join(calls[0]['sources']))

    def test_self_hosted_unknown_and_local_keep_original_acquisition(self):
        for runner, github, missing in [('self-hosted', 'true', False),
                                        ('self-hosted', 'true', True),
                                        ('', 'true', False), ('unknown', 'true', False),
                                        ('github-hosted', '', True)]:
            with self.subTest(runner=runner, github=github, missing=missing):
                calls, main = self.run_setup(runner=runner, github=github, missing_python=missing)
                self.assertEqual(main, calls[0]['main'])
                self.assertIn(AWS, ''.join(calls[0]['sources']))

    def test_unsupported_python_version_keeps_native_package_installation(self):
        for root_mode in [True, False]:
            calls, main = self.run_setup(old_python=True, root_mode=root_mode)
            self.assertEqual(main, calls[0]['main'])
            self.assertIn('python3', calls[1]['args'])

    def test_native_route_update_failure_is_not_ignored_or_noop_success(self):
        for root_mode in [True, False]:
            calls, _ = self.run_setup(missing_python=True, fail_update=True, root_mode=root_mode)
            self.assertEqual(1, len(calls))

    def test_route_ledger_matches_actual_selection_without_environment_echo(self):
        matrix = [({}, 'helper_selected', 'eligible', 'compatible'),
                  ({'runner': ''}, 'native', 'not_github_hosted', 'not-run'),
                  ({'runner': 'self-hosted'}, 'native', 'not_github_hosted', 'not-run'),
                  ({'github': ''}, 'native', 'not_github_actions', 'not-run'),
                  ({'distro': 'debian'}, 'native', 'unsupported_os_arch', 'not-run'),
                  ({'arch': 'aarch64'}, 'native', 'unsupported_os_arch', 'not-run'),
                  ({'rootless': True}, 'rootless', 'rootless', 'not-run'),
                  ({'missing_python': True}, 'native', 'python_probe_unavailable_or_old', 'not-compatible'),
                  ({'old_python': True}, 'native', 'python_probe_unavailable_or_old', 'not-compatible'),
                  ({'privileged_missing': True}, 'native', 'python_probe_unavailable_or_old', 'not-compatible'),
                  ({'root_mode': True}, 'helper_selected', 'eligible', 'compatible'),
                  ({'github': 'CANARY_SECRET_GHA', 'runner': 'CANARY_SECRET_RUNNER', 'secret_path': True}, 'native', 'not_github_actions', 'not-run')]
        for options, route, reason, probe in matrix:
            with self.subTest(options=options):
                calls, original, output = self.run_setup(**options, trace=True)
                lines = [line for line in output.splitlines() if line.startswith('APT acquisition route: ')]
                self.assertEqual(1, len(lines))
                self.assertIn('route=' + route + ' ', lines[0])
                self.assertIn('reason=' + reason + ' ', lines[0])
                self.assertIn('pyProbe=' + probe, lines[0])
                self.assertLess(len(lines[0]), 320)
                self.assertNotIn('CANARY_SECRET', output)
                self.assertNotIn('https://', lines[0])
                if route == 'helper_selected':
                    self.assertNotEqual(original, calls[0]['main'])
                    self.assertIn(CANONICAL, ''.join(calls[0]['sources']))
                elif route == 'native':
                    self.assertEqual(original, calls[0]['main'])

    def test_helper_selected_is_not_a_claim_that_uri_rewrite_was_applied(self):
        for unchanged in [False, True]:
            calls, original, output = self.run_setup(no_rewrite=unchanged, trace=True)
            self.assertIn('route=helper_selected reason=eligible', output)
            self.assertIn('APT acquisition sources: rewrite=' + ('none' if unchanged else 'applied'), output)
            self.assertEqual(unchanged, calls[0]['main'] == original)

    def test_before_after_native_apt_signatures_and_probe_counts_are_identical(self):
        matrix = [{}, {'runner': ''}, {'runner': 'self-hosted'}, {'github': ''},
                  {'distro': 'debian'}, {'arch': 'aarch64'}, {'rootless': True},
                  {'missing_python': True}, {'old_python': True},
                  {'privileged_missing': True}, {'root_mode': True}, {'no_rewrite': True}]
        for options in matrix:
            with self.subTest(options=options):
                before = self.run_setup(**options, baseline=True, audit=True)
                after = self.run_setup(**options, audit=True)
                self.assertEqual(before, after)
                self.assertLessEqual(len(after[1]), 1)

    def test_diagnostic_io_failure_does_not_change_acquisition_or_apt_failure(self):
        for options in [{}, {'runner': ''}, {'rootless': True}]:
            self.run_setup(**options, diagnostic_io_failure=True)
        self.run_setup(runner='', fail_update=True, diagnostic_io_failure=True)

    def test_update_failure_stops_install_no_retry_and_private_copy_is_cleaned(self):
        calls = []
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            main = root / 'host.list'
            parts = root / 'host.d'
            parts.mkdir()
            main.write_text('deb ' + AWS + ' noble main\n')
            selected = []
            def run(command, **kwargs):
                calls.append(command)
                if command[0] == 'apt-config':
                    return subprocess.CompletedProcess(command, 0, 'SourceList=' + shlex.quote(str(main)) + ' SourceParts=' + shlex.quote(str(parts)))
                selected.extend(value.split('=', 1)[1] for value in command if value.startswith('Dir::Etc::sourcelist='))
                raise subprocess.CalledProcessError(100, command)
            with self.assertRaises(subprocess.CalledProcessError):
                APT.install(PACKAGES, run)
            self.assertEqual(2, len(calls))
            self.assertFalse(Path(selected[0]).exists())
            self.assertEqual('deb ' + AWS + ' noble main\n', main.read_text())

    def test_no_regional_mirror_keeps_original_apt_arguments(self):
        calls = []
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            main = root / 'host.list'
            parts = root / 'host.d'
            parts.mkdir()
            main.write_text('deb https://azure.archive.ubuntu.com/ubuntu noble main\n')
            def run(command, **kwargs):
                calls.append(command)
                return subprocess.CompletedProcess(command, 0, 'SourceList=' + shlex.quote(str(main)) + ' SourceParts=' + shlex.quote(str(parts)))
            APT.install(PACKAGES, run)
            self.assertEqual(['apt-get', 'update', '-o', 'APT::Update::Error-Mode=any'], calls[1])
            self.assertEqual(['apt-get', 'install', '-y', '--no-install-recommends', *PACKAGES], calls[2])

    def test_origin_pins_keep_applicability_or_fail_without_rewriting_policy(self):
        with tempfile.TemporaryDirectory() as d:
            pin = Path(d) / 'preferences'
            config = {'Preferences': str(pin)}
            hosts = {'us-east-1.ec2.archive.ubuntu.com'}
            for pattern in ['"*.ubuntu.com"', '"download.private.example"']:
                text = 'Package: *\nPin: origin ' + pattern + '\nPin-Priority: 500\n'
                pin.write_text(text)
                APT.validate_origin_pins(config, hosts)
                self.assertEqual(text, pin.read_text())
            for pattern in ['"*.ec2.archive.ubuntu.com"', '"archive.ubuntu.com"', '/unsupported-regex/']:
                pin.write_text('Package: *\nPin: origin ' + pattern + '\nPin-Priority: 500\n')
                with self.assertRaises(ValueError):
                    APT.validate_origin_pins(config, hosts)
            pin.write_text('Package: *\nPin: release o=Ubuntu\nPin-Priority: 600\n')
            APT.validate_origin_pins(config, hosts)

    def test_effective_hostname_pin_refuses_before_any_apt_operation(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            main = root / 'host.list'
            parts = root / 'host.d'
            parts.mkdir()
            main.write_text('deb ' + AWS + ' noble main\n')
            pin = root / 'preferences'
            pin.write_text('Package: *\nPin: origin "us-east-1.ec2.archive.ubuntu.com"\nPin-Priority: 999\n')
            calls = []
            def run(command, **kwargs):
                calls.append(command)
                return subprocess.CompletedProcess(command, 0, 'SourceList=' + shlex.quote(str(main)) + ' SourceParts=' + shlex.quote(str(parts)) + ' Preferences=' + shlex.quote(str(pin)))
            with self.assertRaises(ValueError):
                APT.install(PACKAGES, run)
            self.assertEqual(1, len(calls))
            self.assertEqual('apt-config', calls[0][0])

    def test_folded_pin_field_is_rejected_before_origin_applicability_can_change(self):
        with tempfile.TemporaryDirectory() as d:
            pin = Path(d) / 'preferences'
            fields = ['Pin:\n origin "us-east-1.ec2.archive.ubuntu.com"',
                      'Pin: origin "*.ubuntu.com"\n "*.ec2.archive.ubuntu.com"',
                      'Pin: release\n o=Ubuntu']
            for field in fields:
                pin.write_text('Package: *\n' + field + '\nPin-Priority: 999\n')
                with self.assertRaises(ValueError):
                    APT.validate_origin_pins({'Preferences': str(pin)}, {'us-east-1.ec2.archive.ubuntu.com'})

    def test_cli_error_does_not_echo_private_configuration_or_proxy_values(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / 'apt-config'
            path.write_text('#!/bin/sh\nprintf "private-proxy-password\\n" >&2\nexit 9\n')
            path.chmod(0o755)
            result = subprocess.run(['python3', str(HERE / 'ci-apt-sources.py'), *PACKAGES],
                                    env={**os.environ, 'PATH': d + ':' + os.environ['PATH']}, capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertNotIn('private-proxy-password', result.stdout + result.stderr)
            self.assertIn('could not be completed safely', result.stderr)

    def test_rootless_tail_and_packages_workflow_remain_unchanged(self):
        base = subprocess.check_output(['git', 'show', '14b0c33d:tools/setup/setup-os.sh'], cwd=HERE, text=True).replace('    report_acquisition_route rootless rootless rootless not-run\n', '')
        current = (HERE / 'setup-os.sh').read_text().replace('    report_acquisition_route rootless rootless rootless not-run\n', '')
        # Only the acquisition option block may differ: bootstrap, private signed
        # configuration, extraction, fonts and the package inventory stay exact.
        start = 'else\n    # Bootstrap requirements'
        old_options = base.index('    APT_CONFIG="$apt_root/apt.conf"')
        new_options = current.index('    # Rootless extraction owns')
        self.assertEqual(base[base.index(start):old_options], current[current.index(start):new_options])
        tail = '    shopt -s nullglob'
        self.assertEqual(base[base.index(tail):], current[current.index(tail):])
        self.assertIn('LUMYTE_SETUP_ROOTLESS:-0', current)
        self.assertEqual(base[base.index('packages=('):base.index('\n\n', base.index('packages=('))], current[current.index('packages=('):current.index('\n\n', current.index('packages=('))])


if __name__ == '__main__':
    unittest.main()
