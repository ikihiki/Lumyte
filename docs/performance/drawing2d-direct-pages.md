# 保持pageを直接描画する矩形試作

- 日付: 2026-11-02
- source: `fcd6e7b`、比較baseline: main `fbc8105` / PR #105 `ffe075c`
- [試作の契約と手順](../../tools/benchmarks/drawing2d-incremental/DIRECT-PAGES.md)、[前の平坦化比較](drawing2d-hardware-comparison.md)
- benchmark-only。製品API・既定renderer・既存PRへは組み込んでいない。

## 今回作ったもの

全体Replay/Endを通さず、immutableなCPU pageを直接resident GPU bufferへ反映する。
CPU保存単位64矩形とGPU描画単位1024矩形を分離し、1万矩形でもdrawは従来同様10回。

- 変更commandだけ既存shaderの6 Vector4へpack。未変更pageは旧snapshotと共有。
- partial更新では変更pageとroot参照配列だけをCOW。全変更では古いpageをcloneしないbulk構築。
- slotごとの前回完了page参照と比較し、未反映の全更新を追従。最新frameの差分だけに依存しない。
- upload/data buffer・argument tableはslot専有。変更chunkだけを転送し、隣接範囲をGPU run内でまとめる。
- 実際のsubmission完了後だけresident版を公開。Record済みをupload済みと誤認しない。
- frameはone-shot。未送信破棄・busy slot・二重Record・早期Completeを検証する。

保存画像の部分描画ではない。毎frame全targetをClearして全矩形を描く。render pass/drawの構成もbaselineと同じ単位に保ち、pass融合による改善と混同しない。

## 結果の要約

**局所更新ではCPU更新だけでなくPrepareも減った。全変更を含め常に優位ではないが、前回の「平坦化で利益が消える」問題は解消できた。**

Radeon 8060S/RADV、1万矩形・1要素変更、PR #105上の同一process構成による240 warm frameの中央値。単位ms。

| 工程 | 既存legacy | 旧保持＋平坦化 | 今回direct |
| --- | ---: | ---: | ---: |
| CPU更新 | 0.2225 | 0.6030 | 0.0018 |
| Prepare | 0.7266 | 0.7450 | 0.0058 |
| Record/Finish | 0.2017 | 0.2324 | 0.1755 |
| frame全体のCPU壁時計 | 2.4181 | 2.8388 | 1.3498 |
| GPU query区間 | 0.1616 | 0.1642 | 0.1025 |
| process-wide managed allocation byte/frame | 754624 | 1553072 | 70240 |

frame全体にはGPU完了待ち・query read・解放を含む。GPU queryはclear/upload/barrier/draw区間で、CPU壁時計と独立に取得した。CPU待機をGPU時間と読み替えていない。

## 本当に仕事が減ったか

以下はdirectが発行した処理のcounterで、driver全体のcaptureではない。warm後の1万矩形、serial depth1。

| 更新 | pack命令数 | upload byte | draw数 | 新規GPU allocation数 |
| --- | ---: | ---: | ---: | ---: |
| 静止 | 0 | 0 | 10 | 0 |
| 1要素 | 1 | 6144 | 10 | 0 |
| 分散1% | 100 | 609792〜614400 | 10 | 0 |
| 全変更 | 10000 | 960000 | 10 | 0 |

1命令の変更でも転送は64矩形のchunk単位で、96 byteだけとは言わない。複数slotではそのslotが見ていない数frame分を追従するので、単一frameの6KBより増える場合がある。
GPU buffer/tableはslot生成時に確保済みで、allocation counterはその割当を最初のPrepareへ帰属させる。warmで0でも常駐メモリが0という意味ではない。

## 3 frame in flightのheadless throughput

PR #105上、1万矩形、2独立runの範囲。frame/s。Present/VSyncや表示FPSではなく、更新・prepare・record・submit・query read・最終drain・解放込み。

