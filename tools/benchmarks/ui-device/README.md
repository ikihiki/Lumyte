# UI native device benchmark

Windowsの実Windowと標準`UiHostPresenter`を使い、Wgpuが選択したadapterへ描画・提示します。adapter、backend、driver、runtime、pixel size、DPIをJSON Linesに記録します。実機比較では記録されたadapterの種類と名前を確認してください。

## 実行

固定SDK・Slang・Windows SDKのDXCを準備し、リポジトリの環境を有効にしてから実行します。DXCがPATHにない場合は、インストール済みWindows SDKの対応する`bin/<version>/<arch>`をPATHへ追加してください。別worktreeでは`artifacts/nuget`を作成し、必要な既存ローカルfeedも復元元へ指定します。

```powershell
dotnet build tools/benchmarks/ui-device -c Release
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_gcServer = '0'
dotnet tools/benchmarks/ui-device/bin/Release/net10.0/Lumyte.UI.Application.Tests.dll --label=current --output=artifacts/ui-device/current.jsonl
```

既定は各caseでwarmup 10 frame・測定60 frameです。`--warmup=5 --frames=30`などで変更できます。build・test・別の測定は終了させてから実行してください。Windowは自動で閉じます。各frameの非同期完了待ちは30秒、実行全体は5分を上限とします。

1/10/100個の矩形を800×600の論理viewportへ隙間なく横並びにし、実DPIでCanvasを拡大します。surfaceのclipとhost側のviewport clipを含みます。`cached`は不変のDrawListを再利用し、`color`は最後の矩形を赤・青へ変更して再記録します。全caseで毎frameをGPUへ提出します。

## 測定範囲

- UI更新・layout・必要な命令記録と、標準presenterの準備・記録・Submit・Present・GPU完了待機・資源解放を直列に実行します。
- `TotalMilliseconds`はCPUの壁時計です。`DrawAndWaitMilliseconds`は標準presenter全体で、GPU timestampによるGPU実行時間ではありません。
- `AllocatedBytes`は`GC.GetTotalAllocatedBytes(false)`の差分です。全threadのmanaged allocationの近似値であり、GPU memoryや常駐量ではありません。
- OS event pumpは通常frameの計測前、および非同期処理待ちの間に実行します。待ちの間の1ms sleep・callback処理は壁時計へ含みます。
- source構築・出力・画素検証は計測外です。GCをframeごとに強制せず、発生したGCの時間は測定へ含みます。

既定の直列modeは標準application loop・複数frameの重なり・OS入力・入力から表示までの遅延・持続FPSを測定しません。短時間のframe分布からp99や統計的信頼区間を主張しません。既存CPU harnessとは測定範囲が異なります。

## 標準application loopの120Hz測定

`--counts=1,100,1000`で矩形数を選べます。100列を上限としたgridでviewportを埋めます。
`--application`は最初のcountで標準`UiApplication`とDesktop driverを起動し、FrameIntervalを1/120秒へ設定します。
完了したframeごとに最後の1要素の色を変え、完了間隔と全threadのmanaged割当を記録します。
`--static`は変更せず毎frameを提示、`--all-changing`は全要素の色を変更します。
`--layout-changing`は全要素の色と幅を変え、幅を100%/95%で切り替えてlayoutも再計算します。

```powershell
git apply tools/benchmarks/ui-device/application-instrumentation.patch
dotnet build tools/benchmarks/ui-device -c Release
dotnet tools/benchmarks/ui-device/bin/Release/net10.0/Lumyte.UI.Application.Tests.dll --application --counts=1000 --warmup=120 --frames=1200 --output=artifacts/ui-device/application.jsonl
```

完了通知用のprivate callbackは任意パッチに限定し、製品コードには含めません。
startup中の非提示frameはSkippedへ記録し、測定から除きます。要求数に達しなければ失敗し、90秒で停止します。
生データのSamplesは測定区間の全完了間隔です。平均Hzは1,000/平均間隔であり、中央値の逆数ではありません。
`summarize-application.ps1 -Paths <JSONL一覧>`はnearest-rankのp50/p95/p99と最大値も出力します。
これはGPU timestampやscanout時刻・入力から表示までの遅延ではなく、OS・GC・GPU通知・owner loopの待機も含む壁時計です。
長い裾を除外せず、平均120Hzでも全frameが8.33ms以内に完了するという意味にはなりません。

[Windows 120Hzでの結果と制限](../../../docs/performance/ui-game120-performance.md)を参照してください。

## 詳細なCPU経路・GC・完了間隔の調査

