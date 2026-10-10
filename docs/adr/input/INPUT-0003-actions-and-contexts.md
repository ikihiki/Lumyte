# ADR-INPUT-0003: アクションマッピングと入力コンテキスト

- 状態: 提案（実装を含む）
- 日付: 2026-10-09

## 背景

物理キーをゲーム処理へ直接結び付けると、利用者ごとのデバイス選択、複合操作、リバインドと保存が分散する。[デバイス補正と仮想入力](INPUT-0002-processing-and-recognition.md)の後に、操作の意味を扱う独立した層を置く。

## 決定

### 配置と更新

`Lumyte.Input.Actions` は Input・Processing と .NET に依存し、Settings や DI コンテナーには依存しない。利用者・プレイヤーごとに ActionSystem を作り、SetDevices で対象を選ぶ。指定しない場合は受け渡された全デバイスを使う。物理・仮想デバイスは同じ InputControl の指定で扱う。

対象外デバイスの記録も受け渡し、現在値と解放を追跡する。マッピングとリバインドの捕捉は選択したデバイスに限定する。再選択時は現在のアナログ値を評価し、押下中のボタンは解放後の新しい押下から受け付ける。

Advance は Sequence 順の新しい InputRecord と InputSystem.ElapsedTime を受け取る。入力が空でもタイマーを進める。時刻・Sequence の逆行、未来の記録、再入・別スレッドからの変更を拒否する。履歴から取り出す場合は HasGap を検査し、欠落したデバイスを Reset して途中の操作を捨てる。現在状態から失われた操作を推測しない。

処理順はコンテキスト選別 → バインディング → アクション値補正 → 操作認識 → 入力バッファ → 通知である。InputSystem の履歴を消費・改変しない。通知例外は各ハンドラーの実行後に集約し、確定済みの Sequence を再実行しない。

### プロファイルとマッピング

ActionProfile は ImmutableArray による不変な ActionDefinition、ActionBinding、InputContext、RecognitionDefinition を持つ。ID は空でない一意の永続文字列とし、参照整合性とパラメーターを適用前に検証する。

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

### 公開 API 一覧

主要 API を差分形式で示す。定義のコレクションは ImmutableArray、イベントの Devices も ImmutableArray とする。

```diff
+ActionProfile(Actions, Bindings, Contexts, Recognitions)
+ActionProfile.Validate()
+ActionDefinition(Id, Kind, Sensitivity = 1,
+    Normalize = false, SmoothingSeconds = 0)
+ActionBinding(Id, ActionId, ContextId, Control, Scale,
+    PressThreshold = 0.5f, ReleaseThreshold = 0.4f)
+InputContext(Id, Priority = 0, Exclusive = false)
+RecognitionDefinition(Id, ContextId, Kind, Actions, Window, TapCount = 2)
+InputControl(InputControlKind Kind, int Index)
+InputControl.ForKey(Key key)
+InputControl.ForButton(ControllerButton button)
+ActionState(ActionValueKind Kind, Vector2 Value)
+ActionEvent(ActionId, Phase, Value, At, Devices, ContextId)
+RecognizedAction(RecognitionId, ContextId, At, Value, Devices)
+IActionValueProcessor.Process(ActionState mapped, TimeSpan now)
+IActionValueProcessor.Reset()
+IActionRecognizer.Advance(IReadOnlyList<ActionEvent> events, TimeSpan now)
+IActionRecognizer.Reset()
+InputBufferOptions(TimeSpan Lifetime, int MaxEntries)
+ActionInputBuffer(InputBufferOptions options)
+ActionInputBuffer.Add(RecognizedAction action, TimeSpan now)
+ActionInputBuffer.TryConsume(string id, TimeSpan now,
+    out RecognizedAction? action)
+ActionInputBuffer.Clear() / ClearContext(string id)
+ActionInputBuffer.ClearDevice(InputDeviceId id) / Prune(TimeSpan now)
+ActionSystem(ActionProfile profile, InputBufferOptions? bufferOptions = null)
+ActionSystem.SetDevices(IEnumerable<InputDeviceId> devices)
+ActionSystem.ActivateContext(string id) / DeactivateContext(string id)
+ActionSystem.SetValueProcessors(string contextId, string actionId,
+    IEnumerable<IActionValueProcessor> processors)
+ActionSystem.SetRecognizers(string contextId,
+    IEnumerable<IActionRecognizer> recognizers)
+ActionSystem.Advance(ReadOnlyMemory<InputRecord> records, TimeSpan now)
+ActionSystem.GetState(string actionId) : ActionState
+ActionSystem.Buffer : ActionInputBuffer
+ActionSystem.Changed : event Action<ActionEvent>
+ActionSystem.Recognized : event Action<RecognizedAction>
+ActionSystem.Reset(InputDeviceId device, TimeSpan now)
+ActionSystem.ExportProfile() : ActionProfile
+ActionSystem.ApplyProfile(ActionProfile profile)
+ActionSystem.SetBufferOptions(InputBufferOptions options)
+ActionSystem.BeginRebind(string bindingId, RebindOptions options)
+RebindSession.SourceProfile : ActionProfile
+RebindSession.Candidate : InputControl?
+RebindSession.Conflicts : ImmutableArray<string>
+RebindSession.IsComplete : bool
+RebindSession.PrepareProfile(RebindConflictPolicy policy) : ActionProfile
+RebindSession.Confirm(RebindConflictPolicy policy) / Cancel()
+InputSettingsCoordinator.SaveRebindAsync(RebindSession session,
+    RebindConflictPolicy policy, CancellationToken cancellationToken = default)
+InputSettingsConverter.BuildProfile(InputActionSettings settings)
+InputSettingsConverter.ToSettings(ActionProfile profile,
+    InputBufferOptions? bufferOptions = null)
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
