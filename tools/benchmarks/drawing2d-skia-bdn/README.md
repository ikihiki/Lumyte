# Drawing2D / Skia GPU — BenchmarkDotNet

The completed clip-normalization comparison is documented in
[ADR-GRAPHICS-0033](../../../docs/adr/graphics/GRAPHICS-0033-drawing2d-normalized-clips.md).
The authoring-only status below refers to the original six-case Skia comparison,
not the separately executed `ClipFrameBenchmarks` runner.

Independent BenchmarkDotNet 0.15.8 executable, not a launcher for another campaign.
No historical-data reader, legacy JSON schema, paired-loop engine, frame-count engine,
or old positional CLI compatibility is provided. Existing harness files are neither
compiled nor executed. Trace/EGL/Skia conversion and Radeon identity checks were copied
as source, then adapted here. The original six-case runner does not change production APIs.

**Authoring status: source/API inspection only. No build, test, GPU initialization,
quality verification or benchmark was run.** The production allocator/graph policy
must be reviewed and frozen by the owner before any commands below are executed.

## Eventual commands (from repository root)

Execute serially, with no competing build/test/benchmark/GPU campaign:

```sh
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
unset COUNTS PROFILE_IDS

dotnet build tools/benchmarks/drawing2d-skia-bdn/Drawing2DSkiaBdn.csproj -c Release

# Independent validation process; NOT called by any Benchmark method.
# Writes 384 raw RGBA files (~3.2 GB) and quality.txt, so reserve disk space.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll   --verify-quality artifacts/drawing2d-skia-bdn/quality

# Standard BDN listing/filtering (parameters are included in full benchmark names).
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll --list flat
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll   --filter '*rect-dynamic*' --artifacts artifacts/drawing2d-skia-bdn/rect-dynamic

# Full six-case / two-backend matrix, automatic BDN scheduling.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll   --filter '*' --artifacts artifacts/drawing2d-skia-bdn/full
```

Other standard BDN CLI options are passed unchanged. Do not override invocation,
unroll, warmup or iteration counts for headline results. No custom subset parser is
needed; the filter above selects both backends for one parameter value.

## One operation = one completed logical frame

Both public Benchmark methods return actual async Task; BDN must await each before
starting the next invocation. There is no Task.Run, async void, private stopwatch
campaign or batched 50,000-frame operation. Pipeline depth is one, headless, no
presentation/VSync. Dynamic recording is inside the invocation. Static recording is
created once in GlobalSetup on **both** sides (Lumyte immutable CPU DrawList2D,
Skia CPU SKPicture). Dynamic SKPicture is disposed after completion inside that
invocation; DrawList2D has no IDisposable and its reference is dropped after completion.

Lumyte: record (dynamic), PrepareAsync, fresh command creation, target barrier,
prepared.Record, Finish, Submit, production submission.WaitAsync, Completed assertion,
submission/command/prepared disposal. No prepared GPU work survives to the next frame.
Skia: record (dynamic), bind EGL on the **current invocation thread**, replay picture
onto a persistent GPU surface, Skia preparation/encoding via replay/Flush, Submit,
standard OpenGL glFinish actual GPU completion, dynamic picture retirement, unbind EGL.
The render method has no suspension between binding/unbinding; its completed Task
allows BDN's async contract without migrating a current context across await.
GlobalSetup releases current EGL; Pixels and Dispose explicitly rebind their own
thread, so correctness does not assume BDN setup/workload/cleanup thread affinity.

**Backend difference:** Skia GL glFinish is a synchronous driver wait; Lumyte uses
the shipped Vulkan async completion queue / WaitAsync and includes continuation
scheduling. This measures each backend's complete public path, not equal wait
implementations, raw GPU kernel time, or isolated CPU preparation time. No GPU
queries or stage-stopwatch probes are present. Standard library internal caches
(pipeline/shader/path/resource caches) remain enabled; equal CPU reuse does not
assert identical internal caching algorithms.

Setup creates devices, persistent target/surface, canvas, paints, gradients, paths,
clips and font-derived outline geometry. A fully completed/retired setup frame on
both sides initializes lazy pipelines/resources outside workloads (not a custom
warmup campaign; BDN still owns automatic warmup). Both get the same deterministic state
sequence 0..31, cycling one state per invocation, even through pilot/warmup.
Static has 32 identical logical states and reuses only state 0's CPU recording.
Dynamic rectangle/glyph translations and cubic control point changes repeat modulo
4, deliberately preserving the source workload's 32-state distribution rather
than pretending these are 32 unique geometries. Automatic iteration lengths can
leave a final partial cycle (state frequencies differ by at most one within any
contiguous iteration); there is no forced frame batching just to balance it.

