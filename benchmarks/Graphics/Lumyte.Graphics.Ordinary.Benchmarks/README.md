# Ordinary 2D diagnostic benchmarks

This integrated executable covers bounded D04/D05/D08/D09/D12 profiles without
altering D01/D02/D03/D11, product code or G04. Build and correctness validation are
recorded in [verification](../VERIFICATION.md); short checks are not formal timing.

## Build and smoke (repository root)

    dest=benchmarks/Graphics/Lumyte.Graphics.Ordinary.Benchmarks
    mkdir -p "$dest"

    dotnet build "$dest" -c Release
    dotnet run --no-build -c Release --project "$dest" -- --smoke

The project links the existing core quarantine owner and provenance collector plus
PublicRetainedApiProbeFrame, Commands and HardwareGpuTimer. It still has the actual
Radeon8060S hardware guard, not portable software/other-device support. It inherits
repository analyzers; fix any diagnostics rather than disabling them. No root build
integration is required to execute this project directly.

## Hardware CLI (after build, serialized)

    dotnet run --no-build -c Release --project "$dest" -- --scenario D04 --variant pan --depth 1 --frames 18 --output artifacts/graphics-benchmarks/D04-pan
    dotnet run --no-build -c Release --project "$dest" -- --scenario D04 --variant zoom --depth 3 --frames 18 --output artifacts/graphics-benchmarks/D04-zoom
    dotnet run --no-build -c Release --project "$dest" -- --scenario D05 --variant 64 --depth 1 --frames 18 --output artifacts/graphics-benchmarks/D05-64
    dotnet run --no-build -c Release --project "$dest" -- --scenario D08 --variant 9 --depth 1 --frames 18 --output artifacts/graphics-benchmarks/D08-depth9
    dotnet run --no-build -c Release --project "$dest" -- --scenario D08 --variant path --depth 1 --frames 18 --output artifacts/graphics-benchmarks/D08-path
    dotnet run --no-build -c Release --project "$dest" -- --scenario D08 --variant gradient --depth 1 --frames 18 --output artifacts/graphics-benchmarks/D08-gradient
    dotnet run --no-build -c Release --project "$dest" -- --scenario D09 --variant 16 --depth 3 --frames 18 --output artifacts/graphics-benchmarks/D09-overlap16
    dotnet run --no-build -c Release --project "$dest" -- --scenario D12 --variant small-cache --depth 3 --frames 18 --output artifacts/graphics-benchmarks/D12-small

All variant inventory:

- D04: pan, zoom.
- D05: 16, 64 (actual Path.Segments count per path, including Move/Close).
- D08: 7, 8, 9, path, gradient.
- D09: 1, 4, 16.
- D12: small-cache, large-cache.

Unknown/duplicate/incomplete options reject before device creation. Minimum18frames
ensures D12 revisits two full9-page cycles. --smoke is CPU fixture verification only,
not GPU coverage; it checks every variant/count/period, D05 actual segment count and
D04 source-visible counts. Every hardware profile independently preflights all trace
phases; D12 preflights18steps, others4. Final targets of all depth1/3slots are checked.

## Real implemented semantics

- D04: static3000-tile map with constant56x56 red tiles at64px pitch, positioned from
  (-640,-640), beyond the target. Public SetTransform changes only the content node.
  pan alternates0/4px:510 geometrically visible rectangles in both phases. zoom
  alternates1/1.1 about origin:510/448 source-visible rectangles, explicitly reported.
  This is not a claim that zoom does constant visible work or that the renderer culls.
  All3000 commands remain submitted. No invisible-map shortcut replaces this input.
- D05:32 filled paths with fixed64x64bounds at fixed positions. Actual16/64segments
  include Move/Close. Increasing collinear top-edge subdivisions changes path complexity
  without enlarging bounds; a central top-edge notch moves0/8px each frame. Canvas
  reconstruction plus public ReplaceContent is timed. Both phase images must differ.
- D08: repeated identical rectangle clip intersections with exact depths7/8/9; a
  separate triangular path clip; a separate one-rectangle-clip nonconstant linear
  red-to-blue gradient with solid-red first half. Static content is retained. Path and
  gradient are not aliases for the rectangle-only case. No claim that depth9 uses a
  particular internal shader is inferred: GPU execution/counters reveal actual work.
- D09: fixed256 translucent red rectangle commands, alpha0.5, in painter order. There
  are256/layers stacks with1/4/16coincident layers per stack. Union area changes with
  layer count: this is the fixed-command overlap variant, NOT fixed coverage. Alpha
  is checked analytically against1-0.5^layers; red uses the premultiplied linear result
  converted to sRGB. The predeclared analytic byte tolerance is2 (blend quantization),
  but Canvas-vs-Scene comparison still requires exact bytes everywhere.
- D12:9 immutable scenes with16 separately recorded content nodes each, selected in
  a fixed round-robin then revisited. Visible color alternates red/yellow across pages;
  all144source asset identities are distinct even when colors coincide. Small cache
  permits8idle assets, large cache512, both64MiB estimated idle bytes and3flight slots.
  Actual public compiled/reused deltas, idle CachedAssets and estimated CachedBytes
  are recorded. There is no invented eviction counter, VRAM, upload bytes or guaranteed
  hit-rate claim. Busy assets are not counted as idle cache memory. Page switching
  does not rerecord geometry and is reported separately as WorkingSetPage.

## Checks, timing and results

Independent reference Canvas streams are generated from the original logical input,
never Scene flattening. Analytic checks establish known tile/path/clip/overlap/page
interiors; D08 additionally checks transparent exterior. D04/D05 preflight additionally
rejects identical phase0/phase1 pixel hashes, proving visible updates occurred.
Full target comparison is exact RGBA8. Text/image/layer equivalence is not claimed.

Per-slot command/query/prepared ownership reuses the core helper. Before reuse wait,
read timestamps and retire; include final drain in elapsed headless throughput. Sample
objects/IDs/GPU result lists are prepared before timing. JSON, pixel checks, reference
recording and readback are outside the measured interval. Preflight has a separately
fresh renderer; measured first Prepare remains cold for that renderer. There is no
warmup, pilot or formal A/B campaign in this diagnostic executable. Do NOT feed its
numbers into warmed formal D01/D02 comparisons or call throughput presentation FPS.
No rendering-time GC is forced. Cpu async continuation/bookkeeping remains in elapsed.

Outputs: manifest.json, frames.jsonl, checks.jsonl, summary.json under requested output,
with ordinary2d-diagnostic-v1 schema, input hashes, created device/driver, source/output
content hashes, actual successful API draw/edit counts and public cache observations.
Frames use original logical IDs, not retirement order. Failure writes failed summary
and checks rather than silently dropping failed profiles. Unknown GPU submissions are
quarantined through the linked helper; no resource reuse while completion is uncertain.

## Explicit remaining gaps

This is a standard diagnostic subset, not complete ADR acceptance. Missing: camera
velocity/large visibility sweeps; curved path/contour sweeps and higher segment limits;
clip sharing/paint complexity/batch-boundary matrices; fixed-coverage overlap family;
D12 longer revisit distributions, byte-budget working sets and live/RSS/native memory;
CPU BenchmarkDotNet components for these fixtures; cold/pilot/formal orchestration
and comparative statistical summaries. Other families have separate executable
profiles and validation contracts, listed in the main inventory. Unsupported product/backend execution must
remain an explicit failed/unsupported observation, never replaced by easier geometry.
