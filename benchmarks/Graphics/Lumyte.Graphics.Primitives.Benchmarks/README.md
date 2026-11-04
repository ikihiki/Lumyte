# Graphics primitives diagnostic fixtures (ADR-GRAPHICS-0027)

Integrated executable: `benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks/`.
See [verification](../VERIFICATION.md) for strict builds and bounded correctness gates.
Repository analyzer, formatter, documentation and warnings-as-errors defaults remain enabled without project-level suppressions. No product changes, external packages beyond existing project dependencies, new shader compiler integration, or fake GPU objects.

## Build and run (repository root)

Do not run concurrently with other builds, tests or benchmark measurements.

```sh
mkdir -p benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks

dotnet build benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks/Lumyte.Graphics.Primitives.Benchmarks.csproj -c Release
# Select a verified hardware ICD, not a guessed adapter name. This host's documented Radeon path:
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks -- --frames 12 --warmup 3 --output artifacts/graphics-benchmarks/primitives-smoke
# Isolate one domain (G01, G02, G05 or G06); both depth 1 and 3 always execute:
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks -- --case G06 --frames 24 --warmup 3 --output artifacts/graphics-benchmarks/primitives-g06
```

Do not disable tiering/PGO/ReadyToRun. The runner records inherited runtime overrides; unset inherited overrides for product-default JIT measurements. It captures physical-device name/vendor/device/type/driver from the **created Vulkan device**, not the CLI selection. Software adapters can verify correctness, never hardware performance claims. Native shader build prerequisites are the existing repository ones.

## Implemented bounded profiles

- G01: exactly 24 real full-screen draws, 1 command/1 submit, 4 commands/1 submit, 4 commands/4 submits. All commands draw into the same slot target, preserve order, and contain real binding/clear/barrier work. No empty-submit approximation. Render-pass count changes with command count and is intentionally part of this diagnostic.
- G02: invokes the unchanged public `PipelineBenchmark.RunAsync` sample separately. New bounded fixtures use its same compiled full-screen/color shaders, with fresh program creation versus resident programs and grouped/alternating front-face raster variants (culling disabled so output remains identical). Fresh native program does not imply cold driver disk cache. Native PSO realization may happen on Draw; reported prepare and encode remain separate. Many-state sweeps are not implemented.
- G05: actual `ModelMesh.CreateAsync`, initial staging upload, `ModelDrawing.CreateArguments/CreateRenderState`, registered raw vertex buffers, depth attachments, real sampled white fallback, `SetArguments` and `ModelMesh.Draw`, following DrawingGpuTests API patterns. Shared and unique GPU mesh allocations are separate. Objects overlap the viewport at distinct Z translations. Fixed draw=4 compares 1/2 triangles per mesh; fixed indexed vertex=24 compares 4 draws×2 triangles with 8 draws×1 triangle. Triangle subdivision preserves coverage. Opaque and straight-alpha transparent variants run separately. No culling/LOD/indirect claims. Material and fallback textures are shared; texture/material diversity sweeps are absent.
- G06: repeated mesh asset generations change actual uploaded vertex colors red→green→red, display them, and retire old generations only after corresponding slot submissions complete. It is an asset **replacement** update, not in-place mutation, a file decode benchmark, or ResourceManager coverage. Staging is released only after real GPU completion; GPU/native allocation sizes and peak memory are **null**, not estimates presented as native memory. The ordinary frame fixture excludes recovery; the independent explicit mode below performs real native WebGPU destruction and Hosting recovery without a managed fake-loss flag.

## Correctness and lifetime

32×32 Rgba8Unorm/DPR1 is explicitly a bounded diagnostic, not the ADR's full-HD sRGB standard profile. All pixels are checked against an independent analytic RGBA reference, including alpha accumulation, with a predeclared 1-UNORM-LSB rounding allowance. Serialized first/mutated/last frames and final **actually pipelined** slot outputs at depths 1 and 3 are checked. Constant full-screen triangles make coverage exactly analytic; this is deliberately not an arbitrary model scene. G06's green reference depends on updated vertex bytes, not merely a changed material.

Each slot owns its target, depth, program, mesh generations, table, command buffers and completion handles. Slots wait before reuse; final drain and retirement are included in throughput. Unknown submit acceptance or failed completion quarantines the entire slot array and device until process exit; it never reuses or destroys unknown-in-flight resources. A failed case invalidates the process; later cases are not silently substituted. No elapsed-time assertions.

## Artifacts and boundaries

