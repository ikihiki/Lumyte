# ADR-UI-0027: UI 更新・配置・入力・描画の計測境界を固定する

- 状態: 提案
- 日付: 2026-11-11
- 基準コミット: `8ea844540cd98d2beb70eebb132e6086be66e662`

## 背景

UI の開発者は、同じ画面でも木の更新、配置の再計算、入力領域の更新、描画記録のどこに時間を使ったかを区別したい。配置と保持描画には再利用経路があり、公開メソッド全体の時間だけでは、再利用された範囲と実行された範囲を判別できない。

基準コードの `src/UI/Lumyte.UI/UiSurface.Layout.cs` の `UpdateLayout` は、`UpdateTree`、必要時だけの `UiLayoutPass.Compute`、`UpdateInput` を順に呼ぶ。配置の入口は独立した Measure メソッドではなく、この経路である。描画の通常経路と保持経路は、それぞれ `UiSurface.Record` と `UiDrawingScene2D.UpdateCore` に分かれている。

## 決定

`Lumyte.UI` 内部に EventSource プロバイダー `Lumyte-UI-Profiling` を置き、下記の同期処理境界と処理量を記録する。EventSource → EventPipe → dotnet-trace の収集経路を使い、既存のキャッシュ分岐で実行・再利用を数えることで、画面側の呼び出しを変えずに原因を分離する。

### 利用者の変更前・変更後

| 同じ画面の調査 | 変更前 | 変更後（提案） |
| --- | --- | --- |
| 配置が遅い理由を調べる | `surface.UpdateLayout()` を外から計時して内訳を推測する | 同じ呼び出しの TreeUpdate、LayoutCompute、InputRefresh を trace で区別する |
| 保持描画の再利用を調べる | `drawing.Update()` 後の `LastRecordingCount` を確認する | 同じ呼び出しから記録数と再利用分岐数も得る |

画面の API 呼び出しと順序は変更しない。収集者は例えば `dotnet-trace collect --process-id <PID> --providers Lumyte-UI-Profiling:0x6:4` で段階とカウンターを有効にする。この `0x6` の例は UI 段階だけの収集であり、フレーム全体の完全性は判定できない。フレーム分析ではホストの Control（`0x1`）も同時に収集する。これは CPU 側の同期区間であり、GPU 完了や提示時間ではない。

### イベントと相関

- Event ID は `1 = StageBegin`、`2 = StageEnd`、`3 = StageCounter` に固定する。StageBegin／StageEnd は Keywords.Stages、StageCounter は Keywords.Counters、Level はすべて Informational（4）とする。
- EventSource 内に `public static class Keywords` と `public const EventKeywords` の Control=1、Stages=2、Counters=4、GPU=8 を宣言する。UI 固有の control／GPU イベントは追加しない。プロバイダー型自体は internal とする。
- 相関キーは `ProfileRunId: Guid`、`FrameId: long`、`SpanId: long`、`ParentSpanId: long` とする。段階番号は下表のモジュール内固定値である。
- 開始時に `Activity.Current` から `Parent` を順にたどり、custom property `Lumyte.Profiling.RunId`（Guid）、`Lumyte.Profiling.FrameId`（long）の両方が型一致する最も近い Activity を選ぶ。同じ Activity の値を組として一度読み、終了とカウンターまで保持する。見つからなければ Guid.Empty／0 とし、フレーム相関不明として扱う。段階ごとの Activity は作らない。
- SpanId は本プロバイダーのプロセス内で一意な正の値とする。ParentSpanId は実際に同期で入れ子となった同じプロバイダーの span のみを指し、親がなければ 0 とする。他プロバイダーの ID を親として流用しない。
- StageEnd の outcome は `0 = 成功`、`1 = 取消`、`2 = 失敗`、`3 = 省略` とする。開始時に有効性を確定し、開始した区間は finally で終了する。途中から収集を始めた場合の欠落ペアは解析側で許容する。
- StageCounter は対応する span／stage と数値 CounterId、long の値を持つ単発の集計値であり、EventCounter の定期サンプルではない。実行途中で失敗した値は、その時点までの部分実測として StageEnd の失敗と組み合わせる。

### 段階と挿入位置

以下のパスはすべて `src/UI/Lumyte.UI/` 配下、上記基準コミットのメンバーを指す。追加する計測の境界であり、基準コードにイベントが存在するという意味ではない。

