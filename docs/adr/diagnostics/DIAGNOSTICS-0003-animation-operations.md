# ADR-DIAGNOSTICS-0003: アニメーション実行者の診断操作

- 状態: 採用
- 日付: 2026-10-11

## 背景

`Lumyte.Animation`は単調時計を使って汎用値を計算し、値の適用を消費側へ委ねる。再生位置、再生状態、速度、アニメーション状態機械の現在状態は実行中の不具合を調べるうえで必要だが、現在は対象プロセス内から各実行者を直接参照する必要がある。

[ADR-DIAGNOSTICS-0001](DIAGNOSTICS-0001-diagnostics-transport.md)は、診断操作を所有スレッド上の実行ポイントへ配送し、通信スレッドからエンジン所有オブジェクトへ直接アクセスしない方針を定めている。[ADR-ANIMATION-0001](../animation/ANIMATION-0001-animation-system.md)はAnimationを値の計算と制御に限定し、対象オブジェクト、描画バックエンド、DI、通信から独立させている。両方の依存方向を維持したまま、既存の診断Operationと汎用UIからアニメーションを観測・制御する接続が必要である。

Animationには任意型のチャンネル値と任意型の状態識別子がある。すべての値やCompositionグラフを暗黙に公開すると、型、データ量、秘密情報、対象寿命を制御できない。このため、診断対象と文字列表現、状態機械のTriggerは消費側が明示的に公開する必要がある。

## 決定

### 接続パッケージと依存方向

`src/Diagnostics/Lumyte.Diagnostics.Animation`に`Lumyte.Diagnostics.Animation`パッケージを追加する。このパッケージは`Lumyte.Diagnostics`と`Lumyte.Animation`へ依存する。`Lumyte.Animation`からDiagnostics、DI、通信への依存は追加しない。

接続はDIスコープごとの`AnimationDiagnosticRegistry`と、一つの`animation`診断サブシステムで構成する。消費側は診断へ公開する実行者だけを安定したIDで登録し、返された登録オブジェクトを破棄して解除する。レジストリは対象を強参照するため、登録の寿命をゲーム実行または対象以下にする。

診断Operationは既存の`IDiagnosticPump<TPoint>`から所有スレッド上で同期実行する。レジストリの登録・解除、Animationの通常更新、診断Pumpを同じ所有スレッドで行う。接続パッケージ内に独自キューやロックを追加せず、通信スレッドから登録対象へアクセスしない。

### 対象と操作

単体の`AnimationPlayback`と`AnimationStateMachine<TState, TContext>`を登録できる。共通のスナップショットはID、対象種別、状態、文字列化した現在状態、位置Tick、速度を持つ。スナップショットはOperation実行中にscalarへコピーされ、送信側からAnimationオブジェクトを参照しない。

`list`と`read`はObserve権限を要求する。`play`、`pause`、`stop`、`seek`、`set-speed`、`set-trigger`、`clear-triggers`はEdit権限を要求する。現行のAnimation実行者は外部変更を通知するRevisionを持たないため、誤った競合検出を避けて`ExpectedRevision`を要求しない。通信層のSessionIdとRequestIdによる重複排除は既存どおり使用する。

状態機械の`play`はPauseからのResumeだけを行う。停止後のStartには`TContext`が必要であり、診断側が任意のContextを生成できないため、停止中は`unsupported`を返す。`seek`は`AnimationPlayback`だけが対応する。対象の種類または状態が操作へ対応しなければ`unsupported`、IDがなければ`not-found`、Animationの入力契約に違反すれば`invalid-value`、状態制約に違反すれば`invalid-state`を返す。

### 公開範囲と上限

登録IDとTrigger名は1～128文字の小文字ASCII英数字、`-`、`_`、`.`に限定する。一つのスコープへ登録できる対象は128件までとする。`list`はIDをOrdinal順に改行で連結した最大16,512文字のscalarとして返す。IDに改行を許可しないため曖昧にならない。

状態機械の状態表示は登録時に渡した`Func<TState, string>`で生成し、4,096文字を上限とする。Triggerは名前と`StateMachineTrigger`の対応を登録時にコピーし、明示されたものだけを操作可能にする。Animationのイベント名、チャンネル値、Contextは暗黙に公開しない。

### 公開API差分

比較元は`origin/main`の`2b40860ebb05e28ef6da92f3cdee078b73e4f19f`とする。

