# ADR-ANIMATION-0002: 状態機械によるアニメーションの選択と遷移

- 状態: 採用
- 日付: 2026-10-09

## 背景

[ADR-ANIMATION-0001](ANIMATION-0001-animation-system.md) は、単調時計による再生と汎用値の計算を定めた。状態機械は独立した PR の対象として残している。UI の通常・押下・無効状態や、キャラクターの待機・移動・一度だけのアクションでは、入力条件や再生完了に応じて次のタイムラインを選ぶ必要がある。

消費側が遷移判定を個別に実装すると、競合する条件の優先順位、入力の消費、再入場時の再生位置、更新間隔が長い場合の扱いが分散する。これらの汎用部分は独立した状態機械の契約とし、Animation には再生との接続を定める。ボーンのデータ構造、対象への値適用、イベント配送は引き続き消費側が担当する。

本 ADR に沿って、この PR で状態機械を実装する。前 ADR のタイムライン・再生契約を置換せず、その上に状態選択の層を追加する。

## 決定

### 責務と範囲

`Lumyte.Animation` に、[汎用状態機械](../core/CORE-0001-state-machine.md)を所有するアニメーション用アダプターを追加する。汎用機械は `Lumyte.StateMachines` に配置し、Animation や時計には依存しない。アダプターは不変の状態とタイムラインの対応、および時計を持つ可変の実行者を提供する。一つの実行者は一つの状態だけを選択し、各状態は既存の `AnimationTimeline` と `AnimationWrapMode` を持つ。状態識別子を `TState`、外部入力を `TContext` とし、文字列のパラメーター辞書やボーン固有の状態を要求しない。

遷移条件は `TContext` と現在状態の再生情報から値を返す純粋な判定とする。トリガーは汎用側の `StateMachineTrigger` をそのまま使い、保留と集約はアダプターが保持し、更新時に汎用実行者へ入力集合として渡す。Animation 独自のトリガー型は設けない。条件の中から値を適用したり、状態機械を操作したりしない。

今回は平坦な状態、条件遷移、トリガー遷移、Once の完了遷移、優先順位、明示した自己遷移を扱う。切替は即時とする。階層状態、複数の同時状態、クロスフェード、ブレンドツリー、状態履歴、シリアライズは今回追加しない。

```mermaid
flowchart LR
    C[Context・トリガー] --> A[アニメーションアダプター]
    K[消費側の単調時計] --> A
    A --> M[Lumyte.StateMachines]
    M --> A
    A --> P[選択状態の再生]
    P --> T[既存タイムライン]
    T --> O[型付き Output]
    P --> E[状態付きマーカー]
    A --> N[時計時点付き遷移通知]
    O --> U[消費側で適用・配送]
    E --> U
    N --> U
```

`Lumyte.Animation → Lumyte.StateMachines` を追加し、既存の `Lumyte.Animation → Lumyte.Core.Time／Lumyte.Composition` の依存方向を維持する。逆方向の依存は作らない。別の Composition 連携プロジェクトや独自 Generator は追加しない。値ソース、Repeat／Reverse、チャネル、補間を状態機械側で再実装しない。

### 汎用機械との接続

継承用の基底クラスではなく、通常 API の合成でベースとして利用する。Animation の実行者は `StateMachineInstance<AnimationStateContext<TContext>, StateMachineTrigger>` と各状態の再生資源を所有する。状態一覧・初期状態・候補の選択・優先順位・ガード・コールバックは汎用側を唯一の実装とし、Animation 側で同じ判定器を作らない。

`AnimationStateContext<TContext>` は消費側の Input と評価済みの Playback 情報を持つ。汎用側はこの型を参照せず、任意の TContext の一つとして扱う。Animation の Builder は `OnCompleted` を `context.Playback.IsCompleted` のガードに変換し、指定した Condition と AND にして汎用 Transition に登録する。Trigger を指定しない遷移には定義ごとの AutomaticTrigger を割り当て、各 Update の入力集合へ追加する。一般用途には OnCompleted という API を追加しない。

アニメーション定義は汎用の Control 定義と、消費側の状態 ID・汎用 State の参照・Timeline／Wrap の対応を持つ。専用 Builder／Composition は両者を一緒に記述する構築定義とし、実行者が受け取る時点でまとめて生成する。既に構築した汎用 Control と再生対応を直接渡す経路も用意する。従ってアニメーション専用の構築 API を使わなくても、このアダプターを利用できる。

直接渡した Control の OnEnter／OnExit／Effect は汎用契約の順序で実行する。汎用インスタンスの Transitioned をアダプターが購読し、選択した遷移を時計時点付き通知へ変換する。FireAny の全アクションには同じ入力と評価済みの旧状態の Playback 情報を渡す。切替後の再生評価はその後に行い、アダプターはコールバックを無効化・置換しない。専用 Builder が作る Control は再生切替以外のユーザーアクションを暗黙に追加しない。

