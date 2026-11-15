# Drawing2D実GPU比較: Radeon 8060S / RADV

> 2026-11-14: docs/performance/dataの圧縮バイナリ生データは削除しました。
> JSON／CSV集計とmanifestは保持していますが、記載されたarchiveは現行treeには存在しません。

- 日付: 2026-11-02
- baseline: main `fbc8105`、PR #105 `ffe075c`
- 計測source: `bc185cf`。[ハーネス](../../tools/benchmarks/drawing2d-incremental/README.md)
- [先行CPU/ソフトウェア比較](drawing2d-incremental-comparison.md)を実GPUで追試。製品コードの最適化やGPU resident cacheの実装ではない。

後続の[保持page直接描画の試作](drawing2d-direct-pages.md)では全Replay/Endを省き、resident GPU bufferの差分更新まで実装して比較した。本書の保持型は旧「保持＋平坦化」経路であり、新しいdirect経路の評価ではない。

## 結論

**実GPU上でも、保持型＋全Replay/平坦化の試作は採用を推奨しない。GPU側の改善は確認できず、CPU準備・割当・全更新の費用が残る。**

10,000矩形・1要素変更でPR #105のserial frame壁時計中央値は2.3265ms、保持＋平坦化は2.7417ms。対応するGPU query区間は0.1577msと0.1711msだった。CPU完了待ち約1.08msをGPU時間と解釈してはいけない。

3 frame in flightのheadless throughputはPR #105で656〜706 frame/s、保持＋平坦化で459〜480 frame/s。全変更ではPR #105の671〜701に対し146〜147まで低下した。表示FPS・VSync付き性能ではない。

## 実GPUの根拠と環境

作成したVulkanDevice自身のphysical deviceを検査し、各runで以下を記録した。

- Radeon 8060S Graphics (RADV STRIX_HALO)、AMD vendor `0x1002` / device `0x1586`、IntegratedGpu。
- driver radv、Mesa 26.0.8-1ubuntu0.3。AMD ICDを明示し、software ICDはhardwareモードが拒否する。
- active queue family 0、timestampValidBits 64、timestampPeriod約10.019036ns。
- renderグループ反映後のプロセスで実行。先行比較時のアクセス拒否は再起動後に解消した。

CPUはRYZEN AI MAX+ 395、Ubuntu 26.04、SDK 10.0.401 / runtime 10.0.12、Release。TieredCompilation=0、TieredPGO=0、workstation GC。
GPU/CPU clock・電力設定は固定しておらず、driver負荷・scheduler・GCの変動を含む。

## 比較条件

- 独立したmain/#105 worktreeへ同じ計測sourceを配布。13個のC# source hash一致を確認。
- buildとCPU semantic検証を全て先に完了し、他のbuild/testと並行計測しない。
- main→#105→#105→mainの4 process。各baselineで2独立run、legacy/保持＋平坦化の順も反転。
- static、1要素色変更、全要素色変更。shape/paint/変換等は同じ入力。
- CPU-only RetainedCommitは既存Rendererへ直接渡せないためGPU性能の列を作らない。比較する保持型は既存Renderer向けの全平坦化を含む。
- 1024×1024 Rgba8Srgb offscreen target、Clear、Presentなし。

## GPU queryとCPU壁時計の区別

計測helperはbenchmark assembly内だけでVulkan backendのnative handleへ接続し、製品Graphics APIを変更しない。

GPU queryはTOP_OF_PIPE→BOTTOM_OF_PIPEで、PreparedDraw2D.Recordが記録するclear/upload/barrier/renderを挟む。初期target transitionは区間外。これはGPU上の経過時間であり、shader演算だけの時間や、他の仕事と重ならないGPU占有時間ではない。

CPUのupdate、Prepare、Record/Finish、Submit、Submit→完了、解放、frameWallを別記する。frameWallにはquery read等の計測処理も含む。GPU完了確認後にだけqueryを読み、command/prepared/query poolを再利用する。

## serial stage比較

cold renderer 1＋warmup 40＋warm 120 frame。1 frame in flightで各frameを完了まで待つ。
以下は10,000命令・1要素変更、2 run合計240 warm frameの中央値。単位ms。p95は個々のwarm frameの分布で、先行BDNのiteration推定値のp95とは異なる。

