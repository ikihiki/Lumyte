# ADR-INPUT-0003: アクションマッピングと入力コンテキスト

- 状態: 提案（実装を含む）
- 日付: 2026-10-09

## 背景

物理キーをゲーム処理へ直接結び付けると、利用者ごとのデバイス選択、複合操作、リバインドと保存が分散する。[デバイス補正と仮想入力](INPUT-0002-processing-and-recognition.md)の後に、操作の意味を扱う独立した層を置く。

## 決定

### 配置と更新

`Lumyte.Input.Actions` は Input・Processing・Composition と .NET に依存し、Settings や DI コンテナーには依存しない。利用者・プレイヤーごとに ActionSystem を作り、SetDevices で対象を選ぶ。指定しない場合は受け渡された全デバイスを使う。物理・仮想デバイスは同じ InputControl の指定で扱う。

対象外デバイスの記録も受け渡し、現在値と解放を追跡する。マッピングとリバインドの捕捉は選択したデバイスに限定する。再選択時は現在のアナログ値を評価し、押下中のボタンは解放後の新しい押下から受け付ける。

Advance は Sequence 順の新しい InputRecord と InputSystem.ElapsedTime を受け取る。入力が空でもタイマーを進める。時刻・Sequence の逆行、未来の記録、再入・別スレッドからの変更を拒否する。履歴から取り出す場合は HasGap を検査し、欠落したデバイスを Reset して途中の操作を捨てる。現在状態から失われた操作を推測しない。

処理順はコンテキスト選別 → バインディング → アクション値補正 → 操作認識 → 入力バッファ → 通知である。InputSystem の履歴を消費・改変しない。通知例外は各ハンドラーの実行後に集約し、確定済みの Sequence を再実行しない。

### プロファイルとマッピング

ActionProfile は ImmutableArray による不変な ActionDefinition、ActionBinding、InputContext、RecognitionDefinition を持つ。ID は空でない永続文字列とし、ContextId・BindingId はプロファイル内で一意、ActionId と RecognitionId は定義するコンテキスト内で一意とする。参照整合性とパラメーターを適用前に検証する。プロファイル直下の既存 Actions は全コンテキストの共通既定値として引き続き扱う。

プロファイルの準備時にアクション ID と ContextId / ActionId ごとのバインディング索引を構築し、バインディングは BindingId の ordinal 順に整列しておく。現在値はコントロール別に参照し、入力ごとに全バインディングや無関係なコントロールを検索し直さない。コンテキストの評価順はプロファイルと有効化状態の変更時に更新する。

内部の作業領域を再利用し、公開する ActionState と寄与デバイス配列は内容が変わる場合だけ置き換える。返却済みの値を書き換えず、アクション値が同じでも寄与デバイスの変化は反映する。GetState は既存状態を割り当てなしで照会する。空入力時の時間更新と入力記録ごとの評価順は維持する。

| 型 | 値 | 合成規則 |
| --- | --- | --- |
| Button | Vector2 の X が 0 / 1、Y は 0 | いずれかのバインディングが押下なら有効 |
| Axis1D | X が -1〜1、Y は 0 | 最大絶対値 |
| Axis2D | 各軸 -1〜1 | 最大長 |

同値のバインディングは BindingId の ordinal 順で選ぶ。Scale はキーを方向へ変換し、スティックでは各軸へ掛ける。軸から Button への変換では PressThreshold と ReleaseThreshold を分け、デバイスごとにヒステリシスを保持する。既定で複数バインディングを加算しない。

PressThreshold 以上で押下、ReleaseThreshold 以下で解放とする。ReleaseThreshold がゼロでも、完全に中立になれば解放する。

Started はゼロから有効、Performed は有効な値の変化、Canceled は中立化を示す。寄与したデバイス ID と ContextId をイベントに含める。

### コンテキスト内の定義と継承

Composition の Context.Actions、Context.Bindings、Context.Recognitions スロットに定義を記述する。ネストしたバインディング・認識の ContextId は包含するコンテキストから設定し、異なる ContextId の明示指定は拒否する。ActionProfile は Context.Actions にローカルなアクション定義を保持し、Bindings・Recognitions は宣言元の ContextId を持つ。

