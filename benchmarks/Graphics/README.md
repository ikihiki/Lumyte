# Scenario graphics benchmarks (ADR GRAPHICS-0027)

This is an incremental implementation, **not completion of all ADR scenarios**.
The versioned inventory is [scenarios/coverage-v1.json](scenarios/coverage-v1.json).
D01-D12, U01-U07 and G01-G06 have bounded executable profiles; this does not mean
every ADR variant or formal measurement campaign is complete. D01/D02 additionally
have managed fixtures, CPU measurements, and a paired-process Vulkan driver.
Existing legacy tools remain unchanged; their cases are not automatically relabelled
as independently verified ADR scenarios. The [ADR proposal](../../docs/adr/graphics/GRAPHICS-0027-scenario-based-graphics-benchmarks.md)
defines the wider contract; its proposal status is unchanged by these implementations.

## Family entry points

Each family documents its precise profile, independent checks, timing boundaries and
remaining gaps. Diagnostic runners are not automatically formal comparison campaigns.
Do not run families, builds or tests concurrently with performance measurements.

- D01/D02, bounded D03/D11: this README and the CPU/GPU projects.
- D04/D05/D08/D09/D12: [ordinary 2D](Lumyte.Graphics.Ordinary.Benchmarks/README.md).
- D06/G03: [images and transfers](Lumyte.Graphics.ImageTransfer.Benchmarks/README.md).
- D07: [native text and distance fields](Lumyte.Graphics.Text.Benchmarks/README.md).
- D10/G04 Record: [executed layers and effects](Lumyte.Graphics.Effects.Benchmarks/README.md).
- U01-U07: [real headless UI](Lumyte.Graphics.Ui.Benchmarks/README.md).
- G01/G02/G05/G06: [graphics primitives](Lumyte.Graphics.Primitives.Benchmarks/README.md).
- G04 CPU build/compile: [RenderGraph components](Lumyte.Graphics.RenderGraph.Benchmarks/README.md).
- Independent paired-process campaigns: [driver](../../tools/benchmarks/graphics-scenarios/README.md).

The ordinary CPU fixture test project is registered in `Lumyte.slnx`; native/GPU
checks remain explicit gates rather than wall-clock assertions in normal unit tests.

## Build and fixture smoke

Run from the repository root, with the repository-supported .NET 10 SDK:

default commands:

    net='dotnet' # use your configured SDK executable
    $net build benchmarks/Graphics/Lumyte.Graphics.Cpu.Benchmarks -c Release
    $net build benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -c Release
    $net test tests/Graphics/Lumyte.Graphics.Benchmarks.Tests -c Release

No Vulkan or native text engine is required by the managed-only fixture tests or CPU
project. Tests assert exact rounded update counts and state parity, static recording
identity, full Canvas rebuilding on changes, partition amplification and determinism;
there are no wall-time thresholds. GPU smoke is separate and required for pixel/lifetime coverage.

## Deterministic D01 / D02 fixture

Exactly 4096 opaque rectangles in a 64x64 grid, all visible at 1920x1080 Rgba8Srgb,
DPR 1. Content sizes are 1, 16, 64, 256, 4096 commands per node; flat sibling tree
and painter order stay fixed. Logical update profiles are static, one, 1%, 10%, all
(actual counts 0, 1, 41, 410, 4096). A fixed seed 27 chooses a contiguous run or a
unique dispersed affine permutation. Selected colors toggle red/white on *every*
logical frame; this is not same-value assignment. Input IDs hash complete ordered
binary color state plus trace version, independently of partition and path.

Canvas keeps its reusable Canvas and completed immutable DrawList on static frames;
any change records the complete list. Scene records each affected content node using
Canvas then calls public ReplaceContent; this cost is included. Canvas never edits a
shadow Scene. Initial construction is excluded from steady-state measurement.

GPU checks independently record original input directly into Canvas, compare every
RGBA byte, and analytically check all 4096 interior colors. They execute before and
after timed work (cold runs check after their first frame); actual final targets of
all active slots are checked too. Red/white endpoints avoid sRGB rounding tolerance.
This fixture does not establish clip, blend order or text correctness for other cases.

## GPU execution

