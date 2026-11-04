# Scenario graphics benchmarks (ADR GRAPHICS-0027)

This is an incremental implementation, **not completion of all ADR scenarios**.
The versioned inventory is [scenarios/coverage-v1.json](scenarios/coverage-v1.json).
D01/D02 currently have managed fixtures, CPU measurements, and a Vulkan runner.
Existing legacy tools remain unchanged; their cases are not automatically relabelled
as independently verified ADR scenarios. G04 is a separately maintained CPU project.

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

Grid/deep trees, shared content, cache sweeps/D12, burst traces, snapshot generation
retention, target format/resolution variants, per-stage allocation/live/RSS memory,
font/texture/shader asset hashing, presentation/window scheduling and recovery are
not implemented here. D04-D10/D12/U01-U07/G01-G03/G05-G06 remain explicit harness gaps,
not product API non-support. Direct Scene effects are genuinely unsupported and
need the RenderGraph path. Existing UI fixtures can be extended rather than replaced
with rectangle stand-ins, but lack this independent correctness contract today.

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
