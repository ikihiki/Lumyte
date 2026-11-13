# Component screening engine (step 1)

Advisory, CPU-only Signals microbenchmark screening, not an async UI component
benchmark or a full-suite qualification. The fixed base
70db1cd8144c22256505a433531efb0e37a4a7d0 has no benchmarks/Graphics suite.
Mathematics, Execution, native rendering, real async scheduling, cold startup,
and the other Signals workloads remain unmeasured. No native dependencies are
required by this harness.

## Commands

Run from the repository root with Python 3 and the exact SDK from global.json
available on PATH (or set DOTNET to the absolute dotnet executable). Prepared
base/head directories must have HEAD equal to the respective exact plan SHAs.
The directories can be the same for an A/A self-check. They are read only:
tracked source is archived into separate output/scratch/base and head copies.
Dirty and untracked product files are intentionally not measured.

~~~sh
python3 tools/benchmarks/component-screening/screen.py plan \
  --base BASE_SHA --head HEAD_SHA --output artifacts/component-plan

# Force the complete representative catalog for manual dispatch or A/A:
python3 tools/benchmarks/component-screening/screen.py plan \
  --base SHA --head SHA --all --output artifacts/component-plan-aa

python3 tools/benchmarks/component-screening/screen.py run \
  --plan artifacts/component-plan/plan.json \
  --base-dir /path/to/base --head-dir /path/to/head \
  --output artifacts/component-result --timeout-seconds 900

python3 -m unittest discover -s tools/benchmarks/component-screening/tests -v

dotnet build benchmarks/Core/Lumyte.Signals.Benchmark -c Release
dotnet benchmarks/Core/Lumyte.Signals.Benchmark/bin/Release/net10.0/Lumyte.Signals.Benchmark.dll --screening-smoke
~~~

Use a fresh run output directory on a filesystem with enough space. Both revisions
are built in Release and all 12 semantic smoke cases run on both before any timing.
Then BDN runs base followed by head, never concurrently. Do not run other workloads
or independent benchmarks during this interval. The total process deadline is
capped at 900 seconds even when a larger value is requested. Timeout kills the child
process group. Commands, raw partial BDN files, result.json and summary.md survive
ordinary errors/timeouts; filesystem exhaustion or forced host termination can
prevent writing final files. There are no automatic retries or weekly runs.

The plan command accepts --repo (default current directory), diffs the exact two
commits (not their merge base), and writes plan.json. GITHUB_OUTPUT, when set,
receives selected=true/false, base_sha, and head_sha. Docs-only and empty changes
skip and immediately produce a skipped summary.md/result.json. --all overrides
that skip. Signals or common build/dependency changes select all 12 cases; unknown
source changes select a single ReadValue sentinel and explicitly list uncovered
paths. Mixed changes preserve that uncovered list. Omissions and the cap are
recorded; no selection implies coverage of unrelated components.

## Frozen workload and job contract

Benchmarks use target-category layout, mirroring tests, with singular project
suffix: benchmarks/Core/Lumyte.Signals.Benchmark and
benchmarks/Core/Lumyte.Mathematics.Benchmark. Screening directly uses the existing
Signals ScalarBenchmarks, AsyncBenchmarks and GraphBenchmarks; there is no generic
screening project or duplicated Sources directory. Workload methods and lifecycle
remain unchanged from the fixed base except for the singular namespace.

The runner overlays the **whole same Signals.Benchmark project** from its own
checkout into both archived product revisions, discarding any archived target
harness first. It never uses each revision's changing benchmark implementation.
The ../../../src project reference resolves in both old-layout base archives and
new-layout revisions. BenchmarkDotNet remains 0.15.8. --screening explicitly selects
the short SignalsScreening-v2 job; --screening-smoke verifies all 12 cases without
timing. Without either flag the normal Signals job and --retention operation are
preserved. Harness version component-screening-v2, case version 2, singular case
identities and the v2 smoke marker reject old plans/results. Regenerate plans when
changing a workload or comparison policy.

The separate screening job uses 1 launch, 3 warmups, 8 measured iterations,
150 ms target, MemoryDiagnoser, full JSON, and OutlierMode.DontRemove.
Parser validation rejects missing/invalid/NaN data, missing allocations, unexpected
or duplicate identities, fewer than 8 retained results, job/parameter mismatch,
and incompatible runtime/CPU/SDK identities. Different global.json SDK policies,
unavailable exact SDK, or plan/harness mismatches are incomparable, never a pass.

The catalog is 12 unparameterized existing methods. Absolute latency floors in ns:

- Scalar ReadValue 1, ReadPeek 1, WriteSame 1, WriteUnobserved 2.
- Async CreateCompleted 20, ReloadCompleted 20, ReloadCompletedObserved 30,
  SupersedeAndCancelPending 100.
- Graph DiamondUpdate 20, SwitchBranch 20, CaptureEightReads 20, CheckReadSet 5.

A latency candidate requires median increase >=10% **and** its absolute floor.
These are pragmatic screening floors, not calibrated noise bounds or formal
statistical significance. Every allocation increase is separately flagged;
zero-base allocation increases have a null percentage and explicit bytes/op.
Candidates exit zero (advisory); execution, unsupported, incomparable, invalid or
missing data exits nonzero. no_candidates means only no candidate in these cases,
not general performance health. Unknown changed paths add `partial_coverage` to the
candidate/no-candidate status and remain explicit in the summary.

## Evidence and CI integration

The result includes exact SHAs and plan, case/harness versions and hash, runner
hash, CPU/OS, SDK and runtime dotnet --info, timing-related DOTNET_/COMPlus_
environment, and SHA-256 hashes of built harness/product/dependency DLLs.
BDN full JSON adds actual runtime/host identity and all iteration measurements.
Archive result.json, summary.md, *.log and raw/ even after a nonzero exit; scratch/
is useful for local diagnosis but need not be uploaded. Plan and raw paths are
local evidence, not trusted input to privileged workflows. GitHub workflow summary
rendering and any notification are owned by the surrounding CI workflow.

The original v1 exact-base A/A integration check completed all 12 cases on both sides
with the frozen harness, exact SDK 10.0.403, and 8 retained measurements per case.
It produced no candidates and identical allocation values. This verifies the
pipeline, not stability or a product improvement. Generated local evidence is
ignored under artifacts/component-screening-aa/ and is not committed.

The v2 target-project integration A/A check also completed against exact product
SHA 70db1cd8144c22256505a433531efb0e37a4a7d0 on both sides, using SDK 10.0.403.
All 12 singular case identities had 8 retained measurements per side, with
identical allocations and no candidates (91.46 seconds for the full engine).
Both semantic smokes passed before timing. Evidence is ignored under
artifacts/naming-integration/aa-result/. This is pipeline wiring verification,
not calibrated noise or evidence of product improvement; Mathematics is built
but is not measured by this Signals-only screening catalog.
