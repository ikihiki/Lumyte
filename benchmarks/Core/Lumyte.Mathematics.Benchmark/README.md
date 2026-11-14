# Mathematics benchmarks

Use the existing Core build target to enumerate cases or select a workload:

~~~sh
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-list true
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-mode screen --benchmark-filter '*GeometryQueries.SpatialBenchmarks*'
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-mode screen --benchmark-filter '*GeometryQueries.ShapeBenchmarks*'
~~~

Only measurement-purpose categories are used: `Screening`, `Comparison`, and `Diagnostic`. Select subjects structurally with `*GeometryQueries.*`, `*ColorOperations.*`, or `*InterpolationFunctions.*`; namespaces and classes describe functionality, not categories. These independent library operations do not represent an application frame.

Coverage includes interpolation and timing curves, 2D geometry and flattening, bounds, scalar/batch distance controls, color conversion/compositing/pixel packing and lookup, curved-shape containment, and 3D distance/projection/bounds/sphere/box queries. Shape queries deliberately combine interior, exact boundary and outside probes; spatial results have analytic oracles in the foundation verification tests.

This is representative family coverage, not every numeric type, generated color format, overload or malformed-input branch. No performance conclusion follows from the correctness tests or catalog enumeration alone.
