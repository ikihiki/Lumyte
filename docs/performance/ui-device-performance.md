# UI Windows実機性能調査

2026-11-01、UIスタックの実機描画を調査し、shader情報のキャッシュと短い描画プログラムの作業配列を改善しました。本体と回帰テストは描画担当のPR #79、測定ハーネスと全frameの記録はPR #97へ置きます。

## 環境と範囲

| 項目 | 条件 |
| --- | --- |
| 変更前 | UI最終head `75ce145e281c14edf29c6f238fa5604586cb5594` |
| 実装commit（PR #79） | cache `dbe2e353`、shader `97f8eee4`、Vulkan caps `c8a18239` |
| CPU | AMD Ryzen 7 7800X3D、8 core / 16 thread |
| GPU・backend | NVIDIA GeForce RTX 4080 SUPER、wgpu-native / Vulkan、driver 617.14 |
| OS | Windows 11、build 26200 |
| .NET・Slang | SDK 10.0.401、runtime 10.0.12、Slang 2026.19、Release |
| runtime設定 | tiered compilation無効、workstation GC |
| Window | 論理800×600、DPI 1.5、描画先1200×900、FIFO |
| 各case | warmup 5 frame、測定30 frame、単一Window・直列提出 |

前回の約66msは、Canvasを実DPIへ拡大する処理が不足したハーネスでの値です。今回は標準hostと同じ拡大とviewport clipを加え、1200×900の描画先を満たす条件で測り直しました。前回の絶対値との差を改善量として扱いません。

[ハーネスと再実行手順](../../tools/benchmarks/ui-device/README.md)を保存しています。実際の標準presenterを使用しますが、application loopや複数frameの重なりは含みません。時間は`Stopwatch`の壁時計で、GPU timestamp・画面走査の完了・入力から表示までの遅延ではありません。各frameの生データを残し、以下は中央値です。段階ごとの中央値の和は全体の中央値と一致しない場合があります。

## shader情報の共有

buffer生成ごとの`ShaderArtifact.GetTarget`による全targetの走査・code/reflectionのコピーと、`ShaderDataLayout.Data`・`Root`・`HasRoot`によるreflection再解析を除きました。

`Data(artifact, target, name)`でartifactごとの展開済みmetadataとlayoutを共有し、Wgpu・Vulkan・Direct3D12・Browserから利用します。既存のtarget引数のAPIも不変のtargetごとに共有します。公開されるcode配列のコピー契約を維持し、異なるartifact・target・同名schemaを混ぜません。初期構築とlayout公開はlockで保護し、成功したlayoutだけを保存します。ownerを`ConditionalWeakTable`で弱く結び付け、破棄済みartifactやGPU資源をprocess全体へ保持しません。

## 完了待ちの原因と改善

汎用fragment shaderは、単純な矩形でも256要素の`float4`作業配列を持ち、4×4 coverageの各サンプルで全要素を初期化していました。短いプログラムには8・16要素の特殊化を使い、17命令以上には従来の256要素の経路を残しました。配列の初期化、命令上限、paint・clip・graphの評価順と16点のcoverageを維持しています。

キャッシュだけでは100矩形の`drain`が約183ms残り、作業配列の改善を加えると約8msになりました。途中の16要素だけの候補も約12msまで減り、8要素を追加するとさらに短縮しました。shader変更のA/Bにより、長い完了待ちの大部分がこの描画経路に起因すると判断します。GPU timestampやcompilerのspill量は測っていないため、ALU・register・local memoryの寄与までは分解していません。

通知pollingを含む空queueのSubmit→完了は中央値2.59msでした。Immediate提示の100矩形でも`drain`は7.74msで、FIFOの約8msと近い値です。通知待ちや提示modeの影響は残りますが、元の大きな待ちの主因とは考えにくい結果です。提示modeの製品既定値はFIFOを維持しています。

## 結果

100矩形の静止描画の主比較です。割当は全threadのmanaged allocationの近似値で、GPU memoryや常駐量ではありません。

