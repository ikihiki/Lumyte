# 保持描画レビュー対応と公開 API の更新・batch 境界

> 2026-11-14: docs/performance/dataの圧縮バイナリ生データは削除しました。
> JSON／CSV集計とmanifestは保持していますが、記載されたarchiveは現行treeには存在しません。

- 対象: PR #108 のレビュー指摘と追加計測
- 修正・測定 source: `916b6de`
- CPU 比較元: `1c4470d10d83ced90c78b06ff2ec0e7b53be38aa`

## 所有権と更新処理の修正

### 古い返却の再試行

返却済み状態を、再貸出される Asset の共有フラグから frame 固有の使用権へ移した。cleanup 失敗後に別 frame が同じ資産を借用しても、古い Retire の再試行はその資産を再公開・再破棄しない。完了した使用権は枠数も二重に減らさない。

回帰 test は、idle 予算1・資産A/B・AのDispose失敗・Bの次frameへの貸出・古い返却の再試行・第三frameの取得を跨いで、Bが再貸出も破棄もされないことを確認する。

### Prepare 失敗後の cleanup

未送信が確定した枠の返却と資産 cleanup を分離した。cleanup が失敗しても枠は一度だけ返し、失敗した資産と部分 batch は cache が所有する。次の Prepare または renderer.Dispose で再試行する。永続的な cleanup 失敗はその例外として通知し、存在しない使用中 frame による busy と取り違えない。

部分 run の全 batch に cleanup を試み、成功済みを二重処理せず、失敗分だけを保持する。準備と cleanup の両方が失敗した場合は元の準備例外と cleanup 例外を AggregateException に保持する。未完了・送信成否不明の GPU 使用を完了扱いする変更ではない。

### 静止 UI の兄弟検索

Move の index 検証後、同じ親の指定位置に既に対象がある場合は直接終了する。静止 adapter 更新で全 placement の位置を確認しても、各々で Children.IndexOf を実行しない。実際の挿入・削除・並べ替えと、借用中・不正 index・循環の拒否は維持する。深い木での祖先検証まで定数時間と主張するものではない。

## 公開 API による測定

内部 WithPage は使わない。合計1,024個の矩形命令を1/16/256個の内容ノードへ分け、通常の Canvas.End() で記録する。一命令変更時も変更ノード全体を新しく記録し、公開 Scene.ReplaceContent を呼ぶ。したがって一ノード当たりの更新命令数は1,024/64/4となる。

比較する Canvas は、scene の内部配列や flattening を再利用せず、同じ図形を独立に記録する。両 mode は同じ入力・scene 更新を行い、準備の境界を比較する。legacy 側の全アプリ CPU 費用を表すものではない。

実 UI は、標準の UiSurface.CreateDrawingScene と UiDrawingScene2D.Update を使用する。CPU 更新は Panel に100/1,000/10,000個の兄弟矩形を置き、レビューが指摘した平坦な兄弟集合を測る。GPU は100/1,000個の矩形を Stack の格子に置き、実 adapter の Prepare/Record と独立した UiSurface.Record の画素を比較する。10,000件の GPU 描画性能を測ったとは扱わない。

静止時の LastRecordingCount は0。一つの葉の fill を変更すると、空の祖先 fragment も無効化されるため、flat Panel では2、grid Stackでは3となる。これを「一命令変更なら一回しか再記録しない」と読み替えない。UI の要素数は fixture が作成した公開要素数を記録し、内部 scene のノード数は公開 API から取得できないため null とする。

### draw 数と cache 予算

IRenderEncoder を透過的に包み、実 backend へ成功して転送した Draw/DrawIndexed/DrawIndirect/DrawIndexedIndirect を数える。renderer の compile/reuse 数から推定した draw 数ではない。一方、GPU 内部の primitive 数・占有率を示すものでもない。この包みによる小さな計数 overhead は record 費用に含む。

