# Drawing2D部分更新アーキテクチャの試作・性能比較

- 日付: 2026-11-02
- 比較元: main `fbc8105`、PR #105 `ffe075c`
- 計測ハーネス: `9e402e9`。[試作と実行手順](../../tools/benchmarks/drawing2d-incremental/README.md)
- [設計検討案](../design/drawing2d-incremental-rendering.md)の固定要素数CPU fragment置換を試作した結果。製品実装・設計採用の完了ではない。

実GPUアクセス復旧後の追試は[実GPU比較レポート](drawing2d-hardware-comparison.md)へ保存した。本書のsoftware結果と権限blockedの記述は、先行測定時点の記録として残す。

## 結論

**局所更新のCPU公開は有効だが、今回の試作をそのまま既存Rendererへ接続する方式は採用しない。**

10,000矩形の1要素変更で、#105の全再記録は255.839µs、保持型のCPU commitだけは0.591µsだった。しかし既存Rendererへ渡す平坦化まで含めると835.612µsとなり、全再記録の約3.3倍遅い。全要素変更ではCPU commitだけでも2,582.576µsで、全再記録267.056µsの約9.7倍遅かった。

保持単位・payload表現・全更新のbulk経路・Rendererへの直接接続を見直す必要がある。GPU資産cacheとdamage redrawは未実装であり、その性能はこの比較で証明していない。

## 条件と測定方法

AMD RYZEN AI MAX+ 395、32 logical CPU、Ubuntu 26.04、Linux 7.0.0-42、SDK 10.0.401 / runtime 10.0.12、Release。
`DOTNET_TieredCompilation=0`、`DOTNET_TieredPGO=0`、workstation GCで統一。productionの既定tiering条件の結果ではない。

独立worktreeで実際のmain/#105を参照した。同じ11個のC# source hash一致を確認。製品側の変更はbenchmarkへのInternalsVisibleToだけで、baselineクラスを擬似再実装していない。

- BDN 0.15.8。1 launch・3 warmup・8測定iteration、目標150ms、outlier除外なし。
- main→#105→#105→mainの各2独立run。全build/正しさ検証後に直列測定し、並行build/testなし。
- primary 120件、粒度・初期構築24件、合計144 benchmark実行すべてで8測定iterationを確認。
- 表は2 run×8 iteration推定値を合算した中央値。p95は[CSV](data/drawing2d-incremental-20261102/updates.csv)へ保存。個々のframe latencyのp95ではない。
- #105・1要素変更の独立run中央値は259.391/250.593µs、CPU commitは0.595/0.589µs。小さな差を普遍的な順位として扱わない。
- 静止はbaselineでもDrawList再利用。毎frame再構築を強制して保持型を有利にしていない。
- geometryとimmutable paintを共有し、同じ色・位置を変更。最大3世代分のCPU rootを3slotで保持する。静止時は同じrootを共有し、GPU frames-in-flight比較ではない。

## 工程の境界

- **LegacyFrame**: 変更frameは同じCanvasへ全命令を再記録してEnd。静止は完成済みDrawListを再利用。
- **RetainedCommit**: 変更したfragmentだけCanvas/Endし、64-fragmentのCOW pageとImmutableList.Builderで公開。既存Rendererへ直接渡せる単一DrawListは作らない。
- **RetainedWithFlatten**: 全fragmentをReplay/Endして既存Rendererへ渡せる型にする。静止は平坦化結果も再利用。

基本比較は1命令1fragment。64は物理的な参照COW page容量であり、後述の再記録の論理粒度とは別。dirty集計・sort・COW費用を含み、既存APIの入力検証を省いた直接command生成は使わない。

## 10,000命令の更新CPU時間

単位µs、中央値。保持型2列は#105のsnapshot実装を利用。

| 変更 | main全再記録 | #105全再記録 | 保持CPU commitのみ | 保持＋平坦化 |
| --- | ---: | ---: | ---: | ---: |
| 静止 | 0.004 | 0.004 | 0.006 | 0.009 |
| 1要素 | 380.248 | 255.839 | 0.591 | 835.612 |
| 連続1% | 375.397 | 253.013 | 13.471 | 842.902 |
| 分散1% | 383.150 | 256.379 | 40.729 | 1016.256 |
| 全要素 | 389.129 | 267.056 | 2582.576 | 4618.665 |

静止値は短いmethodの反復平均であり、数ns差をframe性能差と解釈しない。
PR #105は今回の変更caseでmainより約31〜33%短く、割当も減った。CPU記録工程の改善であり、GPU/アプリFPSの改善率ではない。

## managed allocation

PR #105側、1操作あたりbyte。Allocated/opとscene全体の保持量は別である。

| 変更 | 全再記録 | 保持CPU commitのみ | 保持＋平坦化 |
| --- | ---: | ---: | ---: |
| 静止 | 0 | 0 | 0 |
| 1要素 | 680,313 | 1,356 | 1,481,726 |
| 連続1% | 680,312 | 40,312 | 1,520,669 |
| 分散1% | 680,312 | 100,590 | 1,580,967 |
| 全要素 | 680,313 | 3,935,176 | 5,415,624 |

全変更では、1命令ごとの既存DrawListの配列・wrapper・stateとCOW/tree更新費用が膨らむ。builderでpathを共有してもindexed tree-search費用は残る。