The linked timer currently **requires Radeon 8060S (1002:1586)** and validates the
actual created adapter and driver. It is not a portable backend adapter or software
GPU correctness runner. Verify hardware with vulkaninfo and set VK_DRIVER_FILES to
the verified hardware ICD. Missing/invalid timestamps fail rather than use CPU wait
latency as GPU time. Unsupported timestamp capability is null, never synthesized.

    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --phase smoke --update one --locality dispersed --granularity 64 --path scene --depth 3 --frames 4 --output artifacts/graphics-benchmarks/smoke-scene
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --phase pilot --update one --locality dispersed --granularity 64 --path canvas --depth 1 --frames 2000 --output artifacts/graphics-benchmarks/pilot-canvas
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --phase formal --update one --locality dispersed --granularity 64 --path scene --depth 3 --warmup 200 --frames 2000 --output artifacts/graphics-benchmarks/formal-scene
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --phase cold --update one --locality dispersed --granularity 64 --path scene --depth 1 --output artifacts/graphics-benchmarks/cold-scene

All CLI options require a value; unknown, duplicate and invalid options reject before
creating a device. Run a *new process* per cold observation. Device initialization
and first renderer Prepare are separate; driver disk cache coldness is unknown and
no caches are deleted. Cold fixture initial Canvas/Scene construction is not timed.

Pilot reports every eligible 50-frame boundary from 200 through 2000. It tests the
last four 50-frame median host/gpu windows for max/min <=1.05; unavailable GPU data
is explicitly excluded. Take the larger stable warmup from both paths, fixed for
both; report unstable if the limit fails. Pilot also recommends a fixed measured
frame count for >=10 seconds. Formal requires >=200 warmup and >=2000 measured
frames but **one formal invocation is not a complete comparison campaign**. Choose
larger common fixed counts before formal execution, never adapt formal stopping to
the observed results. Five independent process pairs, alternating A/B then B/A,
and an A/A noise case are required for comparative conclusions. Keep per-process
distributions separate. No build/test/timing concurrency during measurements.

Each slot owns target, commands, prepared frame, submission and query. Slot reuse
waits and retires the old frame; warmup drains before starting elapsed throughput;
final drain/retirement is included in elapsed time. Depth 1 is serialized stage
diagnosis; depth 3 is bounded headless throughput, not Present FPS or input latency.
Completion latency is observation-based host wall time, including delayed observation
at slot reuse. Never add host-frame and GPU spans as independent durations.

## Outputs and counters

Each run writes manifest.json, frames.jsonl, checks.jsonl, summary.json *outside*
all timed windows. No generated results belong in Git. Rows bind run/case/profile,
logical frame and input ID. Objects, sample arrays, GPU-result lists and IDs are
preallocated before timing. Normal renderer/async allocations remain measured;
no forced GC is performed. Failed checks/device errors generate failed summaries,
not successful performance results. Unknown submissions quarantine resources and
the device instead of freeing resources while GPU work may be live.

Record/publish includes Begin/End and Scene content replacement. Prepare includes
borrow/release and awaited resource work. Encode includes commands, initial barrier,
Record and Finish. Submit is the host queue call. Host frame spans Apply through
submit and attachment bookkeeping. Slot wait includes previous completion/retirement.
GPU spans cover Record clear/draw, not Prepare uploads or the initial barrier.
Draw counts are real successful forwarded API calls, not node counts or native
primitive counts. Compile/reuse are public RetentionStatistics differences; cached
asset bytes are estimated *idle* cache storage, not total retained memory or VRAM.
Missing upload-byte, eviction, native VRAM and shader hashes are explicitly null.
Default cache 512 assets/64MiB may cause static misses at fine granularity; do not
reinterpret that budget as an all-VRAM limit or assume all static preparations hit.

Summary contains per-process median/p95/max and host/gpu budget exceedances, plus
final-drain elapsed throughput. p95 is nearest-rank; p99 is intentionally absent.
Process-independent A/B comparison/bootstrap orchestration is not yet part of this
runner. Manifest records created adapter/driver, runtime/JIT env, CPU/OS, diff/hash,
untracked paths, target and counter/window provenance; source content hashes include untracked harness files and output binaries; source contents
must still be committed or archived externally before a reproducible formal campaign.

## CPU BenchmarkDotNet

    dotnet run -c Release --project benchmarks/Graphics/Lumyte.Graphics.Cpu.Benchmarks -- --filter '*RectangleBenchmarks*'

