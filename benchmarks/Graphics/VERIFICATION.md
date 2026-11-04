# Scenario benchmark verification

## Scope

The ADR remains a proposal. These are bounded executable profiles, not completion
of every ADR variant or a claim that one rendering representation is faster.
The core and extension executables leave product APIs unchanged. See
[coverage inventory](scenarios/coverage-v1.json) and each family README for inputs,
measurement boundaries, independent references and explicit gaps.

## Integration gates (2026-11-04)

The takeover preserved the existing `feat/adr0027-benchmarks` source at `1900990`
and its uncommitted family implementations. No live benchmark or build remained.
Existing failed UI-v1 and incomplete timeout attempts were retained, not relabelled
as passing. The corrected UI-v2 source hashes were matched against its validation
ledger before reusing those results.

- All six extension projects: strict Release builds, zero warnings and errors.
- Managed fixture tests: 10 passed, zero skipped or failed.
- Paired-process driver unit tests: 6 passed.
- CLI regression execution: 32 invalid invocations rejected before GPU creation,
  3 valid catalogs and Text BenchmarkDotNet option passthrough passed. Unknown,
  duplicate, missing, conflicting and mode-incompatible options no longer silently
  change the selected work. Smoke count/depth flags are rejected, not ignored.
- Text layout semantics: all 7 real-font cases passed.
- Text GPU smoke: 7 cases x 3 representations x 2 slot depths = 42 combinations,
  168 valid frame/query pairs, 588 successful check rows.
- Final effects smoke: 9 profiles x 2 depths = 18 combinations, 54 frames and
  72 successful check rows at the documented 64x64 diagnostic target.
- Final primitives gates: 26 profile/depth combinations, 104 frames, all four
  G01/G02/G05/G06 domains passed their independent analytic pixel checks.
- Final UI-v2 rerun: 14 profiles at depths 1 and 3, 64 logical frames each;
  1,792 valid GPU query pairs, 196 exact-frame reference checks and 392 analytic
  RGBA samples. All 896 logical input identities and 98 selected exact-pixel
  hashes match across depths. Frame/completion joins and valid queries verified.
- Earlier ordinary2D, image/transfer, effect and primitive diagnostic artifacts
  remain under `extensions-checks-1/`; they are not new formal campaigns.

Hardware: actual Radeon 8060S, vendor/device `1002:1586`, RADV/Mesa 26.0.8.
The selected ICD was `/usr/share/vulkan/icd.d/radeon_icd.json`; the created device
identity and actual timestamp capability were also recorded by the runners.
This is Linux Vulkan validation, not Windows, browser or portable backend coverage.

Local evidence is under ignored `artifacts/graphics-benchmarks/takeover-checks/`
(builds, fixture/driver tests, CLI regressions, layout results, `final-text/`,
`final-effects/`, `final-primitives/`, `final-ui-d1.jsonl`, `final-ui-d3.jsonl` and
`final-validation.json`). The final audit records harness source SHA256 hashes.
Generated results do not belong in Git. Older UI evidence remains separately in
`extensions-checks-2/UI-validation.json`; its process source field was unset.
The final UI processes explicitly recorded `1900990` plus this dirty source tree;
final source file hashes are retained. Do not promote any of these short, unwarmed
correctness runs to source-frozen formal performance comparisons. Text/effect smoke
explicitly uses `unmeasured-smoke` provenance.

## Reproduction

Run from repository root. Finish all builds/tests before GPU work; never run
performance jobs concurrently. Use fresh output paths for every invocation.

