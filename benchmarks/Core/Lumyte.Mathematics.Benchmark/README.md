# Mathematics benchmarks

Use the existing Core build target to enumerate cases or select a workload:

~~~sh
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-list true
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-mode screen --benchmark-filter '*SpatialBenchmarks*'
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-mode screen --benchmark-filter '*ShapeBenchmarks*'
~~~

All mathematics cases are categorized `Standalone` (no DI or integration fixture). Most are `SteadyState`; `GeometryBenchmarks.TimingConstruct` is `Construction`. Standalone does not mean a case represents an application frame.

Coverage includes interpolation and timing curves, 2D geometry and flattening, bounds, scalar/batch distance controls, color conversion/compositing/pixel packing and lookup, curved-shape containment, and 3D distance/projection/bounds/sphere/box queries. Shape queries deliberately combine interior, exact boundary and outside probes; spatial results have analytic oracles in `StandaloneFoundationTests`.

This is representative family coverage, not every numeric type, generated color format, overload or malformed-input branch. No performance conclusion follows from the correctness tests or catalog enumeration alone.
