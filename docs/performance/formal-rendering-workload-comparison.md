# 12ワークロードの正式比較: main / PR #105

> 2026-11-14: docs/performance/dataの圧縮バイナリ生データは削除しました。
> JSON／CSV集計とmanifestは保持していますが、記載されたarchiveは現行treeには存在しません。

- 日付: 2026-11-02
- source: `2402407`、main: `fbc8105`、PR #105: `ffe075c`
- 対象: count=200、1280×720 Rgba8Srgb、Radeon8060S/RADV。正式設定を使用し、quick値を流用しない。

## 結論

**#105は記録を作り直すケースのmanaged allocationを減らすが、この12 workloadのframe全体の支配的な費用は削減していない。**

2DのCPU更新/layout/記録は約1.6〜2.6%、静止HUD＋動く背景は約3.9%短縮。ゲーム数値更新は約1.2%短縮だった。一方、エディタの文字編集・scroll・resizeはCPU時間の差が反復ばらつきと同程度で、明確な時間改善とは言いにくい。

エディタUIのserial frame中央値は約225〜234ms、GPU記録区間は約81〜84msに達した。静止していて文字組みを再実行しないcaseでも同様で、現在の描画Prepare/command/GPU側に大きな費用が残る。これはidle OS hostの実用表示FPSではなく、毎frame全描画する計測fixtureの結果。

## 比較の境界

今回のA/Bは同じ12 fixtureをmainと#105で実行したもの。製品コードの主差分はDrawList2Dのsnapshot storage。静的recording cacheや可視行制限は両側で同じbenchmark adapter policyを使う。

**直接保持方式とのpath/text/clip/UI比較ではない。** [先行する矩形direct試作](drawing2d-direct-pages.md)は別target・別scale・別内容であり、その約3倍という値を今回のUIへ転用しない。

## CPU-only: Update＋Layout＋Record＋3世代保持

中央値は2独立process×8 iteration推定値をpoolした16値から算出。単位µs。右の差は#105/main−1で、負が短縮。p95・各run中央値・byte/opは全集計JSONへ保存。

| case | main µs | #105 µs | 時間差 | main byte/op | #105 byte/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2d-camera-pan | 7.110 | 6.927 | -2.58% | 28200 | 23616 |
| 2d-geometry-stream | 21.768 | 21.424 | -1.58% | 104954 | 100369 |
| 2d-static | 0.156 | 0.156 | -0.04% | 0 | 0 |
| 2d-transform-all | 12.415 | 12.148 | -2.15% | 28152 | 23568 |
| editor-dock-resize | 1986.010 | 1991.245 | +0.26% | 1658562 | 1641586 |
| editor-idle | 0.110 | 0.120 | +8.52% | 184 | 184 |
| editor-line-edit | 270.626 | 271.426 | +0.30% | 289365 | 272590 |
| editor-live-preview | 8.811 | 8.960 | +1.69% | 36985 | 32400 |
| editor-scroll-list | 182.550 | 184.899 | +1.29% | 260048 | 243040 |
| game-hud-static-overlay | 8.484 | 8.156 | -3.87% | 36016 | 31432 |
| game-hud-values | 56.823 | 56.159 | -1.17% | 100142 | 93000 |
| game-menu-animation | 289.559 | 292.996 | +1.19% | 279629 | 277061 |

editor-idleの+8.5%は約0.009µsの差で、recording再利用のharness近傍。これを実アプリの目立つ退化とは解釈しない。editor-line-editは約0.3%増、各run中央値がmain269.5/271.6µsと#105270.3/272.0µsで重なる。scroll/resize/menuの小差も、2 processだけで普遍的な優劣と断定しない。

CPU benchmarkはBDNが呼ぶたびに同じ決定的周期の状態を進める。A/Bで呼出し回数まで固定した同一frame列ではなく、定常workloadの比較。旧snapshotの不変性は本計測のCleanupだけで証明せず、別途実施したCPU correctness smokeを根拠とする。

