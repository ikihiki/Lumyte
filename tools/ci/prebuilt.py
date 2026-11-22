#!/usr/bin/env python3
"""Same-run/path streaming tar/gzip transport; no SDK/build/restore tools are invoked.

Usage: prebuilt.py {create,verify,extract} FILE --rid linux-x64
       --configuration Release --sdk 10.0.403 [--workspace PATH] [--nuget-root PATH]
SDK is the caller's resolved setup-dotnet identity. Use trusted same-run artifact
storage: hashes detect corruption, not malicious replacement. Exclusive workspace
required. Existing files must be identical; no existing file is overwritten.
Run identity excludes consumer attempt; producer_attempt is informational.
Use verify --check-runtime after OS/runtime installation to resolve external ICDs.
A .gz output streams gzip level 1 directly; readers accept gzip or legacy raw tar.
Extraction validates one complete forward pass before a second extraction pass.
"""
import argparse
from contextlib import contextmanager, ExitStack
import gzip
import hashlib
import io
import json
import ntpath
import os
from pathlib import Path, PurePosixPath
import platform
import re
import stat
import subprocess
import tarfile
import zlib


class Invalid(ValueError):
    pass


def require(ok, message):
    if not ok:
        raise Invalid(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def clean(name):
    require(isinstance(name, str) and name and chr(92) not in name and ':' not in name
            and all(ord(c) >= 32 for c in name),
            'unsafe path: ' + repr(name))
    p = PurePosixPath(name)
    require(not p.is_absolute() and all(x not in ('', '.', '..') for x in name.split('/')),
            'unsafe path: ' + name)
    return p.parts


TREES = ('artifacts/text-parley/native', 'artifacts/text-parley/package',
         'artifacts/text-parley/wasm', 'artifacts/wgpu-native/package',
         'artifacts/nuget', 'artifacts/dev-env/dxc/bin', 'artifacts/dev-env/dxc/lib',
         'artifacts/dev-env/sysroot/usr/lib', 'artifacts/dev-env/sysroot/lib',
         'artifacts/dev-env/sysroot/usr/share/vulkan', 'artifacts/dev-env/mesa',
         'artifacts/svg-usvg/native', 'artifacts/single-window-sample',
         'artifacts/diagnostics-ui-server', 'artifacts/browser',
         'artifacts/audio-execution-browser', 'artifacts/audio-browser')
WGPU = {'liblumyte_wgpu_native.so', 'liblumyte_wgpu_native.dylib',
        'lumyte_wgpu_native.dll', 'provenance.json', 'WGPU-THIRD-PARTY-NOTICES.txt',
        'RUST-STANDARD-LIBRARY-NOTICES.html', 'webgpu.h'}


def allowed(name):
    p = clean(name)
    require(not any(part.endswith((' ', '.')) or part.split('.')[0].upper() in
                    {'CON', 'PRN', 'AUX', 'NUL', *(f'COM{i}' for i in range(1, 10)),
                     *(f'LPT{i}' for i in range(1, 10))} for part in p), 'unsafe Windows path')
    if p[0] == 'nuget':
        return len(p) >= 4 and all(re.fullmatch(r'[a-z0-9_.+-]+', x) for x in p[1:3])
    if p[0] != 'workspace':
        return False
    q = p[1:]
    rel = '/'.join(q)
    if len(q) >= 5 and q[:3] == ('tools', 'shaders', 'Lumyte.ShaderCompiler'):
        return q[3] in ('bin', 'obj')
    if q and q[0] in ('build', 'src', 'tests', 'samples', 'benchmarks'):
        return any(x in ('bin', 'obj') for x in q[1:-1])
    if len(q) >= 5 and q[:2] == ('artifacts', 'build') and q[2] not in ('tools', 'invocations'):
        return q[3] in ('bin', 'obj')
    return (any(rel.startswith(t + '/') for t in TREES)
            or (len(q) >= 3 and q[0] == 'artifacts' and q[1].endswith('-aot'))
            or (len(q) >= 6 and q[:2] == ('artifacts', 'direct3d12') and q[-1] == 'lumyte_graphics_d3d12.dll')
            or (len(q) >= 4 and q[:2] == ('artifacts', 'dev-env') and re.fullmatch(r'mesa-[0-9.]+(?:-arm64)?', q[2]))
            or (len(q) == 3 and q[:2] == ('artifacts', 'wgpu-native') and q[2] in WGPU)
            or (rel.startswith('artifacts/dev-env/dxc/LICENSE') and len(q) == 4)
            or rel in ('artifacts/dev-env/lavapipe.json', 'artifacts/dev-env/dxc/provenance.json'))


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args])


