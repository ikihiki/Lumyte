# ADR-GRAPHICS-0027 acceptance ledger

The six conditions below come from the ADR acceptance section. This is an
implementation/verification ledger, not a decision to mark the proposal accepted.
The ADR explicitly does not require the Cartesian product of all parameters.
Additional variants such as a 3D UI preview do not become mandatory merely because
they are listed as future workload choices. Unknown counters remain null.

## 1. Identify scenario inputs, paths, intervals and expected states

Bounded D01-D12, U01-U07 and G01-G06 profiles are executable and identified in
[coverage-v1.json](scenarios/coverage-v1.json). Each family README specifies inputs,
measurement boundaries, independent checks and remaining variants. Direct Scene
layers/effects are genuinely N/A; they use the RenderGraph path instead.

Status: bounded profile identification verified. Not a claim that all variants or
all backend/hardware combinations have been measured.

## 2. Fair 4,096-command granularity comparison and retained amount

The core uses public Canvas/Scene APIs, preserves static Canvas recording reuse,
and measures changed-node rerecording plus ReplaceContent. Forwarded draws, exact
changed element/node counts, rerecorded commands and public compile/reuse/idle cache
observations are recorded. Exact full-target comparisons and analytic interior
colors pass on hardware.

Verified: the 12 Canvas/Scene x depth1/3 x history0/1/3 burst/lifetime hardware
profiles pass; 18 independent-process memory observations cover both paths, all
history budgets, three repeats, named full-GC/RSS checkpoints and after-dispose. Estimated idle-cache bytes must not be relabelled total retained/native
VRAM. Native allocation/VRAM observations unavailable from public counters stay null.

## 3. CPU/GPU/display, first-use/steady/burst and depth 1/3

Core throughput drains warmup before measurement and includes final GPU completion
and retirement. Text, effects and UI now record actual first local device/font/
layout/renderer stages separately from steady-state samples. Nested text layout is
not added twice. Driver/native disk caches are not asserted cold. Fresh processes
are required for independent cold observations.

Verified: scheduled full-update/return bursts, event-relative groups and independent
post-event/history images. Ordinary primitive timing now has 312 valid actual query
pairs across 26 profile/depth combinations. Virtual Weston headless GL/XWayland now executes the actual U03 window/surface/
Present path at depths1/3 with hardware rendering and explicit virtual metadata.
The compositor output is fixed nominal60Hz; synthetic XRandR modes are recorded.
These functional observations are not physical monitor/VSync/scanout measurements.

A separate public-API UseDesktop probe observes real product-host CPU recording
and controlled dispatcher model updates. Recording continues during idle: this
host is periodic, so zero-work demand-idle is N/A for the current implementation.
Unavailable reservation/draw/Present counters remain null. The benchmark-owned
pause/resume case is not substituted for that product behavior. No product API or
scheduler behavior was changed. Physical-display timing is separate unmeasured
scope, not a reason to block virtual functional verification.

## 4. Independent references, logical identities and honest failures

Core, ordinary2D, image/transfer, text, effects, primitives and UI include independent
logical/analytic checks with explicitly bounded quality scope. UI-v2 fixes painted
background extent without relaxing sentinels. Original failed and incomplete runs
remain preserved. Final depth1/3 UI checks join frame/input/query identities and
compare exact pixels.

Verified: post-burst/snapshot hardware checks and three process-local native WebGPU
destroy/callback/Hosting-recovery cycles pass. Old leases reject access, old owners
retire and rebuilt red/green/red model images match analytic pixels. Pair validators
now check compatible schemas/profiles/provenance, input identities and query/check
evidence; actual retained artifacts supplement synthetic negative tests.

## 5. Recomputable process distributions, A/B differences and provenance

Raw frames/checks/manifests/summaries are emitted outside measured windows. Shared
provenance now records actual source commit, tracked dirty-diff SHA256, untracked
paths, CPU/OS/runtime, source/shader-source and output managed/native binary hashes.
Fonts and other assets are identified by their family-specific manifests.
Shader-source/container hashes are not standalone GPU target-code hashes.

Verified on frozen source `0572187`: eight campaigns and 80 qualified independent
formal processes cover core Canvas/Scene at depths1/3, matching Canvas/Canvas
controls, and Text/Effects/UI/asset-lifecycle selfsame controls. Every qualified
observation passes the declared minimum duration and frame budget. The original
depth3 duration-shortfall campaign is retained as unqualified; its replacement
uses a newly predeclared common budget for all five pairs. Archive membership,
raw-file SHA256 and compressed/uncompressed-member byte counts were verified.
Generated measurements and reports remain under ignored artifacts, not in Git.
Do not pool frame samples across independent processes or label BDN iteration
percentiles individual-frame latency.

## 6. Deterministic fixture, boundary and ownership tests

Existing managed fixture tests, paired-driver tests and pre-device CLI rejection
checks pass without elapsed-time assertions. Real GPU checks separately validate
pixels, slot ownership, completion/query matching and disposal boundaries.

Verified: 61 managed fixture/parser/lifetime tests, 30 campaign/statistical/evidence
tests, 17 existing native Hosting regression tests and 32 pre-device CLI rejection
invocations pass. The presentation executable additionally rejects 16 malformed
invocations and verifies managed scheduling/retirement ownership plus the explicit
missing-DISPLAY-before-device path. New actual hardware gates
are listed above; source-freeze formatting and formal campaigns have passed
their separately recorded gates. No unit test uses an elapsed-time or allocation-byte pass threshold.

## Evidence policy

[VERIFICATION.md](VERIFICATION.md) records completed takeover gates. New completion
evidence belongs under ignored `artifacts/graphics-benchmarks/adr-completion/`.
Each new gate must be verified before removing its remaining-work note above.
No timeout, unstable pilot, missing measurement, native recovery failure or pixel
mismatch is converted into successful acceptance. Public pushes are verified by
remote SHA; pushing does not imply merge or ADR adoption.

## Virtual display follow-up

An isolated hardware-rendered Weston headless compositor and its own XWayland
now execute the real U03 window/Acquire/Submit/Present path at depths1/3, plus
benchmark-owned stop/resume. The standard product UseDesktop host was separately
observed through public APIs: idle CPU recording continues and dispatcher model
updates reach a new recording. The product is periodic, so zero-work demand-idle
is N/A for this implementation; private frame/GPU/Present counters remain null.
This resolves the virtual functional-execution path without requiring a physical
login. Physical monitor refresh, VSync and scanout are separate unmeasured scope,
not simulated successes. See the presentation VIRTUAL-DISPLAY.md instructions.