専用 Builder は Loop 状態の OnCompleted を拒否できる。一方、直接渡された汎用条件の内容は解析しない。Loop の IsCompleted は常に false なので、完了を調べる汎用条件は成立しない。この二つの検証範囲を区別する。

### 定義を受け取る時点での自動構築

通常の入口は `new AnimationStateMachine(clock, compositionDefinition)` とする。コンストラクターが Composition 定義全体を検証・コピーし、子の Timeline と内部の汎用制御定義を一括生成する。呼び出し側が状態機械や子タイムラインごとに Build を呼ぶ必要はない。Composition の状態機械ノードには公開 Build を設けない。

Builder を渡す入口も用意し、初期状態とともに受け取った時点で同じ内部構築処理を実行する。Builder.AddState は既存の不変 Timeline と、Composition の Timeline のどちらも受け入れる。Composition の子は、実行者の作成時点でまとめてコピーする。

生成は各コンストラクター呼び出しで一度だけ行い、Start／Update では繰り返さない。作成後に元の Composition ノード・Builder・子コレクションを変更しても、その実行者に反映しない。同じ可変定義を別の実行者へ渡した場合は、その作成時点の新しいスナップショットとなる。可変ノードに暗黙の生成結果キャッシュを持たせない。

内部の汎用 Builder は BuildDefinition() で制御定義だけを確定する。汎用の Build(context) は初期入場まで実行するため、アニメーション構築時には使わず、Start(context) で汎用実行者を生成する。

コンストラクターは構築だけを行い、初期入場・再生開始・値評価・イベント配送は行わない。不正な ID、循環、子数、時間、完了条件などは作成時に例外として返す。Start(context) が初期入場と再生を開始する。構築失敗時に入力の Composition ノードや Builder を変更しない。

複数実行者で生成済み定義を共有したい場合だけ、Builder.Build(initialState) による明示確定と既存の不変定義コンストラクターを使用できる。通常の利用経路に明示 Build を要求しない。直接作成した汎用 Control を再利用する経路も維持する。

### 更新と遷移の順序

`Update(context, output, events, transitions)` 一回の順序を次で固定する。

1. 引数を検証し、注入された時計の `Now` を一度だけ取得する。内部の再生者にはこの取得済み時点を返す時計を渡す。
2. 現在状態をその時点まで評価する。値は内部の再利用可能な出力へ計算し、通過したマーカーを保持する。
3. その評価後の位置・完了情報と今回の Context を AnimationStateContext に包み、内部の汎用機械の FireAny を、保留トリガーと AutomaticTrigger の集合で一度呼ぶ。優先順位順の候補選択と、候補内および成立後のガード短絡は汎用側の契約を使う。
4. 遷移がなければ現在状態の値を外部 Output に書き込む。遷移があれば旧状態の値の寄与を破棄し、新状態を取得済み時点から位置 0 で開始して値を評価する。旧状態の途中位置や超過時間は引き継がない。
5. 通過マーカーと、成立した一件の遷移通知を追記する。手順 3 が正常終了した時点でアダプターが保留を消去する。汎用機械自体は保留を持たない。

一回の Update で最大一遷移とし、新状態からの連鎖遷移を同じ呼び出しでは調べない。同じ時計時点の再呼び出しでも条件判定は行うため、呼び出し回数は消費側が制御する。時計が進まなければ、既存再生契約どおり通過マーカーを重複出力しない。

長い更新間隔で Once が終了した場合も、旧状態のマーカーをその終端まで収集してから、今回の観測時点で新状態を開始する。遷移条件は過去の Context を復元できないため、完了した過去時点まで遡って適用しない。このため、マーカー列の比較には既存タイムラインの規則を使えるが、状態遷移の時刻と値は更新の刻みに依存する。

### 条件とトリガー

遷移の成立条件は、指定された条件すべての AND とする。

- `Trigger` が指定されていれば、その実行者に保留されていること。
- `OnCompleted` が true なら、現在状態が Once の終端に達していること。
- `Condition` が指定されていれば、今回の Context と評価済み再生情報について true を返すこと。

何も指定しない遷移は無条件となる。優先順位は大きい数値を優先する。同順位は安定した登録順とし、数値の差で重み付けや時間制御は行わない。Loop は完了しないため、Loop 状態を出発点に `OnCompleted` を指定した定義は自動構築または明示確定で拒否する。

`SetTrigger` は同じキーの複数回の入力を一件にまとめる。候補の不成立や優先順位負けも含め、内部 FireAny が通常終了した際に保留トリガー全体を破棄し、次状態へ持ち越さない。その後の出力追記が失敗しても入力の消費は巻き戻さない。条件入力を持続させる場合は Context に状態を保持する。イベントが発生するたびにキューとして処理したい場合は消費側が入力キューを管理する。

