# Standard UI retained host verification

The standard Desktop/Browser Application now captures each Presentation through
its persistent UiDrawingScene2D. Direct Canvas callers, staged candidate
validation and visual-image capture/update keep their existing entry points.
Headless/direct-only internal drivers retain the direct compatibility path.

## Reproduce

Use the pinned SDK/shader environment; do not change global.json to match the
system dotnet installation.

From the repository root:

```sh
source tools/setup/activate.sh
LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/UI/Lumyte.UI.Application.Tests
```

The Radeon ICD is this host's hardware manifest, not a portable default. Select
an appropriate verified ICD on another machine. The GPU test is skipped unless
LUMYTE_GRAPHICS_GPU_TESTS=1; a skipped result is not GPU coverage.

Run the related CPU suites through the same runner with --target Test and each
--project below:

- tests/Graphics/Lumyte.Graphics.Drawing2D.Tests
- tests/UI/Lumyte.UI.Tests
- tests/UI/Lumyte.UI.TextEditing.Tests
- tests/UI/Lumyte.UI.Windowing.Tests
- tests/UI/Lumyte.UI.Application.Navigation.Tests

Build both integration targets with --target Compile:

- benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks
- src/UI/Lumyte.UI.Application.Browser

For actual Desktop owner/presentation lifecycle checks, use the maintained
[isolated Weston/XWayland procedure](../benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks/VIRTUAL-DISPLAY.md).
It does not use the physical login/greeter or establish physical scanout/VSync.
The separate product-host probe observes CPU callbacks, model update delivery and
ordered shutdown; its private GPU/Present counters remain unobserved. The other
three cases exercise the benchmark's direct renderer, not retained product UI.

## Verified on 2026-11-05

Baseline: 819e54f. Pinned .NET SDK 10.0.401, Release, repository analyzers enabled.
The explicitly selected Vulkan manifest exposes Radeon 8060S Graphics / RADV
STRIX_HALO, Mesa 26.0.8. No performance comparison was performed.

- Application: 96 passed, zero skipped, including real wgpu retained/direct exact
  full-image equality over five successive states and observed retained reuse.
- Drawing2D: 146 passed; UI: 340 passed; TextEditing: 41 passed;
  Windowing: 47 passed; Application.Navigation: 27 passed. Total: 697 passed.
- Desktop presentation target and Browser package compile with zero warnings/errors.
- Isolated GPU-backed Weston/XWayland validation passed all four maintained cases,
  including the actual standard Desktop host model update and ordered shutdown.
  Evidence: artifacts/ui-retained/virtual-001/validation.json.
- Tests cover inactive coordinator eligibility, fragment reuse, changed leaves,
  custom live drawing, opaque fallback round trips, cross-surface dependency
  convergence, ordering, scale/clip/translation, hide/show and generation reset.
- Real retained-owner lifetime tests cover completion, command-before-frame
  retirement, aborted/unrecorded work, unknown-submit quarantine, confirmed
  device retirement, failed waits and retryable cleanup.

Local generated evidence is kept under ignored artifacts/ui-retained/. Browser
runtime, physical display timing and performance improvements are not established
by these checks.
