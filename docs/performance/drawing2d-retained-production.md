# Drawing2D 保持経路の製品実装と検証

- 日付: 2026-11-03
- 測定source: `c217c08847db36802fc96f18445652f560a101af`
- 対応: mutableな配置木、不変のCPUページ、順序付きのGPU実行列

## 製品として追加した境界

`DrawingContent2D` は64命令の不変ページを保持し、`WithPage` は他のページを共有する。`DrawingScene2D` は一般の挿入・削除・親変更・順序・transform・clipを管理する。IDはscene/slot/世代で検証し、循環・失効ID・不正な派生変換の更新を拒否する。

通常は `Borrow` を保持してPrepareを待ち、その後CPUを編集する。必要な利用者だけが `CaptureSnapshot` で独立した配置の過去状態を取得する。不変contentとclipは共有する。未変更の借用は配置計画を再構築しない。

rendererは最大16ページ（1,024命令）の内容と配置属性をcache keyへ含め、同じdevice/target/属性で完了済み資産を再利用する。順序は資産keyに含めない。CPUページとGPU runは別単位なので、一ページの変更でも該当runを再準備する場合がある。固定4group・矩形だけの試作を製品へコピーした実装ではない。

既存のcommand renderer登録表と標準矩形fast pathを通す。独自rendererは、標準batchへ委譲して返す場合も含めて毎frame準備する。消費済みPreparedDrawを使い回さず、各frameに新しい `PreparedDrawingFrame2D` を返す。

### 所有権と例外経路

Frameは実際のSubmit結果を借用し、Completed・commands Disposed・device Readyを確認してRetireする。Recordが途中で失敗したframeは送信結果をAttachできない。未記録の破棄、明示的な未送信abort、busy frame拒否、renderer破棄後も既存frameの資源を保持する条件を検証した。

資産は同時frame間で排他的に借用し、使用中のbuffer/tableを変更しない。既定は3 active frame、512 idle asset、64MiBのidle資産概算。除外時のDisposeが失敗した資産は再利用cacheから外し、cleanup retryまで隔離する。失敗したRetireの使用権を、成功したものとして返さない。

未完了・待機取消・Submit成否不明・device喪失を通常完了へ変換しない。汎用的なGPU fault injection、全device-loss復旧経路は検証していない。借用texture/viewの生存と正しい送信結果の対応付けは利用者の責務である。

### UI

`UiDrawingScene2D` は既存UiElementのID・layout・無効化へ接続する任意adapterで、別のUI所有権treeは作らない。TextBoxの文書・選択・focus・scroll等で局所contentを更新する。未知のOnRecordは毎回呼び、明示cacheはInvalidateDrawingを必要とする。独自RecordOverrideの順序は分割せずopaque記録で維持する。

既存Application hostを自動変換していない。visual host・画面遷移・除外がある場合は既存surface記録へfallbackする。時刻や提示のreceiptをcache hitで省略する変更はない。

非公開メソッド名検索で発生したtrim解析エラーは、virtual delegateから正しい実メソッドを取得することで解消した。NativeAOTの実行では、cacheした独自要素、動的要素、独自描画順の分類と無効化を確認した。

## 検証

- Drawing2D 125、UI 340、TextEditing 41、UI.Application 80、RenderGraph 77、Resources.Graphics 48。合計711 test成功、skip 0。
- Radeon 8060S: byte一致59比較。混在path/gradient/clipと129命令のページ境界、挿入・削除・親変更・順序、明示snapshot、Prepare後CPU変更、renderer破棄後frameをRgba8Srgb/Rgba16Floatで確認。
- 新UI adapterも実UiSurface/TextBox/Parleyを使い、文字編集・viewport変更を両formatで比較。
- strict Release build、全Markdown、差分空白を検査。trim解析は新規ILエラー0、NativeAOT smokeも実行成功。

旧benchmark-only試作の値を製品実装の性能値に転用しない。性能測定は下記の同一sourceを固定して、独立processで順逆実行する。