ParentId による単一継承をサポートする。祖先から子へ解決し、同じ ActionId の子の定義は感度・正規化・平滑化を含めて置き換える。同じ ActionId の値の型は全コンテキストで一致させる。子であるアクションのバインディングを一つでも定義すると、そのアクションの継承したバインディング群全体を置き換える。別アクションのバインディングは継承する。同じ RecognitionId は子の定義で置き換え、異なる ID は追加する。親の不存在・自己参照・循環、兄弟だけに存在するアクションへの参照、不正なローカル定義を Validate で拒否する。

継承は定義の再利用であり、有効化の連動ではない。子だけを ActivateContext して継承した定義を使える。Priority・Exclusive は各コンテキストの明示値を使い、親から引き継がない。親子を同時に有効にした場合は、通常の優先順位・排他規則に従って独立に評価する。認識の途中状態・ヒステリシス・値補正の状態はコンテキストごとに保持し、一方の無効化で他方をキャンセルしない。状態参照は GetState(contextId, actionId)、認識の消費は Buffer.TryConsume(contextId, recognitionId, now, out action) でコンテキストを選べる。既存の GetState(actionId) は全コンテキスト中の最も強い値、TryConsume(recognitionId, ...) はコンテキストを限定しない最古の操作を返す。独自の SetValueProcessors・SetRecognizers は指定したコンテキストだけに適用し、インスタンスを継承しない。

継承の展開は ActionSystem の構築・ApplyProfile 準備時に索引へ取り込み、各 Advance で祖先を辿らない。ExportProfile と設定保存には ParentId・ローカルな定義だけを保持し、継承によるコピーを保存しない。親の編集は次のプロファイル適用境界で子孫にも反映する。継承したバインディングの BindingId をリバインドすると宣言元の親の定義を変更する。子だけ変更したい場合は、子のバインディングを別の BindingId で定義してから捕捉する。

### アクション値補正

ActionDefinition の Sensitivity、Normalize、SmoothingSeconds で感度、長さの上限、単調時間による平滑化を設定する。デバイス校正をここで二重適用しない。中立化は即時に反映する。

独自処理は SetValueProcessors(contextId, actionId, processors) で指定する。IActionValueProcessor はマッピング結果を受け取り、同じアクション型の有限値を返す。インスタンスはコンテキスト・アクションごとに独立して作る。出力は型の範囲に制限し、キャンセルや設定交換で Reset する。

### 操作認識

| RecognitionKind | 成立条件 |
| --- | --- |
| Press | Started |
| Hold | Started から Window を維持。一度だけ成立 |
| MultiTap | Window 内の別々の Started が TapCount に達する |
| Chord | 指定アクションがすべて有効で、開始時刻差が Window 内 |
| Sequence | 指定順の Started が Window 内で完了 |

通常の解放は長押しを中断する。フォーカス喪失・切断・コンテキスト解除・プロファイル交換では関連する途中状態を破棄する。カスタム認識はコンテキストごとに SetRecognizers を使う。IActionRecognizer.Advance はそのコンテキストのイベントだけを受け取り、空のバッチでもタイマーを更新できる。

デバイスの切断では、そのデバイスが寄与した組み込み認識とアクション値補正だけをリセットする。カスタム認識の Reset はコンテキスト単位なので、そのデバイスのイベントを受け取ったコンテキストを対象とする。

Completed 操作は RecognizedAction として通知し、入力バッファへ追加する。タッチのスワイプ・ピンチ・回転はマッピング前の仮想 Device、長押しやコンボはマッピング後の認識として区別する。

### 入力バッファ

ActionInputBuffer は Lifetime と MaxEntries を必須とする。最古から保持し、満杯なら最古を捨てる。成立時刻を t とすると t ≤ now < t + Lifetime の間だけ有効で、TryConsume は最古の一致を一度だけ取り出す。

正常な解放では成立済みの Press を消さない。ClearContext / ClearDevice は中断した対象を除外し、プロファイル交換では全件をクリアする。バッファそのものは保存しない。

### コンテキスト

優先順位が高いコンテキストから評価し、同順位では直近の有効化を優先する。Exclusive は自身のバインディングに指定したコントロールを下位へ渡さない。押下を失った下位アクションは中立化する。

ActivateContext / DeactivateContext は次の Advance に反映する。新しく有効になったコンテキストに既に押下中のボタンを引き継がず、解放後の新しい押下から受け付ける。アナログは次の評価で扱う。有効化状態と認識途中の情報は保存しない。

