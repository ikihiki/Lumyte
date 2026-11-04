# Real UI scenario benchmarks

This integrated executable uses real UiSurface, Signal and native Parley flows.
Repository analyzers remain enabled. See [verification](../VERIFICATION.md) for
strict builds, native checks and the recovered UI-v2 hardware-validation ledger.

RepoRoot is discovered upward via Lumyte.slnx; pass -p:RepoRoot=/absolute/repo when
needed. Native Parley preparation uses the existing repository targets. The project
links the existing issued-draw decorator and hardware timer, not the legacy runner.
No DrawList replay or recorded-UI flattening is used.

## Build and correctness commands (serialized)

From repository root, use the configured .NET/native environment already used for core runs. Do not use the old wrapper's tiering-disabled defaults accidentally.

    headless_project=benchmarks/Graphics/Lumyte.Graphics.Ui.Benchmarks/Lumyte.Graphics.UiScenarios.csproj
    export WORKLOAD_REPO_ROOT="$PWD"
    export DRAWING_BASELINE="$(git rev-parse HEAD)"
    dotnet build "$headless_project" -c Release --nologo
    dotnet run --project "$headless_project" -c Release --no-build -- --list
    dotnet run --project "$headless_project" -c Release --no-build -- --unit
    dotnet run --project "$headless_project" -c Release --no-build -- --smoke

If the validated native library already exists, build can use -p:LumyteBuildParleyNative=false; its expected path is artifacts/text-parley/native/liblumyte_text_parley.so on Linux. Normal build owns native preparation through existing repository targets. WORKLOAD_REPO_ROOT is mandatory when launching outside repository ancestors; the bundled DejaVuSans.ttf is required, never an OS-font fallback.

GPU correctness + bounded headless hardware runner, using the verified Radeon hardware ICD. The linked HardwareGpuTimer enforces actual non-CPU vendor/device 1002:1586, so this --gpu mode intentionally rejects software/other GPUs; --unit/--smoke do not create a GPU.

    mkdir -p artifacts/graphics-benchmarks/ui-validation
    dotnet run --project "$headless_project" -c Release --no-build -- --gpu --case U01 --warmup 20 --frames 120 --depth 1 > artifacts/graphics-benchmarks/ui-validation/U01-depth1.jsonl
    dotnet run --project "$headless_project" -c Release --no-build -- --gpu --case U01 --warmup 20 --frames 120 --depth 3 > artifacts/graphics-benchmarks/ui-validation/U01-depth3.jsonl
    dotnet run --project "$headless_project" -c Release --no-build -- --gpu --case U06 --variant dpi-once --warmup 20 --frames 120 --depth 3 > artifacts/graphics-benchmarks/ui-validation/U06-dpi-depth3.jsonl

Omit --case/--variant to exercise all 14 real profiles. There is intentionally no --quick alias pretending to be formal. Defaults are 200 warmup/2000 measured frames; 20/120 examples are correctness/smoke only. For formal ADR runs the parent must pilot stability, fix common warmup, schedule five independent alternating A/B processes, include machine/source/font/shader manifests and save distributions. This executable does not implement that outer orchestration or claim formal ADR acceptance on its own.

## Implemented standard profiles

- U01 hud-60 / hud-120: real `Signal<string>` score and `Signal<float?>` health; moving 128-object 2D background each logical frame; 10 HUD events per logical second, proved by pure trace tests. Same world speed per second across 60/120. UI DrawList reused between HUD events.
- U02 transform / layout: transform is direct public Canvas translation around actual UiSurface.Record with static declarations/layout, no replay; layout rebuilds actual menu width and spacing. Transform recordings are intentionally not cached, making the recording cost explicit.
- U03 forced-draw: actual mounted native UI; completed UI DrawList cached; GPU frame still submitted. This is NOT host no-draw idle.
- U04 fractional / turnover, 20 / 100 rows: 10000 logical data items; benchmark-owned keyed visible window; fractional trace never changes first row; turnover changes first row every four frames and reuses surviving keyed elements. 20 rows use 17px native font/26px rows, 100 rows use 8px font/10px rows so all rows fit a 1080-high target. Density/font difference is explicit, not a same-quality speed ratio. Both are real native text and clip flows, not product virtualization.
- U05 same-width / length: actual controlled TextBox/TextDocument replacement and native shaping. Product TextBoxElement.cs:205-206 explicitly shapes at positive infinity (single-line, overflow clipped); a wrapping editor variant is therefore NOT selected in Standard. Dormant wrap fixture logic retains a LineCount>=2 assertion, so manually enabling it cannot silently pass without real wrapping. No OS input, IME or Undo claim.
- U06 resize / dpi-once: actual UiSurface viewport updates; runner recreates the slot texture/view after completion when physical extent changes. resize changes panel and target width. dpi-once uses fixed logical dimensions and DPR 1->2 at logical frame30 (1920x1080 ->3840x2160); this is a synthetic logical DPI event, not OS monitor migration.
- U07 preview-2d: clipped moving 128-object 2D preview at 60Hz, UI declaration update only at input events every60 logical frames. Surrounding UI recording reused between input events.