## 測定境界

矩形は合計1,024/10,240件、二つの共有content配置、target256×256。static、一ページ64命令更新、全page更新、group移動、順序変更を使う。全page更新は専用bulk builderではなくWithPageの反復である。

実UIは既存4 fixtureのlayerを製品sceneへ置く比較で、target1280×720。新UI adapter自体の性能測定ではなく、adapterの画素検証とは区別する。baselineにも同じ入力・scene更新を行い、描画準備の境界を比較する。従来アプリ全体のCPU費用ではない。

21 warmup / 40 measured frame、depth1/3、2 process、mode順反転。mandatory correctnessの3 warmup / 4 frameは同じUI case名を含むが、正式分布に混ぜない。各caseのserialized中央値/p95は80 frame、pipeline throughputは独立2観測であってp95ではない。

全elapsedはfinal drainとframe retirementを含む。cache ownerの最後の破棄はwindow外なので、別時間と、それを加算した補正FPSを併記する。GPU spanはclear/upload/barrier/renderを含む記録区間のdevice時間で、占有率やshader専用時間ではない。allocationはprocess-wide GC counterで計測用objectを含む。idle bytesは概算でdriver VRAMではない。

## 再現

```bash
source tools/setup/activate.sh
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Drawing2D.Tests
dotnet run --project build -c Release -- --target Test --project tests/UI/Lumyte.UI.Tests
dotnet run --project build -c Release -- --target Test --project tests/UI/Lumyte.UI.TextEditing.Tests
dotnet run --project build -c Release -- --target Compile --project tools/benchmarks/drawing2d-incremental
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export WORKLOAD_REPO_ROOT="$PWD"
export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_gcServer=0
export DRAWING_BASELINE="$(git rev-parse HEAD)"
dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --production-retained-checks
RETAINED_REVERSE=0 dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --production-retained-hardware > run-1.jsonl
RETAINED_REVERSE=1 dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --production-retained-hardware > run-2.jsonl
python3 tools/benchmarks/drawing2d-incremental/analyze-production-retained.py . summary.json
```

測定中に他のbuild/test/timingを実行しない。counter・profile・画素の全case数を集計scriptで検証し、欠落を成功として扱わない。変更領域だけの描画・提示、RenderGraph layer/effectの保持編集、GPU group uniformだけの移動はこの直接経路の対象外。

## 測定結果

正式4480 frame（2 process×56 profile×40 frame）、serializedは2240 frame。全profileでGPU query数・sample数と、対応する最終画素の一致を検証した。mandatory correctnessの202比較は正式分布と別に保持する。

### Serialized frame CPU wall（ms、中央値 / p95）

| workload | 件数 | 既存Canvas | 保持経路 | 中央値の増減 |
| --- | ---: | ---: | ---: | ---: |
| rect-static | 10,240 | 2.238 / 2.715 | 1.431 / 1.643 | -36.1% |
| rect-page | 10,240 | 2.624 / 5.249 | 1.547 / 1.750 | -41.0% |
| rect-whole | 10,240 | 3.247 / 13.609 | 4.168 / 14.456 | +28.4% |
| rect-group-move | 10,240 | 2.621 / 7.252 | 3.110 / 3.694 | +18.7% |
| rect-order | 10,240 | 2.590 / 6.005 | 1.363 / 1.878 | -47.4% |
| game-hud-static-overlay | 64 | 18.509 / 19.932 | 12.682 / 13.701 | -31.5% |
| game-hud-values | 64 | 19.107 / 22.434 | 28.785 / 48.561 | +50.7% |
| editor-idle | 64 | 317.461 / 362.498 | 122.823 / 129.354 | -61.3% |
| editor-line-edit | 64 | 322.445 / 336.961 | 297.015 / 369.021 | -7.9% |

### Prepare中央値 / process-wide割当中央値

