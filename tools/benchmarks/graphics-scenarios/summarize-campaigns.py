#!/usr/bin/env python3
"""Inventory qualified and rejected campaigns without pooling independent frames.

Generated reports belong under ignored artifacts/, never in the Git source tree.
"""
import argparse
import hashlib
import json
from pathlib import Path
import statistics
import tarfile

from extension_pairs import read, require, verify_archive


def digest(path):
    checksum = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            checksum.update(chunk)
    return checksum.hexdigest()


def campaign(path):
    plan = read(path / 'plan.json')
    comparison = read(path / 'comparison.json')
    core = 'paths' in plan
    aa = bool(plan.get('aaNoiseControl'))
    qualified = comparison['status'] == 'measured'
    if core:
        pairs = comparison['processPairs']
        observations = [value for pair in pairs for value in pair['summaries'].values()]
        durations = [value['elapsedMs'] for value in observations]
        formal_frames = [value['frames'] for value in observations]
        ratios = {'hostMedianBoverA': comparison['medianHostRatioBoverA'],
                  'throughputBoverA': comparison['medianThroughputRatioBoverA']}
        dispersion = comparison.get('pairDispersion', comparison.get('processPairDispersion'))
        minimum = bool(comparison['minimumDurationMet'])
        require(len(pairs) == 5 and len(observations) == 10, 'incomplete independent core pairs')
        for pair in pairs:
            require(pair['order'] == (['A', 'B'] if pair['pair'] % 2 else ['B', 'A']), 'core order mismatch')
        stage_medians = {
            side: {stage: statistics.median(pair['summaries'][side]['stageDistributions'][stage]['medianMs'] for pair in pairs)
                   for stage in pairs[0]['summaries'][side]['stageDistributions']}
            for side in ('A', 'B')
        }
        first = next(path.glob('pilot-*/manifest.json'))
        commit = read(first)['sourceCommit']
        profile = plan['profile']
        family = 'Core'
    else:
        observations = read(path / 'processes.json') if (path / 'processes.json').exists() else []
        durations = [group['elapsedMs'] for row in observations for group in row['groups'].values()]
        formal_frames = [entry['frames'] for entry in plan['commands'] if entry['phase'] == 'formal']
        minimum = bool(comparison['minimumDurationMet'])
        ratios = comparison.get('processPairs', [])
        dispersion = comparison.get('processPairDispersion')
        stage_medians = None
        commit = read(path / 'pilot-A/provenance-before.json')['commit']
        profile, family = plan['profile'], plan['family']
        if qualified:
            require(len(observations) == 10 and len(ratios) == 5, 'incomplete extension pairs')
    require(not qualified or (minimum and durations and min(durations) >= 10000), 'false qualified duration')
    require(not qualified or (formal_frames and min(formal_frames) >= 2000 and 200 <= plan['warmup'] <= 2000), 'false qualified frame/warmup budget')
    archive = path / 'evidence.tar.gz'
    archive_metadata = None
    if archive.exists():
        membership = verify_archive(archive)
        with tarfile.open(archive, 'r:gz') as tar:
            sizes = {member.name: member.size for member in tar.getmembers()}
        archive_metadata = dict(path=str(archive), compressedBytes=archive.stat().st_size,
                                uncompressedMemberBytes=sum(sizes.values()), sha256=digest(archive),
                                rawFiles=len(membership['files']), rawFileBytes=sizes)
    else:
        require(not qualified, 'qualified campaign missing verified archive')
    return dict(path=str(path), sourceCommit=commit, family=family, profile=profile,
                kind='A/A noise control' if aa else 'A/B implementation comparison',
                actualPaths=plan.get('paths'), status=comparison['status'], qualified=qualified,
                minimumDurationMet=minimum, formalProcessCount=len(formal_frames),
                formalFramesPerProcess=sorted(set(formal_frames)), warmup=plan['warmup'],
                measuredDurationRangeMs=[min(durations), max(durations)] if durations else None,
                processRatios=ratios, independentPairDispersion=dispersion,
                medianOfIndependentProcessStageMediansMs=stage_medians,
                pooledFrameStatistics=False, archive=archive_metadata,
                scopeNote=('A/A identity follows plan. Original core generic Canvas/Scene scope text is preserved, not rewritten.'
                           if core and aa else comparison['scope']))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    entries = [campaign(path) for path in sorted(args.root.glob('formal-*'))
               if path.is_dir() and (path / 'comparison.json').exists()]
    require(entries, 'no completed campaign artifacts')
    summary = dict(schema='adr0027-campaign-inventory/1', campaigns=entries,
                   qualifiedCampaigns=sum(entry['qualified'] for entry in entries),
                   unqualifiedCampaigns=sum(not entry['qualified'] for entry in entries),
                   qualifiedFormalProcesses=sum(entry['formalProcessCount'] for entry in entries if entry['qualified']),
                   policy='Retain rejected campaigns; A/A is noise, never an improvement claim; no pooled independent frames.')
    args.output.write_text(json.dumps(summary, indent=2, allow_nan=False) + chr(10))
    print(json.dumps({key: value for key, value in summary.items() if key != 'campaigns'}, indent=2))


if __name__ == '__main__':
    main()