停止中の `SetTrigger` は InvalidOperationException。一時停止中は保留できるが、Update は条件を調べずトリガーも消費しない。停止位置の値のみを評価し、マーカー走査の基準を進めない。Pause 前に進んだ区間の未通知マーカーは Resume 後の Update で収集する。`ClearTriggers` と Stop は保留を破棄する。条件とマーカーの名前を暗黙に結び付けない。今回のマーカーを消費側が `SetTrigger` に変換した場合、そのトリガーは次の Update から有効になる。

### 状態の再生とライフサイクル

Start(context) は停止中にだけ呼べる。初期状態の位置 0 と長さを含む Context で Control を受け取る汎用実行インスタンスのコンストラクターを呼び、初期 OnEnter を実行する。初期状態を位置 0 で開始し、保留入力と前回更新の基準を初期化する。初期状態の選択自体は遷移通知に含めない。実行中の Start は InvalidOperationException とし、最初からやり直す場合は Stop → Start(context) を使う。

状態へ入場するたびに新しい再生区間を開始する。自己遷移は旧ライブラリと同じく許可し、退出・Effect・入場を実行して再生位置を 0 に戻す。常時再起動を防ぐ条件を定義する責任は作成者にある。

Start は汎用インスタンスを生成し、Stop はそのインスタンスと再生資源を破棄する。Stop による追加の OnExit は実行しない。Pause は Animation のみの状態とし、内部の汎用インスタンスへの FireAny を呼ばない。Pause は現在位置を固定する。Resume は同じ位置から新しい時計基準で続ける。Pause 中の Update は停止位置の値だけを出し、遷移と未配送マーカーの収集は Resume 後へ残す。Speed は既存再生と同じ非負の有限値であり、状態の切替先にも引き継ぐ。Speed が 0 でも、実行中なら Context・トリガーによる遷移は判定する。

Once の完了後も機械は Running のまま終端を保持し、後続 Update で条件遷移できる。`OnCompleted` は一度限りの通知ではなく、完了済みという条件である。Stop は現在状態と保留を破棄し、値の復元や最後の値の書き込みをしない。停止中の Update は出力を変更せず false を返す。

機械は Seek を公開せず、内部再生者も消費側へ渡さない。状態選択と再生位置を別々に操作できる API は後続の用途に応じて決める。

### 出力と通知

外部 Output とイベント・遷移コレクションは消費側が所有し、Update は Clear せず追記する。同じ Output に他の再生者と収集する場合も、後の評価を優先する既存契約を維持する。内部出力の転送は型付きスロットを用いる内部操作として実装し、消費側向けの汎用 setter や対象参照を追加しない。

切替した更新では旧状態の Hold／Release の値を外部へ転送しない。新状態に寄与がないチャネルは Output に出ないので、前の値を保つか復元するかは消費側が決める。切替時の連続性は保証せず、クロスフェードや現在値からの補間を暗黙に行わない。

旧状態が切替までに通過したマーカーは失わず、旧状態の識別子付きで配送する。`AnimationStateEvent<TState>` の `Occurrence.Time` と `LoopIndex` はその状態のタイムライン内の値、`UpdateOffset` は状態機械の前回評価基準からの時計上の時間幅とする。内部再生者の基準が異なる場合は機械が差を補正する。Start 後の最初の基準は Start の時計時点とする。

0 のマーカーを開始操作で追加配送しない。既存 `AnimationPlayback` と同様に、最初に時間が前進した Update で収集する。新状態の 0 のマーカーも次の前進更新で収集する。遷移通知にはソース、ターゲット、切替を観測した `TimePoint` を記録する。異なる時計の TimePoint 同士は比較しない。コレクション間の配送は消費側が行い、必要ならマーカーの基準時点＋UpdateOffset と遷移の ObservedAt から並べる。

## 公開 API の追加差分

比較元は main の `3e31e7d5fae9a8d4d00ce30136bd951f0949ea72`。以下は実装した追加 API。既存の `AnimationPlayback`、`AnimationOutput`、タイムラインの公開シグネチャは変更しない。

