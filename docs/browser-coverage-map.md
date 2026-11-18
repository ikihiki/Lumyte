# Browser coverage boundary map

Browser runtime gates own WebGPU/WebAudio, DOM, trusted input, history and
lifecycle interop, plus the shipped WASM ABI. Shared algorithm tests belong on
the host. Native rendering is GPU coverage, **not** CPU coverage.

## Before → after (seven suites audited)

| Suite | Browser boundary retained | Shared behavior moved / host correspondence |
| --- | --- | --- |
| graphics | BrowserDevice feature/rejection contract; real buffers/textures, bindings, shaders, command submission/readback; GPUCanvasContext acquisition, failed multi-context rollback, two surfaces and foreign-device rejection | No extraction: GraphicsExercises exercises actual BrowserDevice resources, rather than a pure algorithm implementation. |
| text | csbindgen WASM ABI, embedded font registration, multilingual vector upload; WebGPU MSDF/MTSDF, O counters, COLRv1 gradients/multiply/currentColor pixel readback and rendering after engine disposal | Removed pre-device CheckShaping invocation and method. Exact office ligature on/off, 日本語 fallback (same font bytes), ABC אבג visual clusters [6,5,4], and 230px paragraph wrapping preserved by BrowserShapingCoverageTests.FormerBrowserShapingScenariosUseNativeAbi. Existing ParleyLayoutTests also cover DisablingLigaturesChangesTheShapedGlyphSequence, MixedHebrewAndLatinUseVisualPositions, ExplicitFallbackShapesMissingScripts, WhitespaceAndWrappingPreserveParagraphMetrics. |
| text backend conformance | Text-specific WebGPU readback remains, with original pixel tolerances | Removed BuildBackendPaintChecks/CheckBackendPaintPixels calls, reducing target height from 1700 to 1000. Exact 28 modes × source-only/intersection/backdrop-only/neither, 10 blend boundaries, 14 gradient edge-case fixtures moved to native BrowserPaintCoverageTests.AllCompositeAndGradientPixelsMatchIndependentReferenceAsync (Lumyte.Graphics.Svg.Tests). Coordinates, independent reference arithmetic, row packing, barriers, RGBA oracle and tolerance 4 unchanged; only text labels removed. Existing CompositeTests and GradientSemanticsTests remain CPU compilation/representation coverage, not substitutes for pixels. |
| ui-application | Canvas extent/presentation and sRGB GPU capture, repeated readback, actual browser input stopping app, borrowed DOM canvas, DOM freeze/resume not catching up skipped time | Animation midpoint/final pixels are retained because they assert browser frame publication, not a broad easing/layout parameter matrix. Generic animation correctness stays in host animation/UI tests. |
| navigation | Actual dispatched click presents old/new GPU colors; same browser window, fresh surface, unchanged location/history, input stop and borrowed canvas | No broad route algorithm matrix in browser. Host RouteTests, HistoryTests and WindowTests remain authoritative for those algorithms. |
| pages | Same navigation verifier (import), including trusted click, GPU capture, unchanged URL/history and shutdown | Removed Program.cs post-run Created=2/Disposed=2 generic DI assertion. LifetimeTests.PageScopesAreIndependentAcrossHistoryAndShareTheApplicationSingletonAsync in Lumyte.UI.Application.Navigation.Tests covers fresh page scopes, constructor/context sharing, history, and exactly-once retirement/disposal including shutdown. |
| transitions | WebGPU flattened group alpha and live content pixels, Hosted popup presentation/exit drain, connected layer midpoint/bounds/no duplicate, sequential presentation, real input gate and browser location/history invariance, stop | Removed GetDisposed assertions/count predicates (0 during flight; 1/2/3 after navigation/back/skip; 4 after shutdown). BrowserTransitionLifetimeTests.PageLeaseSurvivesFlightThenHistoryAndShutdownDisposeExactlyOnceAsync uses a deterministic pending visual boundary, checks no retirement while Animating, then exact retirement across navigate/back/forward skip and stop. Actual DI scope wiring/exact-once scope disposal stays covered by Application.Navigation LifetimeTests above. Browser GetDisposed remains failure-only diagnostics. |
| audio | AudioContext resume, real clip/source/bus ownership and property interop, playback/update, natural source completion, loop across actual clip duration, pause/stop, second context and cleanup | No extraction: AudioExercise.Run uses real backend objects and JS-facing ownership/state; this is not PCM shaping/routing algorithm coverage. Tone is only fixture construction. |

## Assertion and diagnostic guarantees

No pixel tolerance, deadline, skip, or quarantine policy was relaxed. Runtime
console/exception validation, capture flight recorder, GPU audit and failure
state diagnostics remain. Host-native pixel tests use the existing GpuFact opt-in
and Execution=Gpu trait; a skipped GPU test is not a successful rendering check.
The text browser report no longer claims the host-only shape/composite matrix.
Bundled-font registration remains in WASM; browser-backed font fetch coverage in
other browser fixtures is not removed or replaced by host font loading.

## Targeted verification

Use the pinned SDK via `mise exec -- dotnet`:

```sh
mise exec -- dotnet test tests/Graphics/Lumyte.Graphics.Text.Parley.Tests -c Release --filter FullyQualifiedName~BrowserShapingCoverageTests
mise exec -- dotnet test tests/UI/Lumyte.UI.Navigation.Tests -c Release --filter FullyQualifiedName~BrowserTransitionLifetimeTests
LUMYTE_GRAPHICS_GPU_TESTS=1 mise exec -- dotnet test tests/Graphics/Lumyte.Graphics.Svg.Tests -c Release --filter FullyQualifiedName~BrowserPaintCoverageTests
node --check samples/Lumyte.Graphics.Text.Parley.Browser.Sample/verify-browser.mjs
node --check samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs
```

The extracted native pixel fixture exposed an identical-circle radial gradient
backend regression: the native shader optimizer returned red instead of transparent.
An explicit exact-identical-circle guard now returns transparent before quadratic
contraction. The unchanged oracle failed before the guard and passed after it;
no pixel tolerance or expected value changed. The fixture remains GPU coverage.

## Local evidence (2026-11-18)

- node --check: both modified verifiers passed; scoped git diff --check passed.
- Pinned SDK 10.0.403 Compile target with BuildProjectReferences=false: all
  three new test fixtures compiled with analyzers enabled. The normal build
  cannot complete in this checkout: artifacts/nuget is missing and Slang cannot
  load libdxcompiler. Existing dependency outputs were used only for the
  following explicitly provisional executions.
- Compiled navigation CPU fixture: 1 passed, 0 failed, 0 skipped.
- Compiled Parley native shaping fixture: 1 passed, 0 failed, 0 skipped.
- Native GPU oracle: 1 failed, 0 skipped on existing dependency outputs:
  Radial equal, green channel expected 255, actual 0 at (61,1555).
  All preceding composite/blend assertions were reached without failure.
  The unchanged oracle remains fatal; no tolerance or baseline was altered.
  Rebuild native shaders/dependencies through the parent verification path
  before deciding whether this is stale-output or actual native-backend drift.
- Full browser execution is not claimed here.
