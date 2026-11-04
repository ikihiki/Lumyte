# D07 bounded real text fixtures (version 1)

Implementations use public Parley/TextLayout, the real finite-width paragraph
provider, vector Canvas/Renderer2D and actual MSDF/MTSDF CreateAsync/RecordUpload.
No product changes, native shortcuts, fake glyphs or UI TextBox wrapping controls.
Strict repository analyzers remain enabled; no diagnostic suppression is added.
See [verification](../VERIFICATION.md) for integrated build and correctness gates.

## Build and correctness gates

From repository root, under exclusive benchmark/build access:

```sh
destination=benchmarks/Graphics/Lumyte.Graphics.Text.Benchmarks

dotnet build -c Release "$destination/Lumyte.Graphics.Text.Benchmarks.csproj"
dotnet run -c Release --no-build --project "$destination" -- --list
dotnet run -c Release --no-build --project "$destination" -- --check-layouts > artifacts/graphics-benchmarks/text-layout-checks.jsonl
# Small first GPU gate; both depth1 and depth3 are checked by smoke.
dotnet run -c Release --no-build --project "$destination" -- --smoke --case fixed --method vector --output artifacts/graphics-benchmarks/text-first-smoke
# Full bounded suite: 7 cases x 3 methods x 2 depths, not formal timing.
dotnet run -c Release --no-build --project "$destination" -- --smoke --output artifacts/graphics-benchmarks/text-smoke
```

Requirements: .NET 10, product Parley native ABI2 library and dependencies, shader
build tooling/Slang, host C linker/Rust if native assets must be built, and Vulkan1.3
for GPU runs. Normal project build invokes existing ParleyNative.targets; no custom
native installation. See product Text.Parley README for existing-native-path options.
CPU shaping requires Parley native but does not initialize Vulkan. CPU distance
field generation uses the actual managed generator. GPU needs dynamic rendering,
synchronization2 and maintenance4. Set VK_DRIVER_FILES to the intended verified
adapter. Software Vulkan can validate semantics but not hardware performance.
No process changes product JIT/PGO/GC settings. Never run alongside another build,
test or benchmark. All results/previews belong under ignored artifacts/, never Git.

## Explicit assets and coverage

The project copies font files and notices from the repository's
`tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/`:

- DejaVuSans.ttf and LICENSE.txt: mandatory Latin, digits, Greek Omega and Arabic.
  There is no OS/system-font fallback.
- LumyteSampleJapanese.ttf plus NotoSansJP-OFL.txt: actual bundled subset, used only
  for its verified 日本語 characters through explicit fallback.
- ColorFontTest.ttf plus ColorFontTest-LICENSE.txt: actual bundled COLRv1 linear
  glyph U+F0500, not a fake color emoji or unsupported-API substitute.

These supplemental assets were found in this checkout. If one is absent later,
list/layout/smoke emit **asset-gap** with filename/reason; explicit measured request
fails. They are not declared unsupported APIs. Mandatory DejaVu absence fails hard.
For every input Unicode scalar, a real registered-font shaping lookup must produce
nonzero glyph IDs; full strings are then checked again after contextual shaping.
Every loaded font's SHA256 and actual registered family are saved. Japanese checks
require the intended fallback family, COLR requires actual color glyph/paint graphs.
The suite does not infer broad Japanese/emoji coverage from these tiny fixtures.
Native Parley notices are handled by the referenced product's native build targets.

## Fixed cases and expected semantic changes

All inputs are static strings or a two-state logical trace; no wall-clock scheduling,
random seed or hidden event coalescing. Main text size24 (COLR64), line-height1.2,
Auto direction and explicit locale. Target640x320 Rgba16Float, white opaque clear,
DPR1. Main origin(104,16); independent known DejaVu O counter/ring anchor at64px,
origin(16,16), in every method's own representation.

- fixed: HUD OAV Lumyte; immutable recording and atlas really reused.
- numeric: 0123456789 score 100 -> 101. All digits already occur; exact glyph ID set
  remains the same, but rendered pixels must actually change.
- new-glyph: HUD 0123 -> HUD 0123 Ω. At least one new font/glyph identity must occur,
  and the new state must visibly differ. Atlas ownership is snapshot-scoped, not a
  fictitious persistent glyph cache; setup records each actual state creation.
- bidi: HUD 123 العربية -> HUD 124 العربية. Checks nonzero Arabic glyphs, finite
  advances/origins and a decreasing logical-cluster span in visual painter order.
- paragraph-width: same alpha/beta paragraph, actual provider maximumWidth180 ->360.
  Calls **ParleyUiTextLayoutProvider.CreateLayout(document, maximumWidth)** directly,
  verifies changed line count, bounded advance width and agreement with explicit
  native finite-width layout. UiTextBox's infinity-width path is not involved.
- japanese: 日本語, explicit real bundled fallback; static reference state.
- colr: real U+F0500 gradient, static. In MSDF/MTSDF runs its color paint remains
  **vector passthrough** by the product contract; only the O anchor is distance
  encoded. This case is never presented as distance-field acceleration of COLR.

