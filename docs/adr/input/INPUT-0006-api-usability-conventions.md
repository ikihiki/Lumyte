# ADR-INPUT-0006: 更新境界と型付き入力を明示する補助 API

- 状態: 採用
- 日付: 2026-10-26

## 背景

Input の設定変更は更新境界で反映されるが、`ApplyProfile` や `SetBufferOptions` という名前からは即時変更との区別が難しい。また、入力コントロールの型付き生成がキーとコントローラーボタンに限られ、Try 系 API の成功時の非 null 契約がコンパイラーへ伝わっていない。

## 決定

[INPUT-0002](INPUT-0002-processing-and-recognition.md) と [INPUT-0003](INPUT-0003-actions-and-contexts.md) の更新境界、所有権、スレッド制約は維持する。予約操作は `Queue` で始まるAPIへ改名する。旧 `Apply` / `Set` / `Activate` / `Deactivate` の互換入口は削除し、利用側を一括移行する。戻り値が返った時点では予約済みであり、状態の照会は最後に確定した値を返す。予約を Task に置き換えたり、内部で更新を進めたりしない。

比較元は `main` の `16d856a`。追加する公開 API は次のとおり。

```diff
 namespace Lumyte.Input.Actions
 {
     public sealed class ActionSystem
     {
+        // 検証後に予約し、次の Advance で反映する。作成スレッドで呼ぶ。
+        public void QueueProfile(ActionProfile profile);
+        public void QueueBufferOptions(InputBufferOptions options);
+        public void QueueValueProcessors(string contextId, string actionId, IEnumerable<IActionValueProcessor> processors);
+        public void QueueRecognizers(string contextId, IEnumerable<IActionRecognizer> recognizers);
+        public void QueueDevices(IEnumerable<InputDeviceId> devices);
+        public void QueueContextActivation(string contextId);
+        public void QueueContextDeactivation(string contextId);
+        public void QueueValueProcessors(Compose.Definitions.Context context, Compose.Definitions.Action action, IEnumerable<IActionValueProcessor> processors);
+        public void QueueRecognizers(Compose.Definitions.Context context, IEnumerable<IActionRecognizer> recognizers);
+        public void QueueContextActivation(Compose.Definitions.Context context);
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
         // すべての TryConsume overload の out 引数に以下の属性を追加する。
         // bool が true なら action は非 null、false なら null。
-        public bool TryConsume(string recognitionId, TimeSpan now, out RecognizedAction? action);
+        public bool TryConsume(string recognitionId, TimeSpan now, [NotNullWhen(true)] out RecognizedAction? action);
     }
 }
 namespace Lumyte.Input.Processing
 {
     public sealed class CorrectingInputSource
     {
+        // 既存 Device は次の取得境界で交換する。保持中の再試行バッチは先に完了する。
+        public void QueueProcessorFactory(Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory);
     }
     public sealed class CorrectedInputDevice
     {
+        public void QueueProcessors(IEnumerable<IDeviceDataProcessor> processors);
     }
     public sealed class VirtualizingInputSource
     {
+        // 既存 Device の generator を次の Source.Update で交換する。
+        public void QueueGeneratorFactory(Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory);
     }
 }
 namespace Lumyte.Input.Settings
 {
     public sealed class InputSettingsCoordinator
     {
+        // 確定 revision を読み、後続 InputSystem.Update / ActionSystem.Advance へ予約する。
+        public void QueueCommittedSettings();
     }
 }
```

`InputControl` の従来の汎用コンストラクターは残す。新しい factory はキャストと種別指定を省くもので、profile の検証時期は変えない。`TryConsume` の属性追加は実行時の動作を変えない。

Platform の `IWindow` は OS 要求と観測状態を分離した既存契約を維持する。`WindowSystem.Windows` は登録ノード参照の一覧で、各 `WindowNode` のプロパティは現在状態を参照する。不変の状態 snapshot とは区別して文書化する。内部管理 ID を公開する変更は行わない。

## 検討した代替案

- 既存名を削除・改名する: 呼び出し側を破壊するため採用しない。
- 予約 API をすべて非同期にする: 入力更新と UI pump を利用側が駆動する責務が不明瞭になるため採用しない。
- InputControl の factory を enum 型ごとの overload だけにする: IntelliSense で対象を区別できる明示的な名前を選ぶ。

## 結果と影響

予約と即時参照の区別、型付きコントロールの構築、Try 系 API の成功分岐を、実装を読まずに利用できる。同じ操作の公開名は一つに統一し、利用例もQueue名へ移行する。

回帰検証では予約APIが更新前に状態を変更せず、次の更新で同じ結果を返すことを確認する。入力登録失敗と Platform の生成・表示契約の修正は、既存 ADR の保証を満たすための修正として別の回帰テストで確認する。
