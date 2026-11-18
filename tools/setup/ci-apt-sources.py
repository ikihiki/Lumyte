#!/usr/bin/env python3
"""Privileged CI source acquisition only; no mirror retries or trust/policy changes."""
import os
import fnmatch
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tempfile

REGIONAL = re.compile(r'https?://[a-z]{2}(?:-[a-z]+)+-[0-9]+\.ec2\.archive\.ubuntu\.com(?P<path>/ubuntu/?)\Z')
LEGACY = re.compile(r'(?P<prefix>^[ \t]*deb(?:-src)?[ \t]+(?:\[[^\]\r\n]*\][ \t]+)?)(?P<uri>[^\s#]+)', re.MULTILINE)
FIELD = re.compile(r'^([A-Za-z][A-Za-z0-9-]*):')
SOURCE_NAME = re.compile(r'[A-Za-z0-9_.-]+\.(?:list|sources)\Z')


def canonical(uri, hosts=None):
    match = REGIONAL.fullmatch(uri)
    if match and hosts is not None:
        hosts.add(uri.split('/')[2])
    return 'https://archive.ubuntu.com' + match['path'] if match else uri


def rewrite(text, deb822=False, hosts=None):
    if not deb822:
        return LEGACY.sub(lambda match: match['prefix'] + canonical(match['uri'], hosts), text)
    output = []
    for stanza in re.split(r'(\r?\n[ \t]*\r?\n)', text):
        if re.search(r'^Enabled:[ \t]*no[ \t]*\r?$', stanza, re.MULTILINE | re.IGNORECASE):
            output.append(stanza)
            continue
        current = None
        fields = set()
        lines = []
        for line in stanza.splitlines(keepends=True):
            field = FIELD.match(line)
            if field:
                current = field[1].lower()
                if current in fields:
                    raise ValueError('duplicate-field')
                fields.add(current)
            elif line.strip() and not line.startswith('#') and not line[0].isspace():
                raise ValueError('invalid-field')
            if current == 'uris' and not line.startswith('#'):
                line = re.sub(r'\S+', lambda token: canonical(token[0], hosts), line)
            lines.append(line)
        output.append(''.join(lines))
    return ''.join(output)


