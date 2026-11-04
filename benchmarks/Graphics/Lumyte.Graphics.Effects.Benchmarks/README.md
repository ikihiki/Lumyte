# Executed D10 layers/effects and G04 Record benchmarks

This integrated runner measures real effect execution, not repeated plain DrawLists.
No product API changes or dependency on the G04 CPU benchmark executable.
Direct Scene is **N/A: layers/effects unsupported**. See
[verification](../VERIFICATION.md) for build and bounded correctness results.

## Integration and prerequisites

From repository root, after exclusive hardware access is available:

```sh
destination=benchmarks/Graphics/Lumyte.Graphics.Effects.Benchmarks

dotnet build -c Release "$destination/Lumyte.Graphics.Effects.Benchmarks.csproj"
dotnet run -c Release --no-build --project "$destination" -- --list
dotnet run -c Release --no-build --project "$destination" -- --smoke --output artifacts/graphics-benchmarks/effects-smoke
```

Requires .NET 10 SDK, working project shader build tooling/Slang, Vulkan 1.3 loader
and a real device satisfying VulkanDevice (dynamic rendering, synchronization2,
maintenance4). No native text/Parley, window or swapchain. Use VK_DRIVER_FILES to
select/verify the intended adapter. Software Vulkan may validate pixels/lifetimes;
its timings are not hardware evidence. Nothing overrides JIT, PGO or GC settings.
Use existing product-default environment. Never build/test/time concurrently.

## Bounded profiles, not a Cartesian matrix

`--list` prints nine immutable profiles. All use DPR 1 and linear premultiplied
Rgba16Float (the format required by ImageEffects), not an sRGB-format comparison.
Default dimensions are 1920x1080; one explicit 1280x720 blur profile varies target
size while preserving normalized geometry. AA/filter/composition use product
implementations, no custom optimized rendering. Static logical input has no random
seed/time; profile parameters receive an input hash in the manifest.

- opaque: white left half, red right half; verifies known opaque source/clear.
- isolation: 1 or 4 independent layers on opaque blue; each layer draws white,
  then an overlapping opaque red rectangle; group opacity 0.5 is applied once.
  This differs analytically from multiplying every command's alpha.
- blur: 1 or 4 independent white rectangle layers on blue, true separable Gaussian
  sigma 2 or 6. Exactly two live image-effect passes per layer. This is not a zero
  radius or a blurred constant that could hide missing work.
- backdrop: the opaque white/red step background is captured in full by the product
  contract, Gaussian sigma 2 is executed, and the result replaces either the central
  quarter-area region or the full viewport. Quarter output does **not** imply ROI
  capture/blur optimization. Expected effect-pass count is two in both variants.

Each layer's ContentBounds is the entire viewport; the visible rectangle occupies
its normalized tile center. Therefore four-layer tests intentionally retain four
viewport-sized layer inputs, not a secretly reduced-area input. Blur expands output
bounds by ceil(3*sigma) in each direction. Opacity has one real effect pass per layer.
All graph/resource/pass statistics come from the actual compiled plan, not estimates
from the number of high-level commands.

## Checking before performance

`--smoke` checks all profiles at 64x64 at depth 1 and 3. It submits all slots before
draining them, compares complete packed Rgba16Float bytes across slots, repeated
recordings, before/after slot reuse, and across depths. It independently checks
**every pixel and channel** against a CPU analytic oracle:

- primary-color opaque rectangles and painter order;
- isolation/group alpha and source-over in premultiplied linear space;
- finite discrete Gaussian integration of a known rectangle, with zero samples
  outside finite input bounds and radius ceil(3*sigma);
- backdrop replacement versus source-over, clipping, and edge alpha.

The oracle uses double-precision one-dimensional Gaussian mass integration and
logical rectangle bounds, not product DrawList flattening, GPU output, or product
blur helpers. Independent CPU-vs-GPU absolute tolerance is fixed at **0.004 per
channel** before execution (two half-float filter writes, compositing, float exp).
Same-path checks use **byte equality**, with no tolerance; SHA256 is only an artifact
identity, not the correctness oracle. Non-finite or out-of-tolerance pixels fail.
Do not widen tolerance after seeing results. The opaque control is checked too.

Checks capture bounded snapshots and verify the exact required image-effect count,
zero culled passes, sufficient transient resources, identical graph metadata across
slots, and unchanged actual statistics throughout measurement. Captured metadata
records all declared resources, first/last live use, dependencies and barriers.
Readback/checks/JSON are outside stage timings and throughput. GPU checks are not
claimed to have passed until the parent runs them; this staging task runs no builds,
tests or timings.

## Run commands (after passing smoke)

```sh
project=benchmarks/Graphics/Lumyte.Graphics.Effects.Benchmarks
source=$(git rev-parse HEAD)
# One process, one explicit profile and depth; output directory must be fresh.
dotnet run -c Release --no-build --project "$project" -- --run --profile blur-1-s2-1080p --source "$source" --depth 1 --warmup 200 --frames 2000 --output artifacts/graphics-benchmarks/effects-run1-depth1
dotnet run -c Release --no-build --project "$project" -- --run --profile blur-1-s2-1080p --source "$source" --depth 3 --warmup 200 --frames 2000 --output artifacts/graphics-benchmarks/effects-run1-depth3
```

For integration diagnostics use `--warmup 0 --frames 3`; that is not a formal
measurement. Frames are bounded to 1..100000; warmup 0..2000; depth exactly 1 or 3.
Select a single profile per measured process. Automated all-profiles execution is
limited to smoke. There is no timing-dependent stopping rule.