### リバインドと永続化

BeginRebind は BindingId、タイムアウト、軸しきい値、中止キーを受け取る。対象プレイヤーの範囲で次の適格な入力を捕捉し、開始時の押下、リピート、微小な軸を除外する。捕捉中は通常アクションを抑止する。候補が決まると捕捉を凍結し、同じコンテキストの Conflicts を公開する。

PrepareProfile は Reject / Allow / ReplaceConflicts の方針から候補プロファイルを作り、実行中の値を変更しない。永続化なしなら Confirm、保存するなら InputSettingsCoordinator.SaveRebindAsync を使う。保存前に Confirm を呼ばない。

`Lumyte.Input.Settings` の `input-actions` セクションにはプロファイル定義、認識条件とバッファ設定を保存する。コントロールや認識種別は名前で保存し、enum の数値、実行 ID、押下状態、コンテキスト有効化、delegate は保存しない。ソース生成 JSON メタデータとバリデータで、保存後にも同じ構成を復元できることを確認する。

```csharp
RebindSession session = actions.BeginRebind(
    "jump-key", new RebindOptions(TimeSpan.FromSeconds(5)));
// 通常の Tick を続け、Candidate を UI に表示する。
if (session.Candidate is not null)
{
    var saved = await coordinator.SaveRebindAsync(
        session, RebindConflictPolicy.Reject, cancellationToken);
    // Saved の場合だけ Current / Revision が変わる。
    // 入力管理スレッドの次回 Tick で最新の確定値を適用する。
}
```

失敗・競合時は旧プロファイルと候補を保持し、再確認・再保存または Cancel できる。成功時は ApplyCommittedSettings が最新 Current を予約し、続く Advance でプロファイル交換・バッファ初期化・捕捉終了を行う。保存時の古い候補を二度 Confirm しない。

保存には、確定済み Revision に対応するプロファイルが実際に適用済みで、捕捉開始時のプロファイルとも一致することを要求する。新設定の適用待ちや古いプロファイルからの候補は Conflict とし、適用後に改めて捕捉する。

input-processing と input-actions は個別 Revision を持つ。別セクションの SaveAsync を全体の原子的更新とは扱わない。入力設定の初期化にはそのモジュールの ResetAsync を使う。

### InputSystem と DI の相互作用

