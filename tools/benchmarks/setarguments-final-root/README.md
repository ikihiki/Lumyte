# Final-root SetArguments GPU-completed benchmarks

Four cases only: two public API routes × Count 1 and 64. Vulkan only.
The exact same harness is intended for baseline `abba27197abf33cf84f8d06b0d3eb40a2d09fe3a` and the separately frozen dirty candidate from Lumyte-shader-transfer-pr. No algorithm replicas, source-conditional paths, direct root-encoder calls, ordinary-unmanaged rows, counters or custom timing engine.

## Boundary

Setup compiles real Slang to SPIR-V, creates VulkanDevice, real shader/compute pipeline, one Count-element output buffer and its argument table, disjoint GetElement references and explicit snapshots. Generated ReferenceRoot uses a private ResourcesArgs backing (uint Value + 16-byte ShaderReferenceSlot Output), 20 wire bytes and 32-byte native uniform capacity. Setup asserts captured bytes and reference identity/offset.

Each timed invocation creates a fresh command buffer, records a whole-output reuse barrier and one compute pass, binds pipeline/table, then performs Count SetArguments + Dispatch(1) pairs. One thread stores arguments.Value through arguments.Output. It ends, finishes, submits, waits for actual completion, and disposes submission/commands. No timed copy/readback. This is CPU wall latency through GPU completion, not GPU timestamp duration. Allocations are per whole invocation, not per Set.

* InterfaceSetDispatchComplete reuses one mutable class, sets Value = current invocation generation + i and Output = precomputed reference i. Distinct output values detect deferred mutable-root reuse.
* ExplicitSnapshotSetDispatchComplete reuses setup captures of distinct, permanently retained source objects (Value = 100 + i); capture/input creation is excluded, while backend-owned copying/packing is included.

The initial/reuse barrier covers previous ComputeShader read/write and Copy read, then ComputeShader read/write. Disjoint output elements avoid intra-pass writable aliasing. Host wait is not used as a substitute for the device-side write-after-write dependency. Managed validation remains disabled.

## Reproduce (coordinator-owned execution)

Copy this directory byte-identically into tools/benchmarks/setarguments-final-root in both frozen source trees. Activate the existing repository native dependency environment, select the verified hardware Vulkan ICD, and run serially:

```sh
h=tools/benchmarks/setarguments-final-root
dotnet build "$h/SetArgumentsFinalRoot.Benchmarks.csproj" -c Release -p:RunAnalyzersDuringBuild=false
dotnet "$h/bin/Release/net10.0/SetArgumentsFinalRoot.Benchmarks.dll" --verify
dotnet "$h/bin/Release/net10.0/SetArgumentsFinalRoot.Benchmarks.dll" --filter '*' --join --artifacts /absolute/run-artifacts
```

--verify invokes both actual benchmark methods for both Counts twice, each immediately followed by a separate untimed copy/readback submission checking every output. Repetitions also exercise compute-after-copy reuse. Verification never substitutes a different compute routine. Failure throws and returns a nonzero process exit.

The BDN 0.15.8 job explicitly uses Release, the repository explicit-project generator pattern, one launch, 5–10 warmups, 10–15 actual iterations, 200 ms iteration target, MemoryDiagnoser, Median/P95, full JSON and DontRemove outliers. Report percentiles as BDN iteration estimates, not individual dispatch tail latency. Freeze source/binary hashes and environment before independent alternating comparison processes; do not overlap builds/timing.

## Authoring checks

Only static source/project inspection is authorized in this worker. No build, native verification, timing or commit was run here. The coordinator must compile both source variants and run --verify before timing; static compatibility is not a claim of successful compilation or hardware execution.