def identity(root, nuget, rid, configuration, sdk):
    closure = []
    for name in sorted(filter(None, git(root, 'ls-files', '-z').decode().split(chr(0)))):
        # Fallout regenerates this metadata; it is output, not a build input.
        # Skip before filesystem checks so a fresh consumer may lack it entirely.
        if name == '.fallout/build.schema.json':
            continue
        p = root / name
        require(p.exists() or p.is_symlink(), 'missing tracked source: ' + name)
        require(not p.is_dir(), 'submodules unsupported: ' + name)
        no_link_parents(p.parent)
        closure.append([name, 'link' if p.is_symlink() else 'file',
                        digest(os.readlink(p).encode() if p.is_symlink() else p.read_bytes()),
                        bool(p.lstat().st_mode & 0o111)])
    run = os.environ.get('GITHUB_RUN_ID', '')
    require(run, 'GITHUB_RUN_ID is required')
    require(all((rid, configuration, sdk)), 'RID/configuration/SDK required')
    return dict(head=git(root, 'rev-parse', 'HEAD').decode().strip(),
                source_sha256=digest(canonical(closure)), workspace=str(root),
                nuget_root=str(nuget), os=platform.system(), architecture=platform.machine(),
                rid=rid, configuration=configuration, sdk=sdk, run=run,
                sdk_root=os.environ.get('LUMYTE_PREBUILT_SDK_ROOT', os.environ.get('DOTNET_ROOT', '')),
                abi=list(platform.libc_ver()))


def require_identity(actual, expected):
    if actual == expected:
        return
    if not isinstance(actual, dict):
        raise Invalid('prebuilt identity mismatch: identity is not an object')
    fields = sorted(set(actual) | set(expected))
    differences = []
    # Identity values are non-secret paths, hashes and scalar build context,
    # never source contents, auth configuration or artifact download URLs.
    for field in fields:
        if actual.get(field) != expected.get(field):
            label = repr(field)[:100]
            if field in expected:
                differences.append(label + ': producer=' + repr(actual.get(field))[:512]
                                   + '; consumer=' + repr(expected.get(field))[:512])
            else:
                differences.append(label + ': unexpected producer identity field')
    raise Invalid('prebuilt identity mismatch: ' + ' | '.join(differences))


def no_link_parents(path):
    for parent in (path, *path.parents):
        require(not parent.is_symlink(), 'symlink destination ancestor: ' + str(parent))


def target(name, root, nuget):
    parts = clean(name)
    require(allowed(name), 'path outside transport allowlist: ' + name)
    return (root if parts[0] == 'workspace' else nuget).joinpath(*parts[1:])


def describe(path, name):
    s = path.lstat()
    require(stat.S_ISREG(s.st_mode) or stat.S_ISLNK(s.st_mode), 'unsupported file: ' + name)
    mode = stat.S_IMODE(s.st_mode)
    require(not mode & ~0o777, 'special permission bits: ' + name)
    if path.is_symlink():
        link = os.readlink(path)
        require(not link.startswith('/') and chr(92) not in link and ':' not in link, 'unsafe link')
        return dict(kind='link', mode=mode, link=link)
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return dict(kind='file', mode=mode, size=s.st_size, sha256=h.hexdigest())


def validate_links(entries):
    for name, entry in entries.items():
        if entry['kind'] != 'link':
            continue
        seen = {name}
        current = name
        while entries[current]['kind'] == 'link':
            parts = current.split('/')[:-1]
            for bit in entries[current]['link'].split('/'):
                if bit == '..':
                    require(len(parts) > 1, 'escaping symlink')
                    parts.pop()
                elif bit not in ('', '.'):
                    parts.append(bit)
            current = '/'.join(parts)
            require(current in entries and current not in seen, 'dangling/cyclic symlink')
            seen.add(current)


