# G04 RenderGraph CPU components (scenario version 1)

Independent bounded component of ADR-GRAPHICS-0027 / PR #113. Uses the real public
RenderGraph declaration and compiler APIs. No product changes or simulated compiler.

## Prerequisites and honest dependency boundary

.NET 10 SDK, BenchmarkDotNet 0.15.8, Vulkan 1.3 loader and a device meeting
Lumyte VulkanDevice requirements (dynamic rendering, synchronization2, maintenance4).
Select the ICD explicitly with VK_DRIVER_FILES and record the environment/device
alongside results. Software Vulkan is adequate for these CPU-only checks, but is
not evidence of GPU performance. This project has **no Parley/text/shader-compiler
fixture dependency**; it does require Vulkan initialization outside measurement.

A truly device-free equivalent is not available through today's public API:
CreateTexture requires a non-null IGraphicDevice even though Compile performs no
native allocations. Import-only graph writes are automatically roots, so replacing
transients with fake or imported textures would invalidate unused-pass culling.
We therefore use a real VulkanDevice, never fake textures/devices. Device creation
is GlobalSetup, not a measured operation. Transient texture descriptions are real;
GPU allocations occur only at Record, which this component never calls. Callbacks
throw if accidentally recorded. G04 Record/GPU execution and pixel correctness
are deliberately **not covered** by this CPU component.

## Reproduction (repository root)

Do not run concurrently with any other benchmark, build or test. Build once, smoke
before timing, then smoke again after timing. Parent orchestration owns execution.

```sh
dotnet build -c Release benchmarks/Graphics/Lumyte.Graphics.RenderGraph.Benchmarks/Lumyte.Graphics.RenderGraph.Benchmarks.csproj
dotnet run -c Release --no-build --project benchmarks/Graphics/Lumyte.Graphics.RenderGraph.Benchmarks -- --smoke
dotnet run -c Release --no-build --project benchmarks/Graphics/Lumyte.Graphics.RenderGraph.Benchmarks -- --list flat
dotnet run -c Release --no-build --project benchmarks/Graphics/Lumyte.Graphics.RenderGraph.Benchmarks -- --filter '*GraphBuildAndDispose*' '*GraphCompileAndDispose*' --artifacts artifacts/graphics-benchmarks/g04-cpu
dotnet run -c Release --no-build --project benchmarks/Graphics/Lumyte.Graphics.RenderGraph.Benchmarks -- --smoke
```

For a selected bounded profile use BDN's parameter filter, for example
`--filter '*chain-p32-r32-u0*'` (inspect the listed benchmark names). Default suite
is 11 profiles x 2 methods, not an unrestricted Cartesian product. Job settings:
Release, 5 independent launches, 5 warmup iterations, 15 measured iterations,
250 ms target iteration time, outliers retained, MemoryDiagnoser, median/p95 and
full JSON. No tiering/PGO/GC environment overrides are installed. Keep product
JIT defaults; record inherited DOTNET_*/COMPlus_* variables and do not compare
against older wrappers that disable tiering. BDN p95 is a percentile of iteration
estimates, **not frame latency**. Artifacts/results belong under ignored artifacts/,
never Git. Record commit/dirty diff, SDK/runtime, OS/CPU and ICD/device information.
The explicit project generator avoids ambiguity in a repository with many projects.

## Workload contracts

Each pass owns one output texture per lane, 16x16 Rgba8Unorm, one mip and layer,
CopySource | CopyDestination. Every pass declares full discard CopyWrite; consumers
declare CopyRead. No explicit ordering edges or KeepAlive replace resource hazards.
Names and adjacency arrays are prepared outside measurements. Seed/logical time:
not applicable; all topology is fixed by the versioned profile identifier.

Let P be declared passes, `U = P * unusedPercent / 100`, `L = P - U`, and K be lanes.
All selected ratios divide exactly. Resources = P*K, all transient (including culled).
Unused passes are a suffix of isolated producers with no output mark or consumers.

- chain: pass i reads i-1; mark last output. Live edges L-1.
- fanout: pass 0 feeds passes 1..L-2; sink L-1 reads every branch. Mark sink output.
  Live edges 2L-4. No artificial direct hub-to-sink edge.
- multioutput: four independent interleaved chains; i reads i-4 when i >= 4.
  Mark last four outputs. Live edges L-4. All selected L values are divisible by four.

Let O be output-producing passes (1 or 4). Output resources = O*K. Exact planned
barriers = (2L-O)*K: one initial transition for every live produced texture, plus
one write-to-read transition for each consumed texture. Read fanout does not add
write hazards between readers. Unused resources have lifetime (-1,-1); live first
use is producer index, last use is its final consumer or the producer for outputs.

Profiles: each shape at P=32 and 128, K=1, U=0; chain P=128 with unused ratios
25%/50%; fanout and multioutput P=128 with 50%; chain P=128, K=2 (256 resources)
with U=0. The latter doubles resources while holding passes/topology constant.

## Measurement and ownership

- GraphBuildAndDispose: fresh builder, resource/pass declarations, output marks,
  then builder Dispose. Does not Compile. Includes graph GUID, handle arrays,
  public validation and builder disposal; excludes precomputed names/adjacency/device.
- GraphCompileAndDispose: Compile on the same unchanged predeclared builder,
  consume Statistics, then Dispose the fresh unrecorded plan. Includes the compiler's
  GUID, dependency analysis, culling, barrier planning, standard plan metadata and
  disposal. No snapshot capture or observer during timing. Not a cached-plan lookup.
  Repeated Compile is supported because no retained lifetimes/extensions transfer
  ownership in this fixture. Each plan is disposed inside its operation; no unbounded
  queue/list of plans accumulates. Builder remains alive until GlobalCleanup.

The paired names explicitly include disposal; neither is a disposal-subtracted
estimate. No GPU allocation, Record, Finish, Submit, wait, image effects or FPS.
No full GC is forced by the fixture. Device disposed after all plans and builders.
Failure during declaration disposes the partial builder.

Smoke checks every profile, exact statistics, pass/cull order, transient/output
flags, per-resource first/last-use indices, and exact live dependency edge set.
It compares complete captured barriers/metadata between repeat compilation and
fresh declaration, excluding random graph/run IDs. It verifies disposed builders
reject Compile. Snapshot capture (4 MiB bounded) and JSON output are check-only.
GlobalSetup/GlobalCleanup also verify the selected graph before/after each timed
case; these checks are excluded from the measured methods. These are compiler
semantic/lifetime checks, not GPU allocation-retirement or pixel verification.
