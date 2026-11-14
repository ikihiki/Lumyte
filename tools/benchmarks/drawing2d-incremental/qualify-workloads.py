#!/usr/bin/env python3
"""Stdlib, offline GRAPHICS-0027 stability and evidence qualification.

One JSONL file is one independently launched process (possibly multiple workloads).
Exactly one active-device object supplies identity. Frame rows require workload,
phase (warmup/measured), zero-based per-phase iteration, hostFrameMs and nullable
GPU timestamps. Non-JSON log lines are rejected, not silently discarded.
Optional identity fields become compatibility constraints; traceHash, profile and
source are mandatory for A/A and formal. Files alone cannot prove process isolation,
predeclaration, correctness or source authenticity: those remain operator evidence.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import sys

METRICS = ('hostFrameMs', 'gpuRecordedSpanMs')
IDENTITY = ('count', 'device', 'targetWidth', 'targetHeight', 'dpr', 'format',
            'depth', 'scope', 'source', 'traceHash', 'profile')
LIMITATIONS = ['Offline evidence validation, not proof of process independence or predeclaration.',
               'No correctness, host deadline, physical display, or performance-target qualification.',
               'GPU values never substituted with CPU waits; missing GPU prevents joint qualification.']


def number(value, label):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
        raise ValueError(label + ' must be finite and positive')
    return value


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate JSON key ' + key)
        result[key] = value
    return result


def load_run(path):
    path = Path(path)
    data = path.read_bytes()
    rows = []
    for line_no, line in enumerate(data.decode('utf-8').splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line, object_pairs_hook=unique_object, parse_constant=lambda x: (_ for _ in ()).throw(ValueError('nonfinite JSON constant ' + x)))
        except ValueError as exc:
            raise ValueError(f'{path}:{line_no}: {exc}') from exc
        if not isinstance(row, dict):
            raise ValueError(f'{path}:{line_no}: expected JSON object')
        rows.append(row)
    identities = [r for r in rows if r.get('check') == 'active-device']
    if len(identities) != 1:
        raise ValueError(f'{path}: exactly one active-device identity required')
    identity = identities[0]
    groups = {}
    for row in rows:
        if 'check' in row:
            if row.get('status') not in (None, 'passed'):
                raise ValueError(f'{path}: failed check retained: {row.get("check")}')
            continue
        if row.get('phase') not in ('warmup', 'measured'):
            raise ValueError(f'{path}: frame phase must be warmup or measured')
        name = row.get('workload')
        if not isinstance(name, str) or not name:
            raise ValueError('frame workload required')
        phases = groups.setdefault(name, {'warmup': [], 'measured': []})
        bucket = phases[row['phase']]
        if type(row.get('iteration')) is not int or row['iteration'] != len(bucket):
            raise ValueError(f'{name}: iterations must be contiguous zero-based within each phase')
        if row['phase'] == 'warmup' and phases['measured']:
            raise ValueError(f'{name}: warmup after measurement')
        for key in identity:
            if key in row and key not in ('scope', 'suite') and row[key] != identity[key]:
                raise ValueError(f'{name}: row/identity mismatch: {key}')
        if row.get('scope', identity.get('scope')) != 'stages':
            raise ValueError('only serialized stages frame evidence is supported')
        number(row.get('hostFrameMs'), 'hostFrameMs')
        gpu = row.get('gpuRecordedSpanMs')
        if gpu is not None:
            number(gpu, 'gpuRecordedSpanMs')
        if row.get('gpuQueryValid') is False or identity.get('gpuTimestamp') is False:
            if gpu is not None:
                raise ValueError('GPU value present despite invalid/unsupported query')
        bucket.append(row)
    if not groups:
        raise ValueError('no frame rows')
    for name, phases in groups.items():
        for field, phase in (('warmup', 'warmup'), ('samples', 'measured')):
            if field in identity and (type(identity[field]) is not int or identity[field] != len(phases[phase])):
                raise ValueError(name + ': observed ' + phase + ' count differs from declared ' + field)
        summaries = [r for r in rows if r.get('check') == 'stages-summary' and r.get('workload') == name]
        if any(r.get('frames') != len(phases['measured']) for r in summaries):
            raise ValueError(name + ': summary frame count mismatch')
    return {'path': str(path), 'sha256': hashlib.sha256(data).hexdigest(),
            'identity': identity, 'groups': groups}


def policy(warmup, cycle=50):
    if type(warmup) is not int or not 200 <= warmup <= 2000 or warmup % 50:
        raise ValueError('fixed warmup must be 200..2000 in 50-frame blocks')
    if type(cycle) is not int or cycle < 50 or cycle % 50 or warmup % cycle or 4 * cycle > warmup:
        raise ValueError('cycle must be a multiple of 50, align with warmup and fit four complete cycles')
    return {'warmupFrames': warmup, 'cycleFrames': cycle, 'lastWindows': 4, 'maxMedianRatio': 1.05}


def stability(rows, plan):
    if len(rows) != plan['warmupFrames']:
        raise ValueError('observed warmup differs from predeclared fixed count')
    cycle = plan['cycleFrames']
    history = []
    for end in range(4 * cycle, len(rows) + 1, cycle):
        ratios = {}
        states = {}
        for metric in METRICS:
            values = [r.get(metric) for r in rows[end - 4 * cycle:end]]
            if any(v is None for v in values):
                ratios[metric] = None
                states[metric] = 'unmeasured'
                continue
            for v in values:
                number(v, metric)
            medians = [statistics.median(values[i:i + cycle]) for i in range(0, 4 * cycle, cycle)]
            ratios[metric] = max(medians) / min(medians)
            states[metric] = 'stable' if ratios[metric] <= 1.05 else 'unstable'
        history.append({'frames': end, 'ratios': ratios, 'metrics': states,
                        'stable': all(s == 'stable' for s in states.values())})
    earliest = next((h['frames'] for h in history if h['stable']), None)
    final = history[-1]
    status = 'stable' if final['stable'] else ('unmeasured' if 'unmeasured' in final['metrics'].values() else 'unstable')
    return {'status': status, 'earliestStableFrames': earliest, 'final': final, 'history': history}


def distribution(values):
    a = sorted(values)
    pos = .95 * (len(a) - 1)
    i = int(pos)
    return {'n': len(a), 'median': statistics.median(a),
            'p95': a[i] + (a[min(i + 1, len(a) - 1)] - a[i]) * (pos - i),
            'min': a[0], 'max': a[-1]}


def summarize(run, plan):
    cases = {}
    for name, phases in sorted(run['groups'].items()):
        measured = phases['measured']
        metrics = {}
        for metric in METRICS:
            values = [r.get(metric) for r in measured]
            valid = [v for v in values if v is not None]
            metrics[metric] = {'status': 'measured' if values and len(valid) == len(values) else 'unmeasured',
                               'validSamples': len(valid), 'distribution': distribution(valid) if valid else None}
        cases[name] = {'warmup': stability(phases['warmup'], plan),
                       'measuredFrames': len(measured), 'metrics': metrics}
    return {'path': run['path'], 'sha256': run['sha256'], 'identity': run['identity'], 'cases': cases}


def compatible(runs, allow_source=False):
    first = runs[0]
    for run in runs:
        for key in IDENTITY:
            if key not in run['identity'] or run['identity'][key] is None or run['identity'][key] == '':
                raise ValueError('qualification requires identity field ' + key)
        if str(run['identity']['source']).lower() in ('unrecorded', 'unknown', 'none', 'null'):
            raise ValueError('qualification requires authentic recorded source provenance')
        if set(run['groups']) != set(first['groups']):
            raise ValueError('incompatible workload sets')
        # Also constrain extra metadata (runtime, shader, scenario version, etc.).
        ignored = {'runId', 'processId', 'pid'} | ({'source', 'binaryHash'} if allow_source else set())
        a = {k: v for k, v in first['identity'].items() if k not in ignored}
        b = {k: v for k, v in run['identity'].items() if k not in ignored}
        if a != b:
            raise ValueError('incompatible identity/profile/source/trace')
    paths = [str(Path(r['path']).resolve()) for r in runs]
    if len(set(paths)) != len(paths):
        raise ValueError('independent process files required; duplicate path')
    ids = [r['identity'].get('runId') for r in runs]
    if any(x is not None for x in ids) and (None in ids or len(set(ids)) != len(ids)):
        raise ValueError('runId must be unique for each process')


def aa_report(runs, plan, relative, absolute):
    number(relative, 'relative threshold')
    for value in absolute.values():
        number(value, 'absolute threshold')
    if len(runs) < 5:
        raise ValueError('A/A requires at least five independently launched process files')
    compatible(runs)
    summaries = [summarize(r, plan) for r in runs]
    cases = {}
    passed = True
    for name in summaries[0]['cases']:
        metrics = {}
        stable = all(s['cases'][name]['warmup']['status'] == 'stable' for s in summaries)
        if len({s['cases'][name]['measuredFrames'] for s in summaries}) != 1:
            raise ValueError(name + ': incompatible A/A measured frame counts')
        for metric in METRICS:
            samples = [s['cases'][name]['metrics'][metric] for s in summaries]
            valid = all(s['status'] == 'measured' for s in samples)
            values = [s['distribution']['median'] for s in samples] if valid else []
            spread = max(values) - min(values) if values else None
            rel = spread / min(values) if values else None
            ok = valid and rel <= relative and spread <= absolute[metric]
            metrics[metric] = {'status': 'qualified' if ok else ('noisy' if valid else 'unmeasured'),
                               'processMedians': values, 'dispersion': distribution(values) if values else None,
                               'rangeMs': spread, 'relativeRange': rel}
            passed = passed and ok
        cases[name] = {'warmupStableEveryProcess': stable, 'metrics': metrics}
        passed = passed and stable
    return {'schema': 1, 'kind': 'aa', 'status': 'qualified' if passed else 'not-qualified',
            'policy': plan, 'thresholds': {'relative': relative, 'absoluteMs': absolute},
            'cases': cases, 'runs': summaries, 'limitations': LIMITATIONS}


def formal_report(runs, labels, aa_runs, plan, relative, absolute):
    if len(runs) < 10 or len(runs) % 2:
        raise ValueError('formal requires at least five A/B process pairs')
    expected = [label for i in range(len(runs) // 2) for label in (('A', 'B') if i % 2 == 0 else ('B', 'A'))]
    if labels != expected:
        raise ValueError('formal files must be chronological alternating AB, BA pairs')
    compatible(runs, allow_source=True)
    for label in ('A', 'B'):
        sources = {r['identity']['source'] for r, lab in zip(runs, labels) if lab == label}
        if len(sources) != 1:
            raise ValueError('each formal variant must have exactly one source')
        binaries = {r['identity'].get('binaryHash') for r, lab in zip(runs, labels) if lab == label}
        if len(binaries) != 1:
            raise ValueError('each formal variant must have exactly one binaryHash')
    if not aa_runs:
        raise ValueError('raw A/A files required')
    compatible([*aa_runs, *runs], allow_source=True)
    if aa_runs[0]['identity']['source'] != runs[0]['identity']['source']:
        raise ValueError('A/A source must match formal A baseline')
    if aa_runs[0]['identity'].get('binaryHash') != runs[0]['identity'].get('binaryHash'):
        raise ValueError('A/A binaryHash must match formal A baseline')
    aa = aa_report(aa_runs, plan, relative, absolute)
    summaries = [summarize(r, plan) for r in runs]
    reasons = []
    if aa['status'] != 'qualified':
        reasons.append('A/A not qualified')
    for name in summaries[0]['cases']:
        counts = {s['cases'][name]['measuredFrames'] for s in summaries}
        if len(counts) != 1 or min(counts) < 2000:
            reasons.append(name + ': fixed measured count must match and be >=2000')
        for run, summary in zip(runs, summaries):
            case = summary['cases'][name]
            if case['warmup']['status'] != 'stable':
                reasons.append(name + ': warmup not stable in ' + run['path'])
            # Sum the serialized full frame CPU wall intervals, never host/GPU
            # overlapping spans. Without this duration evidence reject qualification.
            rows = run['groups'][name]['measured']
            duration = [r.get('frameCpuWallMs') for r in rows]
            if not duration or any(v is None for v in duration):
                reasons.append(name + ': frameCpuWallMs duration evidence missing')
            elif sum(number(v, 'frameCpuWallMs') for v in duration) < 10000:
                reasons.append(name + ': measured duration below 10 seconds')
            if any(v['status'] != 'measured' for v in case['metrics'].values()):
                reasons.append(name + ': GPU/host measurement incomplete')
    pairs = []
    for i in range(0, len(summaries), 2):
        a, b = (summaries[i], summaries[i + 1]) if labels[i] == 'A' else (summaries[i + 1], summaries[i])
        changes = {}
        for name in a['cases']:
            changes[name] = {}
            for metric in METRICS:
                av = a['cases'][name]['metrics'][metric]['distribution']
                bv = b['cases'][name]['metrics'][metric]['distribution']
                changes[name][metric] = None if av is None or bv is None else {'differenceMs': bv['median'] - av['median'], 'ratioBoverA': bv['median'] / av['median']}
        pairs.append(changes)
    return {'schema': 1, 'kind': 'formal', 'status': 'evidence-qualified' if not reasons else 'not-qualified',
            'policy': plan, 'reasons': reasons, 'aa': aa, 'runs': summaries, 'pairs': pairs,
            'limitations': LIMITATIONS + ['Evidence qualification is not a regression verdict or target achievement.']}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    for command in ('pilot', 'aa', 'formal'):
        p = sub.add_parser(command, help={'pilot': 'audit fixed warmup prefix; earliest stability never overrides final window', 'aa': 'qualify >=5 same-profile process files', 'formal': 'validate >=5 chronological alternating AB/BA pairs with supplied raw A/A'}[command])
        p.add_argument('--warmup-frames', type=int, required=True, help='predeclared exact warmup count, 200..2000, multiple 50')
        p.add_argument('--cycle-frames', type=int, default=50, help='predeclared aligned cycle, multiple 50; compares last four cycle medians')
        if command != 'pilot':
            p.add_argument('--relative-threshold', type=float, default=.05, help='predeclared maximum A/A process-median relative range (default .05)')
            p.add_argument('--host-absolute-ms', type=float, required=True, help='predeclared maximum A/A host process-median range')
            p.add_argument('--gpu-absolute-ms', type=float, required=True, help='predeclared maximum A/A GPU process-median range')
        if command == 'formal':
            p.add_argument('--aa-files', nargs='+', required=True, help='raw independent same-source A/A process JSONL files; put -- before positional formal files')
            p.add_argument('--order', required=True, help='chronological labels, e.g. A,B,B,A,A,B,B,A,A,B')
        p.add_argument('files', nargs='+' if command != 'pilot' else 1, help='strict JSONL process files; one active-device identity each')
    args = parser.parse_args(argv)
    try:
        plan = policy(args.warmup_frames, args.cycle_frames)
        runs = [load_run(p) for p in args.files]
        if args.command == 'pilot':
            summary = summarize(runs[0], plan)
            report = {'schema': 1, 'kind': 'pilot', 'policy': plan, 'run': summary,
                      'status': 'stable' if all(c['warmup']['status'] == 'stable' for c in summary['cases'].values()) else 'not-qualified', 'limitations': LIMITATIONS}
        else:
            absolute = dict(zip(METRICS, (args.host_absolute_ms, args.gpu_absolute_ms)))
            if args.command == 'aa':
                report = aa_report(runs, plan, args.relative_threshold, absolute)
            else:
                report = formal_report(runs, args.order.split(','), [load_run(p) for p in args.aa_files], plan, args.relative_threshold, absolute)
        print(json.dumps(report, indent=2, allow_nan=False))
        return 0 if report['status'] in ('stable', 'qualified', 'evidence-qualified') else 1
    except (ValueError, OSError, UnicodeError) as exc:
        print(json.dumps({'status': 'invalid', 'error': str(exc)}, allow_nan=False))
        return 2


if __name__ == '__main__':
    sys.exit(main())
