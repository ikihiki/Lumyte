# Backend-native writer comparison

Archived public-API-only BenchmarkDotNet 0.15.8 comparison: A=f27e4b29d02a7e5d78c37b91dc94d33881264e2b → B=3582af8ada8c00ff38553eebc216bc64527171c1. B is the frozen backend-native-writer checkpoint with uniform roots, not latest HEAD. The newer Vulkan push-constant / D3D12 root-constant implementation is unmeasured; do not apply the archived timing/allocation numbers to it. Harness source, scripts and measurement hashes are unchanged. See the [pinned report](../../../docs/performance/backend-native-writers.md).

## Cases

* SetRecordCpu, Count=1/64: update generated mutable whole-wire Inputs (48 bytes, two opaque references, four literal uint fields), create a fresh native command owner, begin compute pass, set pipeline/table, SetArguments Count times, end pass, Dispose **unsubmitted Recording** command buffer. No Finish, Dispatch, Submit or wait. The Vulkan implementation permits disposal after ending its pass; this discards every invocation's retained native argument owners instead of letting them accumulate.
* CopyFromCpu: 64 generated Inputs objects, two references each, same native allocation, rotate reference identities and update numeric input each invocation. Persistent mapped typed upload established in GlobalSetup; timed boundary includes input updates and real public CopyFrom, not Map/Unmap or shader dispatch.
* FullCompletion, Count=1/64: update inputs; create commands; output barrier; set pipeline/table; Count SetArguments+Dispatch pairs; output/readback barriers and CopyBuffer; Finish; Submit; await actual submission WaitAsync (native fence completion route; this revision uses ValueTask); dispose completed submission, then commands. CPU wall latency, not device timestamp. Readback transfer is included, but mapping/assertion is untimed.

GlobalSetup compiles the real Slang kernel and validates fixed expected GPU output once. No handwritten packing/field-replica benchmark. CopyFrom verification checks public reference metadata outside timing. Dependency-owner/List allocations can remain: no zero-allocation claim.

## Preparation and execution

Use an external copy of these source files, identical hashes for both roots, separate obj/bin. ProjectReference supports LumyteProjectRoot (7 regular projects plus generator analyzer). Required shader compiler and native paths come from tools/setup/activate.sh with the existing LUMYTE_ENV_ROOT. Do not run timing during implementation.

To reproduce the archived comparison, use owned baseline f27e4b29d02a7e5d78c37b91dc94d33881264e2b and candidate 3582af8ada8c00ff38553eebc216bc64527171c1 worktrees, not latest HEAD; freeze both and stop concurrent builds/tests, then:

    export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
    BACKEND_WRITERS_FINAL_FROZEN=yes bash tools/benchmarks/backend-native-writers/run-comparison.sh BASELINE_ROOT CANDIDATE_ROOT LOCAL_ARTIFACTS

Runner verifies Radeon 8060S via VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json and vulkaninfo --summary, builds/verifies both variants before timing, then runs serialized fresh BDN processes A1 B1 B2 A2. Job: Release, launch=1, warmup=2, measured iterations=5, target iteration=100ms, DontRemove outliers, MemoryDiagnoser, median/P95, CSV/default exporters and full JSON. Percentiles describe iteration estimates, not individual dispatch latency.

Do not add binaries, compressed archives or raw BDN results to git. Keep raw results and hashes local; publish only text report/summary CSV with exact source hashes/runtime/CPU/driver, all four process results and regressions. A source-only preparation is not measured performance evidence. Check all five cases succeed per run and that hashes/outputs remain stable. Bounds are short iteration targets, not a hard total time cap (BDN pilot/build/diagnostic overhead remains).