`manifest.json`, `frames.jsonl`, `checks.jsonl`, `summary.json` plus tightly packed RGBA reference-check images (32×32×4 bytes). Manifest includes actual adapter/driver, runtime/environment, source commit/diff hash, copied source hashes, scenario input hash, seed and explicit gaps. Each frame includes forwarded call counts, input/frame ID, stages and completion latency; JSON/image comparison and sample arrays are outside timing. Shader target-code hashes are per frame. Pixel-file hashes are in checks. `indexedVertices` is API requested indexed work, not a native GPU counter. Vulkan ordinary frames use a slot-owned native timestamp pair, from the first command's TOP_OF_PIPE to the last ordered command's BOTTOM_OF_PIPE. Initial barriers, mesh uploads, clear and draws are included; readback is excluded. Multi-command render passes have explicit attachment ordering barriers. Separate submits can expose queue-starvation gaps inside the span; it is not GPU busy time. All submissions complete before reading/reusing the query pair. Unsupported capability remains null; invalid queries fail, never substitute host waits. The independently measured native period and valid bits are recorded. Measurement version is primitive-queue-timestamps-v2.

Prepare includes fresh program creation or asset generation replacement. Encode includes command creation, barriers, clear, binding, Draw and Finish. Submit is host call duration. Slot wait is completion wait on prior use. Retirement includes submission/command destruction. Completion is CPU wall time from submit to observed completion (including time spent doing other slots), not GPU duration. Throughput starts after warmup drain and includes the final measured drain/retirement. Managed input generation for G06 is intentionally inside prepare; raw output materialization is outside the measured window.

This is a runnable bounded diagnostic subset, **not** the formal 5-process A/B, 200–2000-frame stability pilot, or 2000-frame standard measurement. Do not present smoke timings as a performance conclusion. Strict builds and bounded semantic smoke are recorded in the verification ledger;
no formal comparative performance campaign is claimed.

## Independent G06 native recovery process

This opt-in mode is separate from the Vulkan ordinary frame suite. It creates its own WebGPU/wgpu-native device through the real product `AddGraphicsDevice` / `IGraphicsDeviceService` registration. It destroys **only that owned native instance** with `wgpuDeviceDestroy`, using the same instance-local callback-trigger path as `NativeDeviceLossTests`. It never resets the host GPU, driver, other applications’ devices, OS configuration, or a global cache. Native callback delivery, Hosting Lost, old-lease rejection and invalidation watermark must all be observed. A CPU/unknown adapter is rejected, not silently presented as real-GPU coverage.

```sh
# Build once, with repository native/shader prerequisites, then serialize runs.
dotnet build benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks/Lumyte.Graphics.Primitives.Benchmarks.csproj -c Release
# Select a verified hardware ICD appropriate to the machine. Each invocation is a fresh process.
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Primitives.Benchmarks -- --recovery --cycles 3 --output artifacts/graphics-benchmarks/g06-native-recovery-001
```

Only `--recovery`, `--cycles 1..100` (default 1) and `--output PATH` are accepted. Unknown flags, duplicate flags, missing values, and mixing `--case`, `--frames` or `--warmup` are rejected before device creation. Existing output directories are rejected. Ordinary `--case G06` never triggers native destruction.

Before destruction and after every `RecoverAsync`, real model shaders, pipelines, textures, argument tables, index and vertex buffers are rebuilt. Real mesh staging uploads change vertex color red → green → red; each submitted image is read back and **every** pixel is checked against an independent analytic RGBA expectation (1 UNORM LSB allowance, blue exactly zero). This is not a clear-only image test. A real Hosting retirement confirmation precedes old resource disposal, lease return and owner retirement. Retirement with a retained lease must release zero owners; after lease return it must release the old owner. Final children and all native owners must complete disposal before success is recorded. Released-child counts are completed explicit Dispose calls/composite mesh owners, not native allocation or leak-profiler counters. Unknown submission acceptance, callback timeout, map, readback, pixel, retirement, or disposal failure invalidates the process and retains its ownership graph in quarantine until process termination; there is no uncertain finally-based native teardown.

Artifacts use independent schema `graphics-recovery/1`: `manifest.json`, `frames.jsonl`, `checks.jsonl`, `summary.json` and raw RGBA images. They include fresh-process ID; source/dirty/binary provenance; actual native adapter/backend and driver description per epoch; pre-specialization WGSL target-code hashes; explicit empty font inputs (no text is rendered); fixed input hash/seed and inherited runtime settings. Device initialization, native destroy→callback, native retirement, old-child/owner release, device recovery, resource rebuild and first completed output are separate host-wall stages. Asset replacement stages are not ordinary warm frames. Full-GC managed live heap, process RSS and OS peak working set are separate checkpoint metrics outside lifecycle timings. Native allocation and VRAM remain null. RSS checkpoint high-water is not a guarantee of the transient memory peak; OS peak working set is process-wide, not GPU-memory attribution.

**Limits:** destruction happens after completed/drained rendering, with live model resources; this does not cover loss during pending work. Single-flight 32×32 headless Rgba8Unorm is a bounded correctness/lifecycle diagnostic, not presentation, full-HD, ResourceManager/file decode, in-place mutation, cold driver-cache proof, native VRAM measurement, timestamp instrumentation, or a formal five-process A/B campaign. No successful native run is claimed merely by adding this source. The parent must serialize strict build, CPU contract tests and the explicit hardware run, then inspect passed pixel/lifetime checks and zero quarantined owners before reporting coverage.
