# Widget subtree update benchmark

100/1,000個の通常Rectangleのうち一つの幅を変更し、Signal代入からUpdateLayoutまでを測定します。
各Rectangleは独立したSignalを持ち、Panel上に重ねて配置します。高さ10、幅10〜29、viewportは800×600です。

```sh
dotnet build tools/benchmarks/ui-widgets -c Release
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 dotnet run \
  --project tools/benchmarks/ui-widgets -c Release --no-build -- --expect-local
```

変更前は同じソースで`--expect-local`を外します。warmup 5回、100回/batchを5 batch順次実行します。
時間・GC.GetAllocatedBytesForCurrentThreadによる割当の中央値と全batchをJSON Linesで出力します。
setup・assert・出力は計測外で、最新値と無関係なroot・兄弟の保持を検証します。
計測中はbuildや別の測定を実行しません。layout・入力の再計算は含み、GPU・OS loop・p99遅延は測定しません。

[測定結果と全batch](../../../docs/performance/ui-widget-subtree.md)を保存しています。
