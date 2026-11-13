# Mathematics geometry benchmarks

Run from the repository root with the pinned SDK:

```sh
DOTNET_TieredCompilation=0 dotnet run --project benchmarks/Core/Lumyte.Mathematics.Benchmark -c Release
```

The transformed-bounds microbenchmark compares the former Drawing2D array-plus-LINQ implementation with `Geometry2D.GetTransformedBoundsBcl`. It checks matching results, warms both paths, alternates measurement order, and reports per-iteration elapsed time and current-thread allocated bytes. Run several independent processes without concurrent workloads. Timings describe this CPU operation only, not GPU rendering or frame rate. Save environment, raw output, and measurements under `artifacts/` or in the PR report, not in the ADR.

## Batch SIMD

```sh
DOTNET_TieredCompilation=0 dotnet run --project benchmarks/Core/Lumyte.Mathematics.Benchmark -c Release -- --batch
DOTNET_EnableHWIntrinsic=0 dotnet test tests/Core/Lumyte.Mathematics.Tests -c Release --filter FullyQualifiedName~GeometryBatchTests
DOTNET_EnableAVX=0 dotnet test tests/Core/Lumyte.Mathematics.Tests -c Release --filter FullyQualifiedName~GeometryBatchTests
```

The batch benchmark compares identical validated scalar and vectorized coordinate-span squared-distance operations, including tiny batches, tails, matching checksums and allocation counters. It reports Vector128/Vector256 acceleration rather than assuming instruction-set support. Keep measurements and environmental provenance under `artifacts/` or in the PR report.