| StageId／名前 | 開始位置 | 終了位置と含む処理 |
| --- | --- | --- |
| 1 TreeUpdate | `UiSurface.Rebuild.cs` の `UpdateTree` の入口、`ThrowIfDisposed` より前 | メソッド全体の出口。RebuildPlan の準備・Publish、`ProcessSignals(true)`、`ReconcileVisuals` を含む |
| 2 LayoutCompute | `UiSurface.Layout.cs` の `UpdateLayout` で `UpdateTree` 後、`_isDisposed` 判定の直前 | dirty 時は `pass.Compute` の直後、例外時はその catch 内の `MarkLayoutDirty` 後。`UpdateInput` と `CompleteVirtualPresentation` は含めない |
| 3 InputRefresh | `UiSurface.Input.cs` の private `UpdateInput` の入口、`_inputDirty` 判定の前 | メソッド出口。入力木・focus 候補の再構築、focus 状態更新、capture 通知と例外集約まで含む |
| 4 InputDispatch | `UiSurface.SignalDispatch.cs` の `Dispatch` 入口、`_dispatchDepth++` 前 | 既存 finally の `ProcessSignals(false)` が終了した後。hit test、bubbling、capture 解放、同期 signal 処理を含む |
| 5 DrawTraversal | `UiSurface.Drawing.cs` の `Record` で `UpdateLayout` 後、viewport／disposed 判定の直前 | 可視時の `ClipRect` から `RecordElement` と `_recordDepth--` まで。外側の `canvas.Restore` は含めない |
| 6 RetainedSync | `UiDrawingScene2D.cs` の `UpdateCore` 入口、disposed／reentry／scene 排他検証より前 | `_updating = false` の既存 finally を含むメソッド出口。依存検証・配置・scene 同期・opaque 記録を含む |
| 7 AnimationAdvance | `UiSurface.Animations.cs` の `AdvanceAnimations` の入口 | メソッド出口。`UpdateTree`、presence 更新、producer 評価・公開・通知と既存の失敗状態設定を含む |
| 8 SignalProcess | `UiSurface.Signals.cs` の `ProcessSignals` 入口、再入・遅延判定の前 | `_processingSignals = false` の既存 finally を含む出口。ジョブと要素更新の収束処理を含む |

LayoutCompute は `UiLayoutPass.Compute` のキャッシュ無効化を含み、配置後の入力更新で失敗しても、この段階自体は成功で終了する。通常の `UpdateLayout` 全体を別 span に包まないため、配置だけの時間を入力時間と混ぜない。

TreeUpdate 内の再入抑止、LayoutCompute の disposed／clean、InputRefresh の clean、SignalProcess の先頭抑止条件、DrawTraversal の disposed／ゼロ viewport は省略とする。省略でも短い開始・終了ペアとゼロの処理数を記録する。TreeUpdate の pending root 不在だけでは省略としない。signal 処理と visual 調停が実行されるためである。

AnimationAdvance は更新無効なら省略とする。`UpdateTree` や presence 更新を実行した後の producer 不在・仕事不在による return は成功とし、評価数 0 で表す。RetainedSync は再記録数 0 でも scene 同期を実行するため成功とする。入力の未処理 return や Cancel 入力の正常配送も成功であり、outcome の取消ではない。

LayoutCompute の正常終了は `_layoutPass` を解除する前とする。入力更新時に既存どおり `_layoutPass` が有効でも、入力内の例外を配置の終了結果へ反映しないよう、計測の catch を Compute の範囲へ限定する。

外へ送出される OperationCanceledException は取消、他の例外は失敗とする。既存コードが例外を集約・変換する場合は、外へ出る最終例外で分類し、例外の型・順序・rollback の有無を変更しない。Record の事前検証／`canvas.Save`／UpdateLayout 自体で失敗した場合には DrawTraversal は開始されない。

### キャッシュと処理量

CounterId は以下に固定する。すべて区間内の実測件数（long、単位は件）であり、推定値・ツリー全体の総数・割合ではない。再帰の入口ごとにイベントを出さず、既存分岐で内部の区間集計だけを更新し、段階末尾に各カウンターを一度出す。

| StageId | CounterId／名前 | 加算位置・意味 |
| --- | --- | --- |
| 1 | 101 PublishedUpdates、102 RemovedElements | `Publish(RebuildPlan)` に入った各 plan の `Updates.Count`、`Removed.Length`。publish が途中で失敗しても対象件数であり、commit 完了数ではない |
| 2 | 201 LayoutEvaluations | `UiLayoutPass.LayoutNode` の `CanReuseLayout(effective)` が false となり `LayoutModified` を呼ぶ直前。失敗する試行も含む |
| 2 | 202 LayoutCacheHits | 同メソッドの `CanReuseLayout(effective)` が true の分岐。一つの再利用 subtree root を 1 件とし、省かれた子孫数を推測しない |
| 2 | 203 HiddenNodes | `UiLayoutPass.StageHidden` の既存 foreach でゼロ配置する各 node。可視配置の評価・hit に含めない |
| 3 | 301 InputNodes | `UiElement.Input.cs` の `UpdateInput` で AcceptsInput を通り InputVersion を設定する node |
| 4 | 401 HitTestVisits、402 HandlerCalls | `UiElement.FindInputHit` の入口と `UiSurface.DeliverInput` の `InvokeInput` 直前。capture で hit test を通らない配送も区別できる |
| 4 | 403 Consumed | `Dispatch` の最終 bool が true なら 1、false なら 0。例外時は出力しない。個々の handler の Handled 数とは異なる |
| 5 | 501 VisibleRecordVisits | `RecordElement` の可視性・visual exclusion 判定を通った回数。VisualHost が描画を引き受ける訪問も含む |
| 6 | 601 Recordings、602 ContentReuse | `Synchronize` の既存 `recordings++` と、その再記録条件が false の分岐。opaque 時は Recordings=1、ContentReuse=0 |
| 7 | 701 ProducerStages | producer snapshot のループで `producer.Stage` を呼ぶ直前。非可視 producer への Stage 呼び出しも含む |
| 8 | 801 SignalJobs | dequeue 済みジョブの `work.Work()` 直前。observer 通知や非同期タスクの総数ではない |

