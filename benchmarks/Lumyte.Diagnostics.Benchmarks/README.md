# 診断の性能測定

生成 Operation の配送、標準 Metrics の収集、4 種類のシリアライザーを BenchmarkDotNet で測定する。固定 seed の Operation / Metrics / Trace / Log / Graph / Image データを全ライブラリで往復し、全フィールド一致を確認してから測定する。

```sh
dotnet run --project benchmarks/Lumyte.Diagnostics.Benchmarks -c Release -- --verify
dotnet run --project benchmarks/Lumyte.Diagnostics.Benchmarks -c Release -- --filter '*' --artifacts artifacts/diagnostics-benchmarks
```

既定は 1 launch / 3 warmup / 5 iteration / 150 ms iteration の ShortRun。速度は専用マシンで追加測定して判断する。CI では時間の閾値を設定せず、ビルド・往復一致だけを検証する。

比較条件、バージョン、環境、割り当て量、サイズ、全測定の生データは [結果レポート](../../docs/benchmarks/diagnostics/README.md) に記載する。
