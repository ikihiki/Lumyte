# ADR-INPUT-0006: 更新境界と型付き入力を明示する補助 API

- 状態: 採用
- 日付: 2026-10-26
- 基準コミット: `16d856a0e9cda6a6ecfa559aaf55d87b4085b139`
- 変更関係: 一部変更
- 変更元: [INPUT-0002 の公開 API 一覧](INPUT-0002-processing-and-recognition.md#公開-api-一覧)、[INPUT-0003 の公開 API 一覧](INPUT-0003-actions-and-contexts.md#公開-api-一覧)
- 変更対象: 設定変更の予約 API 名、入力コントロールの生成、TryConsume の null 許容注釈
- 継続する判断: 更新境界、所有権、スレッド制約、入力の評価方法

## 背景

Input を使う開発者は、設定変更がいつ反映されるかを呼び出し名から判断しにくい。基準コミットの `src/Input/Lumyte.Input.Actions/ActionSystem.cs` では、`ApplyProfile` や `SetBufferOptions` は次の `Advance` への予約を行う。また、`InputControl.cs` の型付き生成はキーとコントローラーボタンに限られ、`ActionInputBuffer.cs` の `TryConsume` は成功時の非 null 契約をコンパイラーへ伝えていない。

## 決定

予約操作を `Queue` で始まる API へ改名し、以下の差分に示す旧名を削除する。入力コントロールには対象の型が分かる生成メソッドを追加し、`TryConsume` には成功時の非 null 注釈を付ける。実行時の契約を維持したまま、呼び出し名と型情報で利用方法を明示する。

### 利用者の変更前・変更後

同じプロファイルを次の入力更新から使う例を示す。`actions`、検証可能な `profile`、未処理の入力 `records`、単調増加時刻 `now` の準備と終了処理は省略する。

変更前は、即時変更に見える名前で予約する。

```csharp
actions.ApplyProfile(profile);
// ExportProfile() はまだ変更前のプロファイルを返す。
actions.Advance(records, now);
// ここから profile に従って入力を評価する。
```

変更後は、同じ反映時期を `Queue` で明示する。

```csharp
actions.QueueProfile(profile);
// ExportProfile() はまだ変更前のプロファイルを返す。
actions.Advance(records, now);
// ここから profile に従って入力を評価する。
```

マウスボタンの指定と認識済み操作の消費も、次のように書き換えられる。`Handle` は利用側の処理である。

変更前:

```csharp
var control = new InputControl(InputControlKind.MouseButton, (int)MouseButton.Left);
if (actions.Buffer.TryConsume("jump", now, out var operation))
{
    Handle(operation!); // 成功時に非 null であることを利用側が指定する。
}
```

変更後:

```csharp
var control = InputControl.ForMouseButton(MouseButton.Left);
if (actions.Buffer.TryConsume("jump", now, out var operation))
{
    Handle(operation); // 成功分岐では非 null と判定される。
}
```

### 維持する契約

予約 API が戻っても状態は変わらず、照会は最後に確定した値を返す。呼び出し側が既存の順序で Input と Actions を更新する。予約を `Task` に変換せず、内部で更新を進めない。

`InputControl` の汎用コンストラクター、`ForKey`、`ForButton` は維持する。追加する生成メソッドはキャストと種別指定を省くもので、プロファイルの検証時期は変えない。`TryConsume` は従来どおり、成功時に非 null、失敗時に null を返す。

Platform の `IWindow` は要求と観測状態を分離する契約を維持する。`WindowSystem.Windows` は登録ノード参照の一覧であり、各 `WindowNode` のプロパティは現在状態を参照する。不変の状態スナップショットではない。内部管理 ID は公開しない。

### 公開 API の差分

基準コミットに対する差分は次のとおり。改名するすべての入口と、非 null 注釈を追加する四つの `TryConsume` を示す。

```diff
 namespace Lumyte.Input.Actions
 {
     public sealed class ActionSystem
     {
+        // 検証後に予約し、次の Advance で反映する。作成スレッドで呼ぶ。
-        public void ApplyProfile(ActionProfile profile);
+        public void QueueProfile(ActionProfile profile);
-        public void SetBufferOptions(InputBufferOptions options);
+        public void QueueBufferOptions(InputBufferOptions options);
-        public void SetValueProcessors(string contextId, string actionId, IEnumerable<IActionValueProcessor> processors);
+        public void QueueValueProcessors(string contextId, string actionId, IEnumerable<IActionValueProcessor> processors);
-        public void SetRecognizers(string contextId, IEnumerable<IActionRecognizer> recognizers);
+        public void QueueRecognizers(string contextId, IEnumerable<IActionRecognizer> recognizers);
-        public void SetDevices(IEnumerable<InputDeviceId> devices);
+        public void QueueDevices(IEnumerable<InputDeviceId> devices);
-        public void ActivateContext(string contextId);
+        public void QueueContextActivation(string contextId);
-        public void DeactivateContext(string contextId);
+        public void QueueContextDeactivation(string contextId);
-        public void SetValueProcessors(Compose.Definitions.Context context, Compose.Definitions.Action action, IEnumerable<IActionValueProcessor> processors);
+        public void QueueValueProcessors(Compose.Definitions.Context context, Compose.Definitions.Action action, IEnumerable<IActionValueProcessor> processors);
-        public void SetRecognizers(Compose.Definitions.Context context, IEnumerable<IActionRecognizer> recognizers);
+        public void QueueRecognizers(Compose.Definitions.Context context, IEnumerable<IActionRecognizer> recognizers);
-        public void ActivateContext(Compose.Definitions.Context context);
+        public void QueueContextActivation(Compose.Definitions.Context context);
-        public void DeactivateContext(Compose.Definitions.Context context);
+        public void QueueContextDeactivation(Compose.Definitions.Context context);
     }
     public readonly record struct InputControl
     {
+        // enum とコントロール種別を対応させる。範囲検証は既存同様 Profile.Validate が行う。
+        public static InputControl ForMouseButton(MouseButton button);
+        public static InputControl ForControllerButton(ControllerButton button);
+        public static InputControl ForControllerStick(ControllerStick stick);
+        public static InputControl ForControllerTrigger(ControllerTrigger trigger);
         // ForKey と ForButton は継続提供する。
     }
     public sealed class ActionInputBuffer
     {
         // bool が true なら action は非 null、false なら null。
-        public bool TryConsume(string recognitionId, TimeSpan now, out RecognizedAction? action);
+        public bool TryConsume(string recognitionId, TimeSpan now, [NotNullWhen(true)] out RecognizedAction? action);
-        public bool TryConsume(string contextId, string recognitionId, TimeSpan now, out RecognizedAction? action);
+        public bool TryConsume(string contextId, string recognitionId, TimeSpan now, [NotNullWhen(true)] out RecognizedAction? action);
-        public bool TryConsume(Compose.Definitions.Recognition recognition, TimeSpan now, out RecognizedAction? action);
+        public bool TryConsume(Compose.Definitions.Recognition recognition, TimeSpan now, [NotNullWhen(true)] out RecognizedAction? action);
-        public bool TryConsume(Compose.Definitions.Context context, Compose.Definitions.Recognition recognition, TimeSpan now, out RecognizedAction? action);
+        public bool TryConsume(Compose.Definitions.Context context, Compose.Definitions.Recognition recognition, TimeSpan now, [NotNullWhen(true)] out RecognizedAction? action);
     }
 }
 namespace Lumyte.Input.Processing
 {
     public sealed class CorrectingInputSource
     {
+        // 既存 Device は次の取得境界で交換する。保持中の再試行バッチは先に完了する。
-        public void SetProcessorFactory(Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory);
+        public void QueueProcessorFactory(Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory);
     }
     public sealed class CorrectedInputDevice
     {
-        public void SetProcessors(IEnumerable<IDeviceDataProcessor> processors);
+        public void QueueProcessors(IEnumerable<IDeviceDataProcessor> processors);
     }
     public sealed class VirtualizingInputSource
     {
+        // 既存 Device の generator を次の Source.Update で交換する。
-        public void SetGeneratorFactory(Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory);
+        public void QueueGeneratorFactory(Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory);
     }
 }
 namespace Lumyte.Input.Settings
 {
     public sealed class InputSettingsCoordinator
     {
+        // 確定 revision を読み、後続 InputSystem.Update / ActionSystem.Advance へ予約する。
-        public void ApplyCommittedSettings();
+        public void QueueCommittedSettings();
     }
 }
```

## 検討した代替案

- 旧名を互換入口として残す: 既存コードの書き換えを避けられるが、同じ予約操作に即時変更に見える名前が残る。入口を `Queue` 名へ統一する。
- 予約 API をすべて非同期にする: 完了を待つ形で表せるが、入力更新を利用側が進める責務が不明瞭になるため採用しない。
- InputControl の生成を enum 型ごとのオーバーロードだけにする: 名前を揃えられるが、補完候補から対象を区別しやすい明示的な名前を選ぶ。

## 結果と影響

予約と即時参照の区別、型付きコントロールの構築、Try 系 API の成功分岐を、実装を読まずに利用できる。旧名の削除は破壊的変更であり、利用側は差分に示す `Queue` 名へ書き換える。反映時期と実行結果は変わらない。

回帰検証では、予約が更新前に状態を変えず、次の更新で従来と同じ結果を返すことを確認する。型付き生成の結果と汎用コンストラクターの結果、`TryConsume` の成否と null の対応も照合する。入力登録失敗と Platform の生成・表示については、既存契約の保証を確認する。