def canonical_package_root(value, windows=None):
    """Only filesystem spelling equivalence; never relocate an assets path."""
    if windows is None:
        windows = os.name == 'nt'
    return ntpath.normcase(ntpath.normpath(str(value))) if windows else os.path.abspath(value)


def require_package_root(assets, nuget, label, windows=None):
    declared = assets.get('packageFolders', {})
    require({canonical_package_root(folder, windows) for folder in declared}
            == {canonical_package_root(nuget, windows)},
            'assets packageFolders must match exact NuGet root: ' + label
            + '; declared=' + repr(list(declared)) + '; expected=' + repr(str(nuget)))


def collect(root, nuget):
    paths = {}
    for base in ('build', 'src', 'tests', 'samples', 'benchmarks',
                 'tools/shaders/Lumyte.ShaderCompiler', 'artifacts'):
        for directory, dirs, files in os.walk(root / base, followlinks=False):
            rel = Path(directory).relative_to(root).as_posix()
            if base == 'artifacts':
                trees = (*TREES,
                         *(p.relative_to(root).as_posix() for p in (root / 'artifacts').glob('*-aot') if p.is_dir()),
                         *(p.relative_to(root).as_posix() for p in (root / 'artifacts/dev-env').glob('mesa-*') if p.is_dir()),
                         'artifacts/direct3d12')

                def descend(child):
                    parts = PurePosixPath(child).parts
                    if any(bit in ('tools', 'invocations', 'target', 'cargo', 'dotnet-home') for bit in parts):
                        return False
                    if child == 'artifacts/build':
                        return True
                    if child.startswith('artifacts/build/'):
                        return len(parts) == 3 or (len(parts) >= 4 and parts[3] in ('bin', 'obj'))
                    # Container parents permit visiting their files, not arbitrary
                    # children such as mise/cache, SDKs or NuGet download state.
                    if child in ('artifacts/dev-env', 'artifacts/wgpu-native'):
                        return True
                    return any(child == tree or child.startswith(tree + '/')
                               or tree.startswith(child + '/') for tree in trees)

                dirs[:] = [d for d in dirs if descend(rel + '/' + d)]
            for name in files + [d for d in dirs if (Path(directory) / d).is_symlink()]:
                p = Path(directory) / name
                archive = 'workspace/' + p.relative_to(root).as_posix()
                if allowed(archive):
                    no_link_parents(p.parent)
                    paths[archive] = p
    packages = set()
    for name, p in list(paths.items()):
        if p.name != 'project.assets.json':
            continue
        data = json.loads(p.read_text())
        require_package_root(data, nuget, name)
        for key, library in data.get('libraries', {}).items():
            if library.get('type') == 'package':
                package = library.get('path', key.lower())
                require(len(clean(package)) == 2, 'invalid package path')
                packages.add(package)
    for package in sorted(packages):
        base = nuget / package
        require(base.is_dir(), 'missing NuGet package: ' + package)
        no_link_parents(base)
        for directory, dirs, files in os.walk(base, followlinks=False):
            for name in files + [d for d in dirs if (Path(directory) / d).is_symlink()]:
                p = Path(directory) / name
                no_link_parents(p.parent)
                paths['nuget/' + p.relative_to(nuget).as_posix()] = p
    require(any('/bin/' in n for n in paths), 'missing prebuilt bin outputs')
    require(any(n.endswith('/project.assets.json') for n in paths), 'missing restore assets')
    return paths


