# ADR0027 real-window U03 presentation harness

Default u03-forced-draw reuses UiProfile.Standard U03/forced-draw and UiScenario from the existing UI benchmark assembly. This is a real mounted product UiSurface, native Parley font/layout, public Canvas recording and Renderer2D Prepare/Record into supported RGBA8-sRGB per-slot intermediates, then the actual product present shader composites into acquired BGRA8-sRGB WebGPU images. It is not a clear proxy. Static completed DrawList caching is the existing benchmark policy; actual frame draws still execute. forced-clear is a separate zero-draw control.

## Build and verification commands

Run from repository root. Finish builds/tests before any presentation measurement.

Use the configured environment and explicit headless gates:

    source tools/setup/activate.sh
    dotnet build benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -c Release
    python3 tools/benchmarks/graphics-scenarios/check-presentation-cli.py
    mkdir -p artifacts/graphics-benchmarks/presentation
    VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -- --check-pixels > artifacts/graphics-benchmarks/presentation/headless.jsonl

The ICD shown is this host's verified Radeon path; other machines must identify
and record their actual selected hardware. The explicit headless gate creates no
window or surface. It is not a presentation measurement.

Only inside the requesting user's already-authorized X11/XWayland desktop, retain
its existing DISPLAY/XAUTHORITY and verify the actual monitor mode externally.
Do not assign guessed display values or use a greeter session. Initial diagnostic
commands (not formal performance campaigns):

    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -- --run --scenario u03-forced-draw --frames 120 --warmup 8 --depth 1 --present-mode fifo --refresh-hz 60 > artifacts/graphics-benchmarks/presentation/u03-depth1.jsonl
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -- --run --scenario u03-forced-draw --frames 120 --warmup 8 --depth 3 --present-mode fifo --refresh-hz 60 > artifacts/graphics-benchmarks/presentation/u03-depth3.jsonl
    dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -- --run --scenario benchmark-idle-u03 --frames 120 --warmup 8 --depth 1 --present-mode fifo --refresh-hz 60 > artifacts/graphics-benchmarks/presentation/owned-idle.jsonl

Replace the refresh assumption with the externally verified mode; the runner
itself does not claim measured refresh. The last command is benchmark-owned idle,
not product-host scheduling proof. Current execution remains blocked without a
requesting-user desktop; these commands are reproduction instructions, not results.

## Commands and strict CLI

Exactly one command first: --list, --validate-options, --check-schedule, --check-pixels, or --run. Parse/list/managed schedule modes make no platform/device/native calls. All options validate before native initialization. --list and --check-schedule accept no options; --check-pixels accepts only --frames (U03 and its target are fixed); --validate-options accepts the full option grammar. Unknown/duplicate/missing options, unsupported enum strings, nonfinite/out-of-range numbers and fractional counts fail with exit 2.

Options:

- --scenario u03-forced-draw|benchmark-idle-u03|forced-clear (u03-forced-draw)
- --frames 2..100000 (120), --warmup 0..10000 (8)
- --depth 1|3 (1; depth 2 is rejected), --idle-ms 1..60000 (1000)
- --present-mode fifo|mailbox|immediate (fifo)
- --refresh-hz 1..1000 (60, invariant decimal, **operator assumption only**)

Exit 0 means managed check or real host-observation run completed, never product-host/photon-latency proof. Exit 1 means execution failure, 2 invalid CLI, 3 missing real 64-bit Linux X11 environment.

--check-pixels is an explicit real-GPU, no-window/no-surface U03 headless correctness gate: actual native adapter query, actual font/native text, RGBA8-sRGB rendering followed by product-shader BGRA8-sRGB composite output, exact full-image fresh-mount equality and correctly swizzled red/blue analytic oracle. It does not require DISPLAY and makes no presentation/performance claim. Unlike list/parse/schedule commands it intentionally initializes hardware; execute only when authorized.

--run checks DISPLAY/OS before window/device creation. Missing DISPLAY fails explicitly, with no Xvfb substitution. A nonempty DISPLAY alone is not authorization or proof: use only the requesting user's connected GUI session, not a greeter display or another user's Xauthority. Wayland-only cannot use current LinuxPlatform; existing user XWayland is acceptable. No desktop configuration, authentication or global display mode is changed.

