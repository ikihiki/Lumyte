# Lumyte.Input.Diagnostics

InputSystem と ActionSystem に同じ scoped `InputInspectionSession` を注入し、Device の現在値、Action の定義・Binding・現在値・認識結果を診断カタログの `input/get-snapshot` 操作で公開する。

```csharp
services.AddInputInspection();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("before-input-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
```

診断サーバーの Input ページは `Devices / Actions / Tape` の各ビューでこのスナップショットと公開済みInput操作を表示する。

Action一覧の`target`は現在のActionProfile世代にだけ有効な不透明IDである。`IInputOverrideService`、`IInputTapeService`、`IInputLiveService`は同じscoped `InputInspectionSession`へ解決され、同じTargetを同時に確保しない。直接Overrideは最大5秒、Tapeは明示的なframe境界、Liveは単調増加するbatch sequenceで適用する。同時操作は合計64件まで、Tape開始は120フレーム先までとする。

ゲームループは診断Pumpより前にフレーム番号を通知し、その後に通常のInput／Action更新を行う。

```csharp
InputInspectionSession inspection = services.GetRequiredService<InputInspectionSession>();
inspection.BeginFrame(frameNumber);
pump.Pump(new(frameNumber, timestampTicks), budget);
input.Update();
actions.Advance(records, input.ElapsedTime);
```

診断操作は`OverrideInput`権限を要求する。`override-acquire`／`override-get`／`override-update`／`override-release`、`tape-play`／`tape-get`／`tape-stop`、`live-begin`／`live-send`／`live-heartbeat`／`live-stop`を公開する。profile変更、ActionSystem破棄、または所有スレッドからの`NotifySessionClosed`で予約と値を解除する。操作値とTapeはメモリ上だけに保持し、設定保存システムへ永続化しない。
