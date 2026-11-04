#!/usr/bin/env python3
"""Independent-process ADR0027 extension campaigns. Standard library only."""
import argparse
import gzip
import hashlib
import io
import json
import math
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import tarfile

ROOT = Path(__file__).resolve().parents[3]
SCHEMA = 'adr0027-extension-pairs/1'
DEFAULTS = {'Text': 'fixed', 'Effects': 'opaque-1080p', 'Ui': 'U01/hud-60', 'Primitives': 'G01'}


def dump(path, value):
    path.write_text(json.dumps(value, sort_keys=True, indent=2, allow_nan=False) + '\n')


def read(path):
    return json.loads(path.read_text())


def lines(path):
    return [json.loads(x) for x in path.read_text().splitlines() if x.strip()]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def number(value):
    require(isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(value) and value >= 0, 'invalid timing')
    return value


def distribution(values, budget):
    if any(x is None for x in values):
        return None
    values = sorted(number(x) for x in values)
    return dict(medianMs=statistics.median(values), p95Ms=values[math.ceil(.95 * len(values)) - 1],
                maxMs=max(values), budgetMs=budget, budgetExceedances=sum(x > budget for x in values))


def hardware(device):
    if isinstance(device, str):
        require(re.search(r'vendor=0x1002; device=0x1586;', device) and
                re.search(r'type=(IntegratedGpu|DiscreteGpu|1|2);', device), 'unverified actual hardware')
        require(not re.search('llvmpipe|lavapipe|software|type=Cpu', device, re.I), 'software GPU')
    else:
        require(isinstance(device, dict) and device.get('vendor') == 0x1002 and
                device.get('device') == 0x1586 and device.get('type') in ('IntegratedGpu', 'DiscreteGpu'),
                'unverified actual hardware')
    return device


def assembly(family, root=ROOT):
    name = 'Lumyte.Graphics.UiScenarios' if family == 'Ui' else 'Lumyte.Graphics.' + family + '.Benchmarks'
    return root / ('benchmarks/Graphics/Lumyte.Graphics.' + family + '.Benchmarks/bin/Release/net10.0') / (name + '.dll')


def command(family, profile, method, depth, dll, source, output, frames, warmup):
    require(method in ('vector', 'msdf', 'mtsdf'), 'single compatible quality method required')
    require(family in DEFAULTS, 'no formal adapter: ordinary/image warmup CLI is unsupported')
    require(1 <= frames <= 100000 and 0 <= warmup <= 2000, 'runner execution limit')
    cmd = ['dotnet', str(dll)]
    if family == 'Text':
        cmd += ['--run', '--case', profile, '--method', method, '--source', source, '--depth', str(depth)]
    elif family == 'Effects':
        cmd += ['--run', '--profile', profile, '--source', source, '--depth', str(depth)]
    elif family == 'Ui':
        case, variant = profile.split('/')
        cmd += ['--gpu', '--case', case, '--variant', variant, '--depth', str(depth)]
    else:
        cmd += ['--case', profile]
    cmd += ['--frames', str(frames), '--warmup', str(warmup)]
    if family != 'Ui':
        cmd += ['--output', str(output)]
    return cmd