## 初期構築・保持量

PR #105の10,000命令の初期構築は、flat 256.035µs / 約0.953 MB割当、保持型1,350.339µs / 約4.036 MB割当。約5.3倍遅く、約4.2倍の割当だった。

BDNとは別のprobeを各variantで起動し、1 process内で各caseを3回測った。100更新・最大3世代root保持後のfull GCによるmanaged live heap差分である。中央値は次のとおり。decimal MBであり、RSS/VRAM/nativeメモリではない。

- 静止: flat 0.822 MB、保持型3.476 MB、保持＋平坦化4.767 MB。
- 1要素変更: flat 2.182 MB、保持型3.478 MB、保持＋平坦化7.090 MB。
- 全変更: flat 2.182 MB、保持型10.714 MB、保持＋平坦化14.325 MB。

未変更内容の共有は成立したが、1 commandをDrawListで包むmetadataが大きい。低いAllocated/opだけを見て省メモリとは言えない。

## 論理fragmentの粒度

PR #105、10,000命令のCPU commit中央値。secondaryは1 launchでありprimaryの2 runとは分けて扱う。

- 1要素変更、1/16/64/256命令単位の再記録: 0.592 / 0.716 / 1.768 / 6.049µs。
- 分散1%変更: 40.315 / 53.359 / 176.070 / 271.140µs。

大きなfragmentはmetadataを減らせても未変更commandを多く再記録する。論理fragmentとGPU batchを分離する方針は維持するが、これだけで本番の最適容量を確定しない。

## software Vulkan統合診断

実GPUは`/dev/dri/renderD128`へのPermission deniedで列挙できなかった。権限は変更していない。以下は**lavapipe上の診断**で、RadeonのGPU性能ではない。

1,000/10,000命令×静止/1要素/全変更でlegacyと保持＋平坦化を実行した。cold renderer 1、warmup 5、warm 18 frame、1 frame in flight、headless、GPU timestampなし。両baselineの計12組のpixel readback SHA一致、baseline間の画素hash一致を確認した。

PR #105、10,000命令・1要素変更のwarm中央値:

- CPU更新: legacy 0.276ms、保持＋平坦化0.796ms。
- Prepare: 0.683ms、0.750ms。
- Record/Finish: 0.130ms、0.138ms。
- Submit→完了のCPU壁時計: 12.026ms、12.030ms。

静止でもPrepareは約0.725/0.732ms残った。CPU root保持だけではRendererのpack/upload準備は自動で省略されない。

submit-to-completionにはdriver・待機・schedulerも含み、純GPU実行時間ではない。工程列にはcommand生成等の一部が含まれず、単純合算を厳密なframe総時間として扱わない。非同期probeの割当はprocess-wideであり、BDNの同期割当と区別する。

### clip参照とbatch境界

同じclip付きの1,000矩形で、型とclip参照で分かれるCPU連続run数はlegacy 1、Replayによる平坦化後1,000だった。命令/clipの意味は同じでもobject identityが変わるためである。

これはGPU captureのdraw数ではない。software統合の時間比較はclipなし矩形で、このclipped workloadのGPU時間は未測定。scope/clip共有とfragmentを跨ぐbatch形成を保持型consumerで維持する必要がある。

## 設計案へのフィードバック

1. **現試作をそのまま製品へ移さない。** 局所CPU公開の成功と全pipelineの改善は別。
2. **1 command = 1 DrawList包装を見直す。** versioned typed pageとrange参照等で配列・wrapper・stateの重複を減らす。所有権移譲もここで検討する。
3. **全更新のbulk経路を用意する。** 高dirty率での一括構築と部分更新を比較する。現在の測定点だけで切替閾値は決めない。
4. **Rendererがscene/pageを直接消費する。** 全Replay/Endを最終形にせず、clip/scope共有とbatchを維持する。
5. **compiled/resident資産の再利用を別に実装・測定する。** GPU使用中pageの世代/退役とupload公開の整合が必要。
6. **damage redrawは別段階。** 今回の結果から部分画面再描画の効果を主張しない。

PR #105は現行flat経路で有効な局所改善だった。一方、それで部分更新が不要になったわけでも、保持型試作が全面的に上回ったわけでもない。

## 未測定・制約

- 実GPU性能、GPU timestamp、複数GPU frame in flight。
- retained sceneを直接受けるRenderer、GPU resident page差分、RenderGraph/layer/effect差分。
- insert/remove/reorder、property scope、動的文字/atlas、実UI host統合の性能。
- 全更新の最適切替閾値、最適page容量、長時間のcache eviction/backpressure。

CPU比較とsoftware統合診断は完了。実GPU比較はアクセス権が利用可能になることが必要で未完了として残す。

## 生データ・検証

- [全集計JSON](data/drawing2d-incremental-20261102/summary.json)、[更新CSV](data/drawing2d-incremental-20261102/updates.csv)
- [raw gzip一覧とSHA-256](data/drawing2d-incremental-20261102/manifest.json)。full JSONに全iteration値・outlier・memory・measurementを保持。
- 144 benchmark実行、両worktree build、各90条件×12 frameのCPU semantic検証、prototype不変性/invalid入力検証、software pixel比較12組が成功。
- 実GPU比較のみ権限でblocked。softwareを実GPU結果へ読み替えていない。
