# Native model descriptor recording

`NativeModelBenchmarks.RecordIndexed` measures real Vulkan CPU preparation and recording.
It includes numeric argument construction/upload command recording, root selection, render state,
indexed draws, finalization and unsent command disposal. Submit, GPU execution and completion are excluded.
Global setup submits a real fixture and checks an opaque red center pixel outside measurement.

Counts 1 and 32 are independent cases. Numeric changes transforms; Static supplies equal numeric values
but still performs explicit uploads; Mutation writes texture/sampler three times before each draw;
CrossPass ends and restarts the render pass between draws.
All four original cases explicitly register the selected buffer range per draw, including Static.
Unchanged keeps a full draw-buffer registration and prebuilt element references unchanged after
untimed setup, while advancing numeric values and emitting every root update. It therefore isolates
unchanged registrations, not a no-op frame or unchanged render state. Each invocation creates a new command while
retaining the table and resources, exercising cross-command/frame descriptor reuse. No case claims FPS.

Use the repository runner and a verified hardware ICD:

```sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export LUMYTE_GRAPHICS_INTEGRATION_GPU=1
export LUMYTE_BENCHMARK_PROJECT="$PWD/benchmarks/Graphics/Lumyte.Graphics.Benchmark/Lumyte.Graphics.Benchmark.csproj"
dotnet run --project build -c Release -- --target Compile --project benchmarks/Graphics/Lumyte.Graphics.Benchmark
dotnet benchmarks/Graphics/Lumyte.Graphics.Benchmark/bin/Release/net10.0/Lumyte.Graphics.Benchmark.dll --mode screen --filter '*NativeModelBenchmarks.RecordIndexed*' --artifacts artifacts/native-model
```

For comparison, copy these same sources and project references into isolated source trees for each
revision before building. Freeze executable and harness SHA-256 hashes, alternate independent runs,
retain failures and outliers, and report iteration-estimate median/p95 plus MemoryDiagnoser bytes.
Keep raw results, environment, hashes and statistical qualification under artifacts, not tracked docs.
Deterministic descriptor counts and ownership are covered by DescriptorReuseGpuTests and
VulkanDescriptorGenerationTests, not wall-clock/allocation-budget unit assertions.

## Fixed-size key comparison controls

Use filters `*FixedKeyNativeBenchmarks*` and `*FixedKeyStateBenchmarks*` with the same runner.
RecordIndexed covers 1/32 draws and 1/8 color attachments. PrepareStates covers
1/32 state changes and matching 0/1/8-attachment passes, without draws or submission.
Zero-color coverage is state/key correctness only: depth readback is not supported by
this backend contract, so there is no zero-color full-record or depth-pixel timing claim.

The model fragment shader writes only location zero. Setup submits a current frame and
checks its opaque-red center at output zero. For eight targets, outputs one through seven
have WriteMask.None and must retain their transparent clear; each is read back separately.
This is not an eight-output shader/MRT shading benchmark. CrossPass supports one attachment
only and rejects other combinations. Public workload target counts outside 0..8 are rejected
before recording; RecordInstances/Verify additionally reject zero.

For A/B, copy the exact same corrected workload and both benchmark classes into each variant,
including the source-linked runner. Freeze the generated BDN child output and all DLL hashes
once per variant, inspect generated benchmark IDs, then launch bounded independent A/B/B/A
processes without rebuilding. Preserve all actual iterations and outliers, per-process
median/p95/dispersion, GC operation denominators, and full preparation exports under artifacts.
Standalone GC bytes per operation are raw measurements, not exporter-corrected MemoryDiagnoser
integers. Report p95 as an iteration-estimate percentile, not individual-frame latency.

## Recording reuse comparison

Run the unchanged main suite before copying the final workload and benchmark class into the base tree.
Use the identical source-linked entry point in both variants. For a bounded independent comparison,
`--mode compare --launchCount 1 --warmupCount 6 --iterationCount 15 --iterationTime 250 --keepFiles`
retains the generated child executable. Freeze the entire child output directory, hash every file,
and execute its generated benchmark IDs in A1/B1/B2/A2 order without rebuilding. The preparation
launches are not comparison samples. Keep every actual iteration and GC operation denominator;
report process median, iteration-estimate p95, dispersion and managed bytes/op, including regressions.
This is CPU recording including unsent command disposal, not GPU time, throughput or FPS.
The combined A/B variant does not establish isolated causal shares for individual optimizations.