5 launches, 5 warmup iterations, 15 measured iterations, target 250ms/iteration,
no outlier removal, MemoryDiagnoser and full JSON export. Product default JIT/PGO/GC
is preserved (do not use legacy tiering-disabling wrappers). One operation is public
record-and-publish, not GPU Prepare or UI layout. Full parameter Cartesian enumeration
is available for explicit CPU campaigns; use BDN filters for smaller verification.
Iteration estimate percentiles must not be called individual-frame latency percentiles.

## Deliberate gaps

The D01/D02 core does not implement grid/deep-tree sweeps, burst traces, retained
snapshot generations, format/resolution sweeps, per-stage live/RSS/native memory,
or presentation/window scheduling. Separate family entry points above implement
bounded image, text, clip, overlap, working-set, effect, UI and primitive profiles;
their READMEs define exact inputs, correctness gates and remaining gaps. A family
being executable does not imply a portable backend or formal performance result.
Direct Scene effects are genuinely unsupported and use the RenderGraph path instead.
Native device-loss recovery, host idle/resume and a 3D UI preview remain unmeasured.
See [verification](VERIFICATION.md) for the integration gates and reproduction commands.

## D03 placement and D11 structural diagnostic subset

    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --scenario D03 --placement parent --sharing shared --depth 3 --frames 8 --output artifacts/graphics-benchmarks/D03-parent-shared
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --scenario D03 --placement individual --sharing distinct --depth 1 --frames 8 --output artifacts/graphics-benchmarks/D03-individual-distinct
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --scenario D11 --depth 3 --frames 8 --output artifacts/graphics-benchmarks/D11-roundtrip

D03 has256 grid placements with identical red content and painter order. All four
parent/individual x shared/distinct combinations render identical inputs. Each step
translates all elements4px right or returns them; immutable content is never rebuilt
during placement updates. This covers translation/all only, not rotation/1%/tree sweeps.

D11 repeats a bounded4-phase trace: insert yellow over red/green/blue; remove yellow
and move red last; reparent blue into second sibling group; return blue and restore
red ordering. Three or four overlapping opaque rectangles make order observable.
Every trace phase is checked against independent Canvas source ordering and known
center colors before measurement; final targets of every in-flight slot are checked
after final drain. Counters report successful public edit calls, real draw calls and
public cache statistics, never estimated structural-work complexity. D11 is deliberately
small, not evidence of large editor-tree scaling or burst behavior.

Mutation mode writes all four artifact files with separate graphics-mutation-v1 schema.
It rejects D01/D02 options and D11 placement/sharing options. It is diagnostic only:
no cold/pilot/formal orchestration yet; its throughput includes the first Prepare of
a fresh renderer, and must not be compared to warmed formal D01/D02. Source input IDs
and sample buffers are prepared before timing; no JSON/readback/reference inside
measurement. Mutation CPU editing is selectable through SceneMutationBenchmarks in
the same product-default BDN configuration; CPU operations exclude GPU preparation.

## Core periodic burst, optional history, and retained memory (rectangles-v2)

The trace version is now rectangles-v2; old CLI profiles retain their meaning, but
v1/v2 input hashes must not be treated as identical scenario versions.
--update burst changes **all 4096 visible colors** at absolute logical frame 30,
returns to the initial colors at 150, and repeats transitions every 120 frames.
Between events the immutable recording is reused: no same-value ReplaceContent,
no recording, zero changed elements/nodes/commands. Repeated or jumped frames compare
actual destination color state, not frame parity. A jump across two events ending
in the same state correctly does no work; the runner normally visits every frame.

Frame rows additionally carry temperature (cold/warm/burst), burstEventName
(full-update/return), burstEventFrame and signed burstRelativeFrame. Anchors select
the nearest scheduled event (ties select the next). Group by event-name + relative
frame for pre/post-event latency and cache recovery; report burst and steady frames
separately. The existing whole-run distribution is **not burst latency**. Runs shorter
than 150 frames do not measure both transitions. Warmup follows the same absolute
schedule and does not reset the burst clock. Burst pilot checks four complete
240-frame state cycles (960 observations minimum) at 240-frame boundaries. Every
exact phase must have a finite positive max/min ratio <=1.05 across the four cycles;
a spike cannot be averaged away by idle frames. This deliberately strict pilot may
report unstable rather than hiding periodic outliers. Non-burst pilot policy is unchanged.

