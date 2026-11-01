# UI Automation Operator

ゲームのWidgetやGraphics backendを参照せず、Contractsと既存Diagnostics transport envelopeだけで操作する外部C#アプリです。OperatorTokenとBaseAddressを設定し、settings Targetへ保存操作・Expect・Enter・countを実行します。Screenshotが許可されていれば、hash検証後にPNGを保存します。

```sh
dotnet run --project samples/Lumyte.Diagnostics.UI.Automation.Operator.Sample -c Release -- http://127.0.0.1:5000 /tmp/ui-remote.png
```

テスト用のLUMYTE_UI_CLOSE_SAMPLE=1は、client/control解放後に既存のサービスAPIでゲーム接続を閉じます。通常のclient.DisposeAsyncは接続を閉じません。GameTokenをこのアプリへ渡す必要はありません。

[ゲームと検証手順](../Lumyte.Diagnostics.UI.Automation.Game.Sample/README.md)／[client API](../../src/Diagnostics/Lumyte.Diagnostics.UI.Automation.Client/README.md)
