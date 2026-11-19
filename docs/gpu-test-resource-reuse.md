# Serial GPU test resource reuse

## Scope and coverage

The FixedRecords, NormalizedClipSamples and LayersEffectsAndBackdrop pixel
checks reuse device-bound drawing shader/pipeline owners within one test method.
They still create their own device per method. GPU methods and assemblies remain
serial; the lane runner, SDK invocation count and category boundaries are unchanged.
No collection/class fixture creates a device during discovery or skipped tests.

The first render of each default/reference path remains a fresh resource boundary.
Subsequent renders use two separate lazy owners:

- Default: canonical embedded shader identities enable fixed records and their
  mode-specific pipeline cache.
- Reference: ExternalDrawingResources deliberately clones shader artifacts,
  preserving the external/legacy route and disabling fixed-record eligibility.

Sharing only DrawingResources2D would retain the primary legacy pipeline but lose
DrawingResourceState2D's fixed-record pipeline families. The small internal
RenderGraphRenderer2D constructor therefore borrows an existing state with its
normal reference-counted lifetime token. It validates physical-device identity.
There is no new public API, device-global cache, reflection, shader change or
backend-specific optimization. Existing public constructors retain their original
ownership and lazy-default behavior.

Each render still has private graph bindings, arguments, targets, readback buffers,
prepared snapshots and command buffers. The fixture disables idle numeric-buffer
retention (zero budget); the intended benefit is pipeline reuse, not smaller pixel
coverage or retaining per-draw uploads. Every submission is awaited, readback is
mapped/copied/unmapped, then commands, compiled plan, prepared drawing and
submission are disposed before another render. The method-scoped owners are
closed before the device. Prepared uses protect pipelines even if the root closes
first, using existing retryable cleanup semantics.

Independent device creation/loss/disposal tests, native ownership gates and the
cold maximum-capacity pipeline guard do not use this fixture. Pixel inputs,
reference outputs, tolerances, ABI validation and shader capacity limits are
unchanged. Deterministic managed tests check pipeline identity/count stability,
private storage allocation, context/device separation, fixed-versus-legacy
eligibility and deferred/idempotent cleanup.

## Reproduction

Use the repository-pinned SDK 10.0.403 and its documented native dependencies.
Build the exact base before editing, freeze its complete test output in a separate
directory, and hash the sources and binaries. Do not compare against previously
built Drawing2D assemblies: the earlier investigation had a stale assembly from
before the identical-circle radial-gradient guard.

For bounded local comparisons, run the same test filter against each frozen output:

```sh
dotnet vstest <frozen-output>/Lumyte.Graphics.Tests.dll \
  --TestCaseFilter:"FullyQualifiedName~WgpuFixedRecordsAsync|FullyQualifiedName~WgpuNormalizedClipSamplesAsync|FullyQualifiedName~WgpuLayersEffectsAndBackdropAsync" \
  --logger:"trx;LogFileName=targeted.trx"
```

Set LUMYTE_GRAPHICS_GPU_TESTS=1, select the absolute lavapipe ICD with
VK_DRIVER_FILES, set MESA_SHADER_CACHE_DISABLE=true and LP_NUM_THREADS=4 for both
variants. Disabling the disk cache avoids an uncontrolled pre-existing cache;
in-process per-pipeline variant reuse remains enabled. Use Release, serialize
builds and timed runs, and retain all observations on a shared host.

Run the full GPU lane using the existing entry point:

```sh
dotnet run --project build -c Release -- --target Test --lane gpu --host-only
```

Run the Drawing2D ownership/cleanup regression project and the CPU/integration
lanes separately. Do not interpret software-GPU timings as hardware execution
latencies or promise a CI wall-clock threshold. The parent PR review and terminal
CI establish the final hosted-run effect.

## Local evidence (2026-11-18)

Base: 55237446bc771b987970cea36bbf3c8e6053b241. The initially clean checkout
was rebuilt before the first edit; its complete output was copied/frozen before
measurement. A detached worktree preserves the exact base source. No project
reference rebuild or candidate mutation overlaps a timed run. Final candidate
assembly hashes also match the assemblies used by the subsequent full GPU lane.
All 18 embedded Drawing2D resource hashes match between base and candidate;
the six draw-record fragment artifacts differ from the stale earlier diagnostic
assembly, confirming that its pre-guard shader payload was not reused.

