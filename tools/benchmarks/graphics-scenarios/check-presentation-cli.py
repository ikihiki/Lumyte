#!/usr/bin/env python3
"""Verify built presentation CLI and missing-display guard without creating a GPU/window."""
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[3]
DLL = ROOT / 'benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks/bin/Release/net10.0/Lumyte.Graphics.Presentation.Benchmarks.dll'


def main():
    if not DLL.is_file():
        raise SystemExit('Build the Release presentation benchmark first.')
    env = dict(os.environ, VK_DRIVER_FILES='/nonexistent/presentation-no-device.json')
    env.pop('DISPLAY', None)
    env.pop('WAYLAND_DISPLAY', None)
    invalid = [
        ['--validate-options', '--depth', '0'], ['--validate-options', '--depth', '2'],
        ['--validate-options', '--depth', '4'], ['--validate-options', '--frames', 'NaN'],
        ['--validate-options', '--refresh-hz', 'NaN'], ['--validate-options', '--refresh-hz', 'Infinity'],
        ['--validate-options', '--refresh-hz', '0'], ['--validate-options', '--present-mode', 'Fifo'],
        ['--validate-options', '--unknown', '1'], ['--validate-options', '--depth', '1', '--depth', '3'],
        ['--validate-options', '--frames'], ['--list', '--frames', '2'],
        ['--check-schedule', '--idle-ms', '10'], ['--check-pixels', '--depth', '3'],
        ['--check-pixels', '--scenario', 'forced-clear'], ['--run', '--run'],
        ['--validate-options', '--display-kind', 'VIRTUAL'],
        ['--validate-options', '--display-kind', 'unknown'],
        ['--check-pixels', '--display-kind', 'virtual'],
        ['--probe-product-host', '--frames', '120'],
        ['--probe-product-host', '--depth', '1'],
        ['--probe-product-host', '--idle-ms', '10001'],
        ['--probe-product-host', '--present-mode', 'fifo'],
        ['--validate-options', '--display-kind', 'virtual', '--display-kind', 'physical'],
    ]
    def invoke(args):
        return subprocess.run(['dotnet', str(DLL), *args], cwd=ROOT, env=env,
                              capture_output=True, text=True, timeout=30)
    for args in invalid:
        result = invoke(args)
        assert result.returncode == 2, (args, result.returncode, result.stdout, result.stderr)
        assert json.loads(result.stdout)['status'] == 'invalid-options', (args, result.stdout)
    result = invoke(['--list'])
    assert result.returncode == 0 and len(result.stdout.splitlines()) == 3
    result = invoke(['--validate-options', '--scenario', 'benchmark-idle-u03', '--depth', '3', '--present-mode', 'fifo', '--refresh-hz', '60', '--display-kind', 'virtual'])
    assert result.returncode == 0 and json.loads(result.stdout)['status'] == 'options-valid'
    assert json.loads(result.stdout)['options']['DisplayKind'] == 'virtual'
    result = invoke(['--check-schedule'])
    schedule = json.loads(result.stdout)
    assert result.returncode == 0 and schedule['status'] == 'managed-policy-check-passed'
    assert not schedule['gui'] and not schedule['productHostProof']
    assert schedule['retirementOwnershipChecked']
    result = invoke(['--run', '--scenario', 'u03-forced-draw', '--frames', '2', '--warmup', '0', '--depth', '3'])
    blocked = json.loads(result.stdout)
    assert result.returncode == 3 and blocked['status'] == 'blocked'
    assert 'No device/window' in blocked['reason']
    assert not any(value in result.stdout + result.stderr for value in ['WgpuDevice.Create', 'vkCreateInstance', 'ERROR_INCOMPATIBLE_DRIVER'])
    result = invoke(['--probe-product-host', '--idle-ms', '10', '--display-kind', 'virtual'])
    assert result.returncode == 3 and json.loads(result.stdout)['status'] == 'blocked'
    print(json.dumps(dict(status='passed', invalidInvocations=len(invalid), managedChecks=3, missingDisplayBlockedCommands=2,
                         missingDisplayBlockedBeforeDevice=True, windowCreated=False, gpuInitialized=False)))


if __name__ == '__main__':
    main()
