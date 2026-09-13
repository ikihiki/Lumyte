# Native feature RenderGraph

`NativeRenderProvider` は共通の機能 graph を DirectX 12／Vulkan 用の内部 pass に展開します。
外部 assembly は `NativeRenderPassRegistry` と `INativeRenderPass<TRequest,TResult>` を使い、
公開された build／record context から機能を追加できます。production の `InternalsVisibleTo` は使いません。

内部の `Read` は先行内容の読取り、`Write` は全範囲初期化、`ReadWrite` は内容を保持する更新です。
共通出力と明示した副作用から必要な pass を残し、使われない writer を除去して未初期化読取りを検出します。
共通宣言との対応、実行をまたぐ内部参照、重複名、callback 終了後のアクセスも確認します。
内部使用から texture usage を確定し、scope、view、descriptor と batch を準備してから記録します。
DirectX 12 は texture transition と buffer barrier、Vulkan は General layout と global dependency を使います。

`NativePassTemplate<TState>` は構築手順を再利用し、`NativePassPreparationCache<TKey,TValue>` は
意味を持つ key ごとに不変 CPU 準備を保存します。cache は件数上限付き LRU です。
provider は内部の生存 pass と寿命の計画を最大 64 件保存し、今回の参照へ index で対応付けます。
`NativeRenderRuntime.PreparationStatistics` で hit／miss と保存数を確認できます。
実行ごとの callback と command 記録は作り直し、cache に古い bindings や GPU 資源を残しません。

同じ description の transient は寿命が重ならなければ同じ物理 buffer／texture を再利用します。
output／export／import はその対象から外し、未完了の execution 間で領域を使い回しません。
異種 resource object を重ねる heap alias は基準実装の対象外です。

`RegisterContent(content, lease, writers)` は内部 writer が受理されるまで GPU 内容世代を公開しません。
同じ manager の受理済み token を渡す overload は、独立した private upload を登録します。
`TryUseContent` は保持と結果依存を同時に取得し、失敗・未受理・culling 済みの内容を再利用しません。
ticket の owner を返却しても取得済み reader と writer の GPU 保持は残ります。
診断失敗は速やかに結果へ伝播し、資源の回収は別途 GPU 使用終了を待ちます。

Submit は最初の await より前に共通 import の使用保持を取得し、pass 内の managed import も直ちに保持します。
execution の export pin と GPU batch の保持は独立しています。`StopAccepting()` は新しい work を閉じ、
終了処理は受理済みの build／upload と GPU 使用を drain します。scope、pin と execution は caller が返します。
lease の実際の返却は Collect／終了処理と直列化し、返却に失敗した lease は再試行せず隔離して通知します。

`NativeGraphResources.ImportBuffer`／`ImportTexture` は raw Native 資源を所有 lease 付きで接続します。
戻り値の `Reference` を共通 graph に渡します。texture は General layout で受け渡し、必要なら同じ
runtime の manager が main queue に受理した先行 token を指定します。生の heap／resource の寿命、
実際の description、外部との順序と重複しない論理 import は caller が保証します。

共通 package upload は `images.sampled` version 1 の準備済み Linear／Premultiplied または Opaque な
RGBA8／BGRA8 を扱います。CPU の row／slice stride が 0 の場合は tightly packed とし、Native の転送 pitch へ
明示的に並べ替えます。file／URI の取得、復号、色変換は `Lumyte.Resources` の担当です。

各公開 API、使用例、所有契約とコード配置は [ADR 0031](../../../docs/adr/0031-native-render-graph-implementation.md) を参照してください。
Model／2D／mesh 等の個別機能は各 pass library の担当です。公開 manager を直接操作する作者は
下位 manager の直列化と明示所有の契約に従ってください。
