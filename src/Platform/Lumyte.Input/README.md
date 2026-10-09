# Lumyte.Input

デバイス別の入力記録、通知、ポーリングと保持方針を提供する .NET 10 ライブラリ。

`InputSystem` に `IEnumerable<IInputSource>` をコンストラクター注入します。Source は接続・切断時に `IInputDeviceRegistry` から Device を登録・削除し、Device は `DrainEvents` で正規化した入力を提供します。ソースの実装は各 Platform バックエンドの責務です。

```csharp
using var input = new InputSystem(sources);
input.Recorded += record => Console.WriteLine(record);
input.Update();
foreach (InputDeviceInfo device in input.DeviceInfos)
{
    input.SetRetentionPolicy(device.Id, new InputRetentionPolicy(MaxRecords: 1024));
    ReadOnlyMemory<InputRecord> records = input.GetRecords(device.Id);
    InputReadResult result = input.ReadRecords(device.Id, afterSequence: 0);
}
```

キーボード、マウス、コントローラー、マルチタッチ、ペン・筆圧の共通データを扱います。筆圧は 0〜1、取得不能なら null。読み取りは非破壊で、保持方針の既定は手動です。長期動作時は保持制限または明示削除を設定してください。削除しても現在状態と取得済みスナップショットは変わりません。

InputSystem は作成スレッドから利用します。注入 Source は構成側が所有し、InputSystem を先に Dispose した後で Source を破棄します。登録 Device は InputSystem が破棄します。

設計・終了順・エラー契約は [ADR-INPUT-0001](../../../docs/adr/input/INPUT-0001-input-system.md) を参照してください。Windows / Linux / Browser の入力取得バックエンドはこのパッケージに含みません。

## 設定の登録・保存・適用

共通ソースを `AddSettings(source)` で指定し、`services.UseInput()` を呼ぶと、InputSystem、設定定義、標準バリデータ、編集 API、InputSettingsProcessor が自動登録されます。二重有効化では登録を重複させません。

```csharp
var settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
var edit = settings.BeginEdit();
edit.Value.Bindings["game"] = new() { ["jump"] = ["key:Space", "controller:South"] };
edit.Value.LeftStick.Inner = 0.2f;
var result = await settings.SaveAsync(edit);
```

Bindings はコンテキスト ID → アクション ID → 物理入力配列です。物理入力は `key:Space`、`mouse:Left`、`controller:South` のような既存 enum の文字列名です。空配列は割り当て解除です。既定のアクションはアプリが定義し、初期 Bindings は空です。同じアクション内の重複は拒否し、異なるアクション間の共有は許可します。

```csharp
var processor = provider.GetRequiredService<InputSettingsProcessor>();
// InputSystem と同じ管理スレッドで、フレーム開始時に実行します。
processor.Refresh();
var jumpBindings = processor.GetBindings("game", "jump");
var position = processor.ApplyStick(ControllerStick.Left, originalPosition);
```

設定は Refresh まで現在フレームに反映されません。デッドゾーンはスティックに放射状変換、トリガーに軸変換を適用します。Inner / Outer は有限値で `0 <= Inner < Outer <= 1` を満たす必要があります。既定 Inner はスティック 0.15、トリガー 0.05、Outer は 1 です。InputSystem の原入力・履歴は変更しません。アクション評価、複合入力、入力伝播は後続の機能です。

共通基盤の使い方は [Lumyte.Settings](../../Core/Lumyte.Settings/README.md) を参照してください。