def create(archive, root, nuget, expected):
    paths = collect(root, nuget)
    tracked = set(git(root, 'ls-files', '-z').decode().split(chr(0)))
    entries = {}
    for name, p in sorted(paths.items()):
        require(allowed(name), 'disallowed payload: ' + name)
        require(not (name.startswith('workspace/') and name[10:] in tracked), 'tracked payload')
        entries[name] = describe(p, name)
    validate_links(entries)
    manifest = dict(version=1, identity=expected, entries=entries,
                    producer_attempt=os.environ.get('GITHUB_RUN_ATTEMPT', ''))
    with ExitStack() as stack:
        output = stack.enter_context(archive.open('xb'))
        if archive.name.endswith('.gz'):
            output = stack.enter_context(gzip.GzipFile(filename='', fileobj=output, mode='wb',
                                                      compresslevel=1, mtime=0))
        tf = stack.enter_context(tarfile.open(fileobj=output, mode='w|', format=tarfile.PAX_FORMAT))
        data = canonical(manifest)
        info = tarfile.TarInfo('manifest.json')
        info.size, info.mode = len(data), 0o644
        tf.addfile(info, io.BytesIO(data))
        for name, entry in entries.items():
            info = tarfile.TarInfo(name)
            info.mode = entry['mode']
            if entry['kind'] == 'link':
                info.type, info.linkname = tarfile.SYMTYPE, entry['link']
                tf.addfile(info)
            else:
                info.size = entry['size']
                with paths[name].open('rb') as stream:
                    tf.addfile(info, stream)
    verify(archive, root, nuget, expected)


@contextmanager
def read_archive(raw):
    """One forward-only tar pass; drain gzip through its CRC/trailer before success."""
    raw.seek(0)
    compressed = raw.read(2) == bytes((31, 139))
    raw.seek(0)
    with ExitStack() as stack:
        stream = stack.enter_context(gzip.GzipFile(fileobj=raw, mode='rb')) if compressed else raw
        tf = stack.enter_context(tarfile.open(fileobj=stream, mode='r|'))
        yield tf
        # tar stops at its end blocks; gzip corruption/truncation at the trailer
        # must still fail during validation, before any consumer output is written.
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            pass


def read_manifest(tf):
    first = tf.next()
    require(first is not None and first.name == 'manifest.json' and first.isfile()
            and first.size <= 64 * 1024 * 1024, 'missing/oversized manifest')
    return json.load(tf.extractfile(first))


