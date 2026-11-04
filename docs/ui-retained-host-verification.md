# Standard UI retained host verification

The standard Desktop/Browser Application now captures each Presentation through
its persistent UiDrawingScene2D. Direct Canvas callers keep their explicit recording API. All application drivers,
including Headless, consume retained snapshots only. Staged candidate validation
uses retained capture; visual-image capture/update prepares its immutable layer
fragment through a retained scene and follows the same explicit frame retirement.
The obsolete whole-frame DrawList cache and direct-driver fallback are removed.
DrawList2D remains the immutable fragment payload used by DrawingScene2D, not a
second application frame implementation.

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

Build the Browser integration target with --target Compile:

- src/UI/Lumyte.UI.Application.Browser

The historical Desktop owner/presentation lifecycle checks below used the isolated
Weston/XWayland procedure and presentation benchmark from commit 819e54f.
That benchmark implementation is not included in this UI-only branch.
It does not use the physical login/greeter or establish physical scanout/VSync.
The separate product-host probe observes CPU callbacks, model update delivery and
ordered shutdown; its private GPU/Present counters remain unobserved. The other
three cases exercise the benchmark's direct renderer, not retained product UI.

## Historical verification on 2026-11-05

These results were recorded before separating the benchmark implementation from
this PR; they are not a rerun on the current main-based tree.

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

## Retained-only host verification on 2026-11-05

After removing the legacy List-frame route on main baseline 5095b7f:

- Application: 93 passed, zero skipped, with LUMYTE_GRAPHICS_GPU_TESTS=1 and
  VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json. This includes both the
  five-state exact retained/direct pixel comparison and real GPU candidate
  preparation plus visual-image capture/update/retirement.
- Navigation: 45 passed; Application.Navigation: 27 passed. Total: 165 passed.
- Browser and Desktop packages compile in Release with zero warnings/errors.
- The old whole-frame cache tests were migrated to retained validation; fragment
  reuse, animated/hidden content, fresh receipts and asynchronous candidate
  snapshot lifetime are covered without retaining the obsolete host path.
- No benchmark implementation, tools, dedicated benchmark tests or solution
  registration changes are included. Browser runtime was not revalidated.