## Native serial stage: #105の費用内訳

各caseは2 process合計240 warm frameの中央値。単位ms。各工程の中央値を足した値はframe中央値と一致するとは限らない。GPUはCPU完了待ちと別のtimestamp区間。

| case | Prepare | Encode/Finish | GPU区間 | CPU frame壁時計 |
| --- | ---: | ---: | ---: | ---: |
| 2d-camera-pan | 17.885 | 5.013 | 9.960 | 39.189 |
| 2d-geometry-stream | 17.982 | 5.244 | 10.036 | 39.766 |
| 2d-static | 9.023 | 3.054 | 1.528 | 16.623 |
| 2d-transform-all | 9.021 | 3.104 | 1.532 | 16.707 |
| editor-dock-resize | 71.227 | 28.527 | 81.818 | 226.621 |
| editor-idle | 74.505 | 26.862 | 81.845 | 225.598 |
| editor-line-edit | 76.235 | 27.905 | 83.751 | 233.996 |
| editor-live-preview | 74.835 | 27.368 | 81.837 | 227.397 |
| editor-scroll-list | 74.685 | 27.307 | 81.098 | 227.622 |
| game-hud-static-overlay | 2.472 | 1.783 | 8.144 | 17.366 |
| game-hud-values | 3.091 | 2.267 | 8.087 | 18.106 |
| game-menu-animation | 3.468 | 2.597 | 8.811 | 20.244 |

editor-idleはnative text layoutが0、CPU recordingもほぼ0だが、Prepare約74.5ms、Encode約26.9ms、GPU区間約81.8msが残る。editor-dock-resizeではlayout約2.32ms、その内数のParley約2.19ms。文字組みも費用だが、このfixtureの全frameを支配しているとは言えない。

case間は同じ描画内容ではない。2D geometry-stream/camera-panにはcurved path/gradientがあり、ゲーム/エディタUIにはvector glyphとclipがある。単色矩形の200命令と同じ仕事として速度比を付けない。

## Bounded pipeline throughput

2独立runのheadless frame/s範囲。表示FPS・Present/VSync・入力遅延ではない。final drainと解放、timer/query read、counter取得、root参照配列コピーなど計測処理も含む。

| case | main depth1 | #105 depth1 | main depth3 | #105 depth3 |
| --- | ---: | ---: | ---: | ---: |
| 2d-camera-pan | 24.97〜25.10 | 24.85〜25.15 | 23.36〜23.55 | 23.11〜23.36 |
| 2d-geometry-stream | 24.64〜25.29 | 24.73〜24.95 | 23.23〜23.29 | 23.17〜23.38 |
| 2d-static | 59.53〜59.55 | 59.27〜60.38 | 50.50〜51.26 | 50.57〜51.08 |
| 2d-transform-all | 59.36〜59.65 | 58.69〜59.26 | 51.03〜51.16 | 51.26〜51.32 |
| editor-dock-resize | 4.38〜4.40 | 4.35〜4.39 | 2.32〜2.38 | 2.32〜2.36 |
| editor-idle | 4.33〜4.36 | 4.37〜4.39 | 2.34〜2.50 | 2.26〜2.30 |
| editor-line-edit | 4.21〜4.27 | 4.23〜4.24 | 2.18〜2.57 | 2.23〜2.23 |
| editor-live-preview | 4.28〜4.37 | 4.35〜4.38 | 2.26〜2.32 | 2.28〜2.30 |
| editor-scroll-list | 4.37〜4.41 | 4.36〜4.37 | 2.27〜2.32 | 2.29〜2.39 |
| game-hud-static-overlay | 55.86〜56.76 | 56.02〜58.53 | 51.12〜51.98 | 51.06〜51.66 |
| game-hud-values | 53.88〜54.95 | 53.06〜57.63 | 50.42〜51.00 | 50.36〜50.69 |
| game-menu-animation | 49.24〜49.51 | 49.94〜52.59 | 45.02〜46.27 | 45.70〜46.78 |