```diff
+using System;
+using System.Collections.Generic;
+using Lumyte.Core.Time;
+using Lumyte.StateMachines;
+
+namespace Lumyte.Animation;
+
+public enum AnimationStateMachineStatus { Stopped, Running, Paused }
+// 条件評価時点の不変情報。Loop の IsCompleted は常に false。
+public readonly record struct AnimationStateInfo(
+    Duration Position, Duration Duration, bool IsCompleted);
+// 消費側の入力と今回評価した再生情報。汎用機械には一つの Context として渡す。
+public readonly record struct AnimationStateContext<TContext>(TContext Input, AnimationStateInfo Playback);
+// 状態と既存タイムラインの対応。データ構造・対象参照は含めない。
+public readonly record struct AnimationStateBinding<TState, TContext>(
+    TState Id, State<AnimationStateContext<TContext>> State,
+    AnimationTimeline Timeline, AnimationWrapMode Wrap = AnimationWrapMode.Once)
+    where TState : notnull;
+public readonly record struct AnimationStateEvent<TState>(
+    TState State, AnimationEventOccurrence Occurrence) where TState : notnull;
+public readonly record struct AnimationStateTransition<TState>(
+    TState From, TState To, TimePoint ObservedAt) where TState : notnull;
+
+// 自動構築または明示確定した不変定義。実行者・対象・現在の Context は保持しない。
+public sealed class AnimationStateMachineDefinition<TState, TContext> where TState : notnull
+{
+    // 任意の汎用定義をベースにできる。全状態との一対一対応を検証しコピーする。
+    public AnimationStateMachineDefinition(
+        StateMachine<AnimationStateContext<TContext>, StateMachineTrigger> control,
+        IReadOnlyList<AnimationStateBinding<TState, TContext>> states, StateMachineTrigger? automaticTrigger = null);
+    public StateMachine<AnimationStateContext<TContext>, StateMachineTrigger> Control { get; }
+    public TState InitialState { get; }
+    // 非 null の場合、各 Update に追加する入力。直接構築では省略可。
+    public StateMachineTrigger? AutomaticTrigger { get; }
+}
+public sealed class AnimationStateMachineBuilder<TState, TContext> where TState : notnull
+{
+    public AnimationStateMachineBuilder();
+    // 正の長さを持つ既存タイムラインを登録する。
+    public void AddState(TState id, AnimationTimeline timeline,
+        AnimationWrapMode wrap = AnimationWrapMode.Once);
+    // 子の Build を要求しない。実行者の作成または明示 Build でコピーする。
+    public void AddState(TState id, ComposeAnimation.Definitions.Timeline timeline,
+        AnimationWrapMode wrap = AnimationWrapMode.Once);
+    // 全指定条件の AND。priority が大きい順、同順位は登録順。
+    public void AddTransition(TState from, TState to,
+        Func<TContext, AnimationStateInfo, bool>? condition = null,
+        StateMachineTrigger? trigger = null, bool onCompleted = false,
+        int priority = 0);
+    // 任意の明示確定。生成済み定義を共有する場合の入口。
+    public AnimationStateMachineDefinition<TState, TContext> Build(TState initialState);
+}
+public sealed class AnimationStateMachine<TState, TContext> where TState : notnull
+{
+    // 通常入口。ネストした子も含めて検証・コピー・自動構築する。
+    // clock／definition の null は ArgumentNullException。再生開始はしない。
+    public AnimationStateMachine(IMonotonicClock clock,
+        ComposeAnimation.Definitions.StateMachine<TState, TContext> definition);
+    // Builder も明示 Build を要求せず受け取る。
+    public AnimationStateMachine(IMonotonicClock clock,
+        AnimationStateMachineBuilder<TState, TContext> builder, TState initialState);
+    // 生成済みの不変定義を共有する入口。再構築しない。
+    public AnimationStateMachine(IMonotonicClock clock,
+        AnimationStateMachineDefinition<TState, TContext> definition);
+    public AnimationStateMachineStatus Status { get; }
+    // 停止中の状態・位置取得は InvalidOperationException。
+    public TState CurrentState { get; }
+    // 最後の更新または時計を取得する制御操作で捕捉した位置。読み出しで時計を進めない。
+    public Duration Position { get; }
+    // 既定 1。非有限・負数は ArgumentOutOfRangeException。0 は許可。
+    public double Speed { get; set; }
+    public void Start(TContext context);
+    public void Pause();
+    public void Resume();
+    public void Stop();
+    // null は ArgumentNullException。定義で未使用のキーは ArgumentException。
+    public void SetTrigger(StateMachineTrigger trigger);
+    public void ClearTriggers();
+    // false は「今回遷移しなかった」。再生完了や値の有無を表さない。
+    public bool Update(TContext context, AnimationOutput output,
+        ICollection<AnimationStateEvent<TState>> events,
+        ICollection<AnimationStateTransition<TState>> transitions);
+}
```

### Lumyte.Composition の定義と生成入口

既存の `ComposeAnimation.Definitions` に StateMachine／State／Transition を追加し、同じ属性と Generator を使う。状態のタイムラインは既存 `Definitions.Timeline` をパラメーターとして受け取るため、Repeat／Reverse と深いネストも既存構築 API で表現できる。状態を指す遷移は参照 ID で保持し、Composition の子グラフ自体に循環を作らない。

