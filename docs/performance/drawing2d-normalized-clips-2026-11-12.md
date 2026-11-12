# Drawing2D normalized clips — 2026-11-12

## Source and storage recovery

- Baseline: c2b941cc767f06cbfbd92b77e6cef2a9fad5a221 (merged PR 125).
- Production implementation: 635dbb9a; final bounded-edge fix: 87b6acaa.
- Writable candidate: /home/hideaki/.cache/lumyte-normalized-clips,
  branch feat/clip-normalization-home.
- Matched baseline: /home/hideaki/.cache/lumyte-clips-baseline, detached baseline.
- The original /tmp/lumyte-drawing2d-normalized-clips worktree and its untracked
  design/harness were preserved, not reset or deleted. /tmp is usrquota-enabled;
  actual native writes failed with EDQUOT despite filesystem free space. The
  home-backed worktree and local TMPDIR resolve this without OS/config edits.
- The old baseline-pilot/FrameBenchmarks-20261112-181517.log is zero bytes.
  It is not a measurement and does not demonstrate benchmark completion.

## Product behavior

Preparation finishes each outer clip scope as None, Empty, integer Scissor,
one analytic sample-index interval, or one immutable per-pixel sixteen-bit mask.
None/Scissor have no residual interpreter clip nodes. Empty ordinary commands
are removed before payload/table allocation. Fractional rectangle intersections
use one fused clip instruction; complex intersections use one mask instruction.
Neither fragment shader loops over the original clip stack.

The CPU result is authoritative under the user's clarification. Existing path
flattening supplies the curve approximation; explicitly ordered binary64 FMA
forwards captured vertices and evaluates winding on the existing 4x4 grid.
GPU shape and paint arithmetic is unchanged. Historical float inverse-space clip
boundary differences are intentional, not an enlarged image-comparison tolerance.
Canonical axis-aligned intersections are quantized once on that same grid, so
GPU analytic evaluation also consumes the finished CPU intersection.

Selection uses immutable geometry, captured transform, fill rule, target extent,
DPR and pixel origin. It never depends on cache state, frame timing, load or
backend performance. Budget exhaustion rejects preparation; it never switches
to GPU path evaluation. The preparation-local cache stores only CPU descriptors
and is bounded to 4096 entries / 32 MiB. Device resources never enter a global cache.
Retained GPU assets continue to use the existing target/property/generation and
completion-safe ownership machinery.

Adjacent paint clips merge within their own scope. Opacity/composition boundaries
are not crossed. Child scopes cannot install a global hardware scissor, so an
integer child clip that still needs coverage remains one analytic scope operation.
Outer integer clips install the actual scissor and need no residual instruction.

Rectangle instancing is retained, including its trivial no-coverage path, and
now consumes normalized analytic/mask clips instead of an eight-clip GPU loop.
There is no buffer-arena, pass-fusion, resource-set restoration, or batching rewrite.
No production geometry dependency was added.

## Bounds, packing and ownership

- General masks: at most 1,048,576 pixels and 268,435,456 sample-edge operations;
  at most 65,536 aggregate flattened edges and 4096 captured elements.
- Coverage targets are bounded to 32768 per dimension; no-clip targets do not
  inherit this extra restriction.
- Bounds come from nondegenerate geometry edges intersected with target and
  rectangle clips, never an AABB-inside-path containment guess. Isolated moves
  do not inflate mask allocations. Holes and winding remain explicit predicates.
- A sixteen-bit mask is stored as an exactly represented integer-valued float,
  four pixels per float4. This is numerical storage, not averaged alpha, a
  normalized texture format, or filtered sampling. Storage is 4 bytes/pixel,
  rather than the theoretical 2 bytes/pixel of a dense ushort buffer.
- The interpreter's existing 262144-vector combined geometry/payload budget is
  unchanged. Rectangle payloads are additionally capped at 32 MiB per batch.
- Mask values travel in existing owned geometry buffers, with the existing
  deferred upload, CopyWrite/ShaderRead declarations and barriers. They require
  no additional GPU reference, mutable global resource, or lifetime side channel.
  Prepared owners and retained assets continue to live until GPU completion
  and command disposal. Shader loads are clamped for inactive SIMD lanes.

## Correctness evidence

Final candidate verification:

- Drawing2D CPU/Graph/lifetime/cleanup suite: **262 passed, 0 failed, 0 skipped**.
- Full Drawing2D public-API GPU suite on Radeon: **29 passed, 0 failed, 0 skipped**.
- Same full suite under the software Vulkan ICD: **29 passed, 0 failed, 0 skipped**.
- Release builds, git diff --check and repository Markdown lint (307 files, zero issues) passed.

New assertions cover classification/instruction counts, no empty payloads,
deterministic budget rejection/cache hits, target origin/DPR, holes, adjacent
float sample boundaries, nondegenerate bounds and opacity-separated paint scopes.
A triangle has an independently specified mask 0x0137 (six covered samples).
GPU tests verify an edge at 0.125 versus its immediately adjacent float produces
CPU-authoritative alpha 1 versus 0.75, then isolate each of the sixteen samples across multiple packed lanes,
on both interpreter and rectangle-instance routes, using an independent rational
triangle oracle. Correlated shape + gradient + clip yields exactly
RGBA=(0.0703125,0,0,0.3125), which an averaged alpha mask cannot reproduce.
A separately clipped opacity/composite test verifies the backdrop is not clipped.