Manifest/summary extension core-phases-history-v2 preserves the existing schema and
non-burst fields. The manifest gives representative frame-to-input IDs including both
burst colors and return state, the selected history budget, and the fixed schedule.
Summary temperatureDistributions separates burst from warm/cold;
burstEventRelativeDistributions groups same-named signed phases across cycles;
burstEvents provides each event occurrence with its event sample and +/-4-frame
pre/post distributions and input/counter records. Whole-run distributions remain
available for compatibility and are explicitly labelled not burst latency.

--snapshot-history 0|1|3 defaults to zero. Before an actual content change, Scene
uses **public DrawingScene2D.CaptureSnapshot()**; Canvas retains its completed
immutable DrawList. Keep the last requested count of *changed generations*, not
copies of static frames. Capture and retention bookkeeping is in record/publish.
Per-slot owners pin histories through GPU completion even after rolling-history
eviction; uncertain submissions quarantine owners. The normal read lease is held
through awaited Prepare; prepared resources keep existing completion-safe ownership.

Separate-renderer preflight/postflight execute the actual sequential burst/return
trace, checking exact independent Canvas RGBA and analytic colors at frames
29/30/31/149/150/151 plus later history generations. Old history images are checked
against their original input after later content mutations. Every active slot's
final measured target is still checked at depths 1/3. Unit coverage includes all
granularities, no-op/repeated/jumped states, bounded history, Scene disposal,
eviction, and idempotent completion-owner disposal; no elapsed assertions.
Public source contracts are Drawing2D/Retained/DrawingScene2D.cs,
Drawing2D/Retained/DrawingSnapshot2D.cs and Execution/Renderer2D.Retained.cs.

Initialization observations in checks.jsonl separately report fixture recording,
renderer construction and target/query creation host time, outside frame timing.
Initial construction remains excluded from the frame interval; first Prepare retains
first shader/pipeline/resource preparation costs. No driver-cache-cold claim is made.
Retirement samples also report idle-cache state after completion. Idle-cache byte
estimates are **not total VRAM** or live GPU allocation.

Run after serialized Release validation and hardware preflight:

    dotnet test tests/Graphics/Lumyte.Graphics.Benchmarks.Tests -c Release
    dotnet build benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -c Release
    dotnet build benchmarks/Graphics/Lumyte.Graphics.Cpu.Benchmarks -c Release
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --phase smoke --update burst --locality contiguous --granularity 64 --path scene --depth 3 --frames 151 --snapshot-history 3 --output artifacts/graphics-benchmarks/burst-scene-history3
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Gpu.Benchmarks -- --phase smoke --update burst --locality contiguous --granularity 64 --path canvas --depth 3 --frames 151 --snapshot-history 3 --output artifacts/graphics-benchmarks/burst-canvas-history3

Repeat both paths at depths 1/3 and history 0/1/3. Formal comparisons still require
fixed warmup/frame budgets and independent alternating process pairs.

ManagedRetentionProbe.cs implements the **separate CPU process mode** below. It
reports full-GC managed live heap and process RSS as distinct checkpoint values,
not BenchmarkDotNet allocation/op or frame latency. Native/GPU allocation remains
null. Version 2 observes baseline, initialized fixture, eight fixed mutate/return
cycles, and after-dispose. A non-inlined ownership scope ends before the final
collection; GC.KeepAlive preserves owners through the earlier observations.
Absolute process memory includes runtime caches, not exact fixture-object bytes.

Default selection sweeps both paths and history 0/1/3 at granularity 64. For fair
retention comparisons, run each selected profile in an independent fresh process
and retain repeated observations rather than a single favorable delta. Full blocking
GC runs only in this dedicated probe. No elapsed/byte thresholds are unit assertions.
Create the output directory before redirection.

    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Cpu.Benchmarks -- --retained-memory --memory-path canvas --snapshot-history 3 --memory-cycles 8 > artifacts/graphics-benchmarks/managed-retention-canvas-h3.jsonl
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Cpu.Benchmarks -- --retained-memory --memory-path scene --snapshot-history 3 --memory-cycles 8 > artifacts/graphics-benchmarks/managed-retention-scene-h3.jsonl

These additions alone do not complete all ADR scenarios, real-window/Present coverage,
backend portability, or a formal performance campaign.