Shared host: Ubuntu 26.04, Linux 7.0.0-42-generic, Ryzen AI MAX+ 395,
32 logical CPUs; SDK 10.0.403, runtime 10.0.14, Slang 2026.19;
Mesa lavapipe 26.0.8-1ubuntu0.3. Both variants use the same pinned corrected
native wgpu output and LP_NUM_THREADS=4. This is software Vulkan, not the
machine's physical Radeon GPU. Shared-host scheduling/frequency is uncontrolled.

### Disk-cache-disabled targeted control

One uninstrumented process per variant; all three tests and their pixel
assertions pass, with no skips. MESA_SHADER_CACHE_DISABLE=true for both.

| Wgpu test | Base seconds | Candidate seconds |
| --- | ---: | ---: |
| FixedRecords | 1210.378 | 62.178 |
| NormalizedClipSamples | 380.555 | 39.674 |
| LayersEffectsAndBackdrop | 173.891 | 66.596 |
| Process wall | 1765.85 | 169.43 |

This deliberately cache-disabled result is **not** a prediction of warm CI.
It has one observation per variant, not a statistical distribution. The candidate
control precedes added fixture-selection and closed-constructor regression
coverage; rendering implementation is identical. Final-source comparable repeated observations follow below.

### Warm-cache bounded ABBA comparison and native operation counts

A copied-source diagnostic Wgpu assembly wraps only the synchronous
wgpuDeviceCreateRenderPipeline call with Stopwatch and one NATIVE_CREATE log.
The same diagnostic assembly and runner are used for both variants. They invoke
the three existing public Wgpu test methods sequentially and time each complete
method, including device creation, all preparation, submissions, completion,
readback/assertions and disposal. No fixture or product source is recompiled by
the runner. No shader, draw count or target dimension changes are involved:
FixedRecords still performs 84 renders, NormalizedClipSamples 36 and Layers 20.

Seed a new Mesa disk-cache directory by running the candidate once (unmeasured
training: 49.680 / 23.005 / 21.699 seconds). Freeze it, then make a fresh copy of
that same seed for **each** measured process. Run base, candidate, candidate,
base, with disk cache enabled. All measured methods pass and Mesa reports zero
misses in every process. The seed is not mutated during these comparisons.

| Case | Base observations (s) | Candidate observations (s) | Median base → candidate (s) | Native creates base → candidate |
| --- | --- | --- | --- | --- |
| FixedRecords | 207.003, 207.484 | 10.728, 10.827 | 207.244 → 10.777 | 162 → 10 |
| NormalizedClip | 70.966, 70.908 | 8.334, 8.171 | 70.937 → 8.252 | 52 → 5 |
| Layers | 14.549, 15.078 | 7.454, 7.451 | 14.814 → 7.452 | 64 → 34 |

Counts are identical across both repetitions of each variant. Median summed
method wall falls 292.995 → 26.482 seconds (90.96%); individual process walls
are base 292.68 / 293.65 and candidate 26.63 / 26.55 seconds. With only two
processes, these medians establish a bounded local comparison, not a CI service
level or tail-latency guarantee.

Median wall inside the native pipeline-creation calls falls:

- FixedRecords: 202.964 → 9.647 seconds.
- NormalizedClip: 69.763 → 8.031 seconds.
- Layers: 14.068 → 7.102 seconds.

This directly confirms fewer native pipeline creations rather than fewer pixels
or merely less readback work. Remaining Layers pipelines include graph-local
effect work not covered by this fixture. Native-call wall is not hardware GPU
time; specialization/module/layout and driver/compiler subphases are not
separately attributed in this final harness.

### Validation

All runs are serialized, with no full-suite/timed-run overlap:

- Drawing2D managed project: 473 passed, 0 skipped, including four new sharing
  tests plus existing creation/failure/lifetime/cleanup gates.
- Direct managed fixture selection test: 1 passed; GPU opt-in was unset.
- Unique empty-cache VulkanProgramCapacitiesAsync process: 1 passed, 0 skipped;
  no fixture reuse, cache restoration or capacity-limit change.