Three existing compiler-representation assertions were updated to the required
normalized representation, retaining the original shape/transform/composition
checks and adding scissor/one-stage assertions. No pixel tolerance, timeout,
feature/API assertion, or GPU skip guard was loosened.

Raw logs are in artifacts/normalized-clips/:
normalized-unit.log, normalized-hardware.log, normalized-software.log,
hardware-identity.txt, software-identity.txt and environment.txt. The standalone arithmetic ambiguity
witness is preserved in arithmetic-boundary/; it is a CPU witness, not an old-GPU
boundary-equivalence claim.

## Completed-frame comparison

The matched harness is tools/benchmarks/drawing2d-skia-bdn/ClipFrameBenchmarks.cs;
see its CLIP-NORMALIZATION.md for the exact boundary and reproduction command.
The copied harness is not in the baseline tree, so both variants receive the
same frozen source files and their SHA-256 manifests are compared exactly.
The 256x192 Radeon fixtures separate dynamic mask creation inside the timed
operation, prepared-mask cache reuse, and a no-clip rectangle control.

The predeclared bounded process order is baseline A, candidate A, candidate B,
baseline B. Eight 100 ms-target measured iterations follow three warmup iterations;
outliers are retained. Median/P95 refer to iteration estimates, not individual
frame latencies. MemoryDiagnoser allocations cover the completed operation.
There are no GPU timestamp queries: wall time through WaitAsync is not GPU time.

An initial harness validation-only attempt rejected a sealed benchmark class;
those console logs are preserved and contain no executed benchmarks. The class
was unsealed as required by BenchmarkDotNet and the final-* comparison uses a
new source freeze. No timing observations were discarded.

### Results

Hardware: Radeon 8060S (RADV STRIX_HALO), Mesa 26.0.8; Ryzen AI MAX+ 395;
Ubuntu 26.04.1; SDK 10.0.403 / runtime 10.0.14. Software verification explicitly
exposed the CPU llvmpipe adapter (LLVM 21.1.8), not the Radeon adapter.

Each cell below gives process A / process B, retaining the predeclared ABBA order.
Latency units are milliseconds; allocations are managed bytes per completed frame.

| Fixture | Baseline median | Candidate median | Baseline P95 | Candidate P95 | Baseline allocated | Candidate allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| mask-cached | 7.721762 / 7.500289 | 0.142192 / 0.158978 | 8.027742 / 7.860633 | 0.153467 / 0.164861 | 1081913 / 1081913 | 10452 / 10525 |
| mask-dynamic | 19.874373 / 20.588669 | 3.542554 / 3.440409 | 20.215780 / 22.193188 | 3.786990 / 3.885901 | 9411856 / 9411577 | 189528 / 189528 |
| rect-control | 0.442389 / 0.454911 | 0.494883 / 0.481076 | 0.464087 / 0.467670 | 0.521035 / 0.503787 | 263635 / 263630 | 264203 / 263979 |

Paired median changes:

- Cached mask: -98.16% / -97.88% (-7.580 / -7.341 ms); allocation about -99.03%.
- Dynamic, freshly generated mask: -82.18% / -83.29% (-16.332 / -17.148 ms);
  allocation -97.99%. Mask creation is inside these measurements.
- **No-clip rectangle control regressed +11.87% / +5.75%**
  (+0.0525 / +0.0262 ms), with +568 / +349 managed bytes/frame.
  Instancing is still used, but this is not an all-workload improvement.

The complex-clipped rectangle workload previously fell back to per-command
interpreter work. It now retains rectangle instancing with one precomposed sample
mask, so the improvement includes that intended execution-path change, not merely
a cheaper arithmetic instruction. No buffer arena or pass fusion is involved.

All 12 case/process results have eight measured estimates with no removed
outliers. The candidate dynamic case used one operation per measured iteration,
so these are short observations, not a stability qualification or a claim about
all clip geometries. Its P95 includes the retained 4.089 ms outlier. The 100 ms
setting is an iteration target, not a guarantee that each actual iteration ran
for that duration. Do not extrapolate to general application FPS or pure GPU time.

### Provenance and limitations

- Full raw JSON, CSV, reports and process logs: artifacts/normalized-clips/final-*
  (final-base-a, final-candidate-a, final-candidate-b, final-base-b).
- Machine-readable observations, per-iteration values, actual operation counts
  and paired deltas: artifacts/normalized-clips/comparison-summary.json; a tracked
  copy accompanies this report under data/drawing2d-normalized-clips-2026-11-12.json.
- Frozen harness source SHA-256 manifests are byte-identical between variants.
  The benchmark assembly and production Drawing2D assembly hashes were recorded
  per variant and verified unchanged after both processes. Baseline provenance is
  also copied into provenance-baseline/. Production source stayed frozen at
  87b6acaa throughout the comparison. BDN generated-host artifacts use its normal
  cleanup; retained hashes identify the benchmark and production assemblies.
- Dynamic masks still consume CPU rasterization time and numerical storage uses
  4 bytes/pixel. The finite mask/edge/payload budgets reject oversized inputs
  rather than changing semantics. Large full-target complex masks can exceed
  the current budget; this is a documented support limit, not a GPU fallback.
- The no-clip control overhead remains a measured tradeoff. No favorable rerun
  or post-measurement code change was used to hide it.
- No GPU timestamps, presentation/VSync, large-target scaling campaign or
  buffer-block optimization was performed. No push/PR/merge was authorized or done.