```diff
+using System;
+using System.Collections.Generic;
+using Lumyte.Composition;
+using Lumyte.StateMachines;
+
+namespace Lumyte.Animation;
+
+public static partial class ComposeAnimation
+{
+    public static partial class Definitions
+    {
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class StateMachine<TState, TContext> where TState : notnull
+        {
+            [ComposeParameter] public required TState InitialState { get; init; }
+            [ComposeContent] public IReadOnlyList<State<TState, TContext>> States { get; set; } = [];
+            // 構築ノード。実行者へ渡した時点で子を含めて自動構築する。
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class State<TState, TContext> where TState : notnull
+        {
+            [ComposeParameter] public required TState Id { get; init; }
+            [ComposeParameter] public required Timeline Timeline { get; init; }
+            [ComposeParameter] public AnimationWrapMode Wrap { get; init; } = AnimationWrapMode.Once;
+            [ComposeContent] public IReadOnlyList<Transition<TState, TContext>> Transitions { get; set; } = [];
+        }
+        [Composable(Factory = "ComposeAnimation")]
+        public partial class Transition<TState, TContext> where TState : notnull
+        {
+            [ComposeParameter] public required TState To { get; init; }
+            [ComposeParameter] public Func<TContext, AnimationStateInfo, bool>? Condition { get; init; }
+            [ComposeParameter] public StateMachineTrigger? Trigger { get; init; }
+            [ComposeParameter] public bool OnCompleted { get; init; }
+            [ComposeParameter] public int Priority { get; init; }
+        }
+    }
+    // 以下の入口は既存 Generator が生成する。
+    public static Definitions.StateMachine<TState, TContext> StateMachine<TState, TContext>(
+        TState initialState, IReadOnlyList<Action<Definitions.StateMachine<TState, TContext>>>? with = null)
+        where TState : notnull;
+    public static Definitions.State<TState, TContext> State<TState, TContext>(
+        TState id, Definitions.Timeline timeline, Optional<AnimationWrapMode> wrap = default,
+        IReadOnlyList<Action<Definitions.State<TState, TContext>>>? with = null) where TState : notnull;
+    public static Definitions.Transition<TState, TContext> Transition<TState, TContext>(
+        TState to, Optional<Func<TContext, AnimationStateInfo, bool>?> condition = default,
+        Optional<StateMachineTrigger?> trigger = default, Optional<bool> onCompleted = default,
+        Optional<int> priority = default,
+        IReadOnlyList<Action<Definitions.Transition<TState, TContext>>>? with = null) where TState : notnull;
+    public static Definitions.StateMachineFactory<TState, TContext> StateMachineFactory<TState, TContext>()
+        where TState : notnull;
+    public static Definitions.StateFactory<TState, TContext> StateFactory<TState, TContext>()
+        where TState : notnull;
+    public static Definitions.TransitionFactory<TState, TContext> TransitionFactory<TState, TContext>()
+        where TState : notnull;
+    public static partial class Definitions
+    {
+        public delegate StateMachine<TState, TContext> StateMachineFactory<TState, TContext>(
+            TState initialState, IReadOnlyList<Action<StateMachine<TState, TContext>>>? with = null)
+            where TState : notnull;
+        public delegate State<TState, TContext> StateFactory<TState, TContext>(
+            TState id, Timeline timeline, Optional<AnimationWrapMode> wrap = default,
+            IReadOnlyList<Action<State<TState, TContext>>>? with = null) where TState : notnull;
+        public delegate Transition<TState, TContext> TransitionFactory<TState, TContext>(
+            TState to, Optional<Func<TContext, AnimationStateInfo, bool>?> condition = default,
+            Optional<StateMachineTrigger?> trigger = default, Optional<bool> onCompleted = default,
+            Optional<int> priority = default,
+            IReadOnlyList<Action<Transition<TState, TContext>>>? with = null) where TState : notnull;
+        public partial class StateMachine<TState, TContext> where TState : notnull
+        {
+            public StateMachine<TState, TContext> this[params State<TState, TContext>[] content] { get; }
+        }
+        public partial class State<TState, TContext> where TState : notnull
+        {
+            public State<TState, TContext> this[params Transition<TState, TContext>[] content] { get; }
+        }
+    }
+}
```

条件デリゲートは例のように型付きの変数で渡す。Optional に包むパラメーターへ匿名関数を直接渡す場合は、まず Func 型へ変換する。

生成される `StateMachineFactory<TState, TContext>()`、`StateFactory<TState, TContext>()`、`TransitionFactory<TState, TContext>()` のデリゲート引数は、それぞれ上記入口と同じとする。Optional、with、インデクサーによる子の置換は既存 Composition 契約に従う。実行者のコンストラクターは入力コレクションと構築ノードを読み取ってコピーし、その後のノード変更を定義に反映しない。共有タイムライン定義は許可するが、その子の循環は既存 Timeline.Build と同じく拒否する。

## 利用例

