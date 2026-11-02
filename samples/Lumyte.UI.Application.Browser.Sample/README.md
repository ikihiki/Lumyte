# UI application Browser

Standard host sample. Input, layout, drawing and cleanup are owned by `UiApplication`.

The GPU smoke probe uses a 30-second screenshot deadline for software rendering in CI. Set `LUMYTE_BROWSER_CPU_THROTTLE=6` and `LUMYTE_BROWSER_CAPTURE_REPEATS=3` when running `verify-browser.mjs` to repeat the stress check. Application automation defaults remain unchanged.

The width is animated through a model Signal and an injected ManualClock. The GPU smoke test checks pixels at widths 160, 200 and 240 and dispatches freeze/resume lifecycle events around a ten-second clock jump to verify that suspended time is excluded. These test controls only move the model and borrowed clock; the application owns animation evaluation.
