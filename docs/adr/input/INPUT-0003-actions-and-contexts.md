# ADR-INPUT-0003: アクションマッピング・リバインド・入力コンテキスト

- 状態: 提案
- 日付: 2026-10-09

## 背景

ゲームの「移動」「決定」等を物理キーに直接結び付けると、複数デバイス対応と設定変更がゲームコードに分散する。メニュー・チャット・ゲームで同じ入力の意味が異なり、入力の優先順位と伝播も必要になる。

[Input 基盤](INPUT-0001-input-system.md)と[加工・認識層](INPUT-0002-processing-and-recognition.md)を利用し、論理操作と利用場面を分離する。

## 決定

### 責務と配置

`src/Platform/Lumyte.Input.Actions/`、名前空間・パッケージ名 `Lumyte.Input.Actions` に実装する。共通 Input と Processing に依存し、Engine、UI、Platform実装には依存しない。利用者・プレイヤーごとに独立した ActionSystem を持ち、デバイス ID の集合と有効コンテキストを設定する。

### アクションとバインディング

ActionId と BindingId は設定内で一意な永続文字列とする。Action は Button、Axis1D、Axis2D の値型を持ち、値はそれぞれ 0 / 1、-1〜1、各軸 -1〜1 とする。複数の物理入力または認識 Signal を一つの Action に結び付けられる。

基礎バインディングはキー・ボタン、スティック、トリガー、正負キーによる1軸、上下左右キーによる2軸、Signal ID を対象とする。加工・複合操作は Processing に委譲し、アクション層で別の認識器を持たない。

Button はいずれかが押下なら有効とする。アナログ値は最大絶対値、2軸は最大長の入力を選び、同値は BindingId の辞書順で決める。加算等の合成方式は明示設定とし、既定にしない。軸から Button へ変換する場合は押下・解放の別しきい値でヒステリシスを設ける。

出力は Started、Performed、Canceled とし、記録順で遷移する。同一更新内の短い押下と解放の両方を通知し、最終値だけから判定しない。GetState は更新終了時の不変状態を返し、通知は更新結果の公開後に行う。

### 入力コンテキスト

コンテキストはバインディング群、優先順位、排他的かどうかを持つ。優先順位の降順で評価し、同順位は有効化順の新しいものを先にする。例えばチャット、メニュー、ゲームの順に評価できる。

高優先の排他的コンテキストで一致した物理コントロールは、その更新で低優先への伝播を抑制する。基盤の記録は削除しない。押下を遮断したコントロールの解放も同じ経路で扱い、下位アクションを押下のまま残さない。Signal認識には入力遮断後のストリームを渡し、排他的コンテキストを越えて認識結果を共有しない。

コンテキスト無効化、割り当て変更、デバイス切断、履歴欠落時は影響するアクションを Canceled・中立値にする。新たに有効化したコンテキストでは既に押下中のコントロールから Started を生成せず、一度解放して次の押下を待つ。アナログ値は次の取得値から再評価する。変更は Advance の開始時にまとめて適用し、通知中の変更は次回に送る。

### リバインドと設定保存

リバインドは対象 BindingId を指定して開始し、指定デバイス範囲内の次の入力を候補として捕捉する。開始時に既に押下中の入力、リピート、微小なアナログ変化を除外する。中止操作、タイムアウト、切断で終了し、元設定を維持する。

候補は直ちに設定へ反映せず、同じコンテキスト内の重複を報告する。利用者が許可・拒否・既存割り当て解除を選んで確定する。UIが候補表示を行えるよう、候補と状態を読み取り専用で取得する。捕捉中の入力は対象利用者の通常アクションへ伝播させない。

保存対象はスキーマバージョン、ActionId / BindingId、コントロール指定、加工設定とコンテキスト設定とする。接続期間限定の InputDeviceId は保存せず、デバイス種類・利用者が選ぶプロファイル等の安定した選択条件を保存する。ファイル I/O は構成側へ委譲する。未対応バージョンや欠けた ActionId は明示的なエラーとし、移行または既定復元を利用者が選ぶ。

### 公開 API 一覧

新規 API の宣言案。設定型とメソッド本体の詳細は省略する。

```diff
+public enum ActionValueKind { Button, Axis1D, Axis2D }
+public enum ActionPhase { Started, Performed, Canceled }
+public sealed record ActionEvent(
+    string ActionId, ActionPhase Phase, Vector2 Value, TimeSpan At);
+public sealed record ActionState(ActionValueKind Kind, Vector2 Value);
+public sealed class ActionSystem
+{
+    public ActionSystem(ActionProfile profile);
+    public void SetDevices(IReadOnlyList<InputDeviceId> devices);
+    public void ActivateContext(string contextId);
+    public void DeactivateContext(string contextId);
+    // 入力記録・時間から加工と認識を進め、アクションを公開する。
+    public IReadOnlyList<ActionEvent> Advance(
+        ReadOnlyMemory<InputRecord> records, TimeSpan now);
+    public ActionState GetState(string actionId);
+    public event Action<ActionEvent>? Changed;
+    public RebindSession BeginRebind(string bindingId, RebindOptions options);
+    // 欠落等で影響する操作と認識器を中断する。
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

ActionProfile は Action・Binding・Context の不変設定、RebindOptions はデバイス選択・捕捉しきい値・期間・中止操作、RebindCandidate は捕捉したコントロール指定とする。RebindConflictPolicy は Reject / Allow / ReplaceConflicts とし、ReplaceConflicts は競合する既存バインディングを無効にする。型の完全な宣言は実装前に本 ADR を具体化する。

### エラーとライフサイクル

管理スレッドで更新し、並行・再入 Advance は禁止する。不正な設定、未登録 ID、非有限値、逆行時刻・Sequence は拒否する。HasGap を検出した利用者は Reset と関連入力バッファのクリアを行い、既存押下から新しい操作を生成しない。

通知例外でも他の通知を継続し、最後にまとめて報告する。処理成功を巻き戻さず、再通知しない。Source・Device の所有権や履歴保持方針は変更しない。

## 検討した代替案

- ゲームコードから物理入力を直接参照する: 小規模用途には適するが、設定保存とUI優先順位が分散する。
- 一つのグローバルなアクション表: 複数利用者や異なる画面の操作が混ざるため、利用者別に管理する。
- リバインド候補を自動確定する: 意図しない入力や競合を確定してしまうため、候補確認を分離する。

## 結果と影響

物理入力とゲームの操作名を分離し、複数デバイス・設定変更・メニュー切替を共通化できる。一方、遮断するコントロールと操作の終了を追跡する必要があり、コンテキストと設定の検証が複雑になる。保存形式と認識設定にはバージョン管理が必要になる。

## 検証方針

複数バインディングの同値解決、短い押下、排他的コンテキストによる遮断と解放、押下中の切替、リバインドの開始・候補・競合・中止・期限、設定保存と再接続、欠落時の中立化、利用者間の独立性を検証する。

## 別途決定する事項

ActionProfile と各設定型の完全な公開 API、保存スキーマと移行規則、安定したデバイス選択条件、UI表示名・アイコンとの連携は実装前に具体化する。本 PR は設計提案のみである。