def source_paths(run, include_config=False):
    keys = {'SourceList': 'Dir::Etc::sourcelist/f', 'SourceParts': 'Dir::Etc::sourceparts/d',
            'Preferences': 'Dir::Etc::preferences/f', 'PreferenceParts': 'Dir::Etc::preferencesparts/d',
            'BinaryPreferences': 'Binary::apt-get::Dir::Etc::preferences', 'BinaryPreferenceParts': 'Binary::apt-get::Dir::Etc::preferencesparts',
            'Root': 'RootDir', 'BinaryDir': 'Binary::apt-get::Dir', 'BinaryEtc': 'Binary::apt-get::Dir::Etc',
            'BinaryList': 'Binary::apt-get::Dir::Etc::sourcelist',
            'BinaryParts': 'Binary::apt-get::Dir::Etc::sourceparts', 'BinaryRoot': 'Binary::apt-get::RootDir'}
    command = ['apt-config', 'shell']
    for key, value in keys.items():
        command += [key, value]
    result = run(command, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    values = {}
    for token in shlex.split(result.stdout):
        key, separator, value = token.partition('=')
        if not separator or key not in keys or key in values:
            raise ValueError('unsupported-config')
        values[key] = value
    # Do not guess resolution of binary-specific/chroot source roots.
    if any(key in values for key in ['BinaryDir', 'BinaryEtc', 'BinaryList', 'BinaryParts', 'BinaryRoot', 'BinaryPreferences', 'BinaryPreferenceParts']) or values.get('Root'):
        raise ValueError('unsupported-source-root')
    paths = [Path(values[key]) for key in ['SourceList', 'SourceParts']]
    if not all(path.is_absolute() for path in paths) or paths[0] == paths[1]:
        raise ValueError('unsupported-source-path')
    return (paths, values) if include_config else paths


def prepare(source_list, source_parts, destination):
    """Copy effective source order/options verbatim, except recognized URI tokens."""
    copied_parts = destination / 'sources.list.d'
    copied_parts.mkdir(mode=0o700)
    changed = 0
    found = 0
    hosts = set()

    def copy(source, target, deb822):
        nonlocal changed, found
        original = source.read_bytes()
        updated = rewrite(original.decode('utf-8'), deb822, hosts).encode('utf-8')
        target.write_bytes(updated)
        target.chmod(0o600)
        changed += int(updated != original)
        found += 1

    try:
        copy(source_list, destination / source_list.name, source_list.suffix == '.sources')
    except FileNotFoundError:
        if source_list.is_symlink():
            raise
    try:
        sources = sorted(source_parts.iterdir())
    except FileNotFoundError:
        if source_parts.is_symlink():
            raise
        sources = []
    for source in sources:
        if SOURCE_NAME.fullmatch(source.name):
            copy(source, copied_parts / source.name, source.suffix == '.sources')
    if not found:
        raise ValueError('no-source-files')
    return changed, destination / source_list.name, copied_parts, hosts


def validate_origin_pins(config, hosts):
    # Changing a hostname must not silently change Pin: origin applicability.
    files = []
    if config.get('Preferences'):
        files.append(Path(config['Preferences']))
    if config.get('PreferenceParts'):
        parts = Path(config['PreferenceParts'])
        if not parts.is_absolute():
            raise ValueError('unsupported-preference-path')
        try:
            files += [p for p in sorted(parts.iterdir()) if re.fullmatch(r'[A-Za-z0-9_.-]+', p.name)
                      and p.suffix in ['', '.pref']]
        except FileNotFoundError:
            if parts.is_symlink():
                raise
    for path in files:
        if not path.is_absolute():
            raise ValueError('unsupported-preference-path')
        try:
            text = path.read_text(encoding='utf-8')
        except FileNotFoundError:
            if path.is_symlink():
                raise
            continue
        pin_field = False
        for line in text.splitlines():
            if not line.strip():
                pin_field = False
                continue
            if line.startswith('#'):
                continue
            if line[0].isspace() and pin_field:
                raise ValueError('unsupported-folded-pin')
            if not line[0].isspace():
                pin_field = bool(re.match(r'^Pin\s*:', line, re.IGNORECASE))
            # Folded Pin fields require native preference parsing. Refuse them
            # instead of skipping their hostname policy or inventing a resolver.
            if re.match(r'^\s*Pin\s*:\s*$', line, re.IGNORECASE):
                raise ValueError('unsupported-folded-pin')
            match = re.match(r'^\s*Pin\s*:\s*origin(?:\s+(.*))?$', line, re.IGNORECASE)
            if not match:
                continue
            tokens = shlex.split(match[1] or '', comments=True)
            if len(tokens) != 1 or tokens[0].startswith('/') or '!' in tokens[0]:
                raise ValueError('unsupported-origin-pin')
            pattern = tokens[0].lower()
            if any(fnmatch.fnmatchcase(host, pattern) != fnmatch.fnmatchcase('archive.ubuntu.com', pattern) for host in hosts):
                raise ValueError('mirror-dependent-origin-pin')


def install(packages, run=subprocess.run):
    # setup-os.sh gates this helper to privileged GitHub Actions Ubuntu/x64.
    (source_list, source_parts), config = source_paths(run, include_config=True)
    with tempfile.TemporaryDirectory(prefix='lumyte-ci-apt-') as directory:
        changed, main, parts, hosts = prepare(source_list, source_parts, Path(directory))
        if changed:
            validate_origin_pins(config, hosts)
        # Only report a bounded decision enum, never source text or config data.
        # Failure of diagnostic I/O must not change the actual APT result.
        try:
            os.write(2, b'APT acquisition sources: rewrite=' + (b'applied' if changed else b'none') + b'\n')
        except (OSError, ValueError):
            pass
        options = ['-o', 'Dir::Etc::sourcelist=' + str(main), '-o', 'Dir::Etc::sourceparts=' + str(parts)] if changed else []
        # Native config still supplies all keys, pins, proxies, TLS and version policy.
        run(['apt-get', 'update', '-o', 'APT::Update::Error-Mode=any', *options], check=True)
        run(['apt-get', 'install', '-y', '--no-install-recommends', *options, *packages], check=True,
            env={**os.environ, 'DEBIAN_FRONTEND': 'noninteractive'})


if __name__ == '__main__':
    try:
        install(sys.argv[1:])
    except (OSError, ValueError, KeyError, UnicodeError, subprocess.SubprocessError):
        print('CI APT source acquisition failed; source configuration or APT operation could not be completed safely.', file=sys.stderr)
        sys.exit(1)
