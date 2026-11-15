# Shader upload strategies (frozen common harness)

## ARCHIVED — historical reproducer, not a current-HEAD build target

This frozen harness records the API and timed workload used by its original
report. It intentionally uses APIs removed by the cursor-only shader transfer
contract (including snapshot root selection where applicable). **Do not build it
against current HEAD**, migrate its timed C# workload, or relabel its results as
measurements of the current implementation.

For reproduction, check out frozen revision `abba27197abf33cf84f8d06b0d3eb40a2d09fe3a`
in a separate worktree and use the exact baseline/candidate source archives and
hash manifest linked by the original report. A dirty candidate must be restored
from that archive, not reconstructed from current HEAD. Instructions below apply
only to those historical source trees; the old workload and measurements remain
unchanged as provenance.

Copy this directory unchanged into each candidate tree at the same relative path.
Both candidates require the new reference-only IShaderDataSource API. The pilot
selected one helper branch with a buffered flag. The final direct candidate removes
that branch and CPU scratch entirely; the experimental buffered candidate retains
its scratch-aware helper/writer. Restore the exact final source variants from the
report manifest rather than toggling a flag in final production. This harness has
no algorithm selection, adapter or replicated production algorithm.

## Cases and boundaries

Six BDN cases: Count 1, 64, 1024 for each method.

- CpuBenchmarks.MappedReferenceWrite: setup first finishes GPU verification, then
  maps once. Each invocation mutates the existing ReferenceData objects (all 16
  matrix entries, scalar, float3 and two preallocated real references), calls actual
  typed upload CopyFrom, and returns one terminal ReadReferences(...).Values.Count.
  There is no per-element metadata read or map/unmap/command submission inside
  this CPU boundary. Cleanup unmaps before disposal.
- GpuBenchmarks.ReferenceWriteCopyComplete: the same mutations, actual
  map/CopyFrom/unmap, fresh copy commands with barriers, finish, submit, awaited
  completion, and submission/command retirement. Buffers/device are reused.
  This is completed host wall latency, NOT GPU timestamp time.

References target real argument-table buffers. Input targets contain 1000+i;
reference indices rotate deterministically each call (Count 1 necessarily retains
its sole reference identity). Output and Results use ReadWrite registrations,
ShaderRead|ShaderWrite allocation flags and read/write barrier access bits.
No snapshots or numeric getters are used to observe uploaded reference data.
SetArguments remains the normal root-argument value API.

## Verification and runner

From the tree root, after the parent-controlled build:

    dotnet tools/benchmarks/shader-upload-strategies/bin/Release/net10.0/ShaderUploadStrategies.Benchmarks.dll --verify
    dotnet tools/benchmarks/shader-upload-strategies/bin/Release/net10.0/ShaderUploadStrategies.Benchmarks.dll --filter '*'

Set the parent-selected Vulkan hardware driver and native dependency paths.
--verify exercises both exact benchmark methods for every Count and checks GPU
compute/readback for all matrix entries, mixed scalar/vector, numeric and float3
array fields, real reference input/output and reference-only metadata (exactly two
uint identities at wire offsets 80/96) on upload and transferred storage. The CPU
write is checked without replacing its upload contents; the GPU write is checked
without re-copying its reference storage. Verification is untimed.

BDN 0.15.8: explicit matching csproj generator, MemoryDiagnoser, median/p95,
full JSON, one launch, 5..10 warmups, 10..15 actual iterations, 200 ms iteration
request, retained outliers. Percentiles describe iteration estimates, not individual
GPU operation latency. Parent owns build, hardware execution, ABBA and analysis.
The Japanese strategy report records completed verification, ABBA measurements,
source/binary hashes, limitations and the source-complete compressed candidates.