def normalized(path, family, profile, method, depth, frames, warmup, source, *, allow_smoke=False):
    """Validate native evidence, retaining native schemas alongside normalized observations."""
    stage_groups = {}
    if family in ('Text', 'Effects'):
        manifests = list(path.glob('*-manifest.json'))
        require(len(manifests) == 1, 'missing/ambiguous manifest')
        prefix = str(manifests[0])[:-len('manifest.json')]
        m, summary = read(manifests[0]), read(Path(prefix + 'summary.json'))
        raw, checks = lines(Path(prefix + 'frames.jsonl')), lines(Path(prefix + 'checks.jsonl'))
        require(m.get('version') == 1 and (allow_smoke or (not m.get('smoke') and not summary.get('smoke'))), 'incompatible schema/smoke')
        require(m['source'] == source and m['depth'] == depth and m['warmup'] == warmup, 'source/depth/warmup mismatch')
        require(m.get('frames', m.get('measuredFrames')) == frames and summary['count'] == frames, 'count mismatch')
        require(summary['status'] == 'passed', 'failed summary')
        hardware(m['device'])
        require(all(c.get('status') == 'passed' for c in checks) and
                {c['phase'] for c in checks} == {'before', 'after'}, 'missing reference checks')
        for phase in ('before', 'after'):
            for slot in range(depth):
                matches = [c for c in checks if c['phase'] == phase and c.get('slot') == slot]
                require(len(matches) == (3 if family == 'Text' else 1), 'missing slot/state checks')
                for c in matches:
                    require(bool(c.get('sha256', c.get('hash'))), 'missing reference hash')
                    if family == 'Effects':
                        require(number(c['analyticMaximumError']) <= number(c['tolerance']), 'analytic failure')
                    else:
                        require(c.get('quality', {}).get('status') == 'passed', 'missing analytic text quality')
        if family == 'Text':
            require(m['input']['Id'] == profile and m['method'] == method and m.get('fonts'), 'text profile/font mismatch')
            layout = read(Path(prefix + 'layout-check.json'))
            require(layout.get('status') == 'passed' and layout.get('Id') == profile and len(layout.get('states', [])) == 2, 'missing/failed layout reference')
        else:
            require(m['profile']['Id'] == profile, 'effect profile mismatch')
        ids = [r['FrameId'] for r in raw]
        host = [r['HostIncludingSlotWaitMs'] for r in raw]
        gpu = [r['GpuSpanMs'] for r in raw]
        completion_key = 'CompletionObservationMs' if family == 'Text' else 'SubmitToCompletionObservationMs'
        for r in raw:
            number(r[completion_key]); number(r['RetirementMs'])
        elapsed = summary['elapsedIncludingFinalDrainAndRetirementMs']
        identity = {k: v for k, v in m.items() if k not in ('warmup', 'frames', 'measuredFrames', 'stability', 'status', 'provenance', 'firstUse', 'deviceInitializationMs')}
        groups = [(profile, host, gpu, elapsed)]
        stage_groups[profile] = frame_stages(raw)
    elif family == 'Ui':
        rows = lines(path / 'stdout.log')
        manifests = [r for r in rows if r.get('scope') == 'manifest']
        require(len(manifests) == 1, 'missing/ambiguous UI manifest')
        m = manifests[0]
        require(m.get('schemaVersion') == 1 and m.get('scenarioVersion') == 'ui-v2' and
                m.get('source') == source and m['depth'] == depth, 'UI schema/source/depth mismatch')
        hardware(m['actualDeviceAndDriver'])
        summaries = [r for r in rows if r.get('scope') == 'pipeline-summary']
        require(summaries and len({r['Key'] for r in summaries}) == len(summaries), 'missing/duplicate UI summary')
        groups, identity = [], dict(manifest={k: v for k, v in m.items() if k not in ('provenance', 'deviceInitializationMs')}, fonts=[{k: r[k] for k in ('Key', 'FontHash')} for r in rows if r.get('scope') == 'ui-preflight'])
        require(identity['fonts'] and all(r.get('FontHash') for r in identity['fonts']), 'missing UI font identity')
        ids = []
        for s in summaries:
            key = s['Key']
            require(key.startswith(profile + '/') and s['samples'] == frames and s['warmup'] == warmup
                    and s['includesFinalDrain'] and s['includesRetirement'], 'UI profile/count/drain mismatch')
            raw = [r for r in rows if r.get('scope') == 'frame' and r['Key'] == key]
            completed = [r for r in rows if r.get('scope') == 'completion' and r['Key'] == key]
            require(len(completed) == frames and len({r['Frame'] for r in completed}) == frames, 'incomplete completions')
            joined = {r['Frame']: r for r in completed}
            for r in raw:
                c = joined[r['frame']]
                require(c['InputId'] == r['inputId'], 'completion input mismatch')
                number(c['CompletionMs']); number(c['RetireMs'])
                require(c['gpuQueryStatus'] in ('valid', 'unsupported'), 'invalid/pending GPU query')
                require((c['gpuSpanMs'] is not None) == (c['gpuQueryStatus'] == 'valid'), 'query/span mismatch')
            checks = [r for r in rows if r.get('check') == 'ui-independent-exact-pixels' and r['Key'] == key]
            require(len(checks) >= 7 and all(r['status'] == 'passed' and r['analyticSentinel'] and r['sha256'] for r in checks), 'missing UI references')
            group_ids = [r['frame'] for r in raw]
            require(sorted(group_ids) == list(range(frames)), 'incomplete UI frame IDs')
            ids = group_ids
            stage_groups[key] = frame_stages([dict(r, **{k: v for k, v in joined[r['frame']].items() if k.endswith('Ms')}) for r in raw])
            groups.append((key, [number(r['hostFrameMs']) + number(r['slotWaitAndRetireMs']) for r in raw],
                           [joined[r['frame']]['gpuSpanMs'] for r in raw], s['elapsedMs']))
    else:
        m, s = read(path / 'manifest.json'), read(path / 'summary.json')
        require(m.get('schema') == 'graphics-primitives/1' and m['status'] == s['status'] == 'passed'
                and m['sourceCommit'] == source and m['frames'] == frames and m['warmup'] == warmup,
                'primitive schema/source/count mismatch')
        hardware(m['actualDevice'])
        raw, checks = lines(path / 'frames.jsonl'), lines(path / 'checks.jsonl')
        require(checks and all(r.get('status') == 'passed' or (r.get('check') == 'device-loss-recovery' and r.get('status') == 'not-measured') for r in checks), 'primitive checks failed')
        groups, ids = [], []
        for case in s['cases']:
            name, d = case['Name'], case['depth']
            require(case['Id'] == profile and case['count'] == frames, 'primitive profile/count mismatch')
            rs = [r for r in raw if r['Name'] == name and r['depth'] == d]
            ids = [r['frame'] for r in rs]
            require(sorted(ids) == list(range(frames)), 'primitive incomplete IDs')
            for r in rs:
                number(r['completionLatencyMs']); number(r['retirementMs'])
                require(r['inputId'] == name + ':' + str(r['frame']) and r['shaderSha256'], 'primitive input/shader mismatch')
                if m.get('measurementVersion') == 'primitive-queue-timestamps-v2':
                    require(r.get('gpuQueryStatus') in ('valid', 'unsupported'), 'invalid primitive GPU query')
                    valid = r['gpuQueryStatus'] == 'valid'
                    require(valid == (r['gpuSpanMs'] is not None) and valid == m['actualDevice']['timestampInstrumented'], 'primitive query/span capability mismatch')
                    if valid:
                        number(r['gpuSpanMs'])
                else:
                    require(not m['actualDevice'].get('timestampInstrumented') and r['gpuSpanMs'] is None, 'unknown primitive timestamp schema')
            require(any(r.get('check') == 'pipelined-final-pixels' and r.get('Name') == name and r.get('depth') == d for r in checks), 'missing final pixel check')
            stage_groups[name + '/depth' + str(d)] = frame_stages(rs)
            groups.append((name + '/depth' + str(d), [r['outerHostMs'] for r in rs], [r['gpuSpanMs'] for r in rs], case['elapsedIncludingFinalDrainMs']))
        require(groups, 'missing primitive cases')
        require(len(raw) == len(groups) * frames and len({g[0] for g in groups}) == len(groups), 'extra/duplicate primitive groups')
        identity = {k: m[k] for k in ('schema', 'scenarioVersion', 'sourceCommit', 'dirtyDiffSha256', 'inputSha256', 'actualDevice', 'target', 'sourceHashes')}
        identity['measurementVersion'] = m.get('measurementVersion', 'legacy-no-timestamps')
    require(sorted(ids) == list(range(frames)), 'incomplete/duplicate frame IDs')
    for _, host, gpu, elapsed in groups:
        require(len(host) == frames and len(gpu) == frames and number(elapsed) > 0, 'invalid process data')
        for value in host:
            number(value)
        for value in gpu:
            if value is not None:
                number(value)
        require(all(v is None for v in gpu) or all(v is not None for v in gpu), 'partial GPU query results')
    return dict(identity=identity, groups=groups, stageGroups=stage_groups)


