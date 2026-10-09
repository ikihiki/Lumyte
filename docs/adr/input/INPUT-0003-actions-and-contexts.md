# ADR-INPUT-0003: アクションマッピング後の加工・認識と入力管理

- 状態: 提案
- 日付: 2026-10-09

## 背景

ゲームの「移動」「決定」等を物理キーに直接結び付けると、複数デバイス対応と設定変更がゲームコードに分散する。メニュー・チャット・ゲームで同じ入力の意味が異なり、入力の優先順位と伝播も必要になる。

[Input 基盤](INPUT-0001-input-system.md)と[デバイス補正・仮想デバイス層](INPUT-0002-processing-and-recognition.md)を利用し、論理操作と利用場面を分離する。

## 決定

### 責務と配置

`src/Platform/Lumyte.Input.Actions/`、名前空間・パッケージ名 `Lumyte.Input.Actions` に実装する。共通 Input と Processing に依存し、Engine、UI、Platform実装には依存しない。利用者・プレイヤーごとに独立した ActionSystem を持ち、デバイス ID の集合と有効コンテキストを設定する。

### アクションとバインディング

ActionId と BindingId は設定内で一意な永続文字列とする。Action は Button、Axis1D、Axis2D の値型を持ち、値はそれぞれ 0 / 1、-1〜1、各軸 -1〜1 とする。複数の実デバイスまたは仮想デバイスのコントロールを一つの Action に結び付けられる。

基礎バインディングはキー・ボタン、スティック、トリガー、正負キーによる1軸、上下左右キーによる2軸を対象とする。仮想デバイスも同じコントロールとして選択する。機器の補正と接触ジェスチャーは Processing、マッピングした値の補正と論理操作の認識は本層の責務とする。

Button はいずれかが押下なら有効とする。アナログ値は最大絶対値、2軸は最大長の入力を選び、同値は BindingId の辞書順で決める。加算等の合成方式は明示設定とし、既定にしない。軸から Button へ変換する場合は押下・解放の別しきい値でヒステリシスを設ける。

出力は Started、Performed、Canceled とし、記録順で遷移する。同一更新内の短い押下と解放の両方を通知し、最終値だけから判定しない。GetState は更新終了時の不変状態を返し、通知は更新結果の公開後に行う。

### アクション値補正と操作認識

処理順はコンテキストによる入力選別 → バインディング評価・合成 → アクション値補正 → 操作認識 → 入力バッファとする。入力選別は元データや仮想デバイスの履歴を消費しない。デバイス補正の設定をここで自動的に再適用しない。

アクション値補正は論理操作の用途に応じた移動ベクトル正規化、照準感度、反応曲線、加速・平滑化を担当する。例えば移動の斜め入力は長さを 1 に制限し、照準の感度はそのアクションだけに適用する。デバイスの中心ずれと筆圧の機器校正は INPUT-0002 に置く。

補正結果はアクション型の範囲と有限値を保証し、設定を不変として保持する。平滑化を有効にする場合は単調時間差を使い、無効化・キャンセルで履歴をリセットする。加工順は設定に明示し、元のマッピング値と補正値を診断できるようにする。

操作認識は物理キーではなく ActionId の値・遷移を受け取る。キーボードでもコントローラーでも同じ「決定の長押し」を判定できる。入力種別に依存しない時間・順序の判定アルゴリズムは再利用してよいが、デバイス補正層は Actions 型に依存しない。

| 認識 | 契約 |
| --- | --- |
| 長押し | 押下成立から設定期間で一度成立。途中の解放・キャンセルで中断 |
| ダブルタップ・連打 | 同じ Action の正常な押下・解放を指定間隔・回数で判定。キーリピートは数えない |
| 同時操作 | 指定 Action がすべて有効で、成立時刻差が許容期間以内なら一度成立 |
| 順序操作 | ActionId 列の順序と時間制限で判定。不一致でリセットし、現在の操作を開始候補に再評価 |

now は入力の RecordedAt と同じ単調経過時間軸とし、逆行を拒否する。入力なしの Advance でも長押し成立・期限切れ・平滑化を進める。バッチ分割によって成立時刻が変わらないよう、次の入力より前に到達した期限を先に評価する。

短い押下・解放を補正・認識へ順序どおり渡し、最終状態だけで認識しない。成立結果は不変の RecognizedAction として出力する。結果から新しいアクションを自動発火させる循環経路は設けない。

### 入力バッファ