```sh
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
vulkaninfo --summary

for family in Ordinary ImageTransfer Effects Primitives Text Ui; do
    dotnet build "benchmarks/Graphics/Lumyte.Graphics.$family.Benchmarks" -c Release -m:1
done
dotnet test tests/Graphics/Lumyte.Graphics.Benchmarks.Tests -c Release
python3 -m unittest discover -s tools/benchmarks/graphics-scenarios -v
python3 tools/benchmarks/graphics-scenarios/check-extension-cli.py

text=benchmarks/Graphics/Lumyte.Graphics.Text.Benchmarks
dotnet run --no-build -c Release --project "$text" -- --check-layouts
dotnet run --no-build -c Release --project "$text" -- --smoke --output artifacts/graphics-benchmarks/text-check-new

ui=benchmarks/Graphics/Lumyte.Graphics.Ui.Benchmarks
export WORKLOAD_REPO_ROOT="$PWD"
export DRAWING_BASELINE="$(git rev-parse HEAD)"
dotnet run --no-build -c Release --project "$ui" -- --unit
dotnet run --no-build -c Release --project "$ui" -- --smoke
dotnet run --no-build -c Release --project "$ui" -- --gpu --depth 1 --warmup 0 --frames 64 > artifacts/graphics-benchmarks/ui-check-new-d1.jsonl
dotnet run --no-build -c Release --project "$ui" -- --gpu --depth 3 --warmup 0 --frames 64 > artifacts/graphics-benchmarks/ui-check-new-d3.jsonl
```

For ordinary2D and image/transfer, `--smoke` is managed-only. Their README hardware
commands execute the actual GPU gates separately. Effects `--smoke` uses 64x64
targets; primitives deliberately uses 32x32 targets. Those successful small-target
checks do not establish full-HD throughput or a formal quality/performance tradeoff.

## Remaining acceptance work

- Formal independent-process performance comparisons for extension families,
  stable warmup selection and complete frozen source/native/shader provenance.
- Selected standard/single-factor profile evidence, retained managed-live/RSS and
  memory-pressure behavior; the ADR does not require a full Cartesian sweep.
- Host no-draw idle/resume, presentation, native device-loss recovery, 3D UI preview.
- Mounted TextBox wrapping, OS input, IME and Undo; the paragraph text fixture
  exercises real bounded wrapping but is not a wrapped editor control.
- Portable hardware/backend validation. Missing observations are not unsupported
  product APIs, and null GPU times are never replaced with host fence waits.

## ADR completion follow-up

The implementation branch was pushed and remote SHA `f3bc4aa` verified before this
follow-up. New work remains subject to its own source-freeze and campaign gates.
The [six-condition acceptance ledger](ACCEPTANCE.md) is authoritative; optional
parameter variants are not a demand to execute the Cartesian product.

Completed follow-up gates under `artifacts/graphics-benchmarks/adr-completion/`:

- Strict Release build and formatting: core GPU/CPU, Text, Effects, Primitives and UI.
- Managed fixture/lifetime/parser tests: 61 passed, zero skipped/failed.
- Pre-device CLI regression invocations: 32 rejected as intended; catalogs and BDN
  passthrough continue to pass. Campaign tests additionally exercise retained real
  artifact schemas, not only mocks.
- Burst/return: Canvas/Scene x depths1/3 x snapshot-history0/1/3, 12 profiles and
  1,812 logical frame/query pairs; all independent exact+analytic checks pass.
  Frame/input IDs match across all profiles, actual updates occur only at scheduled
  events, and historical generation counts remain within their declared budgets.
- Native WebGPU process-local destruction and Hosting recovery: three cycles pass
  native loss callbacks, lease invalidation, native retirement, old child/owner
  disposal, new epochs and rebuilt red/green/red model pixels. Successful run has
  zero quarantined owners. This is drained-work instance destruction, not a host
  GPU reset or uncontrolled driver fault and not pending-work-loss coverage.
- Primitive timestamps: 26 profile/depth combinations, 312 valid actual query
  pairs, including multi-command/multi-submit ordered queue spans. Independent
  analytic and pipelined-final pixel checks pass. Queue starvation can be part of
  a multi-submit span; it is not exclusive GPU busy time.
- Retention memory: 18 independent processes (both paths, history0/1/3, three
  repeats) preserve baseline/initialized/eight-cycle/after-dispose observations.
  Full-GC managed live heap and RSS are separate; native/GPU allocation stays null.
- First-use Text/Effects/UI records contain real device/font/initial-layout and
  first-render stage intervals, with exact-pixel checks. Driver/global native cache
  coldness is unknown and is not inferred from creating a fresh local engine.

