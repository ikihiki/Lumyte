# Retained owner ledger: local CPU comparison

## Scope and decision

The local implementation is commit e3e92eca2dba0cb078fc5ac626527cef7acaa1cc on perf/incremental-owner-ledger. It stacks on published PR #143 head 0ac47b4445a04b6960e737b323eab6ed498e34cc, because #143 was still OPEN at live inspection. The separately recorded main was 762f4b7c23e553cf6774b1e489cfdd0ee0be1e6a. No prior PR or worktree was edited, and nothing was pushed or published.

**Result:** the stable built-in statistics read is constant-cost at about 0.030 µs, with zero measured allocation. At 26 assets the old traversal took about 0.606 µs and allocated 1,040 B/read. Complete CPU frames with one/two statistics reads improved in both ABBA pairs. With no statistics reads, cached-frame medians are effectively unchanged: +0.036 µs (+0.38%) with opposite-signed paired deltas. This is not an unconditional frame-speed improvement.

The no-read dynamic result is only a small observed reduction (-0.448 µs, -2.73%) amidst large process variation; it is not strong qualification. In particular A1/B1 dynamic-zero-read iteration ranges extend to about 28/26 µs, versus about 16/14 µs in A2/B2. Cached one-read B2 p95 is worse than its paired baseline despite its lower median. Preserve these tails and variation rather than retrying selectively.

## Method

The existing standard Drawing2D BenchmarkDotNet 0.15.8 runner and managed CpuGraphicsDevice were used unchanged, with two added public-API suites copied identically to baseline and candidate. The read suite primes actual retained resources and reads statistics once for 1/26 assets. The frame suite uses 26 real retained placements; a dynamic invocation changes one placement's content, then prepares, records, simulates submit/completion, disposes commands and retires. Zero/one/two public statistics reads are included inside that frame boundary. Thus attachment, detachment, pool metadata and owner-size maintenance are charged, not hidden behind read-only microbenchmarks. Setup/initial resident construction is outside timing.

The fixed order was A1, B1, B2, A2. Each invocation ran all eight cases in independent child processes, with MemoryDiagnoser, three warmups and ten measured iterations, standard automatic pilot/default 500 ms iteration target, OutlierMode.DontRemove, median and full JSON exports. Each invocation was bounded to 420 seconds; all completed (128.39/147.61/123.62/127.58 seconds). All 32 cases have ten measured iteration estimates. Percentiles below are of BDN iteration estimates, **not individual-frame latency p95**. Only two process repetitions per variant were collected; shared-host variation remains visible and this is bounded local evidence, not the category-wide stability gate.

The eight-case source and product implementation stayed frozen throughout. Root source hashes match before/after; actual generated child-driver and child product DLL hashes match A1/A2 and B1/B2 independently. The generated driver is Lumyte.Graphics.Drawing2D.Benchmark-Core-compare-1.dll, not the launcher. The child product DLL hashes are 123a83a7f18168273ea90cac596fdf58160131a9b9647d5154f13b53622ccbb8 (A) and 900116bfef3e402697627f01473cb13b9e8d8504abb5b3e589137ed828ae08b7 (B). Full launcher, child and dependency hashes and executed child paths are retained separately.

Host: AMD Ryzen AI MAX+ 395, Linux x64, .NET SDK 10.0.403/runtime 10.0.14, Release, Slang 2026.19. The GPU correctness adapter was verified Radeon 8060S / RADV Mesa 26.0.8; CPU timing does not use GPU execution. No power/affinity configuration was changed and no native-driver or UI frame throughput claim is made.

## Results

The summary uses the median of the two process medians per variant (with two samples, their midpoint). Every individual process, iteration estimate, p95 and allocation is in the full exports and process-results.csv below. Lower time is better; a positive change is a regression.

| Case | A µs | B µs | Δ µs | Δ % | A → B B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Frame(StatisticsReads: 0, Dynamic: False) | 9.48687 | 9.52328 | +0.03641 | +0.38 | 34,688 → 30,560 |
| Frame(StatisticsReads: 0, Dynamic: True) | 16.42084 | 15.97275 | -0.44809 | -2.73 | 50,233 → 45,321 |
| Frame(StatisticsReads: 1, Dynamic: False) | 10.67044 | 9.94047 | -0.72997 | -6.84 | 35,729 → 30,560 |
| Frame(StatisticsReads: 1, Dynamic: True) | 15.15763 | 13.90975 | -1.24788 | -8.23 | 51,273 → 45,321 |
| Frame(StatisticsReads: 2, Dynamic: False) | 10.94529 | 9.68772 | -1.25757 | -11.49 | 36,769 → 30,560 |
| Frame(StatisticsReads: 2, Dynamic: True) | 16.37863 | 13.55824 | -2.82039 | -17.22 | 52,313 → 45,321 |
| Read(NodeCount: 1) | 0.05050 | 0.03030 | -0.02019 | -39.99 | 40 → 0 |
| Read(NodeCount: 26) | 0.60561 | 0.03065 | -0.57495 | -94.94 | 1,040 → 0 |

### Every independent process (median / p95, µs)