操作認識の成立結果を、利用者・コンテキストごとのバッファに保持する。着地前の「ジャンプ」などを後で一度だけ受け付けられる。TryConsume は指定操作に合う最古の一件を取得し、そのバッファ内だけで消費する。消費可能期間は `created <= now < expires` とし、期限ちょうどは無効とする。

期間と最大件数を必須設定にし、満杯では最古を削除して削除件数を返す。コンテキスト無効化、再割り当て、履歴欠落、関連デバイスの切断・キャンセルで影響する候補を破棄する。正常な押下終了は成立済みの先行入力を破棄しない。候補には成立に寄与したデバイス ID を保持し、複数デバイスの操作も適切に中断できる。

### 入力コンテキスト

コンテキストはバインディング群、優先順位、排他的かどうかを持つ。優先順位の降順で評価し、同順位は有効化順の新しいものを先にする。例えばチャット、メニュー、ゲームの順に評価できる。

高優先の排他的コンテキストで一致した物理コントロールは、その更新で低優先への伝播を抑制する。基盤の記録は削除しない。押下を遮断したコントロールの解放も同じ経路で扱い、下位アクションを押下のまま残さない。操作認識にはそのコンテキストで成立したアクションだけを渡す。マッピング前に生成された仮想デバイスも同じ遮断規則に従い、排他的コンテキストを越えて操作認識状態・バッファを共有しない。

コンテキスト無効化、割り当て変更、デバイス切断、履歴欠落時は影響するアクションを Canceled・中立値にする。新たに有効化したコンテキストでは既に押下中のコントロールから Started を生成せず、一度解放して次の押下を待つ。アナログ値は次の取得値から再評価する。変更は Advance の開始時にまとめて適用し、通知中の変更は次回に送る。

### リバインドと設定保存

リバインドは対象 BindingId を指定して開始し、指定デバイス範囲内の次の入力を候補として捕捉する。開始時に既に押下中の入力、リピート、微小なアナログ変化を除外する。中止操作、タイムアウト、切断で終了し、元設定を維持する。

候補は直ちに設定へ反映せず、同じコンテキスト内の重複を報告する。利用者が許可・拒否・既存割り当て解除を選んで確定する。UIが候補表示を行えるよう、候補と状態を読み取り専用で取得する。捕捉中の入力は対象利用者の通常アクションへ伝播させない。

保存対象はスキーマバージョン、ActionId / BindingId、コントロール指定、加工設定とコンテキスト設定とする。接続期間限定の InputDeviceId は保存せず、デバイス種類・利用者が選ぶプロファイル等の安定した選択条件を保存する。永続化は Lumyte.Settings へ委譲し、Input 層に独自のファイル I/O を実装しない。未対応バージョンや欠けた ActionId は明示的なエラーとし、移行または既定復元を利用者が選ぶ。

### 公開 API 一覧

新規 API の宣言案。設定型とメソッド本体の詳細は実装前に具体化する。

```diff
+public enum ActionValueKind { Button, Axis1D, Axis2D }
+public enum ActionPhase { Started, Performed, Canceled }
+public sealed record ActionEvent(
+    string ActionId, ActionPhase Phase, Vector2 Value, TimeSpan At,
+    IReadOnlyList<InputDeviceId> Devices);
+public sealed record ActionState(ActionValueKind Kind, Vector2 Value);
+public interface IActionValueProcessor
+{
+    ActionState Process(ActionState mappedValue, TimeSpan now);
+    void Reset();
+}
+public sealed record RecognizedAction(
+    string RecognitionId, string ContextId, TimeSpan At, Vector2 Value,
+    IReadOnlyList<InputDeviceId> Devices);
+public interface IActionRecognizer
+{
+    // コンテキスト内で補正済みのアクション遷移から認識する。
+    IReadOnlyList<RecognizedAction> Advance(
+        IReadOnlyList<ActionEvent> actions, TimeSpan now);
+    void Reset();
+}
+public sealed record InputBufferOptions(TimeSpan Lifetime, int MaxEntries);
+public sealed class ActionInputBuffer
+{
+    public ActionInputBuffer(InputBufferOptions options);
+    public int Add(RecognizedAction operation, TimeSpan now);
+    public bool TryConsume(
+        string recognitionId, TimeSpan now, out RecognizedAction? operation);
+    public int Clear();
+    public int ClearContext(string contextId);
+    public int ClearDevice(InputDeviceId device);
+    public int Prune(TimeSpan now);
+}
+public sealed class ActionSystem
+{
+    public ActionSystem(ActionProfile profile);
+    public void SetDevices(IReadOnlyList<InputDeviceId> devices);
+    public void ActivateContext(string contextId);
+    public void DeactivateContext(string contextId);
+    // 入力選別・マッピング・値補正・操作認識を順に進める。
+    public IReadOnlyList<ActionEvent> Advance(
+        ReadOnlyMemory<InputRecord> records, TimeSpan now);
+    public ActionState GetState(string actionId);
+    public event Action<ActionEvent>? Changed;
+    public event Action<RecognizedAction>? Recognized;
+    public RebindSession BeginRebind(string bindingId, RebindOptions options);
+    // 欠落等で影響する操作と認識器とバッファを中断する。
+    public IReadOnlyList<ActionEvent> Reset(InputDeviceId device, TimeSpan now);
+    public ActionProfile ExportProfile();
+    public void ApplyProfile(ActionProfile profile);
+}
+public sealed class RebindSession
+{
+    public RebindCandidate? Candidate { get; }
+    public IReadOnlyList<string> ConflictingBindingIds { get; }
+    public void Confirm(RebindConflictPolicy policy);
+    public void Cancel();
+}
```