| 工程 | main legacy | PR105 legacy | PR105保持＋平坦化 |
| --- | ---: | ---: | ---: |
| CPU更新 | 0.2558 | 0.2177 | 0.5976 |
| Prepare | 0.7181 | 0.7157 | 0.7248 |
| Record/Finish | 0.1774 | 0.1748 | 0.1857 |
| Submit→完了のCPU壁時計 | 1.0763 | 1.0764 | 1.0787 |
| frameWall | 2.3789 | 2.3265 | 2.7417 |
| GPU query区間 | 0.1660 | 0.1577 | 0.1711 |

PR #105と保持＋平坦化のframeWall p95は2.8011ms / 6.0349ms、GPU query p95は0.1696ms / 0.2364ms。

静止caseでもPR #105のPrepare中央値は0.7278ms残った。CPU側のDrawList再利用は成立しているが、現在のRendererは毎frame pack/uploadの準備を行う。

全変更のframeWall中央値はPR #105が2.3446ms、保持＋平坦化が4.3102ms。CPU更新中央値は0.2295ms / 2.0217msで、保持型側の全再構築コストが大きい。GPU query中央値は0.1668ms / 0.2273ms。smallなGPU時間差をshader最適化の成果/劣化へ単独帰属させない。

## bounded pipeline比較

slotごとにtarget/view、command、prepared owner、submission、query poolを独立所有する。前のGPU使用が完了したslotだけ再利用する。
warmup 41 frameをdrain後、120 frameを測定し、最後のGPU drainと解放をelapsedへ含める。

下記は10,000命令の2独立runの範囲。frame/sはCPU更新・prepare・record・submit・完了待ち・query read・解放込みのheadless throughput。2点のためprocess throughputのp95は算出しない。

| 変更 | 方法 | depth1 frame/s | depth3 frame/s |
| --- | --- | ---: | ---: |
| 静止 | PR105 legacy | 433〜455 | 773〜850 |
| 静止 | PR105保持＋平坦化 | 429〜458 | 784〜810 |
| 1要素 | main legacy | 349〜370 | 537〜603 |
| 1要素 | PR105 legacy | 383〜401 | 656〜706 |
| 1要素 | PR105保持＋平坦化 | 309〜315 | 459〜480 |
| 全要素 | main legacy | 344〜364 | 475〜506 |
| 全要素 | PR105 legacy | 397〜408 | 672〜701 |
| 全要素 | PR105保持＋平坦化 | 127〜130 | 146〜147 |

重ね合わせると待機を隠せるが、保持＋平坦化のCPU負荷はなくならない。median frameWallの比とthroughputの比が同じとは限らず、GC等のtailも影響する。GPU timestamp区間はframe間で重なり得るので、合計をGPUの排他的busy時間と扱わない。

## 検証・データ件数

- serial: 1,000/1,024/1,025/10,000命令×3変更条件×2方式、全4 run。
- warm stage 11,520 frame、pipeline measured 11,520 frame（各frameのqueryも保存）。cold/warmupは別に保持。
- legacy/保持＋平坦化のreadback比較96組が成功。4 run・depth間・各baseline間の対応画素hashも一致。
- 作成deviceのvendor/device ID、driver、query支持を全4 runで確認。software ICDを渡す負例も拒否。
- [全集計](data/drawing2d-hardware-20261102/summary.json)、[raw gzip一覧とSHA-256](data/drawing2d-hardware-20261102/manifest.json)。全stage値、query値、throughput、環境、source hashを保存。

## 設計判断への意味

先行CPU比較の結論は変わらない。局所CPU commitだけを高速化しても、全Replay/End、全体Prepare、pack/uploadが残れば全pipelineは改善しない。
軽量なfragment/page表現、dirty率が高い場合のbulk経路、保持sceneを直接消費するRenderer、compiled/resident assetの再利用が次の検証対象である。

今回の試作はGPU resident cache・property scope・RenderGraph差分・damage redrawを実装していない。そのような完成版architectureの優劣を、この試作で証明/否定したものではない。

結果は実GPUで確認できたが、一般のUI host・文字/atlas・layer/effect・任意clip・表示FPS・消費電力まで評価したものではない。今回の矩形workloadの判断として扱う。
