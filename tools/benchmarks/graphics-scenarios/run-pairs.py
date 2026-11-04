#!/usr/bin/env python3
"""ADR0027 independent-process GPU comparisons; run after build and correctness gates."""
import argparse
import json
import math
import os
from pathlib import Path
import statistics
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
DLL = ROOT / 'benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks/bin/Release/net10.0/Lumyte.Graphics.Gpu.Benchmarks.dll'

def read(path):
    return json.loads(path.read_text())

def run(output, phase, update, locality, granularity, depth, path, frames, warmup):
    output.mkdir(parents=True, exist_ok=False)
    command = ['dotnet', str(DLL), '--phase', phase, '--update', update,
               '--locality', locality, '--granularity', str(granularity),
               '--depth', str(depth), '--path', path, '--frames', str(frames),
               '--warmup', str(warmup), '--output', str(output)]
    (output / 'invocation.json').write_text(json.dumps(command, indent=2))
    print(phase, path, output, flush=True)
    with (output / 'stdout.log').open('w') as stdout, (output / 'stderr.log').open('w') as stderr:
        result = subprocess.run(command, cwd=ROOT, stdout=stdout, stderr=stderr)
    summary = read(output / 'summary.json')
    if result.returncode or summary.get('status') != 'passed':
        raise RuntimeError(f'Benchmark failed; evidence retained: {output}')
    rows = [json.loads(line) for line in (output / 'frames.jsonl').read_text().splitlines()]
    if len(rows) != frames or summary['frames'] != frames:
        raise RuntimeError(f'Incomplete frame evidence: {output}')
    ids = [row['sample']['frame'] for row in rows]
    if len(set(ids)) != frames:
        raise RuntimeError(f'Duplicate logical frame IDs: {output}')
    return summary

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--update', default='one')
    parser.add_argument('--locality', default='dispersed', choices=['contiguous', 'dispersed'])
    parser.add_argument('--granularity', type=int, default=64, choices=[1,16,64,256,4096])
    parser.add_argument('--depth', type=int, default=1, choices=[1,3])
    parser.add_argument('--pairs', type=int, default=5)
    parser.add_argument('--min-frames', type=int, default=2000, help='Predeclared fixed minimum shared by both variants')
    parser.add_argument('--aa', action='store_true', help='Canvas/Canvas noise control, not performance improvement')
    parser.add_argument('--pilot-only', action='store_true')
    args = parser.parse_args()
    if args.min_frames < 2000 or args.min_frames > 1000000:
        parser.error('Minimum frame budget must be 2000..1000000.')
    if args.pairs < 5:
        parser.error('Formal comparisons require at least five independent pairs.')
    if os.environ.get('VK_DRIVER_FILES') != '/usr/share/vulkan/icd.d/radeon_icd.json':
        parser.error('Select the verified Radeon hardware ICD explicitly.')
    for key in ('DOTNET_TieredCompilation','DOTNET_TieredPGO','COMPlus_TieredCompilation','COMPlus_TieredPGO'):
        if key in os.environ:
            parser.error(f'Primary job requires product runtime defaults; unset {key}.')
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    paths = {'A': 'canvas', 'B': 'canvas' if args.aa else 'scene'}
    fixed = dict(update=args.update, locality=args.locality, granularity=args.granularity, depth=args.depth)
    pilots = {label: run(root / ('pilot-' + label), 'pilot', path=path, frames=2000, warmup=0, **fixed)
              for label, path in paths.items()}
    # Freeze both variants to the greater observed stable warmup and measured-frame budget.
    stable = all(p['pilot']['recommendedWarmup'] is not None for p in pilots.values())
    warmup = max(p['pilot']['recommendedWarmup'] or 2000 for p in pilots.values())
    # Twenty percent headroom is fixed before formal runs; never stop at a favorable observed value.
    frames = max(args.min_frames, math.ceil(max(p['recommendedMeasuredFrames'] for p in pilots.values()) * 1.2))
    plan = dict(schema='adr0027-paired-v1', paths=paths, profile=fixed, pairs=args.pairs,
                warmup=warmup, frames=frames, minimumFrameBudget=args.min_frames, pilotStable=stable, runtimeDefaults=True,
                comparisonStatus='planned' if stable else 'unstable', aaNoiseControl=args.aa)
    (root / 'plan.json').write_text(json.dumps(plan, indent=2))
    if args.pilot_only:
        return
    observations = []
    for pair in range(args.pairs):
        order = ['A','B'] if pair % 2 == 0 else ['B','A']
        pair_rows = {}
        for label in order:
            pair_rows[label] = run(root / f'pair-{pair + 1}-{label}', 'formal',
                                   path=paths[label], frames=frames, warmup=warmup, **fixed)
        observations.append(dict(pair=pair + 1, order=order, summaries=pair_rows,
                                 hostMedianRatioBoverA=pair_rows['B']['host']['medianMs'] / pair_rows['A']['host']['medianMs'],
                                 throughputRatioBoverA=pair_rows['B']['headlessFramesPerSecond'] / pair_rows['A']['headlessFramesPerSecond']))
        (root / 'pairs.json').write_text(json.dumps(observations, indent=2))
    duration_ok = all(s['minimumTenSecondMeasuredDuration'] for row in observations for s in row['summaries'].values())
    result = dict(plan=plan, processPairs=observations,
                  medianHostRatioBoverA=statistics.median(r['hostMedianRatioBoverA'] for r in observations),
                  medianThroughputRatioBoverA=statistics.median(r['throughputRatioBoverA'] for r in observations),
                  minimumDurationMet=duration_ok,
                  status='measured' if stable and duration_ok else 'unstable-or-duration-shortfall',
                  scope='headless public Canvas versus Scene; not displayed FPS; process ratios not pooled frame samples')
    (root / 'comparison.json').write_text(json.dumps(result, indent=2))
    print(json.dumps({k:v for k,v in result.items() if k not in ('processPairs','plan')}, indent=2))

if __name__ == '__main__':
    main()