| Case | A1 | B1 | B2 | A2 |
| --- | ---: | ---: | ---: | ---: |
| Frame(StatisticsReads: 0, Dynamic: False) | 9.17380 / 9.91178 | 9.39142 / 11.27452 | 9.65513 / 10.32648 | 9.79993 / 9.98604 |
| Frame(StatisticsReads: 0, Dynamic: True) | 18.45371 / 27.91107 | 17.91272 / 25.72332 | 14.03278 / 14.37423 | 14.38796 / 15.92691 |
| Frame(StatisticsReads: 1, Dynamic: False) | 11.43744 / 15.82095 | 10.16522 / 13.02489 | 9.71572 / 17.47869 | 9.90343 / 11.06925 |
| Frame(StatisticsReads: 1, Dynamic: True) | 16.27611 / 16.40278 | 14.76267 / 16.00951 | 13.05684 / 13.25417 | 14.03915 / 14.36689 |
| Frame(StatisticsReads: 2, Dynamic: False) | 11.03529 / 12.34835 | 10.42873 / 12.19480 | 8.94671 / 9.39669 | 10.85529 / 11.96689 |
| Frame(StatisticsReads: 2, Dynamic: True) | 17.04997 / 17.71910 | 13.76365 / 13.88637 | 13.35283 / 15.32296 | 15.70728 / 16.19656 |
| Read(NodeCount: 1) | 0.05055 / 0.05072 | 0.03027 / 0.03177 | 0.03033 / 0.03050 | 0.05044 / 0.05058 |
| Read(NodeCount: 26) | 0.64209 / 1.41685 | 0.03092 / 0.03288 | 0.03038 / 0.03112 | 0.56913 / 0.84619 |

The CSV and summary JSON also include every process min/max, paired absolute deltas and between-process spreads. All original iteration values and BDN warnings remain in the JSON/logs.

## Architecture and remaining costs

See [ADR-GRAPHICS-0037](../adr/graphics/GRAPHICS-0037-incremental-retained-owner-accounting.md). Identity-keyed entries retain separate active/idle counts and Int128 totals. Per-asset deduplicated owner/charge arrays are collected once; returning active assets to idle updates only affected owners. Entry metadata uses the existing Microsoft.Extensions.ObjectPool 10.0.0, capped at 256 idle entries globally. Return clears owner references and counters; GPU resources are never returned through this pool. There are no reissued numeric IDs or retained references to recycled entries.

- Steady sealed built-in statistics reads: O(1), including a live idle NumericPool byte getter.
- Collecting/opaque plans: O(distinct variable plans), each updated centrally, until direct materialization proves its statistics immutable. The existing conservative private charge does not change during later pack/upload/CPU-data release; those stages are tested against a fresh union. No mutable size is cached merely by assumption.
- Non-notifying custom memory owners: the existing full-graph fallback remains, preserving mutable identities/charges, first-contribution order, reentrancy and exception cleanup. This path is not O(1) and was not separately performance-measured.
- Attach/detach/scope moves: O(affected asset owner count); initial snapshot building visits that asset's batches/pages. LRU lookup/list movement remains unchanged and is not O(1).

Measured B/op is allocation traffic, **not retained resident memory**. Added metadata includes one exact-count KeyValuePair<object,long> array per stable asset, a live identity dictionary and pooled entries. On the ordinary CLR x64 layout a three-owner snapshot has a 48 B element payload plus its array header; an entry has one object reference, one long and two int fields. At most 256 idle entry objects are retained by the global pool, plus pool bookkeeping; this limit does not cap live owners. Empty ledger dictionary high-water capacity above 4,096 is trimmed. This metadata is not included in the existing drawing-resource CachedBytes statistic and does not replace MaxCachedBytes/MaxCachedAssets. Exact retained managed bytes, initial cold pool construction and first-prepare time were not measured. The warmed frame/read allocation reductions must not be presented as a measured resident-memory reduction.

## Correctness

All original assertions remain. New tests cover one-time stable enumeration, shared/duplicate pages, active+idle union, deterministic seeded attach/detach oracle, saturation recovery, pool reuse, wrong-scope detach, changing shared plan publication, actual built-in packing/pool returns and transactional/reentrant insertion failures.

- Published #143 baseline Drawing2D Release: 403 passed, zero failed/skipped.
- Candidate Drawing2D Release: 411 passed, zero failed/skipped.
- Candidate cache tests Debug: 26 passed, zero failed/skipped.
- RenderGraph: 141 passed; Resources.Graphics: 48 passed; UI: 404 passed; zero failed/skipped.
- UI.Application: initially 115 passed/3 GPU-skipped, then GPU-enabled rerun: 118 passed, zero failed/skipped.
- Radeon Drawing2D + RectanglePool GPU subset, both available backends: 38 passed, zero failed/skipped, including shared retained depth-one/three, reverse retirement and analytic pixel checks. This is the selected subset, not all graphics tests.
- An independent lifetime/accounting review found a fault-injected insertion exception issue; it was fixed before source freeze. The isolated pre-fix insertion control fails its new regression; disabling the stable fast path fails the one-collection guard. Final tests pass. See review-resolution.md.

One final parallel build attempt failed with MSB4166 child-node exits; the indicated temporary diagnostic directory was already unavailable. A single-node build of the unchanged source succeeded with zero warnings/errors. Both the failed log and successful retry are preserved; this was before timing, not omitted benchmark data.

## Reproduction and raw evidence

[Evidence directory](data/incremental-owner-ledger-20261116/) contains run-abba.sh, analyze.py, frozen source/host provenance, full BDN JSON/logs, individual process CSV/summary JSON, child hashes and correctness/negative-control logs. The script names the two isolated HOME worktrees explicitly; recreate the baseline at 0ac47b44, candidate at e3e92eca, and copy the two OwnerAccounting benchmark files identically before building both in Release. Source the repository-supported tools/setup/activate.sh with the recorded LUMYTE_ENV_ROOT. Run the script, then analyze.py. Build/test/native operations must not overlap timed repetitions.

No new external benchmark framework, service or paid dependency was introduced. Cold setup, precise resident managed-memory measurement, arbitrary custom-renderer performance, native-driver CPU effects, GPU throughput and application-level speedups remain unmeasured. Results are local and unpushed.