ActionProfile は Action・Binding・Context の不変設定、RebindOptions はデバイス選択・捕捉しきい値・期間・中止操作、RebindCandidate は捕捉したコントロール指定とする。RebindConflictPolicy は Reject / Allow / ReplaceConflicts とし、ReplaceConflicts は競合する既存バインディングを無効にする。ActionProfile にはアクションごとの値補正設定とコンテキストごとの認識設定も保持する。ActionEvent は成立に寄与したデバイス ID を Devices に保持し、認識結果へ引き継ぐ。型の完全な宣言は実装前に本 ADR を具体化する。

### InputSystem との相互作用とサンプル

以下は設計用サンプルであり、ActionProfile のビルダー、Actions の型、Platform Source は未実装である。InputSystem からの履歴取得は既存 API、ElapsedTime は [INPUT-0002 の追加提案](INPUT-0002-processing-and-recognition.md)を使用する。

#### 構成と更新・ポーリング

プロファイルは構成側で作る。例えば Game.Move にキーボードの方向キー・実コントローラーの Left スティック・タッチ由来の仮想 Left スティックを割り当て、合成後に長さ 1 へ制限する。Game.Confirm には Enter と South を割り当て、そのアクションに長押し認識を設定する。機器のデッドゾーンは Device 側、移動の正規化は Action 側で各一度だけ適用する。

```csharp
var actions = new ActionSystem(profile);
var buffer = new ActionInputBuffer(
    new InputBufferOptions(TimeSpan.FromMilliseconds(150), MaxEntries: 16));
var cursors = new Dictionary<InputDeviceId, ulong>();
actions.ActivateContext("Game");
TimeSpan dispatchTime = TimeSpan.Zero;

actions.Recognized += operation =>
{
    buffer.Add(operation, dispatchTime); // 作成時刻は operation.At、評価時刻は現在。
};

// ゲームループから、InputSystem の管理スレッドで呼ぶ。
void Tick()
{
    input.Update(); // 全 Source・Device の取得、記録、通知、保持評価。
    TimeSpan now = input.ElapsedTime;
    dispatchTime = now;
    actions.SetDevices(selectedDeviceIds); // 実 Device と仮想 Device を選択。
    var batch = new List<InputRecord>();
    var interrupted = new HashSet<InputDeviceId>();

    // アクティブ Devices だけでは最終切断記録を取り逃すので
    // 切断済みも含む DeviceInfos から読む。
    foreach (InputDeviceInfo info in input.DeviceInfos)
    {
        ulong cursor = cursors.GetValueOrDefault(info.Id);
        InputReadResult read = input.ReadRecords(info.Id, cursor);
        if (read.HasGap)
        {
            actions.Reset(info.Id, now);
            buffer.ClearDevice(info.Id);
            // 不完全な履歴を渡さず、次の新規入力から再開する。
            interrupted.Add(info.Id);
        }
        else
        {
            batch.AddRange(read.Records.ToArray());
        }

        cursors[info.Id] = read.NextSequence;
    }

    // デバイス単位の読み取りをSystem全体の順序へ戻す。
    InputRecord[] ordered = batch.OrderBy(record => record.Sequence).ToArray();
    foreach (InputRecord record in ordered)
    {
        if (record.Data is DeviceDisconnectedData or FocusData { IsFocused: false }
            or TouchData { Phase: TouchPhase.Canceled }
            or PenData { Phase: PenPhase.Canceled })
        {
            interrupted.Add(record.DeviceId);
        }
    }

    actions.Advance(ordered, now); // 空バッチでも長押し・期限を進める。
    // 同じバッチの中断より前に成立した結果も、通知後に除去する。
    foreach (InputDeviceId device in interrupted)
    {
        buffer.ClearDevice(device);
    }

    buffer.Prune(now);
    if (canJump && buffer.TryConsume("Jump.Press", now, out _))
    {
        Jump();
    }
}
```

