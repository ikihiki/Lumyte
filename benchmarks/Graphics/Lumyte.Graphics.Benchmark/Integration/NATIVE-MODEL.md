# Native model descriptor recording

`NativeModelBenchmarks.RecordIndexed` measures real Vulkan CPU preparation and recording.
It includes numeric argument construction/upload command recording, root selection, render state,
indexed draws, finalization and unsent command disposal. Submit, GPU execution and completion are excluded.
Global setup submits a real fixture and checks an opaque red center pixel outside measurement.

Counts 1 and 32 are independent cases. Numeric changes transforms; Static supplies equal numeric values
but still performs explicit uploads; Mutation writes texture/sampler three times before each draw;
CrossPass ends and restarts the render pass between draws. Each invocation creates a new command while
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