```diff
+namespace Lumyte.Diagnostics.Animation;
+
+public sealed class AnimationDiagnosticRegistry
+{
+    // 同一スコープ内でIDは一意。上限超過、重複、不正IDを拒否する。
+    public AnimationDiagnosticRegistration Register(
+        string id,
+        Lumyte.Animation.AnimationPlayback playback);
+
+    // 状態表示とTrigger公開範囲を消費側が決める。入力コレクションは登録時にコピーする。
+    public AnimationDiagnosticRegistration Register<TState, TContext>(
+        string id,
+        Lumyte.Animation.AnimationStateMachine<TState, TContext> machine,
+        Func<TState, string> formatState,
+        IReadOnlyDictionary<string, Lumyte.StateMachines.StateMachineTrigger>? triggers = null)
+        where TState : notnull;
+}
+
+public sealed class AnimationDiagnosticRegistration : IDisposable
+{
+    // 対応する登録だけを解除する。複数回呼び出せる。
+    public void Dispose();
+}
+
+public sealed record AnimationDiagnosticSnapshot(
+    string Id,
+    string Kind,
+    string Status,
+    string State,
+    long PositionTicks,
+    double Speed);
+
+public sealed record AnimationDiagnosticTargetList(string Targets);
+
+public static class AnimationDiagnosticsExtensions
+{
+    // AddDiagnosticExecutionPoint<TPoint>の後に一スコープ一度登録する。
+    public static IServiceCollection AddAnimationDiagnostics<TPoint>(
+        this IServiceCollection services)
+        where TPoint : class;
+}
```

### 使用例

```csharp
services.AddDiagnosticExecutionPoint<BeforeAnimationUpdate>("animation.before-update");
services.AddAnimationDiagnostics<BeforeAnimationUpdate>();

AnimationDiagnosticRegistry registry = scope.ServiceProvider
    .GetRequiredService<AnimationDiagnosticRegistry>();

using AnimationDiagnosticRegistration playbackRegistration =
    registry.Register("ui.opening", playback);

using AnimationDiagnosticRegistration machineRegistration = registry.Register(
    "character.motion",
    machine,
    state => state.ToString(),
    new Dictionary<string, StateMachineTrigger>
    {
        ["jump"] = jumpTrigger,
    });

// Animation更新と同じ所有スレッドのフレーム境界で実行する。
pump.Pump(frame, budget);
```

### 検証方針

- 登録、一覧、状態取得、解除と、ID・件数上限を検証する。
- PlaybackのPlay、Pause、Stop、Seek、速度変更が同じ実行者へ反映されることを検証する。
- 状態機械のPause、Resume、Stop、Trigger、Trigger解除と、未公開Trigger・非対応操作の拒否を検証する。
- OperationのObserve／Edit権限、引数スキーマ、対象不在、無効値を検証する。
- プロジェクト参照からAnimation本体がDiagnosticsへ依存していないことを確認する。

## 検討した代替案

### Animation本体をDiagnosticsへ依存させる

実行者から直接Operationを公開できるが、通信を使用しないAnimation利用者にもDIとDiagnosticsの依存を課す。Animationの汎用値計算という責務を維持するため、接続パッケージから一方向に依存する。

### すべての実行者を自動探索する

Animation実行者はDI管理とは限らず、対象の寿命、表示名、秘密情報を自動探索では決められない。消費側による明示登録を採用する。

### 全チャンネル値を毎フレーム送信する

チャンネルは任意型であり、値の適用先もAnimationの責務外である。データ量がフレーム数と対象数に比例し、既存テレメトリーの有界キューを圧迫するため採用しない。

### 診断操作にRevisionを必須化する

診断レジストリ外から行われるPlay、Pause、Update、遷移を完全に観測できず、接続側だけのRevisionは現在状態との競合を正しく表さない。実行者が変更通知またはRevisionを提供するまでは要求しない。

## 結果と影響

- Animationの独立性を保ったまま、既存の診断通信、権限、所有スレッド、汎用UIから実行者を扱える。
- 公開対象とTriggerは明示的で、Contextや任意型の値を通信境界へ漏らさない。
- 登録中はレジストリが対象を強参照するため、消費側が登録を破棄する必要がある。
- 外部からの同時変更をRevisionでは検出しない。所有スレッドとフレーム境界で順序を確定する。
- 毎フレームのMetric／Trace、イベントストリーム、Compositionグラフ、選択したチャンネル値の取得はこの判断の対象外である。標準テレメトリーへ接続する場合は、ゲーム実行IDの伝播、診断無効時の費用、タグのカーディナリティを別途決定する。
