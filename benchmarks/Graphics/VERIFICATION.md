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
- Full ADR parameter sweeps, native/live/RSS memory and memory pressure behavior.
- Host no-draw idle/resume, presentation, native device-loss recovery, 3D UI preview.
- Mounted TextBox wrapping, OS input, IME and Undo; the paragraph text fixture
  exercises real bounded wrapping but is not a wrapped editor control.
- Portable hardware/backend validation. Missing observations are not unsupported
  product APIs, and null GPU times are never replaced with host fence waits.
