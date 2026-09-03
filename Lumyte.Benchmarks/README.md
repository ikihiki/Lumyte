# Lumyte benchmarks

This project measures frequently executed, platform-independent paths:

- action value aggregation;
- interaction candidate resolution;
- state-machine transition selection;
- animation clip sampling.
- RenderGraph construction, compilation, cache, contributor registration, and CPU-only recording.

Run a short local comparison with:

```powershell
dotnet run --project Lumyte.Benchmarks/Lumyte.Benchmarks.csproj -c Release -- --job short --filter "*"
```

Omit `--job short` for a longer measurement. BenchmarkDotNet writes reports
to `BenchmarkDotNet.Artifacts`, which is intentionally excluded from source
control because results depend on the runtime and hardware.

## RenderGraph optimization baseline

The RenderGraph benchmarks are CPU-only. `RecordImportedPlan` uses a no-op command recorder and
`ExecuteTransientPlan` uses an immediate in-process backend, so GPU driver time does not hide managed CPU costs.

The following ShortRun results were measured on 2026-09-03 with .NET 10.0.11 and an Intel Core i7-9700K.
The historical column was captured before the low-allocation implementation. The current API accepts only explicit
state with a static pass callback; the former capture-based overload was intentionally removed. ShortRun is intended
for local comparison rather than release gating.

| Scenario | Historical baseline | Stateful-only implementation |
| --- | ---: | ---: |
| Single-pass build + compile | 1.710 us / 6.66 KB | 1.529 us / 6.61 KB |
| Eight-pass build + compile | 7.913 us / 27.13 KB | 7.556 us / 26.54 KB |
| Cache hit | 5.062 us / 16.91 KB | 4.105 us / 6.42 KB |
| Cache miss | 17.298 us / 36.89 KB | 16.960 us / 32.52 KB |
| Eight contributors + compile | 10.735 us / 31.31 KB | 9.214 us / 29.42 KB |
| Eight-pass record | 951.8 ns / 4.23 KB | 114.6 ns / 56 B |

The cache-hit improvement comes from an allocation-free structural hash followed by exact structural comparison;
hash collisions cannot produce false hits. Cache misses retain the exact snapshot and showed lower allocation but no
speed improvement in ShortRun. Record reuses immutable imported-resource and pass-access lookup data after its first
call. Explicit state is stored directly and passed with a stack-only context, avoiding closure and per-pass context
allocations. The stateful CPU-only transient execution benchmark measured 2.000 us / 5.67 KB.
