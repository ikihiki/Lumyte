# Lumyte benchmarks

This project measures frequently executed, platform-independent paths:

- action value aggregation;
- interaction candidate resolution;
- state-machine transition selection;
- animation clip sampling;
- feature RenderGraph compilation, bounded schedule caching and bindings updates for reused plans.


Run a short local comparison with:

```powershell
dotnet run --project benchmarks/Lumyte.Benchmarks/Lumyte.Benchmarks.csproj -c Release -- --job short --filter "*"
```

Omit `--job short` for a longer measurement. BenchmarkDotNet writes reports
to `BenchmarkDotNet.Artifacts`, which is intentionally excluded from source
control because results depend on the runtime and hardware.
