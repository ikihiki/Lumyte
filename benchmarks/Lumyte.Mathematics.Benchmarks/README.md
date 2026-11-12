# Mathematics geometry benchmarks

Run from the repository root with the pinned SDK:

```sh
DOTNET_TieredCompilation=0 dotnet run --project benchmarks/Lumyte.Mathematics.Benchmarks -c Release
```

The transformed-bounds microbenchmark compares the former Drawing2D array-plus-LINQ implementation with `NumericsGeometry.GetTransformedBounds`. It checks matching results, warms both paths, alternates measurement order, and reports per-iteration elapsed time and current-thread allocated bytes. Run several independent processes without concurrent workloads. Timings describe this CPU operation only, not GPU rendering or frame rate. Save environment, raw output, and measurements under `artifacts/` or in the PR report, not in the ADR.