## Actual adapter and provenance

NativeIdentity uses the benchmark-only RecoveryRunner pattern: UnsafeAccessor of this exact WgpuDevice's _adapter and wgpuAdapterGetInfo. It records selected vendor/architecture/device/driver/backend/type/IDs and rejects CPU/Unknown. The accessor is explicitly version-sensitive, not a public API, but changes no production source/API/IVT. No independently enumerated GPU is substituted as selected-device proof. Shared Provenance.cs captures source/binary/runtime provenance outside measurement. This provenance is captured outside timed intervals.

The UI benchmark ProjectReference uses alias UiBenchmarks to avoid type collisions with shared linked Provenance and draw-counter source. Benchmark-to-benchmark friendship is declared in Lumyte.Graphics.UiScenarios.csproj for Lumyte.Graphics.Presentation.Benchmarks. No production friendship is added.

## Fixed target and presentation contract

1920x1080 physical pixels, DPR 1, mapped-visible normal window, BGRA8-sRGB opaque explicit presentation variant (not nominal RGBA8-sRGB baseline). Unsupported exact format/alpha/present policy fails without fallback. IsResizable=false is merely requested; LinuxWindow does not enforce that flag, so actual extent/scale/visibility/lifetime are checked outside every measured host interval. Mapping does not establish unoccluded visibility; occlusion is unknown.

FIFO requests VSync scheduling; mailbox/immediate do not establish absence of compositor pacing. Refresh metadata is an assumption, actual monitor refresh is null, and fullHardwareLatencyClaim is false. Present call/return is NOT scanout, display onset, measured refresh, native-input sampling or photon latency. The operator must verify the actual user monitor separately; this source does not fabricate it.

## Correctness and real native text

U03 preflight and post-drain validation render the live recording to a separate headless target and compare every byte to a fresh independently mounted UiSurface from the same logical input. Separate fresh renderer instances are used. This checks mounted/cache reconciliation equivalence, not algorithmic independence of product layout/rendering. A genuinely independently specified red plus clipped-blue/order sentinel checks exact known pixels with BGRA-aware channels. Native UiScenario.Verify checks real mounted text/layout/glyphs. The actual font file SHA256 is emitted from UiScenario.FontHash; there is no missing-glyph or OS-font substitution. All validation/readback/hash work is outside performance intervals. Clear control has its own full-image endpoint oracle and is never called U03.

## Ownership and frame trace

Each slot owns the acquired frame, view, PreparedDraw2D objects, command buffer and submission until completion. Each also retains its own persistent RGBA8-sRGB intermediate/view and immutable texture/sampler argument table until the final drain; sampled-to-color reuse barriers are explicit. The global product present pipeline/sampler remain alive until every slot retires. Public draw-counter forwarding counts actual issued abstraction draw calls, including indirect; it does not estimate draws from commands or claim native fragment counts. Submit precedes Present without serial per-frame completion waits. Slot reuse and final drain wait explicitly before freeing commands/prepared/views/frame. Wgpu frame release is currently immediate, NOT a compositor fence.

Completion/release waits are bounded at 15 seconds for presentation slots and UI readback. Unknown submit acceptance or unresolved drain quarantines retained native owners rather than pretending cleanup succeeded. Native synchronous creation/acquisition itself uses the existing backend's blocking contracts; no universal driver-hang guarantee is claimed. Any failure invalidates the run. Warmup is fully drained before measured frame zero. Logical frame, reservation/input sequence, pure UiInput SHA256, content ID, cumulative UI/text/layout counters and actual issued draws accompany frames. Counter snapshots/hashes occur outside measured host intervals; baseline is emitted to distinguish warmup/mount totals.

## Stage measurements

Raw Stopwatch ticks/frequency are output once after timing/drain/checks. GPU timestamp queries are not instrumented: gpuSpanMs is null. Native completion is host observation, not GPU execution duration.

