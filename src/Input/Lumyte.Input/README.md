# Lumyte.Input

`src/Input/` に配置する抽象入力レイヤーです。履歴・状態管理を提供し、補正・認識・アクション・設定連携も Input カテゴリで管理します。OS・ハードウェアに近い入力取得と接続監視の実装は `src/Platform/` に配置し、`Lumyte.Input.Abstractions` で定義する契約を実装します。

デバイス別の入力記録、通知、ポーリングと保持方針を提供する .NET 10 ライブラリ。

`InputSystem` に `IEnumerable<IInputSource>` をコンストラクター注入します。Source は接続・切断時に `IInputDeviceRegistry` から Device を登録・削除し、Device は `DrainEvents` で正規化した入力を提供します。ソースの実装は各 Platform バックエンドの責務です。

```csharp
using Lumyte.Input;
using Lumyte.Input.Abstractions;

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

## Window付きの入力

ordered sourceはdevice登録順によらず取得列の順で取り込み、batch全体の検証・状態と履歴の確定・ackの後にRecordedを通知します。通知例外で同じbatchを再送しません。recordのWindowIdは取得origin、StateWindowIdは開始時の状態owner、Captureは取得原因です。加工や履歴再読で現在のactive Windowへ置換しません。

`GetState(device, windowId)`で対象scopeだけを参照します。既存GetState(device)は全scopeの集約です。押下はOR、位置は座標Windowと対で扱います。Windowのfocus喪失はそのscopeだけを中立化し、物理系列は終端まで別Windowへ移しません。明示StopSourceは終端停止し、ReadRecords.HasAcquisitionGapでretentionの欠落と区別できます。

標準Platform sourceはorderedです。加工する場合はOrderedCorrectingInputSource／OrderedVirtualizingInputSourceを使用し、source自体と生成元Window mapをホストの寿命中保持します。