Formal comparison still needs an external stability pilot, fixed common warmup,
5 independent A/B process pairs in alternating order, fixed machine/ICD and
source/dirty diff provenance. Default 200 is an initial configuration, **not a
claim of stability**; manifest says not-assessed. No automatic stability/p99 claim.
Each process saves its own distribution. Do not pool all processes as independent
frames. Keep software-only correctness results separate from hardware timings.

Record provenance alongside the output before measuring (not during timing):

```sh
git diff --binary > artifacts/graphics-benchmarks/effects-source-dirty.patch
lscpu > artifacts/graphics-benchmarks/effects-cpu.txt
vulkaninfo --summary > artifacts/graphics-benchmarks/effects-vulkan.txt
sha256sum benchmarks/Graphics/Lumyte.Graphics.Effects.Benchmarks/bin/Release/net10.0/Lumyte.Graphics.*.dll > artifacts/graphics-benchmarks/effects-managed-shader-container-hashes.txt
```

The DLL hashes identify containers including embedded shaders; they are not claimed
to be standalone shader-artifact hashes. Manifest shaderHash is explicitly null.
Manifest also records actual device/driver/queue/timestamp properties, ICD, runtime,
OS, relevant inherited DOTNET_/COMPlus_ variables, source and normalized input hash.
Linux CPU identity can be null in-process; lscpu supplies it externally. No VRAM,
native byte count, draw count or memory-retention measurements are fabricated.

## Exact timing boundaries

This is a real-GPU frame runner, **not BDN CPU-only measurements**. Keep its results
separate from the frozen device-prerequisite-only G04 CPU build/compile job.

- GraphBuildPrepareMs: fresh RenderGraph/renderer, import/output declaration,
  Append2D and preparation of actual drawing/effect resources. Includes native
  shader/pipeline/table/buffer creation, uploads and any internal waits. It is
  deliberately **not labeled pure CPU graph-build**. Reuses only the immutable
  recorded logical input; resources/preparation are recreated every frame.
- GraphCompileMs: real public Compile after append; excludes preparation and later
  disposal. Effects/extension ownership transfers into that one-shot plan.
- GraphRecordMs: real plan.Record, including live transient allocation, views,
  barriers and executed drawing/effect command encoding on CPU.
- EncodeMs: command creation + optional timestamp commands + Record + Finish;
  GraphRecordMs is an **inner interval**, never add it again to EncodeMs.
- SubmitMs: queue Submit host call.
- SlotWaitMs: explicit wait on the previous occupant only. Its timestamp read and
  retirement are not part of this inner wait measurement.
- RetirementMs: completed submission/command, plan, prepared owner, renderer and
  builder destruction; does not wait.
- HostIncludingSlotWaitMs: outer host interval from before prior-slot retirement
  through new frame's Submit return. Includes wait/retirement/prepare/compile/encode.
- SubmitToCompletionObservationMs: Submit start until explicit confirmed observation,
  includes host scheduling and delayed slot polling; not exact device latency.
- GpuSpanMs: TOP_OF_PIPE to BOTTOM_OF_PIPE around the graph on its actual Vulkan
  command buffer. Includes graph barriers, clear, real draws/filters/composites and
  final CopySource export; excludes checks/readback/presentation. Does not attribute
  individual effects or imply exclusive GPU busy time. Overlapping spans are not
  summed to GPU utilization. Unsupported timestamps are null, never CPU-wait proxies.

Warmup is fully drained before measured start. Headless throughput includes all
fixed measured frames, final drain and retirement; excludes target/device creation,
preflight, snapshot capture, readback, artifact serialization and postflight. Rows
are allocated/collected in the outer interval (bounded by requested frame count),
not inside individual stage intervals. This diagnostic throughput is not Present FPS.
GPU/CPU stages overlap, so do not sum GPU and CPU times as a frame time.

## Query implementation and ownership

HardwareGpuTimer is the existing benchmark-only Vulkan timestamp pattern adapted
to capability checks rather than Radeon 8060S identity gating. It uses reflection
only on the active backend's Api/NativeDevice/PhysicalDevice/QueueFamily and the
command's Owner/Native, and fails loudly if this contract changes. Each slot owns
its own two-query pool, persistent output texture, fresh command, plan/prepared
resources and submission. No query/target/frame is reused before completion.

After success: await submission, read query, dispose submission, command, compiled
plan, prepared drawing owner, renderer and builder; then reuse the slot. Slot query
and output texture are destroyed only after the final drain, device last. A failure
before Submit discards command recording before disposing graph resources. If a
later build/record fails, finally drains earlier slots. If Submit has an ambiguous
outcome, or successful GPU completion cannot be confirmed, the process reports the
failure and exits with code **3 without native owner teardown/reuse**. This is a
conservative process-isolation boundary, not fake successful cleanup or device-loss
recovery. Other failures return nonzero and save failure.json when possible. In
uncertain-completion paths the process may exit before failure.json; stderr/exit
code are authoritative and the run is invalid. A partially disposed cleanup owner
is not silently retried as a new frame.

Per-profile outputs: manifest.json, checks.jsonl, raw frames.jsonl, summary.json;
failures get failure.json when normal cleanup is possible. Summary contains real
per-process median/p95/max of stage/frame samples plus actual compiler statistics.
Artifact files are generated under ignored artifacts/, never Git. Snapshot/metadata
checks and analytic readback validate this bounded workload, not general effect
coverage, backend equivalence, HDR tone mapping, text, dynamic editing, cold-start,
resource-cache behavior, arbitrary clips, or standalone G04 chain/fanout Record.