Advance は仮想 Device を含む選択済みデバイスだけを評価する。新規デバイスと選択変更は更新境界で適用し、押下中の入力から勝手に Started を作らない。途中のフォーカス・切断等は記録順に認識器へ伝え、候補を中断する。上記は安全側の例として、中断があるデバイスの先行入力をそのバッチ全体について破棄する。同一バッチの中断後に新しく成立した入力まで保持する実装では、キャンセル境界の通知を認識結果と同じ時系列で外部バッファへ渡す API を具体化する。

複数の利用者はそれぞれ cursors・ActionSystem・buffer を持つ。ReadRecords は履歴を消費しない。InputSystem の保持上限が短すぎれば HasGap となるため、利用者の最大処理間隔に合わせて SetRetentionPolicy を設定する。手動削除する場合は、全利用者の読了カーソルの最小値まで RemoveRecordsThrough を呼ぶ。

#### コンテキスト切替とリバインド

```csharp
// 更新境界でメニューに切り替える。変更は次の Advance に反映する。
actions.DeactivateContext("Game");
buffer.ClearContext("Game");
actions.ActivateContext("Menu");

// 以下の options は対象機器・期限・中止操作を含む構成側の設定。
RebindSession session = actions.BeginRebind("Game.Jump.Primary", options);
// Tick は継続する。ActionSystem が ReadRecords の入力から候補を捕捉する。
if (session.Candidate is not null && session.ConflictingBindingIds.Count == 0)
{
    // 現在の割り当てを変更せず、候補を編集用DTOに変換する。
    QueueRebindSave(session); // 次節の保存処理へ渡す構成側のコード。
    // 保存成功後、管理スレッドで確定プロファイルを適用する。
    // 保存失敗時に Confirm して実行設定だけを変更しない。
}
```

コンテキスト変更・リバインド確定では、ActionSystem が影響するアクションと認識をキャンセルする。外部 ActionInputBuffer は利用者が対応するコンテキストをクリアする。Recognized 通知中にプロファイルを変更する場合は次回境界へ保留し、InputSystem.Update へ再入しない。

#### 終了処理

ActionSystem は InputSystem.Recorded を直接購読する必要はなく、上記サンプルはポーリングだけで駆動する。停止時はゲームループを止めて、コンテキストと外部バッファを解除する。その後 InputSystem.Dispose が Source.Shutdown・Device の最終取得と解放を実行し、DI スコープが Source を破棄する。最終記録をアクション層まで配信する必要がある場合は、構成側が Source の切断を取り込む最後の Tick を実行してからループを停止する。

### Lumyte.Settings によるアクション設定の保存・復元

[設定保存基盤](../settings/SETTINGS-0001-user-settings-persistence.md)の `input-actions` セクションに、プロファイル、ActionId / BindingId、コントロール指定、アクション値補正、認識・入力バッファ設定、コンテキストの定義と優先順位を保存する。現在の押下状態、認識途中の時間、バッファ内候補、一時的なコンテキスト有効化、InputDeviceId は保存しない。

ActionProfile の実行型を直接 JSON 化せず、既知の種別文字列・数値・読み書き可能な設定DTO・List等へ変換する。InputProcessingSettings と同様に、ルートは引数なし class とする。enum の内部連番や実行 delegate に保存形式を依存させない。DTO と実行型の変換は任意の Input.Settings 連携モジュールが担当し、Actions の実行コアは Settings に依存しない。

```csharp
builder.Services.AddPersistedOptions<InputActionSettings>("input-actions")
    .Validate(ValidateActionSettings, "Invalid input action profile.")
    .UseJsonTypeInfo(InputActionSettingsJsonContext.Default.InputActionSettings,
        schemaVersion: 1);

// 上記はINPUT-0002と同じAddSettingsの保存ドキュメントに登録する。
var editableActions =
    provider.GetRequiredService<IEditableOptions<InputActionSettings>>();
ActionProfile initial = BuildActionProfile(editableActions.Current.Value);
var actions = new ActionSystem(initial);
```