## CPU component jobs

```sh
project=benchmarks/Graphics/Lumyte.Graphics.Text.Benchmarks
dotnet run -c Release --no-build --project "$project" -- --cpu --list flat
dotnet run -c Release --no-build --project "$project" -- --cpu --filter '*TextBenchmarks*' --artifacts artifacts/graphics-benchmarks/text-cpu-layout
dotnet run -c Release --no-build --project "$project" -- --cpu --filter '*DistanceFieldGenerationBenchmarks*' --artifacts artifacts/graphics-benchmarks/text-cpu-fields
```

BenchmarkDotNet0.15.8, Release, 5 launches, 5 warmup iterations, 15 measured iterations,
250ms target, no outlier removal, MemoryDiagnoser, median/p95 and full JSON. Explicit
project generator avoids repository multi-project ambiguity. Product JIT/PGO/GC
defaults are not overridden. p95 is of iteration estimates, **not frame latency**.
Use filters for bounded first measurements; do not mistake a short smoke for formal
comparison. Fonts and invariant validation are outside measured operations.

ShapeLayoutAndSnapshot calls public Parley CreateDetailedLayout, or the actual
paragraph provider for the width case, on alternating real states. Shaping,
line-breaking/layout, outline extraction and managed snapshot copying are a single
public native operation; **independent shaping-only versus layout-only times are
not exposed and are not fabricated**. Provider additionally builds editing/caret
geometry. Initial font registration/anchor setup is not inside this CPU operation.

GenerateOutlineFields uses pre-shaped real outlines and public Generate for every
outline of the selected state, in actual MSDF/MTSDF mode, texelsPerUnit1/range8.
Consumes actual generated RGBA bytes. Includes neither native texture allocation,
atlas packing nor upload. COLR-only main paint is intentionally excluded from this
outline-generation job. This diagnostic is not subtracted from CreateAsync timings
or added to them: generation is already included in actual atlas creation.

## Real atlas creation/upload and resident GPU run

```sh
project=benchmarks/Graphics/Lumyte.Graphics.Text.Benchmarks
source=$(git rev-parse HEAD)
dotnet run -c Release --no-build --project "$project" -- --run --case numeric --method msdf --source "$source" --depth 1 --warmup 200 --frames 2000 --output artifacts/graphics-benchmarks/text-numeric-msdf-run1-depth1
dotnet run -c Release --no-build --project "$project" -- --run --case numeric --method msdf --source "$source" --depth 3 --warmup 200 --frames 2000 --output artifacts/graphics-benchmarks/text-numeric-msdf-run1-depth3
```

Select one case/method/depth per measured process. Methods: vector, msdf, mtsdf.
Frames1..100000, warmup0..2000, depth exactly1/3. For integration only, use
`--warmup 0 --frames 4`. Output profile/method/depth must not already exist.
Default200 is an initial warmup, **not a stability claim**. Before formal comparison,
run an external stability pilot, fix common warmup/trace counts, use5 independent
A/B process pairs alternating order and preserve process-level distributions.
Do not pool frames across processes. No timing-dependent stopping, p99 or memory
retention conclusion is implemented here.

Fresh local engine is initialized before state0 setup, then state1 is applied. Each
actual state records combined shaping/layout time. For distance methods the public
DistanceFieldText2D.CreateAsync really generates/packs outlines, creates atlas/view/
upload buffer and maps/copies staging. Its interval is named
**AtlasCreateAndStageHostMs**, not CPU-only atlas-generation time. RecordUpload is
executed exactly once per owner, with separate host encode/Submit/wait and optional
native GPU upload span. Actual atlas dimensions and texel payload bytes are saved;
payload is not mislabeled padded transfer count or native VRAM. Static state1 really
reuses state0; numeric/new-glyph/wrapping states own separate real snapshots/atlases.
There is no fake prepopulated font-wide glyph cache.

After uploads complete, immutable atlas owners may be shared read-only by all slots.
Resident frame chooses state = frameId%2, using pre-recorded real text commands and
resident uploaded atlases. It measures renderer PrepareHost, actual RecordHost,
outer encode including target transitions/Finish, SubmitHost, previous-slot wait,
retirement and submit-to-completion **observation**. It is **not** an end-to-end UI
text edit/IME loop and does not include per-frame shaping or atlas regeneration.
New-glyph creation cost is in lifecycle.jsonl, not claimed as a resident cache miss.
Native/global/driver caches are not asserted cold even for a fresh local engine.

PrepareHost may allocate/upload native resources and wait internally; it is not
claimed CPU-only. RecordHost is an inner part of EncodeHost: do not double-add.
Completion observation includes delayed polling/host scheduling. Throughput starts
after warmup is fully drained, includes final drain and all frame retirement, and
excludes font/device/target/atlas setup, snapshots/checks/readback and JSON/images.
Headless throughput is not Present FPS. Row allocation/collection is in the outer
interval, not individual stage intervals. CPU/GPU overlap, so do not sum their times.