[INPUT-0002 の DI 構成](INPUT-0002-processing-and-recognition.md#di-構成と-inputsystem-との相互作用)で入力 Source を登録し、同じスコープに ActionSystem と InputSettingsCoordinator を追加する。

```csharp
services.AddSingleton(provider =>
{
    var settings = provider.GetRequiredService<
        IEditableOptions<InputActionSettings>>().Current.Value;
    return new ActionSystem(
        InputSettingsConverter.BuildProfile(settings),
        new InputBufferOptions(
            TimeSpan.FromSeconds(settings.BufferLifetimeSeconds),
            settings.BufferMaxEntries));
});
services.AddSingleton<InputSettingsCoordinator>();
```

Record 通知を一更新のバッチとして集めれば、手動の履歴保持とは独立して、新しい記録だけを渡せる。ポーリングを使う場合は全対象デバイスの新しい記録を Sequence 順に統合する。

```csharp
var pending = new List<InputRecord>();
input.Recorded += pending.Add;
actions.ActivateContext("game");
actions.Recognized += operation => HandleOperation(operation);

// InputSystem と同じ管理スレッドで実行する。
void Tick()
{
    coordinator.ApplyCommittedSettings();
    pending.Clear();
    input.Update();
    actions.Advance(pending.ToArray(), input.ElapsedTime);
    Vector2 movement = actions.GetState("move").Value;
    if (actions.Buffer.TryConsume("jump-press", input.ElapsedTime, out _))
    {
        TryJump();
    }
}
```

イベントで通知された操作をバッファからも使う場合、二重実行にならないようゲーム側で用途を分ける。終了時は更新ループを止め、InputSystem を先に終了してから Source の DI スコープを解放する。

[実行可能サンプル](../../../samples/Lumyte.Input.Advanced.Sample/Program.cs)は設定の登録・検証、候補保存、更新境界での適用、認識と一回限りの消費を実行する。

### Composition による宣言的定義

Lumyte.Input.Actions.Compose は Lumyte.Composition の Composable、ComposeParameter、ComposeSlot を使う。生成器は Actions のビルド時だけ実行し、利用側には生成済みファクトリを公開する。

```csharp
using static Lumyte.Input.Actions.Compose;

ActionProfile defaults = Profile()[Profile.Contexts()[
    Context("common")[
        Context.Actions()[Action("jump", ActionValueKind.Button)],
        Context.Bindings()[Binding(id: "common-jump", actionId: "jump", control: InputControl.ForKey(Key.Space))],
        Context.Recognitions()[Recognition(id: "jump-press", kind: RecognitionKind.Press,
            actions: ["jump"], window: TimeSpan.FromSeconds(1))]],
    Context("game", parentId: "common")[
        Context.Bindings()[Binding(id: "jump-key", actionId: "jump", control: InputControl.ForKey(Key.J))]]]]
    .Build();
var actions = new ActionSystem(defaults);
actions.ActivateContext("game");
// InputSystem は従来どおり Source を DI で受け取る。
// 新規記録を Sequence 順に渡す。空バッチでも時間を進める。
actions.Advance(records, input.ElapsedTime);
```

構築ノードは編集可能だが、Profile.Build は全定義を不変レコード・ImmutableArray に変換し、ActionProfile.Validate を実行する。スロットは渡された配列をコピーし、同じスロットの再指定は置換する。Build 後のノード編集は実行中プロファイルへ反映されない。変更は再 Build と ApplyProfile によって更新境界で適用する。

設定保存との統合では InputSettingsConverter.ToSettings(defaults) を既定値として登録し、起動時は保存済み設定から BuildProfile して ActionSystem に注入する。保存済みのリバインドを毎回既定値で上書きしない。Composition ノードやデリゲートは保存せず、既存の設定 DTO を保存する。[実行可能サンプル](../../../samples/Lumyte.Input.Advanced.Sample/Program.cs)もこの構成を使う。

### 公開 API 一覧

比較元は `5bf75e46ec8320a889315a0707ca16ea29e54b34`（本 PR 取り込み前の main）。Actions・設定連携・Composition 定義の主要な公開契約を追加として示す。Composition は生成済みのファクトリプロパティ、型付きデリゲートと名前付きスロット拡張も掲載する。

```diff
+using System;
+using System.Collections.Generic;
+using System.Collections.Immutable;
+using System.Numerics;
+using Lumyte.Input;
+
+namespace Lumyte.Input.Actions
+{
+    public enum ActionValueKind { Button, Axis1D, Axis2D }
+    public enum ActionPhase { Started, Performed, Canceled }
+    public enum InputControlKind { Key, MouseButton, ControllerButton, ControllerStick, ControllerTrigger }
+    public enum RecognitionKind { Press, Hold, MultiTap, Chord, Sequence }
+    public enum RebindConflictPolicy { Reject, Allow, ReplaceConflicts }
+
+    // 不変の実行時定義。空配列は許可し、未初期化配列・重複 ID・不正参照・不正値は拒否する。
+    public sealed record ActionProfile(
+        ImmutableArray<ActionDefinition> Actions, ImmutableArray<ActionBinding> Bindings,
+        ImmutableArray<InputContext> Contexts, ImmutableArray<RecognitionDefinition> Recognitions)
+    {
+        public void Validate();
+    }
+    public sealed record ActionDefinition(string Id, ActionValueKind Kind,
+        float Sensitivity = 1, bool Normalize = false, float SmoothingSeconds = 0);
+    public sealed record ActionBinding(string Id, string ActionId, string ContextId,
+        InputControl Control, Vector2 Scale, float PressThreshold = 0.5f,
+        float ReleaseThreshold = 0.4f);
+    // ローカルな定義と単一の親を保持する。Actions の default は空のローカル定義を表す。
+    public sealed record InputContext(string Id, int Priority = 0, bool Exclusive = false,
+        string? ParentId = null, ImmutableArray<ActionDefinition> Actions = default);
+    public sealed record RecognitionDefinition(string Id, string ContextId,
+        RecognitionKind Kind, ImmutableArray<string> Actions, TimeSpan Window, int TapCount = 2);
+    public readonly record struct InputControl(InputControlKind Kind, int Index)
+    {
+        public static InputControl ForKey(Key key);
+        public static InputControl ForButton(ControllerButton button);
+    }
+    public sealed record ActionState(ActionValueKind Kind, Vector2 Value);
+    public sealed record ActionEvent(string ActionId, ActionPhase Phase, Vector2 Value,
+        TimeSpan At, ImmutableArray<InputDeviceId> Devices, string ContextId);
+    public sealed record RecognizedAction(string RecognitionId, string ContextId,
+        TimeSpan At, Vector2 Value, ImmutableArray<InputDeviceId> Devices);
+
+    public interface IActionValueProcessor
+    {
+        // Device のマッピング後の値を補正する。now は InputSystem の単調増加時刻。
+        ActionState Process(ActionState mapped, TimeSpan now);
+        void Reset();
+    }
+    public interface IActionRecognizer
+    {
+        // コンテキスト内の新規イベントを処理し、空バッチでもタイマーを進める。
+        IReadOnlyList<RecognizedAction> Advance(IReadOnlyList<ActionEvent> events, TimeSpan now);
+        void Reset();
+    }
+    public sealed record InputBufferOptions(TimeSpan Lifetime, int MaxEntries);
+    public sealed class ActionInputBuffer
+    {
+        public ActionInputBuffer(InputBufferOptions options);
+        public int Count { get; }
+        public void Add(RecognizedAction action, TimeSpan now);
+        // 寿命内の認識済み操作を一度だけ取り出す。失敗時は false と null を返す。
+        public bool TryConsume(string recognitionId, TimeSpan now, out RecognizedAction? action);
+        // 同じ RecognitionId を継承した親子を区別して消費する。
+        public bool TryConsume(string contextId, string recognitionId, TimeSpan now,
+            out RecognizedAction? action);
+        public void Prune(TimeSpan now);
+        public void Clear();
+        public void ClearContext(string contextId);
+        public void ClearDevice(InputDeviceId device);
+    }
+    public sealed class ActionSystem
+    {
+        // プレイヤー・利用者ごとに生成する。profile は構築時に検証する。
+        public ActionSystem(ActionProfile profile, InputBufferOptions? bufferOptions = null);
+        public ActionInputBuffer Buffer { get; }
+        public event Action<ActionEvent>? Changed;
+        public event Action<RecognizedAction>? Recognized;
+        public void SetDevices(IEnumerable<InputDeviceId> devices);
+        public void ActivateContext(string contextId);
+        public void DeactivateContext(string contextId);
+        // パイプラインの置換は次の Advance 境界で適用する。
+        public void SetValueProcessors(string contextId, string actionId,
+            IEnumerable<IActionValueProcessor> processors);
+        public void SetRecognizers(string contextId, IEnumerable<IActionRecognizer> recognizers);
+        // 全 Device の新規記録を Sequence 順で渡す。再送・時間逆行は拒否する。
+        public void Advance(ReadOnlyMemory<InputRecord> records, TimeSpan now);
+        public ActionState GetState(string actionId);
+        // コンテキスト内の状態を照会する。無効なら中立値、未定義なら例外。
+        public ActionState GetState(string contextId, string actionId);
+        public void Reset(InputDeviceId device, TimeSpan now);
+        public ActionProfile ExportProfile();
+        // 検証後、次の Advance 境界で置換する。
+        public void ApplyProfile(ActionProfile profile);
+        public void SetBufferOptions(InputBufferOptions options);
+        public RebindSession BeginRebind(string bindingId, RebindOptions options);
+    }
+    public sealed record RebindOptions(TimeSpan Timeout, float AxisThreshold = 0.6f,
+        Key CancelKey = Key.Escape);
+    public sealed class RebindSession
+    {
+        // ActionSystem.BeginRebind が生成する。公開コンストラクターはない。
+        public string BindingId { get; }
+        public ActionProfile SourceProfile { get; }
+        public InputControl? Candidate { get; }
+        public ImmutableArray<string> Conflicts { get; }
+        public bool IsComplete { get; }
+        // 現在の割り当てを変えずに、保存する候補プロファイルを作る。
+        public ActionProfile PrepareProfile(RebindConflictPolicy policy);
+        // 保存を伴わず置換を予約する。永続化する場合は SaveRebindAsync を使う。
+        public void Confirm(RebindConflictPolicy policy);
+        public void Cancel();
+    }
+}
```

```diff
+using System;
+using System.Threading;
+using System.Threading.Tasks;
+using Lumyte.Input.Actions;
+using Lumyte.Settings;
+
+namespace Lumyte.Input.Settings
+{
+    public sealed class ContextSettings
+    {
+        public ContextSettings();
+        public string Id { get; set; } = string.Empty;
+        public string? ParentId { get; set; }
+        public System.Collections.Generic.List<ActionSettings> Actions { get; set; } = new();
+        public int Priority { get; set; }
+        public bool Exclusive { get; set; }
+    }
+
+    public static class InputSettingsConverter
+    {
+        // DTO の名前を型付き定義へ変換し、検証する。
+        public static ActionProfile BuildProfile(InputActionSettings settings);
+        public static InputActionSettings ToSettings(ActionProfile profile,
+            InputBufferOptions? bufferOptions = null);
+        public static bool ValidateProcessing(InputProcessingSettings settings);
+        public static bool ValidateActions(InputActionSettings settings);
+    }
+    public sealed class InputSettingsCoordinator
+    {
+        // 捕捉開始時の定義と確定 Revision を照合し、保存成功後だけ適用を予約する。
+        // 構築と更新境界の API は INPUT-0002 に示す。
+        public Task<SettingsSaveResult<InputActionSettings>> SaveRebindAsync(
+            RebindSession session, RebindConflictPolicy policy,
+            CancellationToken cancellationToken = default);
+    }
+}
```

```diff
+using System;
+using System.Collections.Generic;
+using System.Collections.Immutable;
+using System.Numerics;
+using Lumyte.Composition;
+
+namespace Lumyte.Input.Actions
+{
+    // Composition 生成器によるファクトリ。利用側は Generator を追加せずに呼べる。
+    public static partial class Compose
+    {
+        public static Definitions.ActionFactory Action { get; }
+        public static Definitions.BindingFactory Binding { get; }
+        public static Definitions.ContextFactory Context { get; }
+        public static Definitions.RecognitionFactory Recognition { get; }
+        public static Definitions.ProfileFactory Profile { get; }
+
+        public static partial class Definitions
+        {
+            public delegate Action ActionFactory(
+                string id,
+                ActionValueKind kind,
+                Optional<bool> normalize = default,
+                Optional<float> sensitivity = default,
+                Optional<float> smoothingSeconds = default,
+                IReadOnlyList<System.Action<Action>>? with = null);
+
+            // 編集可能な構築ノード。実行中の ActionProfile を直接変更しない。
+            public partial class Action
+            {
+                public Action();
+                public required string Id { get; set; }
+                public required ActionValueKind Kind { get; set; }
+                public float Sensitivity { get; set; } = 1;
+                public bool Normalize { get; set; } = false;
+                public float SmoothingSeconds { get; set; } = 0;
+                public ActionDefinition Build();
+            }
+
+            public delegate Binding BindingFactory(
+                string actionId,
+                InputControl control,
+                string id,
+                Optional<string?> contextId = default,
+                Optional<float> pressThreshold = default,
+                Optional<float> releaseThreshold = default,
+                Optional<Vector2> scale = default,
+                IReadOnlyList<System.Action<Binding>>? with = null);
+
+            // 編集可能な構築ノード。実行中の ActionProfile を直接変更しない。
+            public partial class Binding
+            {
+                public Binding();
+                public required string Id { get; set; }
+                public required string ActionId { get; set; }
+                // ネストした定義では包含 Context の ID。単独 Build では明示指定が必要。
+                public string? ContextId { get; set; }
+                public required InputControl Control { get; set; }
+                public Vector2 Scale { get; set; } = Vector2.One;
+                public float PressThreshold { get; set; } = 0.5f;
+                public float ReleaseThreshold { get; set; } = 0.4f;
+                public ActionBinding Build();
+            }
+
+            public delegate Context ContextFactory(
+                string id,
+                Optional<bool> exclusive = default,
+                Optional<string?> parentId = default,
+                Optional<int> priority = default,
+                IReadOnlyList<System.Action<Context>>? with = null);
+
+            // 編集可能な構築ノード。実行中の ActionProfile を直接変更しない。
+            public partial class Context
+            {
+                public Context();
+                public required string Id { get; set; }
+                public int Priority { get; set; } = 0;
+                public bool Exclusive { get; set; } = false;
+                public string? ParentId { get; set; }
+                // コンテキスト内のアクション・バインディング・認識を適用する。
+                public Context this[params CompositionSlotAssignment<Context>[] content] { get; }
+                public InputContext Build();
+            }
+
+            public delegate Recognition RecognitionFactory(
+                ImmutableArray<string> actions,
+                string id,
+                RecognitionKind kind,
+                TimeSpan window,
+                Optional<string?> contextId = default,
+                Optional<int> tapCount = default,
+                IReadOnlyList<System.Action<Recognition>>? with = null);
+
+            // 編集可能な構築ノード。実行中の ActionProfile を直接変更しない。
+            public partial class Recognition
+            {
+                public Recognition();
+                public required string Id { get; set; }
+                // ネストした定義では包含 Context の ID。単独 Build では明示指定が必要。
+                public string? ContextId { get; set; }
+                public required RecognitionKind Kind { get; set; }
+                public required ImmutableArray<string> Actions { get; set; }
+                public required TimeSpan Window { get; set; }
+                public int TapCount { get; set; } = 2;
+                public RecognitionDefinition Build();
+            }
+
+            public delegate Profile ProfileFactory(
+                IReadOnlyList<System.Action<Profile>>? with = null);
+
+            // 編集可能な構築ノード。実行中の ActionProfile を直接変更しない。
+            public partial class Profile
+            {
+                public Profile();
+                // 指定順にスロットを適用し、同じスロットの再指定は置換する。
+                public Profile this[params CompositionSlotAssignment<Profile>[] content] { get; }
+                // 不変レコードと配列を生成し、ActionProfile.Validate を実行する。
+                public ActionProfile Build();
+            }
+
+        }
+    }
+
+    public static class ComposeContextCompositionExtensions
+    {
+        public static CompositionSlot<Compose.Definitions.Context, Compose.Definitions.Action>
+            Actions(this Compose.Definitions.ContextFactory __factory);
+        public static CompositionSlot<Compose.Definitions.Context, Compose.Definitions.Binding>
+            Bindings(this Compose.Definitions.ContextFactory __factory);
+        public static CompositionSlot<Compose.Definitions.Context, Compose.Definitions.Recognition>
+            Recognitions(this Compose.Definitions.ContextFactory __factory);
+    }
+
+    public static class ComposeProfileCompositionExtensions
+    {
+        public static CompositionSlot<Compose.Definitions.Profile, Compose.Definitions.Action>
+            Actions(this Compose.Definitions.ProfileFactory __factory);
+        public static CompositionSlot<Compose.Definitions.Profile, Compose.Definitions.Binding>
+            Bindings(this Compose.Definitions.ProfileFactory __factory);
+        public static CompositionSlot<Compose.Definitions.Profile, Compose.Definitions.Context>
+            Contexts(this Compose.Definitions.ProfileFactory __factory);
+        public static CompositionSlot<Compose.Definitions.Profile, Compose.Definitions.Recognition>
+            Recognitions(this Compose.Definitions.ProfileFactory __factory);
+    }
+}
```

## 検討した代替案

- ゲーム処理側のキー判定だけで構成: 再割り当て、認識とキャンセルが分散する。
- 全利用者で一つの ActionSystem: デバイス範囲とコンテキストが干渉する。
- 捕捉直後にプロファイルへ適用: 保存失敗でも実行中設定が変わる。
- 現在状態だけで認識: 同一更新内の短い押下・解放や操作順を失う。

## 結果と検証

補正された物理入力・仮想入力を利用者ごとの操作へ変換し、保存成功後の境界で切り替えられる。実 OS バックエンド、リバインド UI、コントロール表示名のローカライズは構成側の責務とする。

索引と内部作業領域を保持するメモリを使い、定常更新の検索と一時割り当てを減らす。構成変更時の索引更新、同値入力の選択順、寄与デバイスの変化、公開済みスナップショットの不変性を回帰テストで確認する。

テストでは Press・Hold・MultiTap、入力バッファの期限と単一消費、フォーカス喪失、コンテキスト切替、リバインド候補とタイムアウト、Sequence 検証、通知例外の集約、保存・再起動・検証失敗を確認する。
