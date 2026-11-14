# Mathematics benchmarks

~~~sh
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-list true
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Mathematics --benchmark-mode screen --benchmark-filter '*BoundsBenchmarks*'
~~~
