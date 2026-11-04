# UI入力の性能測定

描画・GPUを使わず、capture中／captureなしのMove、Down/Up、layoutとWidget内の入力情報の更新を測る。.NET 10 SDKとリポジトリ標準のSlang設定を用意する。結果と測定範囲は[測定記録](../../../docs/performance/ui-input-snapshots.md)を参照する。

## 実行

リポジトリルートから実行する。SlangがPATHにない場合はbuildに `-p:SlangCompilerPath=/path/to/slangc` を追加する。

```sh
dotnet build tools/benchmarks/ui-input/Lumyte.UI.InputBenchmarks.csproj -c Release
DOTNET_TieredCompilation=0 dotnet tools/benchmarks/ui-input/bin/Release/net10.0/Lumyte.UI.InputBenchmarks.dll current /tmp/ui-input-current.json
```

128回warmup、32回pilotの後、約100msを目標とするbatchを7回測る。JSONには各batchの平均ns/操作・managed割当B/操作・Gen0回数、環境情報を保存する。単一イベントのp99や実デバイスの配送遅延を測るツールではない。Moveは内外を交互に移動し、全件の消費を確認する。初回のUpdateLayoutは入力配送前に済ませ、定常Dispatchの計測には含めない。イベント再利用とrecord生成込みを分け、Down/Upは1 Dispatch当たりへ換算する。

## 同一コードで変更前と比較

同じ測定ソースに別worktreeのUIを接続できる。以下の変更前commitは今回の測定基準である。各build後にそのassemblyを実行してから次のbuildへ進む。参照先を変更するためrestoreを省略しない。

```sh
git worktree add --detach /tmp/lumyte-ui-before 7045000ce9fea830466ea1522f8874dd21007bca
mkdir -p /tmp/lumyte-ui-before/artifacts/nuget
dotnet build tools/benchmarks/ui-input/Lumyte.UI.InputBenchmarks.csproj -c Release -p:UiRepositoryRoot=/tmp/lumyte-ui-before
DOTNET_TieredCompilation=0 dotnet tools/benchmarks/ui-input/bin/Release/net10.0/Lumyte.UI.InputBenchmarks.dll before /tmp/ui-input-before.json
dotnet build tools/benchmarks/ui-input/Lumyte.UI.InputBenchmarks.csproj -c Release
DOTNET_TieredCompilation=0 dotnet tools/benchmarks/ui-input/bin/Release/net10.0/Lumyte.UI.InputBenchmarks.dll after /tmp/ui-input-after.json
```

input-only-refreshはprotectedなInvalidateInputがある実装だけを測る。hookは計測開始前にdelegateへ変換する。layout-and-input-refreshはviewport変更とUpdateLayoutを含む。新規Widgetツリーの構築、reconciliation、OS/Browser入力、描画は含まない。比較時は他のbuild・負荷を止め、同じruntime・GC・tiering設定を使う。共有環境の揺れは生データのbatch間差で確認する。

uncaptured-move-in-viewportはcaptureを解除した後、Button内容上とButtonの外側（ともにviewport内）を交互に配送する。汎用イベント版ではhit test・親への伝播を含む。Button/Panelの既定処理が未処理を返すことを全件確認し、画面外の即時returnとは分ける。largeのDown/Upも同じツリーで追加測定する。
