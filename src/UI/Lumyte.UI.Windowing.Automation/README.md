# Lumyte.UI.Windowing.Automation

UiPopupManagerと同じ入力routerを通す任意のAutomation接続です。Popupは独立surfaceのため、親pageの通常locatorには含めません。

```csharp
await using var session = new UiPopupAutomationSession(popups, dispatcherHost, hostFactory);
var infos = await session.ListPopupsAsync();
var popup = session.Popup(infos.Single(p => p.ParentPopupId is null).Id);
await popup.GetByTestId("save").ClickAsync();
await popup.GetByTestId("save").PressAsync(Key.Enter);
```

Popupのpageは遅延解決し、factoryと公開されたsurfaceの照合をdispatcherのUI ownerで行います。閉じたIDを別Popupへ対応し直しません。UI owner上のCreateOwnerHost(surface)とPopupHost(id)は、Diagnostics登録にも使えるrouting hostを返します。PopupHostの終了はBindLifetimeへ自動参加します。

hostFactoryは同じsurfaceを返す借用hostを生成し、root/Popup/submenuで同じ物理入力domainのleaseを共有してください。dispatcher・通常frame・入力の中立待ち・実入力保留・予約pointer identityはアプリの責務です。各surfaceへ独立した入力排他を作るだけでは同じ物理mouse/keyboardを保護できません。

UI.AutomationのIUiAutomationRoutingHostはprobe、keyboard scope、合成Dispatchに使います。driverはlease保持中のkeys/pointerを追跡し、取消も同じrouterへ戻してから元leaseを返します。Menuに隠れたrootへ直接クリックする経路を作りません。単一surface hostの従来の配送は維持します。

hostFactoryのScreenshot対応は実際の描画先を使います。Hostedでは合成済みrootの実画素から当該矩形を切り出してください。独立surfaceだけを再描画してroot合成結果とみなしてはいけません。サンプルはGPU完了後のPNG切り出しを含みます。

Diagnosticsではownerのrouting hostをRegister、Popupのrouting hostをRegisterPopupします。返ったUiAutomationDiagnosticRegistrationをBindLifetimeへ渡せます。親子はControlGroupIdとcontrol/heartbeat/Sequenceを共有し、clientはcontrol.PageFor(childTargetId)で明示選択します。子の入力・画像許可は親とのANDです。対象枠不足ではrootのAutomationCodeにautomation-unavailableを報告し、Popup表示は継続できます。

[ADR](../../../docs/adr/ui/UI-0011-popup-windowing-and-fallback.md)／[Diagnostics](../../Diagnostics/Lumyte.Diagnostics.UI.Automation/README.md)／[サンプル](../../../samples/Lumyte.UI.Popup.Sample/README.md)

RegisterPopupは親root・子host双方のIUiAutomationRoutingHostを確認し、単一surfaceへの直接配送hostをPopup groupへ混ぜることを拒否します。通常画面も最初からCreateOwnerHost経由で登録してください。