nativeの小さな差をDrawList storage変更へ直接帰属させない。例えばHUDのCPU recording側の短縮はµs未満なので、frame壁時計のms単位の差をそれだけで説明できない。clock/driver/schedulerと反復差も含む。

3 frame並行が常に速いわけではなく、このfixtureでは特にエディタUIでthroughputが低下した。資源生成/退役、allocation、driver/scheduler等の分解をせず、GPU待機だけで説明しない。2 runのthroughputからp95や統計的有意差を主張しない。

## 用途別の次の優先順位

- **2D**: geometry/paint/clipのcompiled資産を保持し、camera/instance属性と動的geometryを分ける。#105のrecording allocation削減だけではPrepare約9〜18msが残る。
- **ゲームUI**: 静的HUDを別recordingとして再利用しても、毎frameのPrepare・合成が残る。数値部分の更新/shapeだけでなく、compiled/resident描画の再利用を測る。
- **エディタUI**: このvector glyph/clip workloadでは文字組みより描画準備/記録/GPUが優先。既存の距離場/atlas経路とvector経路、static compiled asset、GPU residencyを同一内容で比較する価値がある。これらの最適化自体は今回未実装。

## 条件・検証・限界

- ABBA順: main1→#1051→#1052→main2。全baseline buildと12caseのcorrectness smoke完了後に直列計測。BDNの生成project buildは各測定の前。
- CPU: 通常設定、launch1、warmup3、measured8、150ms/iteration、MemoryDiagnoser、outlier除外なし。各variantを独立process2回。CPU time percentileはiteration推定値のp95で個別frame p95ではない。
- GPU/native: warmup41＋sample120、serial/depth1/depth3を各2process。GPU queryはTOP→BOTTOMのclear/upload/barrier/draw区間で、排他的GPU busy時間ではない。text layoutはupdate/layoutの内数として別記し二重加算しない。
- CPU execution48件、native serial5760 frame、pipeline11520 frame。全case/iteration/query数・非null Statistics/正のTotalOperationsを確認。
- 全144 readback観測で対応caseのhashがvariant/run/depthをまたいで一致。独立golden imageとの完全正解検証ではなく、A/Bとdepth間の最終frame同値性。
- 同じbenchmark C# source hashを両worktreeで確認。フォントはrepoのDejaVuSans.ttfを固定。runtime/SDK/CPU/affinity/Radeon driver/source hashはraw環境記録へ保存。
- Release、TieredCompilation=0、TieredPGO=0、workstation GC、CPU Ryzen AI MAX+395、Radeon8060S/RADV Mesa26.0.8。clock/powerとOS schedulingは固定していない。
- nativeのcounter取得等のinstrumentationを含む。Update計測開始前のcounter取得は除外したが、frame/pipeline全体は最小overheadのproduction hostではない。managed allocationはnative Parley/VRAM/RSSを含まない。
- UIのline-edit/scroll/resizeは宣言ツリーの再生成も含む。1文字だけの局所layout費用とは呼ばない。visible rows20はbench側policy、idleでも測定では毎frame描画を強制する。
- Count200だけの比較。TextBoxは単一行で、IME/入力/Undo/本物のdocking/3D preview/OS idle省電力は対象外。
- 最初の環境採取は任意cpu.maxファイルがなく計測前に停止した。失敗metadataを保存して修正した後に正式block全体を開始。性能の良い観測だけを選んだ再実行ではない。

[全集計](data/formal-workloads-20261102/summary.json)、[比較CSV](data/formal-workloads-20261102/comparison.csv)、[raw gzip/SHA-256 manifest](data/formal-workloads-20261102/manifest.json)。[再現runner](../../tools/benchmarks/drawing2d-incremental/run-formal-workloads.sh)と[分析script](../../tools/benchmarks/drawing2d-incremental/analyze-formal-workloads.py)を保存。

製品API・既存PRの変更やpushは行っていない。