- Full existing GPU lane plus pipeline validation: 156 passed, 7 skipped,
  0 failed across 9 assemblies; target 5m07s / process 309.77s. This uses a
  separate enabled disk cache copied from the warm seed, not disabled-cache
  conditions. There is no matched full-lane base measurement here.
- Full CPU lane: 3819 passed, 0 skipped, 0 failed across 49 assemblies.
- Full integration lane: 911 passed, 16 skipped, 0 failed across 30 assemblies.
- Lane discovery/classification validation succeeds for all three lanes;
  the existing single SDK invocation per category and serial GPU runner remain.
- Scoped managed format verification passes (workspace-load warnings only);
  repository Markdown lint passes across 369 files, and git diff --check passes.
- Independent read-only lifetime review found no blockers; its suggested direct
  first/following-render and closed-context constructor assertions were added.

GPU skips are the existing opt-in presentation pair, unsubmitted-native stress
case, graphics/map benchmark pair and desktop/native-UI benchmark pair; they are
not claimed as executed coverage. Integration skips are existing browser,
OpenAL/audio and native X11 opt-ins. Browser API execution was not rerun; its
boundary and configuration are untouched. Hosted PR/whole-CI timing and actual
hardware measurements remain for the parent review/terminal CI.

### Evidence provenance and counter reproduction

Local raw evidence resides in /tmp/lumyte-gpu-reuse-evidence: baseline/candidate
frozen outputs, source/binary manifests, native harness sources and hashes,
warm-{baseline,candidate}-{1,2}.log, comparison-summary.json, TRX snapshots,
cold-capacity log, full-lane logs and environment identity. The earlier
/tmp/lumyte-gpu-investigation-phase-55237446 binaries are not used.

Key SHA-256 values:

| Assembly | Base | Final candidate |
| --- | --- | --- |
| Graphics.Tests | 69463fbdf1c4e7148fb473ec056500401c522a255986038c5a2cddbe01e6eac9 | 20befdcbade45289479631fc0ff3d81c65f551682bc07b6f2464abc1ca3fbe0d |
| Graphics.Drawing2D | d328f6f79ed5a78dc490ea15e728b08b744e787d851e255616497fa44ab8b9f5 | 58e8e86699b8ce5a7b973d1f7e1b700bbe014536de6ecb1f6e51c89cb322300d |
| Graphics.Drawing2D.RenderGraph | bfc6c8846258a8c391fdd29e04754eb36527f6c964e27517d17aeaf44dd1648f | 6c0786be95b7e8041fd6aa669720cadaa73ed2b8609338ea5baad6aa0e7b0f0e |

Counter Wgpu DLL: e18f0e97923a4a0d8926197d993c9c8005e4b9be7aa9b2e471001e83dbeb2a38.
Runner DLL: 742a64869012a37d503bc68b8ffa745d5873bcd84f88c20cf1ab6310d4aa5f4f.
These diagnostic DLLs are not shipped or used by the full-lane correctness gate.

To reconstruct the counter harness without stale project references:

1. Freeze complete exact-base and candidate Release test directories. Compile a
   separate net10.0 executable referencing only the frozen base DLLs (no
   ProjectReference); call WgpuFixedRecordsAsync,
   WgpuNormalizedClipSamplesAsync and WgpuLayersEffectsAndBackdropAsync in
   order, awaiting each and logging its Stopwatch interval.
2. Copy the exact base Wgpu C# sources into a separate library project named
   Lumyte.Graphics.Wgpu. Reference the frozen DLLs except the original Wgpu DLL;
   enable unsafe code and the Lumyte.Graphics.Shared global using. Wrap the
   single native creation call with Stopwatch and log its elapsed milliseconds
   once after the call. Do not change Resolve's cache or test assembly code.
3. Make two run directories from the frozen test outputs. Copy the same runner
   DLL/deps/runtimeconfig and the same instrumented Wgpu DLL into both. Hash
   everything before running; never let a launcher rebuild project references.
4. Train and freeze the Mesa seed, make identical independent cache copies,
   execute ABBA as above, and group NATIVE_CREATE rows between per-test markers.
   Retain all process/case times, native counts and cache-hit/miss diagnostics.

The ordinary uninstrumented TRX commands remain the correctness and timing
control. This diagnostic is deliberately not a new build target, test lane,
production cache or benchmark API.
