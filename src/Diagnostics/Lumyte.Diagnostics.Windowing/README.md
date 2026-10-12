# Lumyte.Diagnostics.Windowing

`WindowSystem`を既存の診断Operationへ明示的に公開する接続パッケージです。Windowing本体はDiagnostics、DI、通信へ依存しません。

```csharp
services.AddDiagnosticExecutionPoint<BeforeWindowUpdate>("window.before-update");
services.AddWindowDiagnostics<BeforeWindowUpdate>();

WindowDiagnosticRegistry registry = scope.ServiceProvider.GetRequiredService<WindowDiagnosticRegistry>();
using WindowDiagnosticRegistration registration = registry.Register("main", windowSystem);
```

登録IDは1～128文字の小文字ASCII識別子で、スコープ内で一意です。診断IDは登録中だけ安定した`window-N`であり、Windowingの内部管理IDやNative handleを識別子として公開しません。

`list-systems`、`list-windows`、`read`はObserve権限を要求します。`show`、`hide`、`activate`、`bring-to-front`、`close`はEdit権限を要求し、受理された管理要求IDを返します。結果の状態は後続のWindow更新後に`read`で観測します。

レジストリ、登録対象、診断PumpはWindowSystemと同じ所有スレッドで使用してください。スナップショットはscalar値だけを保持し、`IWindow`、`WindowNode`、HWNDなどのNative資源を診断通信へ渡しません。

対応する診断Serverでは、`Window`ページからWindowSystemとWindowを選択し、親子関係、寸法、可視性、寿命を確認できます。Edit権限がある場合は表示、非表示、活性化、前面化、終了を要求できます。管理要求は自動再送せず、状態の確定は「状態を更新」で明示的に再取得します。

設計判断は[Window診断ADR](../../../docs/adr/diagnostics/DIAGNOSTICS-0005-window-inspection.md)を参照してください。