renderer は既定の512 idle asset・64MiB概算予算、最大3 frameを使う。このprobe自体は直列depth1である。UI1,000件ではasset予算不足による除外・cache missを含み、静止なら全資産が再利用されるとは仮定しない。ノードを跨ぐ batch 統合は追加していない。

### 条件と集計

Radeon 8060S を測定 device の実 handle から検証し、software ICD の負の対照は明示的に拒否する。256×256 Rgba8Srgb、10 warmup / 20 measured frame、独立2 process・順序反転。tiered compilation/PGO/server GC は無効とし、clock/power設定は変更しない。

CPU 比較元にも同じ probe source を置き、製品 source は1c4470dのままにする。before CPU → after CPU/GPU → after逆順 → before CPU逆順と直列実行し、同時のbuild/test/timingは行わない。

中央値/p95は各case40 frameの分布で、独立processごとの中央値も保存する。GPU timestampはclear/upload/barrier/renderを含む記録区間で、初期target barrierは除外する。record/finishはquery終端記録も含む。SubmitとGPU完了待ちは別に計り、GPU spanをCPU待機時間・shader専用時間・占有率と混同しない。Present/VSync/表示FPSの測定ではない。

全profileでsample数、実際のdraw数、LastRecordingCount、有限なGPU query値と参照画素を検証してから集計する。各seriesの前に両状態を比較し、実際に画素が変わることも確認する。readbackとJSON出力はtiming window外である。

## 回帰検証

- Drawing2D 143、UI 340、TextEditing 41、UI.Application 80、RenderGraph 77、Resources.Graphics 48。合計729 test成功、skip 0。
- 既存の実GPU59比較成功。混在path/gradient/clip、両色形式、ページ境界、snapshot、CPU更新とGPU資産寿命を確認。
- NativeAOT UI記録分類・無効化smoke成功。
- CPU/GPUの実測値は以下の新しい公開API結果だけを使用し、旧privateページ編集の値で代用しない。

## 再現

```bash
source tools/setup/activate.sh
dotnet run --project build -c Release -- --target Compile --project tools/benchmarks/drawing2d-incremental
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export WORKLOAD_REPO_ROOT="$PWD"
export DRAWING_BASELINE="$(git rev-parse HEAD)"
export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_gcServer=0
export PUBLIC_RETAINED_WARMUP=10 PUBLIC_RETAINED_SAMPLES=20
PUBLIC_RETAINED_REVERSE=0 dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --public-retained-api > current-1.jsonl
PUBLIC_RETAINED_REVERSE=1 dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --public-retained-api > current-2.jsonl
# CPU比較元は同じharnessを1c4470dへ置き、--public-retained-ui-cpuを実行する。
python3 tools/benchmarks/drawing2d-incremental/analyze-public-retained-api.py raw-directory summary.json
```

## 実測結果

CPU480 frame、GPU800 frame、独立Canvasとの120画素比較を検証した。各caseの分布は独立2 processを合わせた40 frameである。

### 平坦な兄弟UIのUpdate（ms、中央値 / p95）

| 兄弟矩形数 | 状態 | 変更前 | 修正後 |
| --- | --- | ---: | ---: |
| 100 | static | 0.0242 / 0.0261 | 0.0145 / 0.0162 |
| 100 | one-leaf-fill-change | 0.0352 / 0.0481 | 0.0174 / 0.0197 |
| 1,000 | static | 0.7973 / 0.8146 | 0.1382 / 0.1553 |
| 1,000 | one-leaf-fill-change | 0.8081 / 1.0380 | 0.1406 / 0.1496 |
| 10,000 | static | 62.9500 / 64.3468 | 1.4773 / 2.8098 |
| 10,000 | one-leaf-fill-change | 65.8137 / 68.5057 | 1.4419 / 2.8786 |

