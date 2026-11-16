# Vulkan recording reuse

## Scope and ownership

Base source: main `52d6e8312ca3ba5865b7a879cf3f90cfb79b87a5`.
Combined implementation, tests and frozen harness: `6362e176`.
Review checkpoints: `36ccd764` registration snapshots/layout counts;
`bd2f96bc` independent Set/snapshot ownership; `5661e076` native binds.
They are review boundaries, not independently measured causal shares.
Render-state snapshot representation is unchanged. No dependency, driver feature,
automatic wait, raw completion contract or validation gate is added/removed.

The [backend contract](../../src/Graphics/Lumyte.Graphics.Vulkan/README.md) describes
immutable full registration snapshots, sampled-only descriptor ownership, exact
old buffer ranges, sparse layout counts and conservative pass-local compatibility.
ArgumentTable mutation remains caller-serialized; SetArguments always emits constants.

Tests assert deterministic snapshot/layout-scan/native-bind counters, independent
identity deduplication, disposal and failed-operation publication. Native tests
submit ordered graphics pixels and compute roots, including stable registrations,
shape changes and A/B/A. Collection capacity is reserved before either owner is
published; nondeterministic process-OOM injection is not used.

## Correctness and quality gates

Activate the pinned repository environment first. Test and benchmark opt-ins differ.
Run the whole Drawing suite, including VulkanStateKeyTests, not a narrow descriptor filter.

```sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
unset LUMYTE_GRAPHICS_GPU_TESTS LUMYTE_GRAPHICS_INTEGRATION_GPU
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing.Tests
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests
export LUMYTE_GRAPHICS_GPU_TESTS=1
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing.Tests
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests
dotnet run --project build -c Release -- --target CheckFormat --project tests/Graphics/Lumyte.Graphics.Drawing.Tests
dotnet run --project build -c Release -- --target CheckFormat --project tests/Graphics/Lumyte.Graphics.Tests
dotnet run --project build -c Release -- --target CheckFormat --project benchmarks/Graphics/Lumyte.Graphics.Benchmark
dotnet run --project build -c Release -- --target CheckMarkdown
git diff --check
```

On Radeon 8060S, the two AdvancedCommandTests Vulkan cached/uncached exercises
reject the requested D24 image format in VulkanTexture construction. Reproduce
these on exact main separately; preserve failures without skips, substitutions
or relaxed assertions. Presentation tests have independent display prerequisites.
Hardware success is not a Vulkan validation-layer result when the layer is absent.

## Frozen same-harness CPU comparison

Use the [existing native-model suite](../../benchmarks/Graphics/Lumyte.Graphics.Benchmark/Integration/NATIVE-MODEL.md).
Run its original eight RecordIndexed cases unchanged at exact main first. Then
copy the final NativeModelBenchmarks.cs and NativeModelWorkload.cs into the isolated
base tree. Preserve the source-linked BenchmarkEntry.cs, project references and
all product sources. Verify base product blobs against the base commit; do not
infer provenance from an assembly version or directory name. Hash the identical
two workload sources and entry point in both variants.

Final cases: Count 1/32 and CrossPass/Mutation/Numeric/Static/Unchanged. Numeric and
Static replace buffer ranges on every draw; Unchanged retains a full registration
and prebuilt element references while updating numerics and root constants.
Every scenario submits its own analytic pixel check in untimed setup. Measured
work includes real command creation, preparation, recording/finalization and unsent
disposal; it excludes submit, GPU execution and completion.

For each isolated source tree, build once and preserve its generated child:

```sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export LUMYTE_GRAPHICS_INTEGRATION_GPU=1
export LUMYTE_BENCHMARK_PROJECT="$PWD/benchmarks/Graphics/Lumyte.Graphics.Benchmark/Lumyte.Graphics.Benchmark.csproj"
dotnet run --project build -c Release -- --target Compile --project benchmarks/Graphics/Lumyte.Graphics.Benchmark
dotnet benchmarks/Graphics/Lumyte.Graphics.Benchmark/bin/Release/net10.0/Lumyte.Graphics.Benchmark.dll --mode compare --launchCount 1 --warmupCount 6 --iterationCount 15 --iterationTime 250 --keepFiles --filter '*NativeModelBenchmarks.RecordIndexed*' --artifacts artifacts/recording-preparation
```

Freeze the whole child output directory independently for A/B, including product
DLLs and runtime/dependency files. Keep generated source, benchmark-ID mapping,
source SHA-256 and every binary SHA-256. Preparation timings are not comparison samples.
Inspect matching generated IDs/parameters in both variants. Predeclare A1/B1/B2/A2;
for each variant run every ID in a fresh process without rebuilding:

```sh
dotnet <frozen-child.dll> --benchmarkId <generated-id>
```

Hash-check each frozen directory before and after every variant. Serialize builds,
tests and timed workloads. Keep all WorkloadResult observations, GC denominators,
failures and outliers; do not repeat unfavorable launches. Report every process
median, linearly interpolated p95, IQR, CV, min/max and raw managed bytes/op. Include
each AB/BA shift and dispersion, not just an aggregate favorable mean. Percentiles
describe iteration estimates, not individual-frame latency. Two processes per
variant provide bounded evidence, not universal precision, application impact,
GPU throughput or FPS. Store raw JSON/logs and machine-specific results under
artifacts, not as tracked generated measurements.
