# Nested rectangular clip compilation

## Scope and result

An internal compiler optimization preserves the public API, painter order, four-by-four sample coverage, cache budgets and resource retirement. Canonical positive rectangular paths in **nested clip stacks** use the existing rectangle instruction instead of flattening and uploading four winding edges per clip per glyph. Standalone paths, paint graphs and single clips keep the previous compiler route. Curves, additional contours, reversed/empty rectangles, overflowing or inexactly reconstructed extents fall back unchanged.

The unchanged main workload was measured first, before selecting this change. The bottleneck was generic glyph Prepare/Encode and GPU clip evaluation, not native text layout. No external library is needed: the renderer already implements rectangle coverage. ADR-GRAPHICS-0027/0028 remain proposals; this work does not adopt them or implement a new public design.

## Frozen sources and machine

- Before: main `45d269cf3f3e629d64d642c176d517d27496bccc`.
- After production/tests: `c1780b2` (this branch's first commit).
- Harness: unchanged `tools/benchmarks/drawing2d-incremental` at the before commit. No suite from another branch was copied.
- Radeon 8060S, vendor/device `1002:1586`; RADV Mesa 26.0.8-1ubuntu0.3; Vulkan 1.4.335. Actual created device identity and timestamp capability recorded in every process.
- Ryzen AI MAX+ 395, Linux 7.0.0-42-generic x64, .NET SDK 10.0.403/runtime 10.0.14. Release; product-default tiering/PGO/GC; no forced GC or power/clock changes.

## Profile and measurement boundaries

Existing `editor-line-edit --count 200`: 20 mounted visible list rows plus inspector/center labels, real controlled TextBox edit and Parley outline text, nested production clips, 619 drawing commands, one layer. Existing 1280×720 Rgba8Srgb target, DPR1 and embedded DejaVuSans font. This is not the proposed 1920×1080 target profile and is not an Application-host/retained-cache benchmark.

Five independent alternating process pairs: A/B, B/A, A/B, B/A, A/B. Each fresh process uses 200 warmup and 2,000 measured frames (20,000 total measured frames), serialized depth one. Each process's measurement lasts well over ten seconds. Preserve every outlier. JSON serialization, setup and final readback are outside stage timing; process-wide allocation includes instrumentation and asynchronous/background allocations, not BDN bytes/op.

Prepare includes resource creation, instruction compilation/packing and mapped uploads. Encode includes command creation, barriers, PreparedDraw.Record and Finish. GPU TOP→BOTTOM timestamps enclose recorded upload/barrier/clear/render, excluding the initial target barrier; these are elapsed device spans, not exclusive GPU occupancy. Submit-completion is host wall latency. Frame wall includes update/layout/record/prepare/encode, submit completion, timer read and disposal; it is not host-to-Submit or displayed FPS. Text layout is nested within update/layout, not an additive stage.

## Repeated results

Values below are medians of the five process medians. Tail ranges are the five independently computed frame p95 values, not pooled samples.

| Metric | Before | After | Change | Before p95 range | After p95 range |
| --- | ---: | ---: | ---: | ---: | ---: |
| Prepare ms | 63.804 | 49.779 | −22.0% | 72.53–73.54 | 59.45–60.32 |
| Encode ms | 30.754 | 24.257 | −21.1% | 50.21–50.61 | 41.49–42.32 |
| GPU span ms | 83.930 | 65.382 | −22.1% | 84.10–84.12 | 65.57–65.59 |
| Serialized frame wall ms | 225.653 | 185.180 | −17.9% | 244.85–246.58 | 199.90–201.38 |
| Process-wide allocated bytes/frame | 39,805,416 | 37,871,340 | −4.9% | 39.66–39.83 MB | 37.74–37.91 MB |

Paired median improvements: Prepare 20.2–22.4%, Encode 20.4–22.6%, GPU 22.08–22.14%, frame wall 17.5–18.3%. Baseline frame process medians span 223.76–226.70ms; candidate 184.53–185.59ms. Allocation falls by roughly 1.93MB/frame. No timed stage regressed in this profile. Unchanged update/layout also became faster (about 0.11ms layout), so do not attribute every CPU-stage difference directly to changed compiler instructions: lower allocation/GC pressure and CPU/GPU shared power can affect other stages.

The remaining bottleneck is still expensive per-glyph generic preparation/encoding, ~65ms GPU spans and ~40ms destruction. This scoped change does **not** make the UI interactive or satisfy the ADR budgets.

Secondary 10-warmup/20-sample single-process diagnostics (not formal conclusions) also preserved before/after readback hashes: 2d-static count32 GPU 0.243→0.243ms, game-hud-values count32 GPU 8.101→6.268ms, editor-idle count200 GPU 82.020→63.787ms. No observed median Prepare/Encode/frame regression in these short controls; unchanged 2D CPU variation illustrates why these runs cannot qualify small timing changes.

## Correctness

- Drawing2D unit tests: 154 passed, zero failed/skipped, including existing snapshot, failure cleanup and completion-safe lifetime tests. Existing assertions were not weakened.
- Hardware-enabled Drawing2D tests: 16 passed, zero failed/skipped, Vulkan and wgpu on the Radeon ICD. New exact half-float pixel equality compares nested canonical clips to the equivalent extra-collinear-edge winding path across two fill rules, fractional positions, rotation, reflection/nonuniform scale and three DPRs. Existing standalone-owner disposal checks run too.
- All ten measured process readbacks have identical SHA-256: `4EC6BE6E72F98AC2EF33AE5AF274DF11E540E766ECFD293EDDEBD4AEA9CC2B05`. This is before/after equality, not an independent golden for every intermediate frame.
- Deterministic structural guard checks no clip-edge upload/winding instruction for nested canonical rectangles; invalid topology/extents fall back.

## Reproduce and inspect evidence

Use an owned baseline worktree and this branch, activate the same existing absolute `LUMYTE_ENV_ROOT`, and build both before any timing. Do not use `run-workloads.sh`: its historical wrapper disables tiering. Run the unchanged compiled executable directly:

```sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export WORKLOAD_REPO_ROOT="$PWD"
unset DOTNET_TieredCompilation DOTNET_TieredPGO DOTNET_gcServer
dotnet tools/benchmarks/drawing2d-incremental/bin/Release/net10.0/Lumyte.Graphics.Incremental.Benchmarks.dll \
  --workload-hardware --suite editor-ui --case editor-line-edit \
  --count 200 --warmup 200 --samples 2000 > run.jsonl
```

Repeat in the declared alternating order, with distinct output files. The original machine evidence remains under ignored `artifacts/graphics-budget/`: `pair-{1..5}-{baseline,candidate}.jsonl`, matching deterministic gzip files, `archive-manifest.json` (verified uncompressed length/SHA-256 and compressed SHA-256), `summary.json`, `campaign-plan.json`, `campaign-progress.log`, frozen executables and `binaries.sha256`, source/harness/asset hashes, device/OS/runtime details, build logs and TRX test results. Raw/generated measurements are intentionally not committed. Initial pilots are diagnostic only; the first broader candidate pilot was superseded by the final nested-only implementation and is labelled separately.

Summarize the full campaign with:

```sh
python3 tools/benchmarks/drawing2d-incremental/analyze-nested-clips.py \
  artifacts/graphics-budget > artifacts/graphics-budget/summary.json
```

## Limits and uncovered goals

No A/A noise campaign or recorded four-window warmup stability test was available in the unchanged harness. Independent A/B dispersion is reported, but full ADR warmup qualification is not claimed. No CPU BDN job, bounded-depth throughput, native/VRAM live-retention convergence, cold/burst distribution, full-update matrix, effects, real host deadlines, Present/scanout, or all-frame pixel oracle was measured. Existing synchronous stage probes cannot establish deadline miss rates or host-frame p95 budgets. Five-pair results establish improvement only for this fixed UI profile; secondary short runs are diagnostics, not formal regression qualification. Dynamic clocks and background system activity were not controlled.
