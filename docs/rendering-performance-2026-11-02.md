# Drawing2D performance investigation — 2026-11-02

## PR split and evidence provenance

This measurement-only PR does not include the production snapshot optimization. It builds and runs independently on main; results named `cpu-after` and `native-after` describe the optimization preserved in original investigation commit b3fafbacce78557144ad1640c7ec30896743b0ba, published separately as [PR #105](https://github.com/ikihiki/Lumyte/pull/105) on branch `perf/drawing2d-snapshot-arrays`. To reproduce those after results, combine that optimization with this benchmark branch; running this branch alone measures the baseline implementation. Original combined investigation commits are retained for provenance. No measured values were changed while splitting PRs. Machine hostname and checkout prefixes in captured logs are redacted; hardware, compiler/driver versions, timings and test outcomes are unchanged.

## Environment and reproducibility

Baseline revision b832114366c804c2e53526d6021206af814dc5db. Linux Ubuntu 26.04.1, kernel 7.0.0-42, Ryzen AI MAX+ 395 (16 cores/32 threads), Radeon 8060S Graphics (RADV STRIX_HALO), Mesa 26.0.8-1ubuntu0.3, Vulkan 1.4.335. SDK 10.0.401, .NET 10.0.12, Release. GPU commands explicitly select VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json; the parent verified the only exposed device is hardware Radeon, not lavapipe. Full environment/caps and existing PSO baseline are in local artifacts/rendering-performance/.

See [benchmark commands and stage contracts](../benchmarks/Lumyte.Graphics.Benchmarks/README.md). Measurement runs are sequential, not concurrent with builds/tests. No paid tools or extra benchmark frameworks were introduced. BenchmarkDotNet 0.15.8 is already used by Signals; the new project follows it. The sole infrastructure adaptation is an exact-project generator override because BDN solution traversal follows setup's mise trust symlinks and finds duplicate projects. Initial failed discovery runs contain no measurements and are excluded.

## Existing PSO baseline (not a new optimization)

The existing PipelineBenchmark renders a fullscreen triangle into an 8x8 target. Two warmup rounds, six measured rounds per cache mode, alternating order. Parent-owned raw data: pipeline-baseline.jsonl. Warm medians, cache on/off (ms): first Draw 0.02895/0.0269; next 200 Draw calls CPU recording 0.12095/0.6057 (~5.01x); Submit+Wait wall latency 1.29965/1.33335; pipeline disposal 0.00375/0.01725. This supports keeping the existing pipeline cache; it does not establish actual GPU execution speed or Drawing2D frame performance. First cold cache-on Draw was 5.0414ms; excluded from warm statistics.

## Measurement scope and limits

- BDN samples operation throughput in each measured iteration, reports per-operation means/median/p95 and managed allocations; its p95 is a percentile of iteration estimates, not individual frame-tail latency. Full JSON retains raw samples. Single launch, short iterations, normal power management: useful local evidence, not cross-machine guarantees.
- Native probe measures serialized frame latency to isolate CPU stages and preserve one-shot prepared-resource lifetimes. It **does not measure pipelined frame throughput**. Submit+completion includes queueing, execution and host wait; never label it GPU time. Cold means fresh renderer/resources per count, not fresh process/driver; first count also includes initial JIT/device warmup.
- Common graphics APIs expose no timestamp/query-pool mechanism. No broad API changes were added just for timing. perf is installed but perf stat fails with perf_event_paranoid=4; dotnet-trace and renderdoccmd are unavailable. No sampling-stack or GPU timestamp claim is made. Stage-isolation and allocation benchmarks are the profiling evidence here.
- Rectangle expected draws and upload size derive from implementation (ceil(count/1024), 96 bytes/rectangle without clips), not runtime counters; clear is an additional empty pass. Native allocations/VRAM and GPU utilization are not measured.
- PathsGradientClip measures recording with shared immutable geometry/paint, not complete fallback GPU rendering. FlattenAndStroke separately measures actual renderer CPU curve/stroke tessellation. Static text uses a real cached Parley layout; dynamic includes changing shaping/outlines and recording; font load is setup, not timed. BlurLayers is layer/effect **recording**, not effect execution/RenderGraph compile.
- Initial-pass omissions below were subsequently filled by the extended investigation at the end of this report. GPU timestamps and other backends remain unavailable/out of this Vulkan scope. Existing pixel regression suites cover semantics, not performance.

## Results and optimization

Initial isolated snapshot measurements showed 10,000 commands cost 41.764us median (p95 41.992us), allocating 452.17KiB; complete reused-paint recording cost 250.035us median (p95 262.959us), allocating 921.01KiB. At 1,024→1,025 snapshot allocation jumped 36.73→52.78KiB from List capacity doubling, independently of the GPU pool boundary. Code inspection confirmed eleven temporary typed Lists, growth arrays, and final ToArray copies on every snapshot. The candidate optimization counts command types into eleven stack counters, allocates exact immutable typed arrays, then fills them in painter order. It preserves the ordered copy/read-only wrapper, typed offsets, public APIs, and mixed/layer fallback behavior. This targets measured allocation pressure without changing shaders, clipping, or GPU lifetime semantics. Before/after results and validation follow below.

### Native stage baseline

Twenty warm frames per count; ms median (p95):

| Rects | CPU build | CPU prepare | CPU record+Finish | Submit+completion wall |
|---:|---:|---:|---:|---:|
|100|0.01455 (0.02071)|0.05555 (0.05831)|0.06870 (0.07882)|1.09190 (1.09637)|
|1000|0.12355 (0.32929)|0.32910 (1.05959)|0.07250 (0.08280)|1.09510 (1.10048)|
|1024|0.09440 (0.09936)|0.31910 (0.32607)|0.06810 (0.13262)|1.09450 (1.09901)|
|1025|0.10910 (0.19297)|0.36950 (0.54941)|0.10710 (0.14890)|1.10605 (1.13203)|
|10000|0.26740 (0.66922)|2.52590 (3.17692)|0.33575 (0.41540)|1.10275 (1.11598)|

The 10,000-rect CPU preparation is the largest measured CPU stage; it includes eligibility/geometry/color conversion, pooled upload mapping/copy, and binding setup, not merely a GPU call. The 1,025 boundary adds an expected second rectangle draw (1,000 and 1,024 have one); 10,000 has ten. Upload payloads are 9,600/96,000/98,304/98,400/960,000 bytes. These are inferred, not captured counters. First 100-rect cold-renderer prepare/record were 46.6172/79.8531ms. Sub-millisecond CPU timings in this asynchronous short run include JIT/tiering and scheduling noise (e.g. 1,000 prepare CV 306%, 1,024 record CV 194%); do not infer small optimization wins from these frames. BDN is the controlled CPU comparison. Raw native-before.jsonl and summary retain all samples/outliers.

Font: /usr/share/fonts/truetype/dejavu/DejaVuSans.ttf, SHA-256 b4c632e3cdf9acc7f28758fb5a323c8524d7fc6660d46904d9b6cbe2809c419c.

### CPU before/after (same BDN job)

Reused-paint rectangle recording median/p95 in microseconds and allocated KiB:

|Count|Before median / p95|After median / p95|Before / after KiB|
|---:|---:|---:|---:|
|100|1.231 / 1.239|1.017 / 1.034|9.40 / 6.95|
|1000|10.337 / 10.378|9.538 / 9.756|83.23 / 66.71|
|1024|10.975 / 11.008|10.062 / 10.229|84.83 / 68.30|
|1025|11.151 / 11.251|9.764 / 9.818|100.92 / 68.38|
|10000|250.035 / 262.959|104.707 / 111.029|921.01 / 664.37|

The large case is ~2.39x faster (-58.1% median), allocating 27.9% less. Exact arrays avoid the temporary 16,384-element reference array (large object heap) that the old 10,000-item List created; BDN Gen2 collection signal disappears in this workload. This is a CPU recording/allocation improvement, not a GPU-speed claim. Color convenience overload still costs ~2.7x reused paint at 10,000, so callers should retain immutable paints where practical rather than adding global color caches.

Tradeoff: the additional counting pass makes 1,000/1,024 shared-path recording ~9% slower (12.618→13.773us, 13.146→14.313us medians), though allocations drop ~20%; 10,000 paths improve 271.117→140.280us and ~28% fewer bytes. The change is kept for the broad allocation reduction and large-workload gain, with this small-workload regression explicitly recorded. More invasive command representation or memoized tessellation was not justified by the available evidence.

Isolated 10,000-command snapshot falls 41.764→18.001us median (p95 41.992→19.629us), 452.17→195.52KiB (-56.8%). Snapshot after-run stddev/mean is 5.60% at 10,000 versus 0.39% before; full recording CV is 3.25% after/2.16% before. These gains are substantially larger than observed noise; 1–3% differences elsewhere are not treated as wins. BDN's default upper-outlier filtering was retained; full JSON includes original measurements, with 10–12 retained estimates per case.

Additional after-change CPU results (median / p95; managed KiB): cached static text recording 0.6306 / 0.6348us, 2.94KiB; changing text layout+recording 33.132 / 33.478us, 77.15KiB. They use different strings and work contracts, so their ~53x ratio is not an equal-work optimization comparison. Actual curve flatten+round stroke costs 29.94 / 29.97us and 258.66KiB for one sheared cubic/polyline; this allocation-heavy geometry path warrants separate future profiling before a cache/API change. Ten blur-layer recording costs 2.871 / 2.911us, 23.45KiB; no blur executes in this timing.

Raw data is committed under [rendering-performance-data/2026-11-02](rendering-performance-data/2026-11-02/): CPU before/after full BDN JSON, environment, PSO baseline and native frame JSONL/summaries. Failed discovery runs remain local only and are excluded.

### Native after-change sanity check

After-change ms median/p95, same 20 warm frames:

|Rects|CPU build|CPU prepare|CPU record+Finish|Submit+completion wall|
|---:|---:|---:|---:|---:|
|100|0.02140 / 0.02470|0.10960 / 0.11952|0.11485 / 0.13282|1.11710 / 1.13309|
|1000|0.09745 / 0.19651|0.33255 / 1.07051|0.07235 / 0.08508|1.08920 / 1.09620|
|1024|0.07550 / 0.08031|0.32185 / 0.32768|0.06490 / 0.07325|1.08735 / 1.14778|
|1025|0.07940 / 0.08735|0.35905 / 0.38027|0.10585 / 0.12682|1.09150 / 1.09922|
|10000|0.18060 / 0.59005|2.10245 / 2.48364|0.28635 / 0.34410|1.08815 / 1.10121|

Managed bytes/frame median at 10,000 drops approximately 1,019,944→752,076. Preparation remains the largest CPU stage; GPU-facing unchanged work differs substantially between runs (even the small case regresses), confirming that short native runs are too noisy/tiering-sensitive for causal claims outside the BDN-isolated snapshot improvement. No GPU speedup or pipeline throughput improvement is asserted.

## Correctness verification

All commands source tools/setup/activate.sh first:

- dotnet build benchmarks/Lumyte.Graphics.Benchmarks -c Release --nologo — zero warnings/errors.
- dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release --nologo — 108 passed, zero skipped/failed. Includes six new snapshot tests (0/1/1024/1025/10000 copies/read-only storage and mixed runs), all-command typed dispatch, geometry and snapshot lifetime regressions.
- dotnet test tests/Graphics/Lumyte.Graphics.Text.Tests -c Release --nologo — 10 passed.
- dotnet test tests/Graphics/Lumyte.Graphics.RenderGraph.Tests -c Release --nologo — 77 passed.
- dotnet test tests/Graphics/Lumyte.Graphics.Text.Parley.Tests -c Release --nologo — 114 passed.
- VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --nologo --filter "FullyQualifiedName~Drawing2DTests.Vulkan" — 7 passed, zero skipped/failed, real Radeon. Covers geometry/clips, paints/composites, images/glyph representations, layers/effects/backdrop, standalone sRGB and rectangle batch pixel equality including 1,030 overlapping translucent rectangles crossing the chunk boundary.
- Both BDN runs completed: 20 before cases and 24 after cases, all measured; both native probes completed 130 rows each. Raw JSON parsing and git diff --check succeeded.

No shader/API semantics changed. No solution/build-runner registration is necessary for the standalone opt-in benchmark command. No generated .fallout/build.schema.json change is included.

## Extended investigation (same task)

Two independent native processes, DOTNET_TieredCompilation=0 for both (not compared directly against the earlier tiered runs), 50 initial cold/warmup frames then **100 measured frames per workload per resolution**, no outlier removal. Direct rendering retains its renderer; graph rendering intentionally recreates graph/renderer/resources each frame and measures actual append/compile/record/blur submission. 512² and 1536² targets change attachment/clear cost, **not logical content size**: fallback covers one repeated 30×30 curved clipped gradient, text remains ~26 Latin glyphs, blur ROI remains 200×200. These are representative integration workloads, not fullscreen fill-rate benchmarks. Both processes also measure six drained 100-frame throughput blocks at max depth 1 and 3 after one warmup block, with 10,000 rectangles in a reused recording. Each slot owns a separate target, command buffer, submission and PreparedDraw2D. Slot reuse waits for completion, then disposes submission/commands/prepared in order; final drain precedes renderer/target disposal. Thus depth 3 genuinely allows outstanding work without recycling in-use pool entries.

**Explicit acceptance of the path tradeoff:** retain exact-array snapshot optimization despite ~1.2us cost at 1,000 shared paths: (a) measured large 10,000-item recordings improve ~2×, not noise; (b) snapshot bytes drop across all measured sizes/types, avoiding LOH temporary growth; (c) the measured 100-command fallback already costs ~5ms prepare+record; comparing 1.2us against that is only a scale reference, not an equal-count end-to-end measurement. Adding alternate storage paths for this microsecond-scale regression would increase complexity before evidence that it affects application frame budgets. Small pure-recording clients may care; report preserves the regression rather than claiming universal speedup. Added a deterministic architecture-scaled allocation budget regression test for 10,000 snapshots (two reference arrays + int offsets, ≤10KB metadata slack), which the old ~463KB constructor would fail. No wall-time assertion is added to tests.

### Extended results

Run 2 warm ms median / p95 (run 1 retained independently in raw evidence):

|Workload / target|Build|Prepare/Append|Compile|Record+Finish|Submit+completion wall|
|---|---:|---:|---:|---:|---:|
|rect1000 / 512|0.0457 / 0.0471|0.0682 / 0.0731|—|0.0243 / 0.0272|1.0697 / 1.0733|
|fallback100 / 512|0.0114 / 0.0286|3.1810 / 4.6680|—|2.3790 / 4.0463|15.9665 / 18.1091|
|static-text / 512|0.0034 / 0.0089|0.6354 / 1.0256|—|0.4989 / 0.7692|3.2035 / 3.2221|
|dynamic-text / 512|0.0773 / 0.0932|0.7510 / 1.0197|—|0.6087 / 0.8283|3.2233 / 4.2819|
|mtsdf-text / 512|0.0031 / 0.0046|0.6497 / 0.9072|—|0.5893 / 0.8984|2.1512 / 2.1726|
|layers-blur / 512|—|5.2974 / 5.5314|0.0275 / 0.0321|0.3910 / 0.4625|1.1275 / 1.1340|
|rect1000 / 1536|0.0635 / 0.1846|0.0691 / 0.1996|—|0.0272 / 0.0849|1.0718 / 1.1272|
|fallback100 / 1536|0.0072 / 0.0214|2.8517 / 3.9394|—|2.2833 / 3.3834|17.0857 / 19.1140|
|static-text / 1536|0.0036 / 0.0059|0.5726 / 0.7563|—|0.4753 / 0.6986|3.2054 / 3.2815|
|dynamic-text / 1536|0.0603 / 0.0723|0.5946 / 0.7864|—|0.4924 / 0.6694|3.2085 / 3.2249|
|mtsdf-text / 1536|0.0035 / 0.0043|0.5585 / 0.7650|—|0.5060 / 0.6780|2.1493 / 2.1536|
|layers-blur / 1536|—|5.2546 / 5.6978|0.0273 / 0.0397|0.4170 / 0.5604|1.0943 / 1.1280|

Top three **measured CPU integration bottlenecks** (not sampling-stack attribution):

1. **Per-frame blur graph append/resource creation**: 5.25–5.45ms medians across both runs/resolutions, versus compile ~0.027–0.030ms. Append allocates ~9.07MB managed/frame and creates fresh renderer/effect resources in this integration path. This is an ownership/resource reuse investigation target, not evidence that RenderGraph topological compilation itself is slow. Retained-resource graph redesign is deliberately not slipped into this change.
2. **Fallback curved gradient+clip preparation**: 2.85–3.18ms median for 100 repeated commands (~3.73MB managed/frame). Source path/paint compilation, per-command geometry/clip preparation and upload/resource setup are included; exact sampled function ranking remains blocked by profiler permission. Even the entire snapshot regression is negligible against this work.
3. **Fallback command recording**: 2.28–2.47ms median, reflecting per-command bindings/draw/pass work in the existing general evaluator. This is ~85–100× the ~0.024–0.029ms fast rectangle recording despite 10× fewer commands, but workloads/coverage differ, so it is a dispatch/workload cost signal, not an equal-image speedup claim.

Vector text prepare/record remain ~0.57–0.81 / 0.48–0.68ms; changing layout adds ~0.06–0.09ms build. MTSDF reuses a genuinely generated/uploaded atlas, not fabricated glyph quads. Its CPU preparation/recording is not clearly cheaper than vector (same general per-glyph path), while submit-to-completion wall medians are ~2.15ms versus ~3.20ms vector. That is end-to-end submission latency evidence only, not a GPU-time claim. Atlas cold generation/storage creation was 90.49ms in run 1; 32×623 RGBA atlas (79,744 unpadded bytes), upload record 6.20ms and submit+completion 5.32ms; those are single cold observations, not distributions. Dynamic MTSDF regeneration is not measured: dynamic text case uses changing vector layouts; this avoids implying atlas rebuilding is free.

Resolution changes do not yield a credible general fill-rate conclusion: fallback completion increases ~15.97→17.09ms, while rectangle/text completion is largely unchanged and CPU text timing shifts reverse between runs. Frame CVs are retained (e.g. fallback prepare 17–19%, graph append 3–8%, small compile 23–52%); disabled tiering removes one confounder but not scheduling/power/driver noise. Dominant millisecond stage ordering repeats across processes; sub-0.1ms differences are not causal claims.

### Bounded frames-in-flight throughput

Reuse a 10,000-rectangle immutable tape, 800² attachments; preparation, record, submit, slot retirement/resource disposal and final drain included; tape building excluded. At depth 3 slots permit three independent outstanding submissions; only wait when recycling a slot or draining. No Present/swapchain or vsync.

|Run|Depth|100 frames ms median / p95|Median frames/s|Block CV|
|---:|---:|---:|---:|---:|
|1|1|217.7357 / 218.5894|459.3|0.33%|
|1|3|116.6999 / 121.4147|856.9|2.50%|
|2|1|218.4404 / 221.4864|457.8|2.11%|
|2|3|119.4252 / 122.7832|837.3|5.32%|

Depth 3 delivers ~1.83–1.87× the measured headless end-to-end throughput of depth 1 on this workload. This is an actual bounded pipeline throughput result, not serialized Submit+Wait mislabeled as throughput and not a promise of UI frame rate. The explicit resource owners stay alive across GPU work; memory scales with slots. Six measured blocks/process is modest; raw individual block values are committed.

Reproduction: source tools/setup/activate.sh; build the benchmark Release; run twice sequentially with VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json DOTNET_TieredCompilation=0 and --extended. Summarize with tools/benchmarks/summarize-extended.py. Each process completed **1,816 JSONL rows**: 1,800 frame stages, two atlas cold/upload records, fourteen throughput blocks. Both stderr files empty. Existing initial results use default tiering and are not mixed with these distributions.

Remaining explicit limitations: true GPU timestamps (common API unavailable; no broad query API added), VRAM/native allocation accounting and sampled CPU stacks (perf denied), presentation/interactive latency, full-content scaling rather than attachment-only resolution variation, dynamic MSDF atlas rebuilding and other backend comparisons. The requested representative Vulkan fallback/text/MTSDF/graph execution/compile/resolution/frames-in-flight cases are now measured, rather than deferred merely for boundedness.

Extended validation: benchmark build zero warnings/errors; Drawing2D suite rerun **109 passed** including new allocation guard; real-Radeon Vulkan Drawing2D pixel suite rerun **7 passed**, zero skipped/failed. Logs extended-tests-final.log and extended-vulkan-tests.log committed beside raw measurements. No production changes beyond the already measured snapshot implementation were introduced in this extension.