`--detail`はSignalの通知・effect実行・変更候補生成・検証・binding・公開、layout/input、描画走査・DrawList固定化、矩形pack、surface能力照会、GPU完了待ち、owner待機を記録します。
使用するのは[detail-instrumentation.patch](detail-instrumentation.patch)です。既存の2つの計測パッチを含むので、他のパッチと重ねず、未計測の製品ソースへ適用してください。
scopeの時間はinclusiveの壁時計です。親子scopeやGPU待ちとowner待機を足し合わせないでください。
割当は同threadの差分で、threadをまたいだspanは`CrossThreadSpans`へ数えます。
GC回数と累積停止時間の差分、scopeの開始/終了timestampもframeごとに保存します。

```powershell
git apply tools/benchmarks/ui-device/detail-instrumentation.patch
dotnet build tools/benchmarks/ui-device -c Release
dotnet tools/benchmarks/ui-device/bin/Release/net10.0/Lumyte.UI.Application.Tests.dll --application --detail --all-changing --counts=1000 --warmup=240 --frames=1200 --output=artifacts/ui-device/detail.jsonl
```

[capabilities-ablation.patch](capabilities-ablation.patch)は、その上に適用する固定サイズWindow専用の切り分けです。
surface能力の再照会を省略する影響だけを比較します。resize・monitor・surface/device更新時の無効化を実装した製品用cacheではありません。
適用した場合はこのpatchを先にreverseし、その後detailのpatchをreverseしてください。

EventPipeでは測定frameのmarkerを使い、startupとwarmupを集計から除きます。
今回のツールはdotnet-trace 10.0.750501、解析器はTraceEvent 4.0.0です。両方のbuildを測定前に済ませ、解析は測定終了後に実行します。

```powershell
dotnet tool install dotnet-trace --version 10.0.750501 --tool-path artifacts/perf-tools
dotnet build tools/benchmarks/ui-device-trace -c Release
artifacts/perf-tools/dotnet-trace collect --profile gc-verbose --providers Microsoft-DotNETCore-SampleProfiler,Lumyte-UI-Device-Benchmark --output artifacts/ui-device/all.nettrace -- dotnet tools/benchmarks/ui-device/bin/Release/net10.0/Lumyte.UI.Application.Tests.dll --application --detail --all-changing --counts=1000 --warmup=240 --frames=1800 --output=artifacts/ui-device/trace-all.jsonl
dotnet tools/benchmarks/ui-device-trace/bin/Release/net10.0/TraceAnalysis.dll artifacts/ui-device/all.nettrace artifacts/ui-device/trace-analysis.json
```

サンプラは待機中のthreadも観測します。Managed/Externalを分け、stackの件数をCPU時間の割合へ換算しません。
GCの停止には`SuspendForGC`だけを使い、サンプラ等の`SuspendOther`を混ぜません。
allocation tickはサンプル推計で、型ごとの正確な全数調査ではありません。
`summarize-detail.ps1 -Paths <JSONLまたはJSONL.gz一覧>`で段階別の分布、GCとの重なり、記録開始・提出・完了の間隔を再集計できます。

[追加調査の結果・生データ・限界](../../../docs/performance/ui-device-deep-profile.md)を参照してください。

## 切り分けと画素検証

`--queue-probe`は空のcommand bufferのSubmitから完了通知までを測ります。描画shaderとsurface取得・提示を含めず、通知pollingの基礎的な遅延を確認します。`--present=immediate`は提示modeをImmediateへ変えます。対応しないsurfaceでは失敗させ、暗黙にFIFOへ戻しません。

`--verify`は既定でwarmup 2・測定2 frameとし、計測外で6 caseの実画素を読み戻します。最後の矩形の期待色をassertし、RGBA全体のSHA-256を記録します。同一条件のbaselineと候補で6 hashを比較してください。

```powershell
dotnet tools/benchmarks/ui-device/bin/Release/net10.0/Lumyte.UI.Application.Tests.dll --verify --label=pixels --output=artifacts/ui-device/pixels.jsonl
```

段階別の切り分けには任意の[instrumentation.patch](instrumentation.patch)を両比較元へ適用してからbuildします。`prepare`・`acquire`・`record`・`submit-present`・`drain`の壁時計を記録します。通常の製品コードにはこのcallbackを含めません。`drain`はsubmission完了と提示資源の解放を含みます。Wgpuの提示資源はPresent後すぐ解放可能とする既存契約なので、今回の大きな差は主としてsubmission完了待ちに現れます。

```powershell
git apply tools/benchmarks/ui-device/instrumentation.patch
dotnet build tools/benchmarks/ui-device -c Release
```

比較する両checkoutへ同じハーネスを置き、先に両方をbuildして実行ディレクトリを保存します。その後buildを挟まず順次実行します。private adapter取得と既存friend assembly名はこの非配布harnessに限定しています。

[実機での調査結果・全frame](../../../docs/performance/ui-device-performance.md)を参照してください。
