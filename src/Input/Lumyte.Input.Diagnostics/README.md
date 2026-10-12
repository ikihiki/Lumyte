# Lumyte.Input.Diagnostics

InputSystem と ActionSystem に同じ scoped `InputInspectionSession` を注入し、Device の現在値、Action の定義・Binding・現在値・認識結果を診断カタログの `input/get-snapshot` 操作で公開する。

```csharp
services.AddInputInspection();
services.AddDiagnosticExecutionPoint<BeforeInputProcessing>("before-input-processing");
services.AddDiagnosticSubsystem<InputDiagnostics, BeforeInputProcessing>(new("input", "Input", 1));
```

診断サーバーの Input ページは `Devices / Actions / Tape` の各ビューでこのスナップショットと公開済みInput操作を表示する。