## Determinism, correctness, counters

UiInput.At(profile,frame) fixes logical state and SHA256 input ID. Profile.Key + input ID identifies the exact profile/input; frame IDs are logical, not retirement order. Warmup drains completely and the measured trace restarts at frame0 on the warmed fixture, so one-time frame30 DPI/input events are not consumed only during warmup. Initial measured slot allocations/resets are visible in targetResizeMs/targetReallocated; they are not falsely excluded as steady-state.

--unit: pure deterministic trace/input-hash checks, range/target validity, exactly10 HUD updates/sec, no fractional row crossing, one DPI transition. No native text/device initialization required.

--smoke: real native provider, mounted text document consistency, no .notdef glyphs, finite advances, nonempty glyph outlines, expected Signal/rebuild event counts, static DrawList identity reuse, independent mount output. No elapsed thresholds.

--gpu: exact same-format readback comparison at frames0,6,12,30,60,61 before measurement plus final logical state after drain. Reference constructs a NEW independent UiSurface/Signals/native engine directly from pure logical input and records it onto Canvas, never reuses the live UI draw lists or Scene flattening. This checks incremental reconciliation/cache correctness but is NOT independent of the product UI layout algorithm. Separate opaque red/blue interior pixel probes validate color, painter order and clip boundary analytically outside every UI/preview region. Full-frame exact comparison and analytic checks are both required.

Native font hash is emitted; missing glyphs fail rather than render blank. Additional CheckGlyphs layouts run only in Verify outside timed work. LayoutCalls/TextLayoutTicks count actual timed provider calls; glyph metadata preflight does not inflate those counters. TextBox layouts are single-line even when the provider is capable of bounded paragraph wrapping; native CheckGlyphs reference metadata at bounded width does not establish mounted TextBox wrapping. No Japanese/COLR/MSDF suite is claimed here. DejaVu native asset/glyph coverage is checked for actual displayed strings.

Frame rows carry profile key, input ID, logical time, update/layout/record/target-resize/prepare/encode/submit host intervals, host-frame, slot-wait+retirement, issued draw count and command count. host-frame excludes previous-slot waiting but includes required target recreation. TextLayoutTicks are nested layout work, not additive to layout. Workload counters are cumulative snapshots, not per-frame deltas. JSON serialization/output occurs after measured drain; allocating row objects/counter snapshots is outside host-frame but INCLUDED in elapsed throughput. Parent should reuse its shared preallocated frame schema for low-overhead formal measurements.

GPU spans are emitted on completion rows, joined to frame rows by profile key/frame ID/input ID; frame rows carry query-pending/source metadata rather than a misleading always-null span. The final drained slots emit their completion rows too. Completion rows use the matching slot frame/input ID; completion latency is submit-start to observed fence completion, including host delay/queue wait. Every in-flight slot owns an independent HardwareGpuTimer/query pair. Begin runs on the underlying Vulkan command after the initial target barrier; End runs after all prepared draws/inter-layer barriers and before Finish. Span is TOP_OF_PIPE to BOTTOM_OF_PIPE, including clear/draw/inter-layer barriers but excluding initial target barrier, Prepare uploads, separate readback and CPU queue latency. After confirmed completed submission status, CompleteAsync reads and validates finite nonnegative timestamps before commands are disposed or the query can be reused. Unsupported timestamps remain null with unsupported query status; invalid/read failures fail the run, never substitute CPU fence time. Slot-wait includes observed completion and retirement; retirement is additionally exposed as a nested diagnostic, not additive twice. Throughput includes final drain and resource retirement. Each slot owns target/view/commands/prepared draws/submission/query until completed; target resize cannot touch live work. Unknown submission acceptance/fence failure quarantines resources, query pool and device instead of reusing/disposal under uncertain GPU work. Slot.Dispose releases its query only when no submission remains and the slot is non-uncertain (confirmed complete or never submitted). Readback honors backend row alignment and strips padding for exact comparison.