| 指標 | 変更前 | cacheのみ | cache＋shader |
| --- | ---: | ---: | ---: |
| 全体 ms/frame | 456.63 | 190.51 | 14.02 |
| 準備 ms/frame | 142.80 | 1.28 | 1.19 |
| コマンド記録 ms/frame | 123.97 | 4.42 | 2.80 |
| 完了待ちを含むdrain ms/frame | 183.09 | 182.83 | 8.18 |
| managed割当 MB/frame | 133.68 | 4.09 | 4.05 |

最適化版の再測定は全体13.62ms・drain 8.07msでした。製品用の計測callbackを外したbuildも全体13.19ms、色変更13.85ms、割当約4.05 / 4.08MBでした。短時間の逐次測定なので、持続FPS・p99・任意のUIへの速度比を保証する数値ではありません。100個の矩形ごとのbuffer・argument table・render pass等の生成と、約4MB/frameのmanaged allocationは残っており、次の改善候補です。

## 測定の追跡

主比較は同じハーネスDLL・同じ計測用presenter DLLへ揃え、先にbuildを終えて実行ディレクトリを保存してから順次実行しました。`cache-only`は正式なcacheのbuildに変更前のDrawing2D DLLを組み合わせ、shader変更だけを外した切り分けです。計測用presenterの差分は任意適用の[パッチ](../../tools/benchmarks/ui-device/instrumentation.patch)へ保存し、製品コードには含めません。

- 主比較: [baseline](data/ui-device/baseline.jsonl)、[cache-only](data/ui-device/cache-only.jsonl)、[optimized](data/ui-device/optimized.jsonl)、[optimized再測定](data/ui-device/optimized-repeat.jsonl)。
- 追加切り分け: [16要素のみ](data/ui-device/small16-first.jsonl)、[同再測定](data/ui-device/small16-repeat.jsonl)、[8要素候補の初回](data/ui-device/small8-first.jsonl)、[同再測定](data/ui-device/small8-repeat.jsonl)、[Immediate](data/ui-device/immediate.jsonl)、[空queue](data/ui-device/queue-probe.jsonl)。
- 製品状態: [計測callbackなし](data/ui-device/production.jsonl)。
- 初期比較も保持: [baseline初回](data/ui-device/baseline-initial.jsonl)、[cache初回](data/ui-device/cache-only-initial.jsonl)、[ハーネスDLLを揃える前のbaseline](data/ui-device/baseline-before-harness-copy.jsonl)。各ファイルのLabelと構成を確認してください。
- [全caseの集計](data/ui-device/summary.json)、[DLLのSHA-256](data/ui-device/binary-manifest.json)、[ソースのSHA-256](data/ui-device/source-manifest.json)。

`summarize.ps1 -Paths <JSONL一覧>`で集計を再生成できます。1/10/100矩形の静止・色変更をすべて残し、好都合なcaseだけを抽出していません。各実行のcase間でpixel areaを一定にしているため、矩形数と時間の関係を単純な比例として解釈しないでください。

## 検証

- 最終スタックとPR #79単独で、実GPUを有効にしたGraphics全103件が成功。
- cacheの並行共有、artifact/target/schema分離、未収録target・欠落schema・未対応ABI、ownerのGC回収とcodeコピーを回帰検証。
- GPU画素で8/9・16/17・256命令、既存のgeometry・paint・composite・画像・glyph・layer・effect・backdrop・UI redrawを検証。WgpuとVulkanを実行。
- [変更前](data/ui-device/baseline-pixels.jsonl)、[最適化版](data/ui-device/optimized-pixels.jsonl)、[計測callbackなし](data/ui-device/production-pixels.jsonl)の6 caseすべてで、期待色のassertと全RGBAのSHA-256一致を確認。
- Drawing2Dの単体テスト102件、BrowserとDirect3D12 backendのRelease buildが成功。Browser/D3D12の実機画素は今回の実行範囲に含めない。

全Graphicsテストで、実Vulkan GPUがstorage binding上限約4GiB・buffer上限2GiBを返すため、既存の共通Caps契約テストが失敗しました。公開するbinding上限を実際に生成可能なbuffer上限へ丸め、全103件の成功を確認しました。キャッシュとshaderの性能差を、このCaps補正の効果として扱っていません。