以下は実装した API の使用例。消費側のボーン構造・UI 型に依存せず、float のチャネルで待機・移動・一度だけのアクションを表す。

### Composition で状態とタイムラインを構築する

```csharp
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.StateMachines;
using static Lumyte.Animation.ComposeAnimation;

var amount = AnimationChannel<float>.Create();
var action = StateMachineTrigger.Create();
Func<MotionInput, AnimationStateInfo, bool> isMoving = (input, _) => input.IsMoving;
Func<MotionInput, AnimationStateInfo, bool> isIdle = (input, _) => !input.IsMoving;
var idle = new Tween<float>(0, 0, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear);
var moving = new Tween<float>(0, 1, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear);
var pulse = new Tween<float>(0, 1, Duration.FromSeconds(0.2), AnimationInterpolators.Float, AnimationEasing.Linear);

ComposeAnimation.Definitions.StateMachine<Motion, MotionInput> definition =
    StateMachine<Motion, MotionInput>(Motion.Idle)[
        State<Motion, MotionInput>(Motion.Idle,
            Timeline()[Track<float>(amount, idle)], wrap: AnimationWrapMode.Loop)[
            Transition<Motion, MotionInput>(Motion.Action, trigger: action, priority: 100),
            Transition<Motion, MotionInput>(Motion.Moving, condition: isMoving)
        ],
        State<Motion, MotionInput>(Motion.Moving,
            Timeline()[Track<float>(amount, moving)], wrap: AnimationWrapMode.Loop)[
            Transition<Motion, MotionInput>(Motion.Action, trigger: action, priority: 100),
            Transition<Motion, MotionInput>(Motion.Idle, condition: isIdle)
        ],
        State<Motion, MotionInput>(Motion.Action,
            Timeline()[Sequence()[Repeat(2)[Sequence()[
                Track<float>(amount, pulse), Reverse()[Track<float>(amount, pulse)]
            ]], Marker("ActionFinished")]])[
            Transition<Motion, MotionInput>(Motion.Moving,
                condition: isMoving, onCompleted: true, priority: 10),
            Transition<Motion, MotionInput>(Motion.Idle, onCompleted: true)
        ]
    ];

enum Motion { Idle, Moving, Action }
readonly record struct MotionInput(bool IsMoving);
```

Action への切替は入力条件より優先する。Action は Repeat と Reverse を含む有限 0.8 秒の状態であり、完了時点の Context によって移動か待機へ戻る。ループ状態には完了遷移を置かない。

### 時計を進め、結果を消費側で取得する

次のコードは上記の型宣言より前へ置く。表示やボーンへの適用は TryGet 後に消費側で行う。

```csharp
var clock = new ManualClock();
var machine = new AnimationStateMachine<Motion, MotionInput>(clock, definition);
// ここで内部制御定義とすべての Timeline を自動構築する。
var output = new AnimationOutput();
var events = new List<AnimationStateEvent<Motion>>();
var transitions = new List<AnimationStateTransition<Motion>>();
machine.Start(new MotionInput(IsMoving: false));
machine.SetTrigger(action);

output.Clear();
events.Clear();
transitions.Clear();
machine.Update(new MotionInput(IsMoving: true), output, events, transitions);
// Idle → Action。時計時点 0 で Action を開始し、位置 0 の値を返す。

clock.Advance(Duration.FromSeconds(1));
output.Clear();
events.Clear();
transitions.Clear();
bool changed = machine.Update(new MotionInput(IsMoving: true), output, events, transitions);
// ActionFinished を Action のイベントとして収集し、Action → Moving。
// 超過 0.2 秒は引き継がず、Moving の位置 0 の値を返す。
if (output.TryGet(amount, out float value))
{
    // 消費側が UI やボーンへ value を適用する。
}
```

### Builder を直接渡す

```csharp
var builder = new AnimationStateMachineBuilder<Motion, MotionInput>();
builder.AddState(Motion.Idle, Timeline()[Track<float>(amount, idle)], AnimationWrapMode.Loop);
builder.AddState(Motion.Action,
    Timeline()[Sequence()[Repeat(2)[Sequence()[
        Track<float>(amount, pulse), Reverse()[Track<float>(amount, pulse)]
    ]], Marker("ActionFinished")]]);
builder.AddTransition(Motion.Idle, Motion.Action, trigger: action, priority: 100);
builder.AddTransition(Motion.Action, Motion.Idle, onCompleted: true);
var simple = new AnimationStateMachine<Motion, MotionInput>(clock, builder, Motion.Idle);
// 子の Timeline もここでまとめて構築する。
```

### 汎用状態機械を直接ベースとして使う

専用 Builder を使わず、汎用側の API で条件を作る例。idle と pulse は上の例と同じ値ソースを使う。

