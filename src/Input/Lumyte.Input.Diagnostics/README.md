# Lumyte.Input.Diagnostics

InputSystem と ActionSystem に同じ scoped `InputInspectionSession` を注入し、Device の現在値、Action の定義・Binding・現在値・認識結果を診断カタログの `input/get-snapshot` 操作で公開する。

```csharp
services.AddInputInspection();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("before-input-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
services.AddInputActionSettingsDiagnostics<BeforeInputProcessing>();
```

診断サーバーの Input ページは `Devices / Actions / Tape` の各ビューでこのスナップショットと公開済みInput操作を表示する。

Action一覧の`target`は現在のActionProfile世代、Device一覧のControl targetは現在のDevice接続世代にだけ有効な不透明IDである。Device targetはKey、MouseButton、ControllerButton、ControllerStick、ControllerTriggerに加え、Mouse位置・Wheel、観測中のTouch/Penの位置・筆圧を状態・履歴・通知へ反映する前に置換する。`IInputOverrideService`、`IInputTapeService`、`IInputLiveService`は同じscoped `InputInspectionSession`へ解決され、同じTargetを同時に確保しない。直接Overrideは最大5秒、Tapeは明示的なframe境界、Liveは単調増加するbatch sequenceで適用する。同時操作は合計64件まで、Tape開始は120フレーム先までとする。

`AddInputActionSettingsDiagnostics<TPoint>()`は既存の設定診断を`settings.input-actions`として公開する。Actions画面は`read-tree`、`save-tree`、`save-result`を表示し、InputActionSettings全体の検証・Revision競合・永続化は共有設定システムが担当する。保存後はInputSettingsCoordinatorの通常の更新境界でActionSystemへ適用する。

ゲームループは診断Pumpより前にフレーム番号を通知し、その後に通常のInput／Action更新を行う。

```csharp
InputInspectionSession inspection = services.GetRequiredService<InputInspectionSession>();
inspection.BeginFrame(frameNumber);
pump.Pump(new(frameNumber, timestampTicks), budget);
input.Update();
actions.Advance(records, input.ElapsedTime);
```

診断操作は`OverrideInput`権限を要求する。`override-acquire`／`override-get`／`override-update`／`override-release`、`tape-play`／`tape-get`／`tape-stop`、`live-begin`／`live-send`／`live-heartbeat`／`live-stop`を公開する。profile変更、ActionSystem破棄、または所有スレッドからの`NotifySessionClosed`で予約と値を解除する。操作値とTapeはメモリ上だけに保持し、設定保存システムへ永続化しない。

## Window scopeとschema 2

snapshotのschemaVersionと診断subsystemのSchemaVersionは2です。deviceは一件を維持し、windowScopesに個別状態・操作targetを記録します。履歴の直近32件/deviceにはwindowId／stateWindowId／captureを保持し、UInt64はdecimal stringで送ります。旧schemaの画面として解釈しません。

Window付きのkey/button/controlにはGetWindowTarget、mouse/touch/penには既存名のWindow引数付きoverloadを使用します。未知・退役済み・切断済みscopeを現在Windowで補いません。Windowなしの集約targetをWindow付きdeviceへ適用する要求はwindow-requiredで拒否します。Focus喪失・取消・終了は該当scopeのleaseだけを終了させます。

Tapeは取得レコードの再送ではなく、aliasの操作プログラムです。再生時にWindow付きtargetを明示してbindします。Captureの取得stampを新入力へ再利用しません。