| 更新 | legacy | 旧保持＋平坦化 | direct |
| --- | ---: | ---: | ---: |
| 静止 | 843〜938 | 815〜933 | 2063〜2085 |
| 1要素 | 650〜662 | 459〜465 | 1961〜2001 |
| 連続1% | 641〜704 | 432〜469 | 1887〜2055 |
| 分散1% | 666〜713 | 403〜412 | 1631〜1674 |
| 全変更 | 694〜696 | 141〜147 | 542〜567 |

局所更新で約3倍のthroughputを確認できた。一方、全変更ではdirectがlegacyより遅い。2 runしかないprocess throughputのp95は算出していない。

## 残った退化と代償

全変更のCPU更新中央値はlegacy 0.2312ms、direct 0.6595ms。Prepareは0.7138ms→0.1310msに減ったが、direct側のmanaged allocation中央値は約1.76 MB/frameで、legacyの約0.76 MBより多い。
serial frameWall中央値は2.3525ms→2.1949msと少し短い一方、pipeline throughputは低下した。medianだけではGC等のtailや並行条件を説明できず、allocation増加との因果の分解は追加profileが必要である。

分散1%のdirect GPU query中央値0.1950msはlegacyの0.1641msより大きかった。CPUは軽くなっても、離れたcopy範囲・barrierがGPU側に安いとは限らない。uploadの粗密に応じたcoalescing/full-run転送の選択は次の課題。

GPU常駐bufferのコストもある。1万矩形ではslotごとに約1.92 MBのupload/data論理payload容量、3 slotで約5.76 MBにtable/pipeline等が加わる。これは容量からの計算で、VRAM/RSSの実測ではない。CPU snapshot/pageも保持する。

## 検証と計測条件

- CPU: 63/64/65/1000/1024/1025/10000命令×5変更条件、35 case×132 frame。入力分離・旧snapshot・拒否時の不変性を確認。
- GPU: 作成したactive deviceがAMD 1002:1586、Radeon 8060S/RADV、64bit timestamp対応であることを全runで確認。
- 1,000/1,024/1,025/10,000命令×静止/1要素/連続1%/分散1%/全変更×3方式。serialとdepth1/3 pipelineを比較。
- 各caseはwarmup41＋測定120 frame。初期scene構築・GPU slot生成の時間は今回は測定していない。
- main→#105→#105→main、各2 process、方式順も反転。同じ固定sourceで全build/check後に直列実行。clock/powerは固定しない。
- .NET 10.0.12 / SDK 10.0.401、Release、TieredCompilation=0/TieredPGO=0、workstation GC。Ubuntu26.04、Mesa26.0.8。
- warm stage 28,800 frame、pipeline 57,600 frame。各stage groupは240 frameからmedian/p95を計算し、outlierを除外しない。
- 3方式の画素一致240 groupに加え、alpha重なりを含むslot差分追従fixtureも成功。4 run・方式・depth・baseline間の対応hash一致を確認。
- GPU完了前のresident公開、未送信準備/recordの誤公開、busy slot、二重Recordを拒否。failed Queue.Submitのfault injectionとdevice loss回復は未検証。
- [全集計JSON](data/drawing2d-direct-pages-20261102/summary.json)、[raw gzip/SHA-256 manifest](data/drawing2d-direct-pages-20261102/manifest.json)。全frame/throughput/counter/source hash/build/checkを保存。

## 制約と次の判断

対応は固定順・固定個数・identity transform・clipなし・単色矩形・固定1024 targetのみ。任意path/text、reorder、clip/property scope、layer/effect、device epoch復旧、資源hot reloadは含まない。未対応入力は拒否する。

この限定経路では保持をRendererまで繋ぐことで利益が出た。ただし全更新のbulk表現/割当と、分散更新のcopy範囲選択は改善余地がある。一般Drawing2D/UIへ採用する前に、これらと未対応セマンティクスを検討する。製品の置換や既存PRの変更は行っていない。