```csharp
var idleState = new State<AnimationStateContext<MotionInput>>("Idle");
var actionState = new State<AnimationStateContext<MotionInput>>("Action");
var tick = StateMachineTrigger.Create();
var control = new StateMachineBuilder<AnimationStateContext<MotionInput>, StateMachineTrigger>(idleState);
control.AddTransition(new Transition<AnimationStateContext<MotionInput>, StateMachineTrigger>(
    idleState, actionState, action));
control.AddTransition(new Transition<AnimationStateContext<MotionInput>, StateMachineTrigger>(
    actionState, idleState, tick).When(context => context.Playback.IsCompleted));
var baseDefinition = control.BuildDefinition();
var adapted = new AnimationStateMachineDefinition<Motion, MotionInput>(baseDefinition,
    new AnimationStateBinding<Motion, MotionInput>[]
    {
        new(Motion.Idle, idleState, Timeline()[Track<float>(amount, idle)].Build(), AnimationWrapMode.Loop),
        new(Motion.Action, actionState, Timeline()[Track<float>(amount, pulse)].Build())
    }, automaticTrigger: tick);
var animation = new AnimationStateMachine<Motion, MotionInput>(clock, adapted);
```

汎用の StateMachine を別に公開・操作するのではなく、アニメーション実行者がその可変インスタンスを内部所有する。Control 定義は外部で作成・共有できるが、現在状態だけを外から変更して再生との対応を壊すことはできない。

## 検証・所有権・失敗時の契約

汎用定義は初期状態と全遷移端点から参照同一性で状態集合を構築する。アニメーション定義は消費側 ID の一意性、Control の全状態と再生対応の一対一の一致、null、Wrap の有効値と正のタイムライン長を検証する。汎用 State の Name を消費側 ID のキーとして使わない。自動構築と専用 Builder の明示確定はさらに Loop 状態の OnCompleted を検証する。`TState` の等価比較には `EqualityComparer<TState>.Default` を使用する。ID の等価性とハッシュ値は定義の寿命中に変えてはならない。到達不能な状態と複数の同一条件は許可するが、曖昧な条件に優先順位を付ける責任は定義作成者にある。

null の必須引数は ArgumentNullException、不整合な定義と未使用トリガーは ArgumentException、無効な数値・列挙値は ArgumentOutOfRangeException、時計逆行は InvalidOperationException、時間演算のオーバーフローは OverflowException とする。Pause／Resume は対象状態以外では何もしない。Stop は繰り返しても安全。参照型 Context の null は Update の変更前に拒否する。

定義は共有できるが、実行者と出力は一スレッドだけが操作する。条件が捕捉する可変オブジェクトと参照型 Context は消費側が管理する。Start で渡した初期 Context は内部インスタンスが保持する。各 Update の Context は明示 Context の FireAny に渡し、更新後に保持しない。条件は Update 中の固定入力として扱い、外部の時計や可変状態の読み直しを避ける。Update 中の再入呼び出し・操作は InvalidOperationException とする。

汎用 FireAny のガード例外時は汎用側の状態を維持する。アダプターの保留も維持する。退出／Effect／入場／通知の例外時の状態は汎用 ADR の部分完了契約に従う。ただしアニメーション側は既に再生を評価しており、独自条件・値ソース・追記先コレクションが例外を投げた場合、更新全体の完全なロールバックは保証しない。消費側はその更新結果を適用せず、Stop → Start(context) または実行者の作り直しで復旧する。引数の事前検証に失敗した場合は状態と出力を変更しない。

内部の値・イベント格納とトリガー集合は再利用する。定常更新の標準数値型について割り当てゼロを目標とし、状態の切替も有限状態ごとの再生資源を再利用する。独自条件・ソースの割り当てや任意のイベント出力量は保証しない。Native ハンドル・Dispose・実行時リフレクション・動的コード生成は要求しない。

## 検討した代替案

### 入れ子ごとに明示 Build を要求する

確定時点が明示的になるが、通常利用で状態機械と各タイムラインの Build が重複する。実行者へ渡す時点で全体を確定し、生成済み定義を共有する場合だけ明示確定を使う。

### 消費側で状態選択をすべて実装する

小さい用途は実現しやすいが、入力消費と遷移優先順位の規則が用途ごとに異なる。タイムラインを使い続ける単独再生は維持しつつ、共通の選択制御を追加する。

### Animation に状態選択を閉じ込める

一般用途でも時計や再生型への参照が必要になる。条件・優先順位・トリガーを汎用基盤へ分離し、完了情報はアダプターが Context に変換する。

### 自動連鎖と超過時間の引継ぎを提供する

大きな時間差で完了状態を連続して進められる一方、入力がいつ変化したか不明なまま過去の条件を判定し、無条件・自己遷移の循環対策も必要になる。観測時点で一遷移とする。

### 最初からクロスフェードを導入する