def stability(host, gpu, period=1):
    """Four 50-sample phase-matched windows; periodic windows start at equal trace phase."""
    stride = math.ceil(50 / period) * period
    for end in range(3 * stride + 50, len(host) + 1):
        starts = [end - 50 - i * stride for i in range(4)]
        ratios, absolute = [], []
        for values in (host, gpu):
            if any(v is None for v in values):
                ratios.append(None)
                continue
            medians = [statistics.median(values[s:s + 50]) for s in starts]
            ratios.append(max(medians) / min(medians) if min(medians) > 0 else math.inf)
        if all(r is not None and r <= 1.05 for r in ratios):
            return dict(stable=True, warmup=end, ratios=ratios, period=period)
    return dict(stable=False, warmup=None, ratios=None, period=period,
                reason='missing GPU timestamps or no four phase-matched stable windows')


def provenance(root, dll):
    def git(*args):
        return subprocess.check_output(['git', *args], cwd=root, timeout=30)
    tracked = git('ls-files', '-z').decode().split('\0')
    files = {p: sha(root / p) for p in tracked if p and (root / p).is_file()}
    untracked = git('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
    files.update({p: sha(root / p) for p in untracked if p and (root / p).is_file()})
    payload = {str(p.relative_to(dll.parent)): sha(p) for p in sorted(dll.parent.rglob('*')) if p.is_file()}
    require(dll.is_file() and payload, 'missing binary payload')
    return dict(commit=git('rev-parse', 'HEAD').decode().strip(), status=git('status', '--porcelain=v1').decode(),
                diff=git('diff', '--binary', 'HEAD').decode(), sourceHashes=files, binaryNativeShaderFontHashes=payload,
                cpu=[line for line in Path('/proc/cpuinfo').read_text().splitlines() if line.startswith(('model name', 'vendor_id', 'cpu family', 'model\t', 'stepping', 'microcode'))] if Path('/proc/cpuinfo').exists() else platform.processor(),
                os=platform.platform(), runtime=subprocess.check_output(['dotnet', '--info'], timeout=30).decode(),
                environment={k: v for k, v in os.environ.items() if k.startswith(('DOTNET_', 'COMPlus_', 'VK_', 'LD_', 'DRAWING_', 'WORKLOAD_'))})


def execute(path, cmd, env, timeout, root, dll):
    path.mkdir()
    before = provenance(root, dll)
    dump(path / 'provenance-before.json', before)
    dump(path / 'invocation.json', dict(command=cmd, environmentOverrides=env))
    result = dict(returncode=None, error=None)
    try:
        with (path / 'stdout.log').open('w') as out, (path / 'stderr.log').open('w') as err:
            process = subprocess.run(cmd, cwd=root, env=dict(os.environ, **env), stdout=out, stderr=err, timeout=timeout)
        result['returncode'] = process.returncode
    except (subprocess.TimeoutExpired, OSError) as error:
        result['error'] = str(error)
    after = provenance(root, dll)
    dump(path / 'provenance-after.json', after)
    result['provenanceUnchanged'] = before == after
    dump(path / 'execution.json', result)
    require(result['returncode'] == 0 and result['provenanceUnchanged'], 'process failure/timeout/provenance mutation')
    return before


def archive(root):
    """Deterministic tar+gzip: sorted paths, fixed ownership/mtime, no gzip filename."""
    files = [p for p in sorted(root.rglob('*')) if p.is_file() and p.name not in ('evidence.tar.gz', 'archive-manifest.json')]
    manifest = {str(p.relative_to(root)): sha(p) for p in files}
    dump(root / 'archive-manifest.json', dict(schema=SCHEMA, files=manifest))
    files.append(root / 'archive-manifest.json')
    with (root / 'evidence.tar.gz').open('wb') as output:
        with gzip.GzipFile(fileobj=output, mode='wb', filename='', mtime=0) as zipped:
            with tarfile.open(fileobj=zipped, mode='w') as tar:
                for p in files:
                    data = p.read_bytes()
                    info = tarfile.TarInfo(str(p.relative_to(root)))
                    info.size, info.mode, info.mtime = len(data), 0o644, 0
                    tar.addfile(info, io.BytesIO(data))
    verify_archive(root / 'evidence.tar.gz')


def verify_archive(path):
    with tarfile.open(path, 'r:gz') as tar:
        names = tar.getnames()
        require(len(names) == len(set(names)), 'duplicate archive members')
        manifest = json.load(tar.extractfile('archive-manifest.json'))
        require(set(names) == set(manifest['files']) | {'archive-manifest.json'}, 'archive membership mismatch')
        for name, digest in manifest['files'].items():
            require(hashlib.sha256(tar.extractfile(name).read()).hexdigest() == digest, 'archive hash mismatch: ' + name)
    return manifest


def order(pair):
    return ['A', 'B'] if pair % 2 == 0 else ['B', 'A']


def frame_stages(rows):
    return {key: [r[key] for r in rows] for key in rows[0] if key.lower().endswith('ms') and all(key in r for r in rows)}


def dispersion(values):
    median = statistics.median(values)
    return dict(count=len(values), median=median, min=min(values), max=max(values),
                sampleStdev=statistics.stdev(values) if len(values) > 1 else 0,
                medianAbsoluteDeviation=statistics.median(abs(v - median) for v in values))


def analyze(data, budget):
    return {key: dict(host=distribution(host, budget), gpu=distribution(gpu, budget), elapsedMs=elapsed,
                      minimumTenSeconds=elapsed >= 10000,
                      stages={stage: distribution(values, budget) for stage, values in data.get('stageGroups', {}).get(key, {}).items()}) for key, host, gpu, elapsed in data['groups']}


def campaign(args):
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    dll = assembly(args.family)
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, timeout=30).decode().strip()
    profile = args.profile or DEFAULTS[args.family]
    periods = {'Text': 2, 'Effects': 1, 'Primitives': 1}
    # UI's trace has one-time events and monotonically changing inputs. Only U01 has a bounded update cadence.
    period = periods.get(args.family, 6 if profile == 'U01/hud-60' else 12 if profile == 'U01/hud-120' else None)
    require(period is not None, 'UI profile has no declared phase-equivalent stability contract; pilot-only exploratory use is not formal support')
    commands = []
    plan = dict(schema=SCHEMA, family=args.family, profile=profile, method=args.method, depth=args.depth,
                aaNoiseControl=True, FormalComparisonsOnly=True, pilotFrames=args.pilot_frames,
                pilotWarmup=0, period=period, pairs=5, minimumFrames=2000, minimumDurationMs=10000,
                timeoutSeconds=args.timeout, maxFrames=args.max_frames, commands=commands)
    dump(root / 'plan.json', plan)
    def invocation(label, phase, frames, warmup):
        path = root / label
        cmd = command(args.family, profile, args.method, args.depth, dll, source, path, frames, warmup)
        return dict(label=label, phase=phase, frames=frames, warmup=warmup, command=cmd)
    commands.extend(invocation('pilot-' + x, 'pilot', args.pilot_frames, 0) for x in ('A', 'B'))
    dump(root / 'plan.json', plan)
    baseline = None
    def run(entry):
        nonlocal baseline
        p = root / entry['label']
        evidence = execute(p, entry['command'], {'DRAWING_BASELINE': source, 'WORKLOAD_REPO_ROOT': str(ROOT)}, args.timeout, ROOT, dll)
        data = normalized(p, args.family, profile, args.method, args.depth, entry['frames'], entry['warmup'], source)
        compatibility = dict(provenance=evidence, identity=data['identity'])
        dump(p / 'validated-identity.json', data['identity'])
        if baseline is None:
            baseline = compatibility
        require(compatibility == baseline, 'incompatible source/device/profile/runtime/provenance')
        dump(p / 'observation.json', analyze(data, args.budget_ms))
        return data
    try:
        pilots = [run(e) for e in commands[:2]]
        tests = [stability(host, gpu, period) for p in pilots for _, host, gpu, _ in p['groups']]
        stable = all(t['stable'] for t in tests)
        plan['pilotStability'] = tests
        plan['warmup'] = max((t['warmup'] or 2000 for t in tests))
        plan['frames'] = max(2000, math.ceil(max(args.pilot_frames * 10000 / elapsed for p in pilots for _, _, _, elapsed in p['groups']) * 1.2))
        plan['formalSupport'] = stable and plan['frames'] <= args.max_frames
        plan['status'] = 'ready' if plan['formalSupport'] else 'unstable-or-execution-limit'
        if not args.pilot_only and plan['formalSupport']:
            for pair in range(5):
                commands.extend(invocation(f'pair-{pair + 1}-{label}', 'formal', plan['frames'], plan['warmup']) for label in order(pair))
        dump(root / 'plan.json', plan)  # all formal commands fixed before any formal process
        observations = []
        for entry in commands[2:]:
            data = run(entry)
            observations.append(dict(label=entry['label'], groups=analyze(data, args.budget_ms)))
            dump(root / 'processes.json', observations)
        duration = bool(observations) and all(v['minimumTenSeconds'] for o in observations for v in o['groups'].values())
        ratios, absolute = [], []
        for pair in range(5) if observations else []:
            a = next(o['groups'] for o in observations if o['label'] == f'pair-{pair + 1}-A')
            b = next(o['groups'] for o in observations if o['label'] == f'pair-{pair + 1}-B')
            ratios.append({k: b[k]['host']['medianMs'] / a[k]['host']['medianMs'] for k in a})
            absolute.append({k: {metric: b[k][metric]['medianMs'] - a[k][metric]['medianMs'] if a[k][metric] is not None and b[k][metric] is not None else None for metric in ('host', 'gpu')} for k in a})
        dump(root / 'comparison.json', dict(status='measured' if stable and duration else 'pilot-only' if args.pilot_only else 'not-qualified',
             processPairs=ratios, absoluteMedianDeltasBminusA=absolute,
             processPairDispersion={k: dict(hostRatio=dispersion([r[k] for r in ratios]),
                 hostAbsoluteDeltaMs=dispersion([r[k]['host'] for r in absolute]),
                 gpuAbsoluteDeltaMs=dispersion([r[k]['gpu'] for r in absolute]) if all(r[k]['gpu'] is not None for r in absolute) else None) for k in (ratios[0] if ratios else {})},
             gpuQualification='supported native timestamps required; missing capability excluded, never replaced with CPU timing',
             pooledFrameStatistics=False, minimumDurationMet=duration,
             scope='selfsame A/A noise, headless; null GPU is not a completion-wall-time proxy'))
        if not args.pilot_only and not (stable and duration):
            raise ValueError('Campaign not qualified: unstable pilot, execution limit or duration shortfall; evidence retained')
    except Exception as error:
        dump(root / 'campaign-failure.json', dict(status='failed', error=str(error)))
        raise
    finally:
        archive(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--family', choices=list(DEFAULTS))
    parser.add_argument('--profile')
    parser.add_argument('--method', choices=['vector', 'msdf', 'mtsdf'], default='vector')
    parser.add_argument('--depth', type=int, choices=[1, 3], default=1)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--pilot-frames', type=int, default=2000)
    parser.add_argument('--max-frames', type=int, default=100000)
    parser.add_argument('--timeout', type=int, default=1800)
    parser.add_argument('--budget-ms', type=float, default=16.6666667)
    parser.add_argument('--pilot-only', action='store_true')
    parser.add_argument('--verify-archive', type=Path, help='read-only hash revalidation; never starts a runner')
    args = parser.parse_args()
    if args.verify_archive:
        verify_archive(args.verify_archive)
        return
    require(args.family and args.output, '--family and --output required')
    require(200 <= args.pilot_frames <= 2000 and 2000 <= args.max_frames <= 100000 and
            1 <= args.timeout <= 86400 and math.isfinite(args.budget_ms) and args.budget_ms > 0, 'invalid stable execution limits')
    require(not any(k in os.environ for k in ('DOTNET_TieredCompilation', 'DOTNET_TieredPGO', 'COMPlus_TieredCompilation', 'COMPlus_TieredPGO')), 'unset tiering overrides')
    campaign(args)


if __name__ == '__main__':
    main()
