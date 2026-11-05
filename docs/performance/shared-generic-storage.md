# Shared storage for generic drawing batches

## Scope and selection

The unchanged current main `1851d3b81984b3b4982e6b555d0c21920b0a8296` was measured before choosing the optimization. A 20-warmup/60-frame diagnostic of the existing editor-line-edit count 200 profile measured median Prepare 47.14ms, Encode 30.92ms, GPU timestamp span 65.40ms, disposal 39.32ms and full serialized frame wall 184.08ms. These are current-main diagnostics, not the older PR 117 campaign and not formal results.

Source inspection showed four native buffer allocations, two mappings, two copies and four buffer barriers per generic command. Vulkan allocates a separate device-memory object for each buffer. This repeated work is especially expensive for hundreds of outline glyph commands. The existing typed command batch, shader-data staging and non-owning buffer slices already provide the required facilities; no new library, public API, shader or renderer architecture is introduced.

On the native-address SPIR-V route, the implementation combines instruction storage/staging and geometry storage/staging within the existing prepared batch. Each program keeps its own argument table and local instruction/data indices, bound to its own range. Compilation, painter order, scissor, coverage, clips, fill rules and draw calls stay unchanged. Shared uploads complete before any draw in the batch. The batch owns all four allocations through the existing GPU completion/command-disposal retirement boundary. There is no cross-frame pool or new cache ownership.

Single-command batches, non-SPIR-V backends and conservatively oversized combined allocations keep the original per-command implementation. Portable descriptor-range alignment is deliberately not assumed. The unchanged implementation is retained separately as the fallback rather than broadening backend contracts. Allocation eligibility uses the actual backend instruction stride and overflow-safe byte/element limits. Cache estimates retain conservative descriptor/CPU overhead and include all actual shared allocation bytes. ADR-GRAPHICS-0027/0028 remain proposals. Snapshot storage and public bulk drawing designs are not changed.

## Frozen comparison

Production/tests frozen at `e9d373f3f03c50de742c7636089494c1b4d4b859`; baseline main `1851d3b81984b3b4982e6b555d0c21920b0a8296`. The timing harness C# and embedded font are unchanged. The only analysis change makes the existing five-pair analyzer's frame count configurable. Both variants use the same frozen native Parley binary. Earlier pilot observations in `artifacts/graphics-second/candidate-pilot.jsonl` are superseded and excluded from conclusions.

Machine: Ryzen AI MAX+ 395, Linux 7.0.0-42-generic x64, .NET SDK 10.0.403/runtime 10.0.14; Radeon 8060S RADV Mesa 26.0.8-1ubuntu0.3, vendor/device 1002:1586. Every process created this device and recorded timestampPeriodNs 10.019036293029785 and timestampValidBits 64.

The table reports medians of five independent process medians. Tail ranges are five independently calculated frame p95 values, not pooled-frame percentiles. All ten processes completed 200 samples and exceeded ten seconds of measured frame time.

| Metric | Main | Candidate | Change | Main frame p95 range | Candidate frame p95 range |
| --- | ---: | ---: | ---: | ---: | ---: |
| Prepare ms | 49.064 | 32.736 | -33.3% | 58.299–63.444 | 42.037–42.716 |
| Encode ms | 24.430 | 22.773 | -6.8% | 40.332–41.975 | 34.817–37.238 |
| GPU span ms | 65.403 | 63.274 | -3.3% | 65.575–65.628 | 63.405–63.434 |
| Dispose ms | 39.712 | 10.780 | -72.9% | 41.191–42.419 | 12.062–12.803 |
| Serialized frame wall ms | 184.299 | 138.436 | -24.9% | 198.075–204.745 | 142.691–146.179 |
| Process-wide allocated MB/frame | 37.730 | 33.736 | -10.6% | 37.748–37.914 | 33.584–33.765 |