ボーン、UI、離散値で合成やチャネル欠落の意味が異なる。既存の後勝ち Output に暗黙の補間を追加せず、即時切替を先に定める。クロスフェードは明示した合成契約を伴う別の判断にする。

## 結果と影響

- Context、トリガー、完了条件の評価と優先順位を共通化できる。
- 既存の時計・タイムライン・Repeat／Reverse と Composition の構築方法を再利用できる。
- 通常利用の Build 呼び出しを実行者の作成へまとめ、定義の検証エラーも作成時に返せる。
- 更新単位の切替により履歴入力を要求せず、無限の自動遷移を避けられるが、遷移結果は更新の刻みに依存する。
- 切替時の値の連続性と、前状態だけが持つチャネルの復元は消費側の責務となる。
- マーカーと遷移通知はデータとして外へ出すため、UI／ボーン固有のスレッド・配送規則を持ち込まない。
- 汎用状態機械を UI・ゲーム進行などから独立利用でき、Animation はそのアダプターになる。
- 既存公開 API の破壊的変更は不要。実装には型付き出力の内部転送と取得済み時計時点の共有が必要になる。

## 検証方針

以下を実装の受け入れ条件とする。実行済みの結果は末尾に記録する。

- ManualClock で初期状態、条件・トリガー・完了遷移、自己遷移、最大一遷移、同時刻の再評価を確認する。
- 優先順位と同順位の登録順、候補内と成立後の短絡評価、条件の AND、未使用トリガー、保留の消費、Pause 中の保留を確認する。
- Pause／Resume、Speed 0、速度変更と入場への継承、Once 終端保持、Loop に完了がないこと、停止・再開始を確認する。
- 一回の Update が時計を一回だけ読むこと、入場時点が同じであること、ゲームと UI の時計を独立して進められることを確認する。
- 長い更新間隔で旧マーカーを収集し、新状態を観測時点から開始すること、旧値を破棄しチャネルを復元しないことを確認する。
- 状態をまたぐ UpdateOffset と ObservedAt、Repeat／Reverse／Loop のマーカー、0 マーカーの配送と重複防止を確認する。
- 汎用 Control を直接渡す構築、Composition／Builder の自動構築と明示確定の等価性、作成後の変更分離、複数実行者の独立性、共有タイムラインと無効定義を確認する。
- コンストラクターが一度だけ生成し、Start／Update が生成しないこと、構築失敗で再生・コールバック・入力変更が起きないことを確認する。
- 同じ可変ノードからの二つの作成がそれぞれの時点のスナップショットとなり、生成済み定義の共有では再構築しないことを確認する。
- 実際の Composition Generator と別アセンブリで、二つの型引数・notnull 制約・Optional の条件デリゲート・ネスト例をコンパイルする。
- 汎用候補選択を一更新で一度だけ呼ぶこと、独自条件例外からの再生側の復旧、入力と再生情報の合成を確認する。
- ウォームアップ後の定常更新・繰り返し遷移について標準数値型の割り当てを測定する。Windows／Linux／Browser で同じ入力の遷移順と値を比較する。

## 別途決定する事項

- クロスフェード、ブレンドツリー、チャネル欠落時の合成、離散値の切替時点。
- 階層・並列状態、状態履歴。汎用側の拡張と再生側の拡張は別々に判断する。
- 任意位置からの入場、状態と再生位置の保存・復元、編集・シリアライズ形式。
- ボーン姿勢の合成、ルートモーション、UI・シーンへの適用とイベント配送。

## 参考資料

- [ADR-CORE-0001: アニメーションに依存しない汎用状態機械](../core/CORE-0001-state-machine.md)
- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-ANIMATION-0001: 単調時計とタイムラインによる汎用値の計算](ANIMATION-0001-animation-system.md)
- [ADR-COMPOSITION-0001: デリゲート型ファクトリとノード操作の生成](../composition/COMPOSITION-0001-declarative-composition.md)
- [既存の再生実装](../../../src/Animation/Lumyte.Animation/AnimationPlayback.cs)
- [既存の Composition 定義](../../../src/Animation/Lumyte.Animation/ComposeAnimation.cs)

## 実装・検証記録

2026-10-09、Composition からの自動構築、汎用制御との接続、時計・再生・トリガー管理とサンプルを実装した。Linux / .NET 10 で専用テスト 17 件と既存テスト 23 件が成功した。ネストした Repeat / Reverse、完了条件、マーカー、Pause / Resume、速度、型付き出力の合成、例外復旧を含む。ウォームアップ後の数値更新と繰り返し遷移では管理ヒープ割り当てゼロを確認した。ソリューション全体は 126 件成功、GPU 実行環境を必要とする既存テスト 2 件はスキップ。Windows / Browser の実行と 1 / 8 / 32 状態の性能測定は未実施。