- slot wait/retirement; real acquisition
- product UI update, layout, Canvas record, renderer Prepare
- actual UI plus fullscreen-composite encode/Finish (including the composite draw); Submit call
- host-frame to Submit return; Present call/return
- consecutive Present-return intervals (include intervening event/check/idle gaps)
- submit-to-completion observation at slot retirement (includes deferred observation/host/queue delay)
- controlled managed input-to-Present-return, NOT native input/photon latency

Summed host work includes final drain and excludes validation, event pumping, intentional idle and output. It is not continuous display throughput, so no misleading FPS is emitted. Output has raw frame rows; parent owns postprocessing/percentiles/repetitions. No displayed-frame performance observations have been collected in the current environment.

## Idle scope — explicit remaining limitation

benchmark-idle-u03 uses the same real U03 content, drains at midpoint, stops frame reservations, waits for a managed timer/event, then resumes with the next controlled input ID. It records idle reservation delta, idle endpoints and controlled wake/input timestamp. X11 events are not pumped during this wait: native input is NOT the wake source. This is **benchmark-owned host policy**, not UiApplication/DesktopDriver scheduling proof or GUI responsiveness coverage.

Actual product-host scheduling instrumentation remains a separate integration gap. Public UiApplication can be hosted, but this harness does not exercise its scheduler and does not infer absence of all possible public diagnostics hooks. No fabricated API, production IVT, private draw or scheduler claim is used.

## Verification status

The integrated Release build passes with repository analyzers enabled and no
suppression. Formatter verification passes. The executable CLI regression gate
checks 16 invalid invocations, list/option/scheduling checks and the missing-DISPLAY
path with a deliberately invalid ICD: the latter exits 3 before device/window
creation. The managed scheduling check also verifies command-before-prepared cleanup, parent quarantine propagation and no retry/release of lower owners after an injected managed Dispose failure. It creates no native objects and does not prove product-host behavior.

The real WebGPU --check-pixels gate passes for initial/final U03 states on the
identified Radeon adapter. Both the live and fresh-mount reference use the same
RGBA8-sRGB intermediate and actual product-shader BGRA8-sRGB composition route;
full-image byte equality, unchanged exact red/blue oracle, native glyph checks,
font hash and target-code hashes are recorded. This is not a window/Present proof.
Local evidence is under ignored artifacts/graphics-benchmarks/adr-completion/:
presentation-composite-build.log, presentation-format.log, presentation-cli-final.log
and presentation-headless-composite.jsonl. The first direct-BGRA failure remains
separately preserved in presentation-headless-pixels.jsonl.

Actual window/Present execution remains blocked: there is no authorized connected
user desktop. Greeter displays and another user's authentication are not valid
substitutes. Refresh verification and actual product-host idle/resume proof remain
open acceptance work. Do not mark the ADR complete from these headless checks.

## Supported intermediate and format correction

The parent preserved a real --check-pixels failure from the first implementation: Renderer2D correctly rejected direct BGRA8-sRGB targets. That failure is not a successful UI gate. The revised route does NOT switch the BGRA output to an easier format or weaken assertions. It renders UI only to supported RGBA8-sRGB, transitions it to Sampled, then uses UiHostPresenter's actual embedded present vertex/fragment artifacts and the exact UiPresentArguments shape/name to draw a fullscreen triangle into BGRA8-sRGB. sRGB attachment encoding uses EncodeSrgb=0, matching the product. Main presentation adds the final Present barrier; headless validation transitions the composed BGRA output to CopySource and checks its raw bytes. Both fresh mounts use the same actual two-pass route.

Shader artifacts load through the public UiApplication assembly (new project reference); the existing Graphics.Generators analyzer generates local argument serialization. There is no new Slang compiler integration, production friendship, private presenter access, substitute shader, tolerance change, or RGBA-only claimed presentation proof. Metadata emits SHA256 for actual loaded vertex/fragment target-code bytes before runtime binding specialization, separately from shared source/container/binary provenance. Actual issued draw totals include the composite, and CompositeDrawCalls records its one-draw contribution.

The corrected compositor has now passed strict compilation and the real headless WebGPU gate. Real GUI Present remains blocked and unclaimed.