Optional native timestamp adapter follows the existing benchmark pattern, without
fixed-vendor gating. Each slot owns a query pair. Actual device/driver/queue and
valid-bit/timestamp period are recorded. It reflects only backend native handles;
contract changes fail explicitly. GPU rendering span covers target transitions,
clear/text draws and final CopySource export, not atlas upload/readback/Present.
Upload GPU span is separate. Unsupported timestamps are null; wait is never a GPU
proxy. Overlapping slot spans are not summed as GPU occupancy.

## Correctness and quality scope

Preflight and postflight exercise both states and 0->1->0 round trips. All slots are
submitted before draining, so depth3 checks real overlapping owners. Checks assert:

- nonzero glyph IDs, finite ink/advance bounds/origins/advances, valid UTF-8 and
  UTF-16 clusters, deterministic glyph order/positions/outlines, and native-engine
  independent snapshots after engine disposal;
- same glyph set for numeric, introduced real glyph for Omega, bidi cluster order,
  genuine finite provider wrapping, explicit Japanese/COLR asset use;
- real CPU Generate output determinism in both modes and outside-sign encoding at
  the known O counter;
- per-method known O hole remains white, ring contains substantial dark ink,
  alpha remains opaque, finite channel range, clean margins and real visible main
  glyphs; COLR has multiple genuinely saturated gradient colors;
- **all packed Rgba16Float bytes match** for the same method/state across repeated
  frames, slots, before/after reuse, and smoke depths1/3. SHA256 is artifact identity,
  not the independent correctness gate;
- vector DrawText output exactly equals an independently recorded logical snapshot
  adapter using FillPath/FillRect in glyph painter order, not DrawList flattening;
- dynamic text/width states really change pixels, static states do not.

These checks intentionally do not assert vector==MSDF==MTSDF pixels or equal quality.
Each is a quality representation at1 atlas texel/logical pixel, range8. The known
counter/ring and per-method replication are bounded analytic/semantic gates, not a
full independent font rasterizer or complete Arabic typographic conformance suite.
Inspect saved images before recommending representation tradeoffs, especially at
other scales/hinting. No scale sweep, browser equivalence, OS edit/IME or full
Japanese/emoji corpus is claimed.

Preflight saves each state as raw packed little-endian RGBA16F and PPM P6 previews
(linear RGB converted to sRGB for display). Postflight rechecks exact bytes rather
than duplicating identical images. checks.jsonl records per-state hashes and explicit
expected/actual visible-change proofs. layout-check.json contains font hashes,
actual glyph IDs/families/clusters/advances/bounds and introduced glyph identities.
FontId is engine-local; cross-engine determinism compares family+glyph identity.
Full smoke is bounded42 combinations and can generate roughly200MB of image data;
use case/method filters for a small first gate.

## Lifetime, failures and artifacts

Slots exclusively own output target/view, Renderer2D, one-shot prepared resources,
command, submission and query pair. Never overwrite a slot/query until completion.
Normal order: confirmed GPU completion -> read query -> dispose submission/command/
prepared frame -> next frame. All slots drain before resident atlas owners are
released; views precede texture, device last. Atlas upload completes before any
sampling, and staging remains owned until atlas disposal after the final drain.

Failure cleanup first confirms **all** remaining submissions, even when a different
slot's query read or validation failed, then discards pending command/prepared owners
and destroys slot resources. Before-Submit failure discards commands before prepared
resources. Ambiguous Submit or unconfirmed completion exits code3 without native
owner teardown/reuse; OS process isolation is the safety boundary, not fake clean
recovery. Other failures are nonzero and save failure.json where possible. Setup
failure can precede a manifest; stderr/exit code are authoritative. No unstable,
unsupported, OOM or pixel failure is converted into a successful smaller workload.

Artifacts: manifest.json, layout-check.json, lifecycle.jsonl, checks.jsonl,
frames.jsonl, summary.json, raw/PPM reference states, optional failure.json.
Manifest records actual font/input hashes, source supplied by caller, runtime/OS,
ICD/device and inherited DOTNET_/COMPlus_ settings. Source dirty patch, CPU identity,
actual shader-container/native library hashes and native version provenance should
be captured beside results **outside timing**:

```sh
git diff --binary > artifacts/graphics-benchmarks/text-source-dirty.patch
lscpu > artifacts/graphics-benchmarks/text-cpu.txt
vulkaninfo --summary > artifacts/graphics-benchmarks/text-vulkan.txt
sha256sum benchmarks/Graphics/Lumyte.Graphics.Text.Benchmarks/bin/Release/net10.0/Lumyte.Graphics.*.dll > artifacts/graphics-benchmarks/text-managed-shader-container-hashes.txt
```

ShaderHash remains null rather than inventing standalone shader hashes; DLL hashes
identify containers including embedded shaders, not individual shader artifacts.
Linux in-process CPU identifier may be null; lscpu provides it separately. No native
VRAM, glyph-cache hit/eviction counter or full-process retained memory is fabricated.
