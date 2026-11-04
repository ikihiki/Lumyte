# UI application Headless

Standard host sample. Input, layout, drawing and cleanup are owned by `UiApplication`.

The sample injects a borrowed `ManualClock` through `UiApplicationOptions.AnimationClock`. A normal automated click changes the model width from 80 to 160; the standard host applies widths 120 and 160 after clock advances, then delivers completion once. A typed Timeline channel simultaneously changes height from 48 to 64 and delivers its midpoint marker once. The sample never calls `AdvanceAnimations` or constructs a signal scope/runtime. Its existing NativeAOT smoke target exercises this path too.