Paired frame-median improvement spans 24.0–26.2%. Main process frame medians span 182.60–185.15ms; candidate 134.98–138.78ms. Prepare improves 32.6–33.8%, disposal 72.7–73.0%, GPU spans 3.25–3.27%. Allocation falls roughly 4.0MB/frame. The final candidate includes a short-lived one-element native layout probe per shared batch, in addition to the four shared allocations; no probe remains resident.

**Regressions and attribution:** update/layout/CPU record and Submit medians increase. Layout is 0.464→0.577ms (+24.4%); nested text layout 0.367→0.467ms (+27.2%); CPU record 0.066→0.076ms (+15.9%); Submit 0.456→0.486ms (+6.6%). Layout p95 ranges 0.508–0.612→0.627–0.644ms. These unchanged application stages are not attributed to an algorithm change: different GC timing, default tiering and shared CPU/GPU power are possible contributors. Their increases are included in the measured net frame improvement, not discarded.

## Correctness and review

- Unit/lifetime suite: 156 passed, 0 failed, 0 skipped on the frozen source. New deterministic bounds tests cover actual instruction stride, byte limits, overflow-safe eligibility and the backend shader-element count limit. Existing snapshot, cleanup and completion-safe ownership assertions remain intact.
- Hardware-enabled Drawing2D: 20 passed, 0 failed, 0 skipped, exercising Vulkan and wgpu on the Radeon ICD. New tests compare shared preparation with independent one-command allocations across varying path sizes, curves, fractional/transformed clips, reflection, three DPRs and both fill rules. Separate sampled paints use distinct textures and different instruction counts; independently specified pixels check red/blue blending and transparent exterior. Existing inverse-clip and resource-owner retirement regressions still run.
- All ten primary process readbacks match SHA-256 `249B43005137962C390304740DF937A0B11FAC8770CEDFD6134F88668151C4F4`. The compiled Drawing2D DLL used by both test suites and the candidate benchmark has the same SHA-256 `dfd0af1a6514bf579b4a84a2f78d61c3f93474b4cdcc1e7593aa43a3025dc1ec`.
- A wrong/software ICD is rejected before workload execution, with no silent fallback. The expected negative-control process exits nonzero; its exception log is retained.
- Independent read-only source review found no blocking defect. Actual stride/element-limit eligibility, sampled-resource coverage and a comment explaining the deliberately retained fallback were added before freezing/retesting. A full recording-device failure-injection/allocation-counter suite is still absent; existing lifetime tests and native readback do not replace that coverage.

## Controls and bounded depth

The first short 2d-static control showed a CPU regression despite unchanged GPU spans/allocation. It was investigated with a separately predeclared sustained five-pair control: count 32, 1000 warmup, 10000 measured frames/process, alternating A/B order, same frozen binaries. Every process exceeded ten seconds. Frame median-of-medians is 3.276→3.269ms (−0.24%); independent p95 ranges 3.580–3.595→3.571–3.599ms; GPU 0.243663→0.243623ms and allocation essentially unchanged. Thus the short-control CPU regression did not reproduce under sustained comparison; no meaningful gain is claimed for this unchanged path. All ten control readbacks agree.

Other short single-pair diagnostics (20 warmup/40 samples; not formal regression qualification) preserve matching readback hashes: game-hud-values count 32 frame 16.88→14.26ms, GPU 6.279→6.048ms; editor-idle count 200 frame 179.78→123.39ms, GPU 63.881→61.835ms. Raw stage/allocations are preserved, but these short runs cannot qualify small timing changes.

Existing pipeline runner diagnostics use 20 warmup/120 measured frames, including final drain and retirement. Headless throughput: depth 1 main 5.357→candidate 7.825 completed frames/s; depth 3 main 2.350→candidate 6.627. All four final hashes agree, and each process has 120 GPU query spans. **Depth 3 is slower than depth 1 for both variants**; overlap is not automatically beneficial for this allocation-heavy profile. These are one process per variant/depth, not a repeated throughput distribution or latency p95. No displayed FPS claim is made.

