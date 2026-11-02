# UI application Browser

Standard host sample. Input, layout, drawing and cleanup are owned by `UiApplication`.

The GPU smoke probe uses a 30-second screenshot deadline for software rendering in CI. Set `LUMYTE_BROWSER_CPU_THROTTLE=6` and `LUMYTE_BROWSER_CAPTURE_REPEATS=3` when running `verify-browser.mjs` to repeat the stress check. Application automation defaults remain unchanged.