def verify(archive, root, nuget, expected, extract=False, check_runtime=False):
    tracked = set(git(root, 'ls-files', '-z').decode().split(chr(0)))
    # Keep the same archive descriptor for both passes, never a raw tar spool.
    with archive.open('rb') as raw:
        with read_archive(raw) as tf:
            manifest = read_manifest(tf)
            require(manifest.get('version') == 1, 'unsupported manifest')
            require_identity(manifest.get('identity'), expected)
            require(expected['rid'].startswith('win-') == (platform.system() == 'Windows'), 'RID/OS mismatch')
            machine = platform.machine().lower()
            require(expected['rid'].endswith('-arm64') == (machine in ('aarch64', 'arm64')), 'RID/architecture mismatch')
            entries = manifest['entries']
            require(isinstance(entries, dict) and entries, 'empty payload')
            seen, folded, required_packages = set(), set(), set()
            # tf.next() avoids iterating the already-consumed manifest twice.
            while (info := tf.next()) is not None:
                name = info.name
                require(name.casefold() not in folded, 'case-colliding archive member')
                folded.add(name.casefold())
                require(name not in seen and name in entries, 'duplicate/unknown archive member')
                seen.add(name)
                destination = target(name, root, nuget)
                require(not (name.startswith('workspace/') and name[10:] in tracked), 'tracked source payload')
                entry = entries[name]
                require(entry['mode'] == info.mode and not info.mode & ~0o777, 'invalid mode')
                if entry['kind'] == 'file':
                    require(info.isfile() and info.size == entry['size'], 'invalid file type/size')
                    metadata = (name.endswith('/project.assets.json')
                                or name == 'workspace/artifacts/dev-env/lavapipe.json')
                    require(not metadata or info.size <= 64 * 1024 * 1024, 'oversized metadata')
                    data = bytearray() if metadata else None
                    stream = tf.extractfile(info)
                    h = hashlib.sha256()
                    for block in iter(lambda: stream.read(1024 * 1024), b''):
                        h.update(block)
                        if data is not None:
                            data.extend(block)
                    require(h.hexdigest() == entry['sha256'], 'payload hash mismatch: ' + name)
                    if metadata:
                        document = json.loads(data)
                        if name.endswith('/project.assets.json'):
                            require_package_root(document, nuget, name)
                            for key, library in document.get('libraries', {}).items():
                                if library.get('type') == 'package':
                                    package = library.get('path', key.lower())
                                    require(len(clean(package)) == 2, 'invalid package path')
                                    required_packages.add(package)
                        else:
                            path = Path(document['ICD']['library_path'])
                            if path.is_absolute():
                                if root in path.parents:
                                    require('workspace/' + path.relative_to(root).as_posix() in entries,
                                            'missing absolute lavapipe library closure')
                                elif check_runtime:
                                    require(path.is_file(), 'missing system lavapipe library')
                else:
                    require(entry['kind'] == 'link' and info.issym() and info.linkname == entry['link'], 'invalid symlink')
                    require(not info.linkname.startswith('/') and chr(92) not in info.linkname and ':' not in info.linkname,
                            'unsafe symlink')
                no_link_parents(destination.parent)
                if destination.exists() or destination.is_symlink():
                    require(describe(destination, name) == entry, 'refusing to overwrite existing path: ' + name)
            validate_links(entries)
            actual_packages = {'/'.join(clean(n)[1:3]) for n in entries if n.startswith('nuget/')}
            require(actual_packages == required_packages, 'NuGet package closure mismatch')
            require(any('/bin/' in n for n in entries), 'missing bin closure')
            require(any(n.endswith('/project.assets.json') for n in entries), 'missing restore closure')
            for name in entries:
                require(not any('/'.join(name.split('/')[:i]) in entries
                                for i in range(1, len(name.split('/')))), 'payload ancestor collision')
            require(seen == set(entries), 'missing payload member')
        # Entire archive, metadata closure and gzip CRC passed before these writes.
        if extract:
            (root / 'artifacts/nuget').mkdir(parents=True, exist_ok=True)
            with read_archive(raw) as tf:
                require(read_manifest(tf) == manifest, 'archive changed between passes')
                while (info := tf.next()) is not None:
                    destination = target(info.name, root, nuget)
                    if destination.exists() or destination.is_symlink():
                        continue
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    no_link_parents(destination.parent)
                    if info.issym():
                        os.symlink(info.linkname, destination)
                    else:
                        with destination.open('xb') as output:
                            stream = tf.extractfile(info)
                            for block in iter(lambda: stream.read(1024 * 1024), b''):
                                output.write(block)
                        destination.chmod(info.mode)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('create', 'verify', 'extract'))
    parser.add_argument('archive', type=Path)
    parser.add_argument('--workspace', type=Path, default=Path.cwd())
    parser.add_argument('--nuget-root', type=Path,
                        default=Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages'))))
    parser.add_argument('--check-runtime', action='store_true',
                        help='After runtime provisioning, require external ICD libraries to exist')
    parser.add_argument('--kind', choices=('native', 'browser', 'aot'), default='native')
    for option in ('rid', 'configuration', 'sdk'):
        parser.add_argument('--' + option, required=True)
    args = parser.parse_args()
    try:
        root, nuget = args.workspace.absolute(), args.nuget_root.absolute()
        no_link_parents(root)
        no_link_parents(nuget)
        require(root != nuget and nuget not in root.parents, 'invalid NuGet root')
        if root in nuget.parents:
            require(nuget == root / 'artifacts/dev-env/nuget-packages', 'unsupported internal NuGet root')
        expected = identity(root, nuget, args.rid, args.configuration, args.sdk)
        expected['kind'] = args.kind
        require(json.loads((root / 'global.json').read_text())['sdk']['version'] == args.sdk, 'SDK differs from source pin')
        if args.command == 'create':
            create(args.archive, root, nuget, expected)
        else:
            verify(args.archive, root, nuget, expected, args.command == 'extract', args.check_runtime)
        print('prebuilt ' + args.command + ': verified')
    except (Invalid, OSError, EOFError, ValueError, KeyError, TypeError, tarfile.TarError, zlib.error,
            subprocess.CalledProcessError) as error:
        parser.exit(1, 'prebuilt: ' + str(error) + chr(10))


if __name__ == '__main__':
    main()
