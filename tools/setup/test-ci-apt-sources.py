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

    def run_setup(self, github='true', distro='ubuntu', arch='x86_64', legacy=True, rootless=False, root_mode=False, runner='github-hosted', missing_python=False, privileged_missing=False, old_python=False, fail_update=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / 'tools'
            tools.mkdir()
            source = (HERE / 'setup-os.sh').read_text()
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
            (tools / 'environment.sh').write_text('export LUMYTE_ENV_ROOT="$FIXTURE_ROOT/env"\n')
            (tools / 'ci-apt-sources.py').write_text((HERE / 'ci-apt-sources.py').read_text())
            main = root / 'sources.list'
            parts = root / 'parts'
            parts.mkdir()
            content = ('deb [signed-by=/unchanged/key.gpg arch=amd64] ' + AWS + ' noble main universe\n') if legacy else ('Types: deb\nURIs: ' + AWS + '\nSuites: noble noble-updates\nComponents: main universe\nSigned-By: /unchanged/key.gpg\n')
            selected = main if legacy else parts / 'ubuntu.sources'
            selected.write_text(content)
            (parts / 'thirdparty.list').write_text('deb https://user:password@private.example/pkg stable main\n')
            binaries = root / 'bin'
            binaries.mkdir()
            scripts = {
                'uname': '#!/bin/sh\nif [ "$1" = -s ]; then echo Linux; else echo "$FIXTURE_ARCH"; fi\n',
                'sudo': '#!/bin/sh\nif [ -n "$FIXTURE_PRIV_PATH" ]; then PATH="$FIXTURE_PRIV_PATH"; export PATH; fi\nwhile [ "$1" = -n ] || [ "' + chr(36) + '{1#--preserve-env=}" != "$1" ]; do shift; done\nexec "$@"\n',
                'apt-config': '#!/usr/bin/env python3\nimport os,shlex\nprint("SourceList="+shlex.quote(os.environ["FIXTURE_MAIN"]))\nprint("SourceParts="+shlex.quote(os.environ["FIXTURE_PARTS"]))\n',
                'apt-get': '''#!/usr/bin/env python3
import os,sys,json
from pathlib import Path
args=sys.argv[1:];main=Path(os.environ['FIXTURE_MAIN']);parts=Path(os.environ['FIXTURE_PARTS'])
if os.environ.get('APT_CONFIG'):
 import re
 config=Path(os.environ['APT_CONFIG']).read_text()
 main=Path(re.search(r'Dir::Etc::sourcelist "([^"]+)"',config)[1])
 parts=Path(re.search(r'Dir::Etc::sourceparts "([^"]+)"',config)[1])
for value in args:
 if value.startswith('Dir::Etc::sourcelist='):main=Path(value.split('=',1)[1])
 if value.startswith('Dir::Etc::sourceparts='):parts=Path(value.split('=',1)[1])
sources=([main.read_text()] if main.exists() else [])+[p.read_text() for p in sorted(parts.iterdir())]
with open(os.environ['FIXTURE_LOG'],'a') as f:f.write(json.dumps({'args':args,'sources':sources,'proxy':os.environ.get('HTTPS_PROXY'),'frontend':os.environ.get('DEBIAN_FRONTEND'),'main':str(main),'parts':str(parts)})+'\\n')
if os.environ.get('FIXTURE_FAIL_UPDATE')=='1' and args[0]=='update':sys.exit(100)
'''}
            for name, script in scripts.items():
                path = binaries / name
                path.write_text(script)
                path.chmod(0o755)
            # Fake apt/config use an absolute interpreter solely to stay runnable
            # while the target bootstrap PATH genuinely has no named python3.
            for name in ['apt-get', 'apt-config']:
                path = binaries / name
                path.write_text(path.read_text().replace('#!/usr/bin/env python3', '#!' + sys.executable, 1))
            for name in ['bash', 'dirname', 'mkdir', 'cat', 'env', 'true']:
                (binaries / name).symlink_to(shutil.which(name))
            if old_python:
                path = binaries / 'python3'
                path.write_text('#!/bin/sh\nexit 1\n')
                path.chmod(0o755)
            private_path = ''
            if privileged_missing:
                private = root / 'privileged-bin'
                private.mkdir()
                for path in binaries.iterdir():
                    (private / path.name).symlink_to(path)
                private_path = str(private)
            log = root / 'calls.jsonl'
            env = {**os.environ, 'PATH': str(binaries) if missing_python else str(binaries) + ':' + os.environ['PATH'], 'FIXTURE_ROOT': str(root),
                   'FIXTURE_FAIL_UPDATE': '1' if fail_update else '0', 'FIXTURE_MAIN': str(main), 'FIXTURE_PARTS': str(parts), 'FIXTURE_LOG': str(log), 'FIXTURE_ARCH': arch,
                   'GITHUB_ACTIONS': github, 'RUNNER_ENVIRONMENT': runner, 'FIXTURE_PRIV_PATH': private_path, 'HTTPS_PROXY': 'https://proxy.example:1234', 'LUMYTE_SETUP_ROOTLESS': '1' if rootless else '0', 'FIXTURE_EUID': '0' if root_mode else '1000'}
            result = subprocess.run(['bash', str(tools / 'setup-os.sh')], env=env, capture_output=True, text=True)
            if fail_update:
                self.assertNotEqual(0, result.returncode)
                calls = [json.loads(line) for line in log.read_text().splitlines()]
                self.assertEqual(1, len(calls))
                self.assertEqual('update', calls[0]['args'][0])
                self.assertEqual(str(main), calls[0]['main'])
                self.assertEqual(content, selected.read_text())
                return calls, str(main)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertNotIn('password', result.stdout + result.stderr)
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            self.assertEqual(2, len(calls))
            self.assertEqual(content, selected.read_text())
            self.assertIn('APT::Update::Error-Mode=any', calls[0]['args'])
            self.assertEqual(PACKAGES, calls[1]['args'][-len(PACKAGES):])
            self.assertEqual(None if rootless else 'noninteractive', calls[1]['frontend'])
            self.assertEqual(env['HTTPS_PROXY'], calls[1]['proxy'])
            if not rootless:
                for call in calls:
                    self.assertIn('https://user:password@private.example/pkg', ''.join(call['sources']))
            return calls, str(main)

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
        base = subprocess.check_output(['git', 'show', '969a47dc:tools/setup/setup-os.sh'], cwd=HERE, text=True)
        current = (HERE / 'setup-os.sh').read_text()
        self.assertEqual(base[base.index('else\n    # Bootstrap requirements'):], current[current.index('else\n    # Bootstrap requirements'):])
        self.assertIn('LUMYTE_SETUP_ROOTLESS:-0', current)
        self.assertEqual(base[base.index('packages=('):base.index('\n\n', base.index('packages=('))], current[current.index('packages=('):current.index('\n\n', current.index('packages=('))])


if __name__ == '__main__':
    unittest.main()