Fixtures are 1920x1080 transparent-clear RGBA8 sRGB, premultiplied alpha:

- rect-static / rect-dynamic: 1,000 integer-aligned 30x28 solid RGB rectangles.
- path-static / path-dynamic: 200 cubic paths, linear red/blue gradient, nested
  path clips and small rotations.
- glyph-static / glyph-dynamic: 200 instances of DejaVu Sans B outline (not text
  shaping or glyph atlas performance), solid paints, nested clips and rotations.

## BDN, statistics and evidence

Job.Default controls automatic pilot, warmup and actual iterations. Only the job
ID and OutlierMode.DontRemove are changed. Retain all actual observations, including
slow iterations. Median and P95 columns are percentiles of BDN's normalized
**iteration estimates**, not percentiles of individual frame latencies. Full JSON
export retains the BDN measurements; this harness introduces no old frame-row JSON.
MemoryDiagnoser is standard BDN managed allocation/GC evidence, not native Skia,
Vulkan/driver or VRAM accounting. Async allocation attribution needs interpretation
against runtime GC traces; do not silently relabel it total native memory.

EventPipeProfiler uses the actual cached 0.15.8 constructor documented in

dev-env/nuget-packages/benchmarkdotnet/0.15.8/lib/net8.0/BenchmarkDotNet.xml:

```text
EventPipeProfiler(EventPipeProfile,
    IReadOnlyCollection<Microsoft.Diagnostics.NETCore.Client.EventPipeProvider>, Boolean)
```

The supplied providers are Drawing2D, RenderGraph, UI, UI.Application (all keywords,
Informational), runtime GC/alloc (Verbose, Default | GC | GCHandle | Exception), and CPU sampling,
in addition to EventPipeProfile.CpuSampling. performExtraBenchmarksRun:true is
explicit: traced extra executions are diagnostic and never headline timings.
The v0.15.8 upstream EventPipeProfiler.MapToProviders source was also inspected:
providers deduplicate by name with user entries first. Thus the explicit runtime
provider retains Default CPU/JIT/loader keywords rather than accidentally replacing
them with GC-only flags. EventPipeProfileMapper confirms the CPU and GC profiles.
Sources: <https://github.com/dotnet/BenchmarkDotNet/tree/v0.15.8/src/BenchmarkDotNet/Diagnosers>
UI/RenderGraph providers are requested for a consistent four-module profile, but
this direct Drawing2D fixture does not execute UI/RenderGraph, so their absence
is expected, not fabricated zero-cost module measurements. No activity/frame-ID
instrumentation is added to headline methods; unassociated module events must
remain unassociated, not be guessed into per-frame stage timings. Retain nettrace,
BDN logs, full JSON and exact source/binary hashes with a frozen commit/diff,
OS/CPU/runtime/driver identity and environment. Do not reuse previous campaigns'
measurements as new BDN evidence.

## Hardware and quality gates

EGL rejects anything except Radeon 8060S and rejects llvmpipe. Vulkan requires the
explicit Radeon ICD and checks the actual physical device vendor 1002 / device 1586,
rejecting CPU adapters. Driver identity is printed; a configured ICD alone is not
proof of hardware selection. Hardware proof uses only properties, no timestamp
query allocation. SkiaSharp and native assets are pinned to 4.155.0.

QualityVerification.RunAsync is independently callable, outside benchmark methods.
All 32 states of every fixture are saved for both backends. Rectangles must match
an independent full-image exact RGBA oracle on **each** side. The complex-case
report includes hashes, differing-pixel count, maximum and mean channel errors;
it deliberately labels AA/color-space qualification **required**, even if the
two backends happen to match. Skia AA is enabled; Lumyte's production coverage,
sRGB/linear gradient interpolation and clip-edge behavior must be inspected.
Raw files are tightly packed top-down 1920x1080 RGBA. Complex hashes/diffs are not
an independent rasterizer, nor an unconditional pixel-equivalence pass. Review
curve/clip edge coverage, glyph holes, gradient interior colors and color-space
handling before calling complex timing quality-equivalent; record unresolved
differences and do not publish a speed winner based on lower rendering quality.
No false pass threshold is invented here.

## Remaining owner checks

1. After production freeze, compile and validate BDN generated async harness/CLI,
   setup targets, package resolution, and cached API usage against the actual SDK.