1万兄弟の静止Updateは62.95→1.48ms、葉一つ変更は65.81→1.44ms。静止LastRecordingCount=0は前後とも同じであり、再記録を省いたからではなく兄弟の線形検索を除いた改善である。残る全要素走査はあるため、イベント駆動で更新自体を省略した結果ではない。大きな外れ値も捨てず、独立process別中央値と全分布をsummaryへ保存した。

### 公開ReplaceContentで一命令変更（1,024矩形、ms中央値）

| 内容ノード数 | 変更時の再記録命令数 | mode | 実issued draw数 | Prepare | Record/Finish | GPU span |
| ---: | ---: | --- | ---: | ---: | ---: | ---: |
| 1 | 1024 | independent-canvas | 1 | 0.0713 | 0.0325 | 0.0194 |
| 1 | 1024 | public-retained | 1 | 0.1629 | 0.0359 | 0.0203 |
| 16 | 64 | independent-canvas | 1 | 0.0694 | 0.0266 | 0.0199 |
| 16 | 64 | public-retained | 16 | 0.0362 | 0.3059 | 0.0730 |
| 256 | 4 | independent-canvas | 1 | 0.0703 | 0.0278 | 0.0199 |
| 256 | 4 | public-retained | 256 | 0.2220 | 4.5490 | 0.9680 |

再記録命令数は共通の入力/scene更新で作り直す内容ノードの大きさで、legacy referenceは別途1,024命令を記録する。小分割でprepare量は下がっても、drawとrecord費用は増える。今回の単純矩形では16分割も256分割もrecord増を打ち消せず、合計費用が改善したとは言えない。

### 実UiDrawingScene2Dの一葉変更（ms中央値）

| 矩形葉数 | mode | 実issued draw数 | Prepare | Record/Finish | GPU span | compile/reuse |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 100 | independent-canvas | 1 | 0.0223 | 0.0348 | 0.0213 | 0/0 |
| 100 | public-retained | 100 | 0.0638 | 1.7155 | 0.5302 | 1/99 |
| 1,000 | independent-canvas | 1 | 0.1593 | 0.0319 | 0.0248 | 0/0 |
| 1,000 | public-retained | 1000 | 9.9238 | 26.9650 | 5.1993 | 489/511 |

1,000葉の保持経路では489 compile/511 reuseとなった。これは一葉の更新だけでなく、既定512 asset予算に収まらない資産の除外・再準備を含む。空の祖先2fragmentと変更葉の計3fragmentを再記録する。

### 判断と残る制約

- 今回の修正で未変更UIの二乗的兄弟検索を除いたが、GPU側のノード間batch統合は追加していない。
- **多数の単純矩形を一要素一内容ノードへ分ける現在のadapterは、既存Canvasより有利ではない。** このfixtureで保持経路を標準hostへ自動適用する根拠にはならない。任意導入を維持し、そのような用途では既存Canvasを選ぶ。
- 大きなDrawListのReplaceContentは全ページが新しい参照になる。内部WithPageの疎更新結果を、公開APIでの更新費用として転用しない。
- 内容ノードを細かくするほど常に速い、というAPI説明をしない。利用者が選ぶ更新単位と実行batchの結合は現状トレードオフであり、将来のcross-node batching/別の更新モデルは別途設計・計測が必要。
- 静止・動的の全case、p95、独立process別値、retire費用、GPU spanは [summary.json](data/retained-public-api-20261103/summary.json) に保存する。

## 生データと来歴

[manifest](data/retained-public-api-20261103/manifest.json) は採用した4独立processと検証ログを区別する。未完成probeのsmoke・不一致で停止したsmokeは正式集計に含めない。公開コピーはhome/checkout/host/userを伏せ、元SHA256と公開コピーのbytes/SHA256を別々に保存する。C# source hashは凍結後不変を確認し、同じharnessを比較元へコピーしている。測定中の変更はこのMarkdown草稿のみであり、metadataのsourceDirty=0は凍結した測定対象C#sourceについての値である。
