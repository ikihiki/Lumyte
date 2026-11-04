# Function subtree update benchmark

100/1,000個の兄弟関数のうち一つのSignalを変更し、宣言更新とUpdateLayoutまでを測定します。
既存のFunctionTestUi.Counter（ローカルSignalを持つ40×20のButton）を使う非配布ハーネスで、変更前後に同じソースを適用できます。

```sh
dotnet build tools/benchmarks/ui-functions -c Release
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 dotnet run \
  --project tools/benchmarks/ui-functions -c Release --no-build -- --expect-local
```

変更前は`--expect-local`を外します。各caseはwarmup 5回の後、100回/batchを5 batch順次実行します。
時間とGC.GetAllocatedBytesForCurrentThreadによる割当の中央値・全batchをJSON Linesで出力します。
setup、結果のassertと出力は計測外です。最新値の公開を確認し、変更後は無関係なrootと兄弟の保持もassertします。
測定はbuildや他の測定と並行実行しません。入力geometryの再計算も含み、GPU・OS loop・p99遅延は測定しません。

Panel上に重なったButtonを800×600のviewportへ配置します。各関数は独立したlabelのSignalを持ち、一つのlabelだけを更新します。初期構築と常駐メモリは計測せず、結果は更新時の時間と割当の比較です。

[測定結果と全batch](../../../docs/performance/ui-function-subtree.md)を保存しています。
