import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('virtual_results', Path(__file__).with_name('check-virtual-results.py'))
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class VirtualResultsTests(unittest.TestCase):
    def fixture(self, root):
        (root / 'weston.log').write_text('--backend=headless --renderer=gl --xwayland --no-config'+chr(10)+'xserver listening on display :9'+chr(10)+'Using rendering device: /dev/dri/renderD128'+chr(10)+'GL renderer: Radeon 8060S')
        (root / 'allocated-display.txt').write_text('DISPLAY=:9'+chr(10)+'WAYLAND_DISPLAY=owned')
        (root / 'xrandr-virtual-modes.txt').write_text('Screen 0 current 1920 x 1080')
        for name in ['client-exit.txt', 'compositor-exit.txt']:
            (root / name).write_text('0')
        for name, depth in [('u03-depth1', 1), ('u03-depth3', 3), ('owned-idle', 1)]:
            data = dict(status='presentation-host-observations-completed', displayEnvironment=dict(kind='virtual', physicalScanoutVerified=False),
                        fullHardwareLatencyClaim=False, measuredMonitorRefreshHz=None, options=dict(Depth=depth), extent=dict(dpr=1),
                        nativeAdapter=dict(vendorId=0x1002, deviceId=0x1586),
                        samples=[dict(LogicalFrame=i, InputId=i+1, LogicalInputSha256='state', CompositeDrawCalls=1, IssuedDrawCalls=2,
                                      PresentReturnTick=i+2, SubmitStartTick=i, CompletionObservedTick=i+3) for i in range(120)],
                        uiChecks=[dict(status='passed')], finalUiGate=dict(status='passed'),
                        idle=dict(idleReservations=0, idleStart=1, idleEnd=2, resumeInputTick=3))
            (root / (name+'.jsonl')).write_text(json.dumps(data))
        host = dict(status='product-host-observed', productHostProof=True, displayKind='virtual', frameRequests=None,
                    gpuDrawCalls=None, presents=None, physicalDisplayProof=False, monitorRefreshHz=None,
                    update=dict(recordedRevision=1, updateRequested=1, updateApplied=2, updatedRecord=3),
                    idle=dict(recordsBefore=1, recordsAfter=40, recordCallbackDelta=39))
        (root / 'product-host.jsonl').write_text(json.dumps(host))

    def mutate(self, root, filename, change):
        path=root/filename
        data=json.loads(path.read_text())
        change(data)
        path.write_text(json.dumps(data))

    def test_virtual_scope_and_unknown_host_counters_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); self.fixture(root)
            result=MODULE.verify(root)
            self.assertTrue(result['productHostIdleRecordingContinued'])
            self.assertFalse(result['physicalScanoutOrVsyncProof'])
            self.assertFalse(result['productHostDemandIdleClaim'])

    def test_false_scope_missing_input_and_fabricated_counters_rejected(self):
        cases=[('u03-depth1.jsonl', lambda d:d['displayEnvironment'].update(physicalScanoutVerified=True)),
               ('u03-depth1.jsonl', lambda d:d['samples'][1].update(InputId=1)),
               ('product-host.jsonl', lambda d:d.update(frameRequests=0)),
               ('owned-idle.jsonl', lambda d:d['idle'].update(idleReservations=1))]
        for filename,change in cases:
            with tempfile.TemporaryDirectory() as directory:
                root=Path(directory);self.fixture(root);self.mutate(root,filename,change)
                with self.assertRaises(ValueError):MODULE.verify(root)

    def test_software_compositor_is_not_hardware_proof(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.fixture(root)
            (root/'weston.log').write_text('--backend=headless --renderer=gl --xwayland --no-config'+chr(10)+'xserver listening on display :9'+chr(10)+'Using rendering device: /dev/dri/renderD128'+chr(10)+'GL renderer: llvmpipe')
            with self.assertRaisesRegex(ValueError,'software'):MODULE.verify(root)


if __name__=='__main__':
    unittest.main()