The remaining main-profile bottlenecks are ~63ms GPU spans, ~33ms generic preparation and ~23ms encoding, plus ~11ms destruction. The change does not meet editor interaction/deadline budgets.

## Reproduction

Use this owned checkout and the recorded main baseline, build each before timing, and freeze separate executable directories. The pinned global.json SDK is 10.0.403; the measured runtime is recorded in artifacts/graphics-second/environment.txt. The same native Parley library and DejaVuSans asset are used by both variants. The build command below reuses the already frozen native library/notices in baseline-bin; stage the tested native asset there before building.

```sh
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
source tools/setup/activate.sh
dotnet build tools/benchmarks/drawing2d-incremental -c Release \
  -p:LumyteBuildParleyNative=false \
  -p:LumyteParleyNativeOutput="$PWD/artifacts/graphics-second/baseline-bin"
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export WORKLOAD_REPO_ROOT="$PWD"
unset DOTNET_TieredCompilation DOTNET_TieredPGO DOTNET_gcServer
dotnet tools/benchmarks/drawing2d-incremental/bin/Release/net10.0/Lumyte.Graphics.Incremental.Benchmarks.dll \
  --workload-hardware --suite editor-ui --case editor-line-edit \
  --count 200 --warmup 200 --samples 200 > run.jsonl
python3 tools/benchmarks/drawing2d-incremental/analyze-nested-clips.py \
  artifacts/graphics-second --samples 200 > artifacts/graphics-second/summary.json
```

Each formal process uses the existing 1280×720 Rgba8Srgb target, DPR 1, editor-line-edit count 200, 619 commands and one layer. Five independent alternating process pairs use A/B, B/A, A/B, B/A, A/B; every process has 200 warmup and 200 measured frames. Product-default tiering/PGO/GC are preserved. A fixed 200-frame measurement budget is used for both variants, not extended after observing results. Every process must exceed ten seconds of measured frame time. Builds, tests and timed processes are serialized.

Prepare includes compilation, creation and mapped upload. Encode includes command creation, barriers, Record and Finish. TOP-to-BOTTOM GPU timestamps enclose upload/barrier/clear/render, excluding the initial target transition; this is elapsed device time, not exclusive occupancy. Submit-completion is CPU wall latency, not GPU duration. Full frame wall includes update/layout/record/prepare/encode, submission completion, query read and disposal; JSON serialization is outside timing. Text layout is nested inside update/layout, not additive. Process-wide allocation includes instrumentation/background allocation, not BDN bytes/op.

## Evidence and limitations

Raw JSONL, immutable executable copies, source/harness/asset hashes, build logs, device identity, TRX, the predeclared campaign script and archive checks are under ignored `artifacts/graphics-second/`. The primary files are `pair-{1..5}-{baseline,candidate}.jsonl`, `summary.json`, `campaign-plan.json` and `campaign-progress.log`; sustained controls are `control2d-pair-*` and `control2d-summary.json`. `archive-manifest.json` verifies raw length/SHA-256 and deterministic gzip SHA-256 for all 32 JSONL files (95,216,580 uncompressed bytes). `binaries.sha256`, `harness-assets.sha256`, source tar archives and `tests/{unit,gpu}-production.trx` bind the evidence to the frozen code. Generated measurements are not committed. The created device must identify Radeon 8060S vendor/device 1002:1586 and expose native timestamps in every process. Dynamic clocks and background activity are not controlled.

The bounded-depth runner owns independent target/command/prepared/submission/query resources per slot and includes final drain/retirement. These headless throughput diagnostics are not displayed FPS, Present, VSync, physical scanout or deadline qualification. They do not establish per-frame latency under overlap.

No A/A noise campaign or four-window warmup-stability qualification, public retained host integration, VRAM residency convergence, cold/burst distribution, full-update/effects matrix or all-frame pixel oracle is claimed. Final readback hashes check the final frame only. Independent unit/lifetime tests and Vulkan/wgpu pixel tests complement those hashes. This scoped improvement does not establish the proposed ADR performance budgets or make all editor workloads interactive.