| workload | 既存Prepare ms | 保持Prepare ms | 既存 B/frame | 保持 B/frame |
| --- | ---: | ---: | ---: | ---: |
| rect-static | 0.766 | 0.005 | 73,800 | 65,600 |
| rect-page | 0.776 | 0.138 | 1,803,616 | 237,864 |
| rect-whole | 0.782 | 1.376 | 3,754,752 | 3,935,992 |
| rect-group-move | 0.762 | 1.254 | 1,794,976 | 1,976,840 |
| rect-order | 0.760 | 0.005 | 1,794,680 | 65,600 |
| game-hud-static-overlay | 3.032 | 0.044 | 4,100,068 | 713,400 |
| game-hud-values | 4.212 | 11.024 | 4,165,648 | 4,019,960 |
| editor-idle | 142.715 | 0.015 | 39,494,128 | 6,010,536 |
| editor-line-edit | 144.649 | 136.801 | 41,660,476 | 40,018,580 |

### Depth3 headless throughput（独立2 processのFPS、表示FPSではない）

| workload | 既存2観測 | 保持2観測 |
| --- | ---: | ---: |
| rect-static | 770.61 / 839.00 | 2674.69 / 2095.82 |
| rect-page | 456.09 / 459.64 | 1665.22 / 1668.27 |
| rect-whole | 247.86 / 229.15 | 205.25 / 191.67 |
| rect-group-move | 334.93 / 555.08 | 443.64 / 466.38 |
| rect-order | 480.10 / 488.15 | 1991.69 / 2011.91 |
| game-hud-static-overlay | 44.34 / 41.38 | 81.67 / 83.24 |
| game-hud-values | 43.30 / 44.64 | 38.21 / 37.02 |
| editor-idle | 1.63 / 1.42 | 6.92 / 6.88 |
| editor-line-edit | 1.38 / 1.39 | 2.30 / 2.25 |

cache owner破棄を含めた補正FPS、各process中央値、全1,024件結果、GPU span、cache量は [summary.json](data/retained-production-20261103/summary.json) に保存する。

### 判断

- 静止と局所更新・順序変更では保持経路を使う理由がある。editor-idleは317→123ms、静止overlay HUDは18.5→12.7msで、未変更資産の再利用を確認した。
- **game-hud-valuesは19.1→28.8msに退化**した。root/layer単位で内容が変わると再準備とcache管理が増えるため、動的HUDへ一律適用しない。既存Application hostを自動切替していないことが重要である。
- 全件更新は3.25→4.17ms、group移動は2.62→3.11msに退化した。常時全変更する用途は既存の直接Canvas経路を維持する。親属性のGPU uniform化や専用bulk入力の利益を、この実装の実測として主張しない。
- editor-line-editは中央値が少し下がったがp95は悪化した。各processで40 compile・0 reuseで、UI layer全体を差し替えるこのfixtureには局所cache hitがない。新UI adapterのleaf単位再記録と画素一致は別検証であり、その速度をここから推定しない。
- 任意のCPU snapshotにしただけでGPU/encode費用が消えるわけではない。全用途を新しい保持経路へ統一せず、静止・局所更新とbulk更新を選び分ける。

## 証拠の扱い

[raw manifest](data/retained-production-20261103/manifest.json) はaccepted・checks・rejected-disposal-hardeningを区別する。最初の7c0fc08の計測はcleanup例外経路の追加修正前なので採用しない。c217c08の全matrixを順逆2 processで再実行し、旧値を混ぜず集計した。

公開ログはhome/checkout/host/userを伏せ、公開コピーのbytes/SHA256と元SHA256を区別する。全C# source hashが凍結時から変わっていないことを確認する。公開gzipからも同じsummaryを再生成できる。

> NOTE: このレポートと保存済み JSON・gzip・manifest は、API 統合前の凍結コミット `c217c08` の計測記録です。現在の公開 API は `DrawList2D` を直接 scene に渡し、ページ構造を内部に保持します。本文の旧内容型・ページ更新の記述は凍結時点の実装を指し、現在の公開編集 API や統合後の性能測定を示すものではありません。