RetainedSync の Recordings は成功時に `LastRecordingCount` と一致させる。失敗時は古い LastRecordingCount を読まず、その区間で完了した記録だけを使う。opaque 経路で `surface.Record` が走れば DrawTraversal が子となる。通常の fragment ごとの span は作らない。

HandlerCalls は InputDispatch の集計に限り、InputRefresh 内の capture 通知を混ぜない。TreeUpdate の Publish が入れ子となる場合、対象 plan は最も近い TreeUpdate にだけ加算する。他のカウンターも最も近い該当段階に所属させ、再入により外側へ二重加算しない。段階時間は子を含むため、RetainedSync とその LayoutCompute／DrawTraversal などの時間を単純合計しない。

### 同期・非同期と互換性

上記 span はすべて同期呼び出しの寿命で閉じる。signal が dispatcher に予約された待ち時間や `ObserveDispatchAsync` の await 完了を、元フレームの TreeUpdate／InputDispatch に延長しない。後から実行する ProcessSignals は、その実行時の Activity の相関値で独立に記録し、以前の span を親として保持しない。

公開 API、dispatcher 契約、friend assembly は変更しない。相関の読み出しと段階スタックは Lumyte.UI 内部だけで完結し、ホストの update wrapper、Drawing2D の準備、GPU 実行を UI の段階として再定義しない。

Stages／Counters が無効なら相関プロパティの読み出し、span 採番、追加の列挙・文字列化を行わない。Counters のみ有効な場合も区間に採番して集計し、収集側は begin／end のない単独 snapshot として扱う。計測用カウンターのために CollectElements や別のツリー走査を追加しない。

## 検討した代替案

- 公開メソッド全体だけを計る: 挿入は少ないが、UpdateLayout の配置・入力更新や再利用の区別を失う。実作業の境界を分ける。
- 全 node の開始・終了を常時出す: 個別部品まで追えるが、再帰中のイベント量が木の大きさに比例する。区間集計を選び、個々の override の内訳は CPU スタック採取で調べる。
- 独自 sink／session を UI に持たせる: 任意の収集形式を選べるが、所有権・停止・保存の責務が増える。EventPipe へ統合する。

## 結果と影響

開発者は配置の cache hit と実評価、保持描画の再記録と再利用を同じフレームで区別できる。保持内容を使った描画でも traversal／scene 同期の CPU 費用は残るため、hit 数を短縮時間や GPU 負荷に換算しない。計測の有効化による負荷は別途比較する必要があり、速度改善値は保証しない。

### 検証方針

基準コミットの次のテストを振る舞いの基準とし、イベントの受け入れ条件を追加する。テストは `tests/UI/Lumyte.UI.Tests/` 配下である。

- `LayoutTests.UnchangedInputsDoNotRepeatLayout`: 2 回目は LayoutCompute が省略、配置 201〜203 は 0。InputRefresh は独立に dirty 判定する。
- `LayoutCacheTests.DirtySiblingReusesTheWholeCleanSubtree`／`ChangedViewportReusesOnlyMatchingEffectiveConstraints`: 202 は再利用した root 数で、子孫数を含めず、201 は実際の LayoutModified 試行数に一致する。
- `LayoutCacheTests.HiddenDescendantsRecomputeWhenTheirAncestorReturns`／`InvalidCachedChildUseFailsAndTheNextPassRecomputes`: hidden 件数、失敗終了、次回再評価を区別し、失敗時のキャッシュ無効化を維持する。
- `InputRoutingTests.ThrowingHandlerDoesNotLeaveCapture`／`CaptureRefreshCanReenterCancellation`: 配送・後処理を含め一つの終了を出し、capture の解放と例外集約を変えない。
- `RebuildTests.FailedPreparationKeepsTheOldTree`: TreeUpdate は失敗し、Publish に到達しない plan を 101／102 に数えない。
- `RetainedDrawingTests.StaticBuiltinsReuseLocalContent`／`PositionOnlyChangeKeepsLocalContent`: 601 は初回 2、無変更の次回 0、602 は実際の再利用分岐数。移動だけで再記録を増やさない。
- `RetainedDrawingTests.BorrowRejectsUpdatesUntilReleased`／`PageTransitionsUseFreshOpaqueRecordings`: 排他失敗は RetainedSync の失敗、opaque は 601=1／602=0 とし、通常 fragment と重複しない。
- `UiSurfaceTests.TreesCanBeUpdatedAcrossThreadsSequentiallyAsync` と `AsyncSignalTests.cs`: 逐次の別スレッド利用で親を持ち越さず、遅延実行に古い frame／親 span を貼り付けない。
- 有効／無効、Stages のみ／Counters のみ、再入、取消例外、途中収集を確認する。段階 ID、keyword、level、型付きキーを検査し、無効時の割当・追加走査が増えないことを比較する。