InputActionSettings と JSON Context は入力連携モジュールが定義する未実装の型とする。ValidateActionSettings はIDの一意性・参照整合、値型、補正と認識の範囲、割り当て競合方針、保存後の復元可能性を検証する。Settings は保存候補を復元・検証してから確定するため、Configure / PostConfigure とバリデータは副作用なしとする。

#### リバインド候補を保存してから適用する

```csharp
// UI側の編集・非同期保存。入力の管理スレッドへ再入しない。
var edit = editableActions.BeginEdit();
WriteRebindCandidate(edit.Value, bindingId, session.Candidate!, conflictPolicy);
var result = await editableActions.SaveAsync(edit, cancellationToken);
if (result.Status != SettingsSaveStatus.Saved)
{
    ShowSettingsError(result.Status, result.Errors);
    return; // 現在のActionProfileを維持し、候補は再確認または中止する。
}
```

WriteRebindCandidate は候補と競合解決方針をDTOへ反映する構成側の処理とする。保存前に session.Confirm を呼ばず、保存成功の確定値から ApplyProfile する。保存待ちの間は同じセッションを二重確定せず、捕捉を凍結する。捕捉停止・保留状態の完全な API は実装前に具体化する。保存後に元の候補を再Confirmすると別Revisionを上書きし得るため、保存結果ではなく最新の確定スナップショットを使う。

```csharp
// ゲームループの開始時。awaitの継続スレッドでは実行しない。
if (editableActions.Revision != appliedActionRevision)
{
    SettingsSnapshot<InputActionSettings> snapshot = editableActions.Current;
    actions.ApplyProfile(BuildActionProfile(snapshot.Value));
    buffer.Clear(); // 全プロファイル候補をクリアする追加提案API。
    appliedActionRevision = snapshot.Revision;
}
// ApplyProfileは境界へ保留し、続くAdvanceで認識を中断・中立化する。
Tick();
```

全プロファイルの交換時は関連する認識・バッファを中断する。ActionInputBuffer.Clear() は全候補を削除するAPIとして追加提案する。継続中の押下から新しい操作を生成しない。設定変更は InputSystem の履歴や Source の接続構成を書き換えない。

input-processing と input-actions は別のモジュールRevisionを持つため、無関係な編集同士は衝突しない。両セクションを同時に変更する機能は個別 SaveAsync をトランザクションと見なさず、全体保存APIの契約に従うか、一つのセクションへまとめる追加設計を行う。共通ファイル全体のResetは未登録セクションも削除するため、入力設定だけの復元にはモジュールResetAsyncを使う。

### エラーとライフサイクル

管理スレッドで更新し、並行・再入 Advance は禁止する。不正な設定、未登録 ID、非有限値、逆行時刻・Sequence は拒否する。HasGap を検出した利用者は Reset と関連入力バッファのクリアを行い、既存押下から新しい操作を生成しない。

通知例外でも他の通知を継続し、最後にまとめて報告する。処理成功を巻き戻さず、再通知しない。Source・Device の所有権や履歴保持方針は変更しない。

## 検討した代替案

- ゲームコードから物理入力を直接参照する: 小規模用途には適するが、設定保存とUI優先順位が分散する。
- 物理キーだけで操作認識する: リバインドや別デバイスで同じ操作を再利用できないため、アクションを認識対象にする。
- 一つのグローバルなアクション表: 複数利用者や異なる画面の操作が混ざるため、利用者別に管理する。
- リバインド候補を自動確定する: 意図しない入力や競合を確定してしまうため、候補確認を分離する。

## 結果と影響

物理入力とゲームの操作名を分離し、複数デバイス・設定変更・メニュー切替を共通化できる。一方、遮断するコントロールと操作の終了を追跡する必要があり、コンテキストと設定の検証が複雑になる。保存形式と認識設定にはバージョン管理が必要になる。

## 検証方針

デバイス補正とアクション補正の適用境界、仮想デバイスの割り当て、複数バインディングの同値解決、短い押下、排他的コンテキストによる遮断と解放、押下中の切替、リバインドの開始・候補・競合・中止・期限、設定保存と再接続、欠落時の中立化、利用者間の独立性、異なるデバイスから同じアクションへの長押し・連打・同時・順序操作、入力なしの時間進行、バッファの期限境界・一度だけの消費を検証する。

## 別途決定する事項

ActionProfile と各設定型の完全な公開 API、保存スキーマと移行規則、安定したデバイス選択条件、アクション認識の発生元情報・結果API、UI表示名・アイコンとの連携は実装前に具体化する。本 PR は設計提案のみである。
