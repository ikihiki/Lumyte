# Wire cursor and metadata: frozen common harness

## ARCHIVED — historical reproducer, not a current-HEAD build target

This frozen harness records the API and timed workload used by its original
report. It intentionally uses APIs removed by the cursor-only shader transfer
contract (including snapshot root selection where applicable). **Do not build it
against current HEAD**, migrate its timed C# workload, or relabel its results as
measurements of the current implementation.

For reproduction, check out frozen revision `7e89cd7e8cecd2a4a507758253b934c95b612cef`
in a separate worktree and use the exact baseline/candidate source archives and
hash manifest linked by the original report. A dirty candidate must be restored
from that archive, not reconstructed from current HEAD. Instructions below apply
only to those historical source trees; the old workload and measurements remain
unchanged as provenance.

One BenchmarkDotNet 0.15.8 project, exactly eight cases: Count 1 and 64 for
FinalRootBenchmarks.InterfaceSetDispatchComplete,
FinalRootBenchmarks.ExplicitSnapshotSetDispatchComplete,
CpuBenchmarks.MappedReferenceWrite, and
GpuBenchmarks.ReferenceWriteCopyComplete. Count 1024 is deliberately excluded.
The coordinator owns serial A/B/B/A execution (8 cases × 4 processes = 32 raw
case results), builds, hardware selection, source freezing and analysis.

## Source provenance (not historical result relabeling)

Root fixture/types and entry points are copied from

tools/benchmarks/setarguments-final-root at
7e89cd7e8cecd2a4a507758253b934c95b612cef; upload fixture/types are copied from

tools/benchmarks/shader-upload-strategies introduced at
abba27197abf33cf84f8d06b0d3eb40a2d09fe3a and present unchanged at 7e89cd7.
Their source namespaces remain separate. Those original directories and reports
are not edited, and their measurements do not become measurements of this harness.
This harness compares baseline 7e89cd7e8cecd2a4a507758253b934c95b612cef with
the coordinator-frozen dirty original candidate. Copy this directory byte-identically
into both trees. No production algorithm, metadata owner, cursor or encoder is
replicated here. Real public Device.Create APIs and generated shader data/classes
exercise each tree's actual implementation. Generated ReferenceRoot uses its
private ResourcesArgs backing, uint Value plus 16-byte output slot: 20 wire bytes
and 32-byte native uniform capacity, asserted in setup for both trees.

## Timed boundaries

- Interface root: reuse one mutable generated class, update scalar/reference per
  selection, fresh command buffer, explicit output reuse barrier, compute pass,
  Count SetArguments + Dispatch(1), finish/submit/await completion/retirement.
  Every output has a distinct expected scalar; no timed readback.
- Explicit snapshot root: identical command/completion boundary, but setup-captured
  distinct permanently retained providers (100+i) and their snapshots are reused.
  Capture/input creation is excluded; real backend copying/packing is included.
- CPU upload: setup persistently maps the real typed upload allocation. Each call
  updates existing generated class instances (all 16 matrix entries, scalar,
  float3 and two preallocated real GPU references), then actual CopyFrom. Return
  the source array count AFTER CopyFrom. No metadata getter, reflection, map/unmap,
  readback, command submission or algorithm surrogate occurs inside this method.
  This intentionally differs from the prior upload harness's terminal metadata
  count read; do not compare its old timings as if the boundary were unchanged.
- Completed upload: identical reference-data updates, real map/CopyFrom/unmap,
  fresh copy commands and barriers, finish/submit/wait/retirement. Buffers and
  device are reused. There is no timed verification dispatch/readback.

GPU-involving rows are host wall latency through actual completion, not GPU
timestamps. Allocations are per entire invocation. Count dispatches and Count
uploaded elements are different workload units; do not pool them.

## Equivalent obligations across the API change

Baseline stores common immutable ShaderValue fixups; candidate exposes
IShaderReferenceMetadata and backend-private ordered ID ownership. Those are
part of the WHOLE production change being measured, not normalized-away adapters.
The common fixture preserves identical source classes, inputs, reference rotation,
real registered read-only/read-write resources, commands and output oracles.
Only untimed metadata verification adapts to the API: ReadReferences uses var;
reflection on object selects References (new) or Values (legacy). Both must retain
exactly the same two ordered `IGpuRef<uint>`/IShaderReference identities and access
modes on upload and copied storage. Legacy also asserts uint type and offsets
80/96. New IDs deliberately have no public wire offsets: the real GPU field and
resource read/write oracle checks their actual fixup consequence instead.
No static Values access on the ReadReferences return type and no per-call timed
reflection are used. Explicit root snapshots retain their common Values API.

## Verification and reproduction (coordinator only)

After activating repository native dependencies and the verified hardware Vulkan
ICD, use each frozen tree serially:

    dotnet build tools/benchmarks/wire-cursor-metadata/CursorMetadata.Benchmarks.csproj -c Release -p:RunAnalyzersDuringBuild=false
    dotnet tools/benchmarks/wire-cursor-metadata/bin/Release/net10.0/CursorMetadata.Benchmarks.dll --verify
    dotnet tools/benchmarks/wire-cursor-metadata/bin/Release/net10.0/CursorMetadata.Benchmarks.dll --filter '*' --join --artifacts /absolute/run-artifacts

--verify calls all eight ACTUAL benchmark entry points twice (16 calls), with
separate untimed GPU compute/copy/readback verification after each call. CPU
verification unmaps without overwriting the last upload, copies and verifies it,
then remaps. GPU upload verification preserves the already-copied storage and
does not substitute another upload/copy. Root verification copies the actual
preceding output without another compute implementation. Deterministic expected
values are checked each repeat; root interface generation advances, snapshots
remain fixed. All numeric fields, all 16 matrix fields, scalar/vector, twelve-byte
float3 array stride, reference input IDs/ranges, writable target results, and
exact two-ID metadata are retained. Failure throws (nonzero exit).

The matching explicit project generator names CursorMetadata.Benchmarks.csproj;
all five production project references use ../../../src. BDN job explicitly uses
Release, one launch, 5–10 warmups, 10–15 actual iterations, 200 ms target,
MemoryDiagnoser, median/P95, full JSON, and DontRemove outliers. Percentiles are
BDN iteration estimates, not individual GPU operation tail latencies.
Freeze source/harness/binary hashes before the serial campaign.

## Authoring validation boundary

This worker performed source/project inspection and whitespace checks only;
no build, hardware verification, timing, commit or production edit is authorized.
Compilation and --verify on BOTH frozen source variants are required coordinator
gates before timing. No claim of successful compilation or GPU execution is made.