## Honest remaining integration gaps (not unsupported-API labels)

- U02 isolated group-opacity needs a RenderGraph layer/effect adapter; current ordinary UI Widget does not expose group transform/opacity. Translation is implemented using public Canvas around real recording, not a widget animation claim.
- U05/U06 mounted TextBox wrapping is unavailable on this selected product control because TextBoxElement forces positive-infinity layout width. Panel/window resize still drives real layout and target recreation, but it must not be advertised as text reflow. General Parley paragraph wrapping is supported and would require a distinct wrapping UI element fixture.
- U03 displayed host scheduling idle/resume needs actual host/window instrumentation. No fabricated no-draw latency/FPS.
- U07 actual 3D preview requires model/render graph fixture; current real variant is explicitly 2D.
- Process manifest now records HardwareGpuTimer.DeviceDescription from the created VulkanDevice: actual physical device name, vendor/device/type, driver name/info/version, active queue family, timestamp period/valid bits and support. No null identity placeholders remain. The identity-only process timer has no recorded query work; real GPU query pairs are slot-owned. This is deliberately the same hardware-specific adapter as the parent, not a portable capability claim.
- Parent shared manifest/artifact analyzer/BDN and process orchestration remain integration work. This runner emits JSONL to stdout and returns nonzero on failures; it does not swallow failures into unsupported results.
- Per-depth selected pixels compare with the same deterministic fresh-mount reference; the verified UI-v2 ledger below also joins depth1/depth3 profile/frame/input IDs and hashes.

## Sentinel fixture correction (ui-v2; GPU revalidation passed)

The first ui-v1 GPU checks failed the unchanged exact red sentinel assertion at U01/frame0, after full actual/reference pixel equality had already passed. Source trace proves the root Backed Panel is laid out under UiBoxConstraints.Tight(viewport); Tighten clamps requested panel width/height up to the viewport minima. Its background Rectangle originally had no dimensions and RectangleElement.FillBounded therefore painted the full target at alpha0.95 AFTER the background sentinel/world. The expected red/blue colors were valid, but the fixture claim that this UI paint stayed in the panel region was false.

Backed now explicitly sizes its painted Rectangle to the supplied panel width/height. Verify additionally asserts the mounted background size for every backed profile. The red/blue pixel coordinates, exact equality and tolerances are unchanged. This restores the intended moving-world/static-HUD coverage instead of concealing occlusion by moving probes to a final overlay. Scenario version is ui-v2 because corrected pixels differ from invalid ui-v1. Old failed GPU logs are preserved. The following ledger records the completed rerun.

## Verified Radeon ui-v2 gates

Evidence directory: artifacts/graphics-benchmarks/extensions-checks-2/. Machine-readable audit: UI-validation.json.

- UI-build-clean.log: strict serialized (-m:1) Release build, **0 warnings / 0 errors**, analyzers enabled. Earlier build retry warning logs remain preserved.
- UI-unit.jsonl and UI-smoke.jsonl: all14 deterministic/native profiles pass, including the new painted-background-extent assertion.
- UI-U01-d1.jsonl: initial corrected U01 60/120Hz GPU sentinel/reference gates pass before full suite.
- UI-d1.jsonl and UI-d3.jsonl: actual Radeon8060S vendor/device1002:1586, Mesa26.0.8; each depth completes all14 profiles x64 frames with warmup0 and final drain/retirement. Per depth:896 uniquely matched frame/completion/input IDs,896 finite nonnegative valid GPU query pairs,98 independent exact-frame pixel checks and196 expected analytic RGBA samples.
- Cross-depth audit:896 logical input IDs match; all98 selected exact-pixel hashes match. Red samples are exactly[255,0,0,255]; blue samples exactly[0,0,255,255] in actual and independent reference. No analytic assertion or tolerance was relaxed.
- The initial combined depth1/depth3 shell hit its600s overall tool deadline during depth3; UI-d3-overall-timeout.jsonl and its status JSON preserve that incomplete attempt. A fresh FULL depth3 retry (extended command deadline1200s) independently completed all14 profiles; no partial attempt is treated as passing.
- These are correctness/lifetime/query gates, NOT formal warmed or replicated performance measurements. GPU process source environment was unset (source:null); audit records post-run HEAD and harness file hashes, not fabricated process source metadata. Formal parent runs must export DRAWING_BASELINE and full source/dirty provenance.
- All owned validation processes finished. Exclusive build/GPU ownership is released to parent after reporting.
