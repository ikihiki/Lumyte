# ADR-GRAPHICS-0031: レンダーグラフの構築とコマンド記録を分けて計測する

- 状態: 提案
- 日付: 2026-11-11
- 基準コミット: `8ea844540cd98d2beb70eebb132e6086be66e662`
- 変更関係: 一部変更
- 変更元: [ADR-GRAPHICS-0015「診断システムへの接続」](GRAPHICS-0015-render-graph.md#診断システムへの接続)
- 変更対象: observer が無効でも EventSource の明示購読時には CPU 区間を発行する
- 継続する判断: 公開 API、observer 通知、要求時だけの構造 capture、GPU 資源の寿命

## 背景

本体開発者は、graph の依存構築・culling と、実際の command 記録のどちらに時間が掛かっているかを調べたい。既存の compile／record 集計だけでは、pass callback、一時資源の遅延生成、barrier 記録の違いが分からず、詳細 snapshot を毎回作る必要のない調査にも構造情報の生成負荷が入り得る。

基準コードは `src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraph.cs` の `Compile`／`CompileCore` と、同 directory の `CompiledRenderGraph.cs` の constructor、`Record`、`EnsureAllocation`、`Dispose` である。`RenderGraphStatistics.cs` に固定サイズの計画件数があり、`Record` 自身は Finish・Submit・GPU 完了待ちを行わない。

## 決定

`Lumyte.Graphics.RenderGraph` assembly が所有する `Lumyte-RenderGraph-Profiling` から、下記の境界で EventSource イベントを発行する。通常の詳細計測は graph 単位の区間と件数にし、pass 単位の区間だけは追加 keyword に分け、構造 snapshot の有無から独立して選べるようにする。

### 利用者の変更前・変更後

変更前は `IRenderGraphObserver` を有効にして compile／record の集計を得るか、明示 capture で構造を調べる。変更後は呼び出しを変えず、標準ツールから provider の keyword を購読すると、その実行の区間と件数が追加される。

```csharp
using Lumyte.Graphics.RenderGraph;

// device、commands、observer の準備と pass 宣言は省略。
var graph = new RenderGraph(device, "main", observer);
// 同じ graph.Compile(frameId: 42) と plan.Record(commands) を使う。
```

利用コード、診断の Observe 権限、失敗時の command 破棄と GPU 完了後の plan 解放は変わらない。observer 未指定でも provider を購読でき、両方無効なら追加の計時と発行を行わない。

### 挿入位置と区間

stage ID は本 provider 内で固定する。以下の時間はすべて CPU 経過時間で、親区間を含む区間を足し合わせない。

| Stage ID／名称 | 基準コード上の挿入位置と開始・終了 | 含むもの／含まないもの |
| --- | --- | --- |
| 1 Compile | `RenderGraph.Compile` の既存 observer の Begin 後、`CompileCore(run, capture)` を呼ぶ直前から戻った直後 | 依存構築・culling・plan 生成と要求された snapshot。observer の通知、後続の lifetime 移譲は含まない |
| 2 Dependencies | `CompileCore` 冒頭の indexes 作成から、出力 producer を roots に加えるループの終了まで | 明示依存と resource hazard、root の構築 |
| 3 CullAndOrder | `CompileCore` の live／pending 作成直前から、循環の検証成功まで | 必要 pass の逆向き走査と、全宣言 pass に対する topological order |
| 4 BuildPlan | `CompileCore` 末尾の `new CompiledRenderGraph(...)` の直前から戻るまで | barrier 計画、統計、名前配列と、要求時だけの snapshot |
| 5 Record | `CompiledRenderGraph.Record` の既存 observer Begin 後から、末尾 export barrier の記録と `_successful = true` まで | 一時資源の必要時生成、barrier、pass callback。observer 通知、Finish、Submit、GPU 実行は含まない |
| 6 AllocateTransient | `EnsureAllocation` の `_allocations.ContainsKey` が false の後、`resource.Create` 直前から所有リストと辞書への登録終了まで | 実際に生成した resource の処理のみ。既存 import と辞書 hit は区間を作らない |
| 7 DisposePlan | `CompiledRenderGraph.Dispose` の重複・command 状態検証を通過後から、views／owned／lifetimes の解放、辞書 clear と disposed 確定まで | 許可された解放の試行。GPU 完了待ちを挿入しない |
| 8 PassCallback | `Record` の `pass.Callback(context)` 直前から、command 状態検証と `context.Close()` の finally 完了まで | opt-in の pass 別 CPU 記録区間。資源確保と事前 barrier は含まない |

CompileCore 内の失敗は段階の finally で失敗の終了を出し、既存 catch の `plan?.Dispose` と `observer.Failed` を維持する。Stage 1 が成功しても後続の lifetime 移譲や通知が失敗する場合があるため、公開 Compile の戻り／例外は別の event ID 7=`GraphOperationResult`、keyword `0x1` で operation と outcome を発行する。終了処理の時間を成功した Stage 1 の時間に混ぜず、Stage 1 の成功を公開呼び出し全体の成功とみなさない。Record も開始済みの段階を finally で閉じ、元の例外を維持する。公開 Record の結果も GraphOperationResult へ分けて残す。呼び出し前の引数・状態の拒否は、その処理を実行した区間とはしない。

Dispose の途中失敗ではその試行を失敗で閉じ、再試行を新しい Span ID で記録する。解放済み資源数は差分で記録し、予定数を実解放数として出さない。既存の所有権・再試行の順序は変えない。

### 件数と単位

Counter ID は成功した段階の終了前に一度だけ集計発行し、item ごとのイベント列は作らない。失敗時に部分値を出す場合は失敗の outcome と達成件数であることを付ける。すべて実測した件数またはその時点の既存統計で、GPU の実行件数ではない。

| Counter ID／名称 | 取得元・意味 | 単位／集計範囲 |
| --- | --- | --- |
| 1 LivePasses、2 CulledPasses | `plan.Statistics` の同名値 | pass／Compile 一回 |
| 3 Resources、4 TransientResources | `Statistics` の宣言 resource 数。unused も含む | resource／Compile 一回 |
| 5 Dependencies、6 PlannedBarriers | `Statistics.Dependencies`／`Barriers`。後者は export を含む | edge または barrier／Compile 一回 |
| 7 CreatedAllocations | `EnsureAllocation` で `resource.Create` が成功した回数 | resource／Record 一回 |
| 8 ExecutedCallbacks | `pass.Callback` と command 状態検証が成功した回数 | callback／Record 一回 |
| 9 RecordedBarriers | `RecordBarriers` 内の `resource.Barrier` が成功した回数。export を含む | barrier／Record 一回 |
| 10 ReleasedViews、11 ReleasedAllocations、12 ReleasedLifetimes | Dispose 内で Dispose 成功後に list から削除した数 | object／Dispose 試行一回 |

宣言時の TransientResources と実際の CreatedAllocations、計画上の Barriers と記録成功した RecordedBarriers を別にする。texture byte 数や GPU memory 常駐量を descriptor から推測して実測として出さない。snapshot 生成を、これらのカウンター取得の前提にしない。

### イベントと選択

公開 API の変更はない。EventSource は内部型で、共通の区間 event ID 1=`StageBegin`、2=`StageEnd`、3=`StageCounter` を使う。キーは ProfileRunId（Guid）、Frame ID（long）、provider 内の Span ID／Parent Span ID（long）。Counter は stage、counter ID、long の値を持つ。終了 outcome は 0=成功、1=取消、2=失敗、3=省略とする。定義は固定の数値で、動的な pass 名を毎回文字列化しない。

CPU 区間は `0x2`、カウンターは `0x4`、graph 相関情報は制御 `0x1`、Stage 8 だけは追加の `0x10` を使う。Stage 8 は通常の event ID 1／2 と分け、event ID 5=`PassBegin`、6=`PassEnd` の属性にも `0x10` を指定する。`IsEnabled(Informational, 0x10)` で判定し、通常の `0xF` 購読には含まれない。別 listener が pass 計測を有効化しても、`0xF` だけの収集へ pass イベントが混ざらない。pass 区間では execution index を数値 payload として保持し、pass 名と resource 内容は保持しない。EventSource の nested `Keywords` に各 bit を `public const EventKeywords` で宣言する。

### Graph の相関と Record の遅延

Compile の入口で標準 Activity の `Lumyte.Profiling.RunId`／`Lumyte.Profiling.FrameId` を読み、既存 `RenderGraphRun.GraphId` と Compile ごとに作る `RenderGraphRun.RunId` を対応付ける。後者は GraphRunId であり、実行全体の ProfileRunId ではない。event ID 4=`GraphRelation`、keyword `0x1` に両方の Guid、ProfileRunId、Frame ID、対象の root Span ID、operation（1=Compile、2=Record、3=Dispose）と、引数 `frameId` の一致状態を記録する。各 root の開始時に Control が有効なら必ず発行し、同じフレームで複数の graph を扱っても Span ID から GraphRunId を一意に引けるようにする。内部段階と PassBegin／PassEnd は保存した Parent Span ID の鎖でその root へ結ぶ。GraphOperationResult も同じ root Span ID と operation を持つ。

引数の `frameId` が 0 なら未指定とし、相関側の Frame ID をプロファイルに使っても既存 Run.FrameId を書き換えない。両方が正で異なる場合は不一致とし、解析器が正常な同一フレームとして集計しない。相関がない場合は ProfileRunId を空、Frame ID を 0 として GraphRunId 単位の調査を許す。

ProfileRunId と Frame ID の値だけを CompiledRenderGraph の内部フィールドへ保持する。Record／Dispose の Current が別フレームでも Compile 時の値を使い、各 root 開始時には、その root Span ID と operation を付けて GraphRelation を再発行する。Compile 時に未購読で相関を捕捉していなければ、途中接続した Record に無関係な Current を代入せず、未帰属として記録する。Control を購読しない場合や途中接続・欠落で root の対応が得られない span は、graph 帰属不明とする。Frame ID と時刻の近さだけで GraphRunId を推定せず、不一致フレームの除外も対応を確認できた root に限る。profile のために Activity や辞書、GPU resource を追加で保持しない。

### Observer と GPU の境界

Compile／Record の既存 observer が有効な場合は既存の開始時刻と終了計時を共有する。新しい段階の event header の時刻には発行負荷も含み得るため、既存 observer が受ける duration と完全に同じ値であるとは扱わない。observer の Begin／結果通知／失敗通知、capture の予算・権限・状態を変更しない。

このモジュールに GPU timestamp、submit、Present、完了待ちの計測点は置かない。pass callback の区間は command の CPU 記録だけで、GPU 完了を表さない。既存の command 状態検証、失敗時の command 破棄と安全な Dispose を維持する。

## 検討した代替案

- Compile／Record の外側だけを計測する: 安価だが、依存構築と callback、allocation の切り分けができない。主要な内部段階を固定して追加する。
- 常に pass ごとにイベントを出す: 詳細は増えるが、多数 pass の負荷と記録量が増える。Stage 8 だけ独立した keyword にする。
- observer を新しい composite へ差し替える: 本体の正確な境界を直接記録できず、既存接続の再構成も必要になる。本体から EventSource へ発行する。

## 結果と影響

開発者は graph の compile と record の内部費用を、構造 snapshot を作らず比較できる。計測点と固定 ID の保守が増えるが、公開 API と GPU の所有権は変わらない。

受け入れ条件は、既存の `tests/Graphics/Lumyte.Graphics.RenderGraph.Tests/RenderGraphDiagnosticsTests.cs`、`RenderGraphCaptureTests.cs`、`RenderGraphLifetimeTests.cs` が検証する通知・capture・寿命を維持した上で、次を追加確認することとする。

- observer／EventSource の各 ON・OFF の組み合わせで、通知回数と capture 条件を維持し、両方 OFF では追加時計・profile 用割当がない。
- culling、循環、未初期化 read、callback 失敗、barrier 失敗で区間と達成件数が一致し、無実行・失敗を正常 0ms と扱わない。
- keyword `0xF` では pass 区間がなく、`0x10` 追加時だけ execution index ごとの区間がある。
- 同じ graph の複数 Compile、同じフレームの別 graph、別スレッド・別フレームの Record、途中接続、frame ID 不一致を root Span ID と GraphRunId ごとに区別する。Control なし・relation 欠落では graph 帰属不明になる。
- Record と Dispose に待機・submit が追加されず、Dispose の拒否・途中失敗・再試行で資源寿命と解放済み件数が変わらない。
