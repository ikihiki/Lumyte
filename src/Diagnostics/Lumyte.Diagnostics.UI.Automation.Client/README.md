# Lumyte.Diagnostics.UI.Automation.Client

既存Diagnosticsサービスへ接続する外部C# clientです。Operator認証とBaseAddressを設定したHttpClientを借用します。ゲーム用token、Widget型、native backendを必要としません。

```csharp
await using var client = new UiAutomationDiagnosticsClient(http, sessionId);
var target = (await client.ListTargetsAsync()).Single(x => x.Name == "settings");
await using var control = await client.AcquireControlAsync(target.Id);
await control.Page.GetByRole("button", name: "保存").ClickAsync();
await control.Page.Expect(control.Page.GetByTestId("saved-indicator")).ToBeVisibleAsync();
await control.Page.GetByTestId("save").PressAsync("Enter");
byte[] png = await control.Page.ScreenshotAsync();
```

ゲーム接続がHTTPかMagicOnionかにかかわらず、Operatorは既存のHTTPサービスAPIを利用します。最初にschema version、Operation ID、権限、traffic、引数・結果fieldと制約をcatalogで照合します。外側のDiagnosticValueのInt64文字列と、内側planJsonの数値を混同しません。

`client.Page(targetId)`は観測専用です。入力はcontrol.Pageでだけ呼べます。Locatorは同じpage内でscopeを連結でき、Expectに別pageのLocatorは渡せません。入力の待機とassertionはゲーム側の一つのJobで実行し、clientがクリックを繰り返す方式ではありません。

controlは1秒ごとに更新し、同じcontrolの並列入力を拒否します。RPCの再試行は同じRequestId・本文・Sequence・配送期限で一度だけ行います。応答が不明ならoutcome-unknownとInputMayHaveBeenSentを報告し、新しいSequence/control/sessionで操作を再実行しません。callbackの失敗や画面変更も、クリックの再送で解決しません。

高水準呼出は既定30秒の一つの期限に受付・状態照会・artifact取得を含めます。ゲームJob期限は既定5秒、最大10秒で別に固定されます。状態pollは250ms以上、Stream RPCは1秒以下です。artifactはoffset・ID・byte数・全体サイズ・SHA-256を検査し、hashが一致するまで成功結果として返しません。

成功した高水準操作はForgetで保持枠を返します。低水準のStartAsync／StartObservationAsyncはUiRemoteJobを返し、GetStatusAsync／WaitAsync／CancelAsync／DownloadArtifactAsync／ReleaseAsyncで管理します。Cancelは受付まで、Waitはhostの後処理完了までです。失敗した高水準操作のJobIdからは`client.Job(jobId, inputJob: true)`等で保持中のstatusとtraceを回収できます。artifactを読むOperatorにはObserve権限が必要です。

DisposeAsyncは更新を止め、進行中renewを回収し、Revisionを確認してcontrolを解放します。通信断では既定5秒の終了期限で結果不明を報告し、ゲーム側のlease失効を最終解除手段とします。HttpClientやゲーム接続を閉じて他Actorへ影響させません。

[設計](../../../docs/adr/ui/UI-0010-diagnostics-ui-automation.md)／[実行例](../../../samples/Lumyte.Diagnostics.UI.Automation.Operator.Sample/README.md)

Popupのtarget metadataはParentTargetId、PopupId、ControlGroupIdを含みます。同じgroupの子はcontrol.PageFor(childTargetId)で明示選択し、既存controlのheartbeatとSequenceを使います。所属・許可はサーバーが毎回検証します。この版のContracts/Clientを同時に更新してください。