`correctness-ledger.json` validates these counts/scopes against raw evidence.
Earlier incomplete and failed runs remain preserved. Formal campaign execution
evidence is distinct from these correctness and retained-memory observations.

Displayed Present/VSync/host-idle execution is still blocked by the absent GUI
execution target: DISPLAY and WAYLAND_DISPLAY are unset and no node is connected.
A GUI target was requested; headless rendering is not substituted for this gate.

### Frozen formal measurement evidence

Source/binary mutation was excluded while running the `0572187` campaigns. Eight
campaigns / 80 independent formal processes qualified, with five alternating pairs
per campaign and explicitly fixed common warmup/frame counts. This includes core
Canvas/Scene and matching Canvas/Canvas noise controls at depths1/3, plus selected
Text, Effects, UI and asset-lifecycle selfsame controls. The initial core depth3
campaign failed minimum duration and remains preserved; the replacement is a new
complete campaign with a predeclared larger common frame floor, not a favorable
subset retry. No process frames are pooled as independent process observations.

Generated values, raw data, stage distributions, absolute pair deltas/dispersion
and the report remain under ignored `adr-completion/`. Recompute inventory with:

```sh
python3 tools/benchmarks/graphics-scenarios/summarize-campaigns.py --root artifacts/graphics-benchmarks/adr-completion --output artifacts/graphics-benchmarks/adr-completion/campaign-inventory.json
```

The inventory verifies qualified duration/frame budgets and every evidence archive
member/hash, and records compressed bytes and uncompressed member bytes. A/A is a
noise control, never a claimed improvement. The original core A/A raw scope text
was generic; its explicit `aaNoiseControl` and actual Canvas/Canvas plan are
authoritative. The future-run scope label is corrected without rewriting raw data.

Actual presentation remains the separate external execution gate above.

### Presentation preparation and remaining gate

The real-UI presentation executable builds and passes formatter verification. Its
CLI regression script checks 16 invalid invocations plus three managed modes and
the explicit missing-DISPLAY guard before device/window creation. The real WebGPU
headless U03 gate passes initial/final native-text/UI states, using a supported
RGBA8-sRGB intermediate and the actual product present shaders into BGRA8-sRGB.
Live/fresh-mount full-image equality, analytic red/blue endpoints, selected adapter,
font and actual shader target-code hashes are recorded. The initial direct-BGRA
Renderer2D rejection remains preserved as failed evidence; no oracle was weakened.

Use `python3 tools/benchmarks/graphics-scenarios/check-presentation-cli.py` after
building the presentation executable. `--check-pixels` is the explicit real GPU
headless gate; `--run` requires an authorized logged-in user desktop.

No window was created, no Present or actual monitor/VSync measurement was made,
and benchmark-owned scheduling is not product-host idle/resume proof. The local
X11 sockets belong to the login greeter, not a requesting-user desktop; they were
not used or reauthenticated. ADR acceptance remains open for this GUI/host gate.

### Virtual-display functional follow-up

The physical-login blocker was resolved for functional testing by an isolated
Weston14 headless GL compositor with its own XWayland DISPLAY. Distribution
packages were extracted into artifacts, without sudo/system installation or
persistent configuration. The owned compositor uses renderD128/Radeon hardware;
client native metadata independently identifies Radeon8060S. Greeter displays
and authentication were not used.

U03 forced drawing at depths1/3 and benchmark-owned stop/resume passed120 frames
each, including actual Present returns, completion ordering and exact/analytic UI
checks. The separate standard UseDesktop host probe passed controlled dispatcher
model-update delivery and ordered Stopped shutdown; idle CPU recording continued,
matching its current periodic implementation. Private GPU/Present/reservation
counters remain null. Demand-idle is not claimed as an implemented product feature.

Evidence is under ignored virtual-validated-001/. Reproduce with
run-virtual-display.sh and validate with check-virtual-results.py. Configured
nominal60Hz and synthetic XRandR modes are virtual context, not physical VSync,
monitor refresh, scanout or input-to-photon proof. The virtual processes terminate
after their client script; no long-lived desktop service is installed.