2. Verify both runtime identities on the Radeon, complete all rectangle oracles,
   review complex AA/color-space images and explicitly qualify or block comparison.
3. Inspect frame lifetime on success/device loss, EGL rebinding under BDN process
   execution, sequential invocation, and native resource retirement after completion.
4. Inspect trace runtime CPU+GC events and Drawing2D events, confirm extra traced
   runs are separate, and distinguish unexecuted providers from lost events.
5. Confirm full JSON, iteration median/P95, retained outliers, MemoryDiagnoser and
   automatic pilot/warmup are actually present; archive provenance before analysis.

No results or performance claims exist until those checks are performed.

## 診断と失敗時の owner

headline では profiling provider が無効なため frame Activity を生成しない。EventPipe extra run で Drawing2D provider が有効になった場合だけ、run ID と単調増加する diagnostic frame ID を付ける。setup は frame ID -1 として分離し、診断 frame を BDN の個別 iteration percentile と同一視しない。UI/RenderGraph provider も構成するが、この直接描画 fixture で未実行のモジュールのイベントが無いことを coverage と呼ばない。

Lumyte の呼出しは実 GPU 完了を await し、command memory、prepared resources、submission の順で返す。失敗した wait や不明な Submit acceptance では owner を保持し、cleanup で明示的な device retirement 確認を行う。native command が安全に返せなければ dependent resource を破棄せず失敗を維持する。Skia は呼出し thread ごとに EGL を bind/release し、glFinish によって同じ完了境界を保証する。

## Fixed logical record comparison

`RecordFrameBenchmarks` is a separate selectable matrix using the same BDN
framework. It retains the existing known 1,000-rectangle and 200-path fixtures,
adds 341/342 count controls (not allocation or draw boundaries) and a
200-command mixed-kind fixture, and separates
completion-published prepared reuse from dynamic recording and preparation.
The mixed fixture has a shared analytic gradient, a nonrectangular clip and a
fractional rectangular clip. Its mask extent keeps both variants within the
unchanged default retention budget.

One operation records dynamic input when applicable, prepares, encodes,
submits, awaits actual GPU completion, disposes commands and retires the frame.
The adaptive job has 6–10 automatic warmup iterations and 15–30 automatic actual
iterations, standard pilot/iteration-time policy, retained outliers,
MemoryDiagnoser, median/P95 and full JSON. P95 describes normalized iteration
estimates, not individual-frame latency percentiles. Independent matrix launches
must run serially in the predeclared A1/B1/B2/A2 order with identical executable
harness sources
sources and no concurrent build/test/GPU workloads.

```sh
export TMPDIR="$HOME/.cache/lumyte-fixed-record-evidence/tmp"
export LUMYTE_RECORD_BENCH=1
# Keep the explicit Radeon ICD and activated SDK environment from above.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll \
  --filter '*' --artifacts "$HOME/.cache/lumyte-fixed-record-evidence/abba/A1"
# Repeat on the frozen candidate for B1/B2, then the frozen baseline for A2.
node tools/benchmarks/drawing2d-skia-bdn/summarize-records.mjs \
  "$HOME/.cache/lumyte-fixed-record-evidence/abba" summary.json

# Separate correctness processes, never benchmark invocations.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll \
  --record-quality-write "$HOME/.cache/lumyte-fixed-record-evidence/quality"
# Run against the same directory with the frozen candidate.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll \
  --record-quality-compare "$HOME/.cache/lumyte-fixed-record-evidence/quality"

# Diagnostic-only observed calls/transfers, payload buffer sizes and CPU intervals.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll \
  --record-diagnostics mixed200-dynamic
# Candidate-only selection of five GPU-resident records beginning at index 16.
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll \
  --record-range-diagnostics rect1000-cached
```

Quality capture checks 32 state positions per fixture, stores compressed full
RGBA baseline images and compares all channels. Integer rectangles also require
an exact independently constructed full-image oracle on each variant. Repeated
state positions are preserved; the fixture geometries vary modulo four.
Diagnostic inspection occurs after GPU completion and is excluded from the
reported disposal interval. Observed payload backing sizes are public buffer
sizes, not native driver heaps, pipeline scratch or total VRAM.

`LUMYTE_RECORD_TRACE=1` adds EventPipeProfiler in a separate extra execution.
Only that enabled execution creates per-frame diagnostic Activities; setup is
frame -1. Native GPU timestamps are not provided by this harness. Performance
results, qualifications and regressions belong in the implementation PR, not
this runner documentation or the ADR.
