#!/usr/bin/env python3
"""Validate virtual-display functional evidence; never qualifies physical timing."""
import argparse
import json
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def verify(root):
    log = (root / 'weston.log').read_text()
    require(all(flag in log for flag in ['--backend=headless', '--renderer=gl', '--xwayland', '--no-config']), 'unverified synthetic compositor configuration')
    allocated = dict(line.split('=', 1) for line in (root / 'allocated-display.txt').read_text().splitlines())
    require(allocated.get('DISPLAY') and ('xserver listening on display ' + allocated['DISPLAY']) in log, 'client/server display mismatch')
    require('Using rendering device: /dev/dri/render' in log and 'GL renderer:' in log, 'missing compositor GPU identity')
    require(not any('GL renderer:' in line and any(word in line.lower() for word in ['llvmpipe', 'softpipe', 'software']) for line in log.splitlines()), 'software compositor')
    require('current 1920 x 1080' in (root / 'xrandr-virtual-modes.txt').read_text(), 'virtual extent mismatch')
    require((root / 'client-exit.txt').read_text().strip() == '0' and (root / 'compositor-exit.txt').read_text().strip() == '0', 'session/client failure')
    results = []
    for name, depth in [('u03-depth1', 1), ('u03-depth3', 3), ('owned-idle', 1)]:
        data = json.loads((root / (name + '.jsonl')).read_text())
        require(data['status'] == 'presentation-host-observations-completed', 'failed presentation run')
        require(data['displayEnvironment']['kind'] == 'virtual' and not data['displayEnvironment']['physicalScanoutVerified'], 'misclassified display')
        require(not data['fullHardwareLatencyClaim'] and data['measuredMonitorRefreshHz'] is None, 'false physical timing claim')
        require(data['options']['Depth'] == depth and data['extent']['dpr'] == 1, 'depth/DPR mismatch')
        require(data['nativeAdapter']['vendorId'] == 0x1002 and data['nativeAdapter']['deviceId'] == 0x1586, 'selected runner adapter mismatch')
        samples = data['samples']
        require(len(samples) == 120 and [r['LogicalFrame'] for r in samples] == list(range(120)), 'lost frame identities')
        require([r['InputId'] for r in samples] == list(range(1, 121)) and all(r['LogicalInputSha256'] for r in samples), 'lost logical input identities')
        require(all(r['CompositeDrawCalls'] == 1 and r['IssuedDrawCalls'] > 0 and r['PresentReturnTick'] >= r['SubmitStartTick'] and r['CompletionObservedTick'] >= r['SubmitStartTick'] for r in samples), 'invalid submit/present/completion evidence')
        require(all(r['status'] == 'passed' for r in data['uiChecks']) and data['finalUiGate']['status'] == 'passed', 'failed exact/analytic UI check')
        if name == 'owned-idle':
            require(data['idle']['idleReservations'] == 0 and data['idle']['idleEnd'] > data['idle']['idleStart'] and data['idle']['resumeInputTick'] >= data['idle']['idleEnd'], 'owned idle trace mismatch')
        results.append(dict(case=name, frames=len(samples), depth=depth))
    host = json.loads((root / 'product-host.jsonl').read_text())
    require(host['status'] == 'product-host-observed' and host['productHostProof'] and host['displayKind'] == 'virtual', 'product host not observed')
    require(host['frameRequests'] is None and host['gpuDrawCalls'] is None and host['presents'] is None, 'invented private host counters')
    require(not host['physicalDisplayProof'] and host['monitorRefreshHz'] is None, 'false physical product-host claim')
    require(host['update']['recordedRevision'] == 1 and host['update']['updatedRecord'] >= host['update']['updateApplied'] >= host['update']['updateRequested'], 'model update observation mismatch')
    require(host['idle']['recordCallbackDelta'] == host['idle']['recordsAfter'] - host['idle']['recordsBefore'], 'record callback accounting mismatch')
    return dict(status='virtual-functional-checks-passed', displayKind='virtual', cases=results,
                compositorAndClientHardwareVerified=True, productHostCpuRecordingAndUpdateObserved=True,
                productHostIdleRecordingContinued=host['idle']['recordCallbackDelta'] > 0,
                productHostDemandIdleClaim=False, physicalScanoutOrVsyncProof=False,
                scope='Functional virtual X11/Wayland/compositor/Present path; not physical monitor timing or native input-to-photon latency')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.root), indent=2))


if __name__ == '__main__':
    main()
