# Lumyte.UI.Automation.Tests

```sh
dotnet test tests/UI/Lumyte.UI.Automation.Tests -c Release
```

GPUやOS入力を使わず、通常のUiSurface.Dispatchを通して自動操作を確認します。ManualClockはframeごとに単調時刻とtimerを進め、期限・予約待ち・取消を実時間のSleepなしで検証します。Fixture.RuntimeButtonとOffsetPanelは部品専用の状態更新としてlayout/inputを無効化します。

検索scope、前順、非表示と重複、snapshotの不変性と再接続ID、背景除外、clipとoverflow、probeと独自入力消費部品、二frame安定、既存press、通常のpointer/key系列、修飾キー、lease待機中の対象交換、Move／修飾キーcallbackでの対象変更、再送の禁止、取消・例外・終了後の後処理、page分離を検証します。

PNGの実画素は[描画サンプル](../../../samples/Lumyte.UI.Sample/README.md)、DOM入力とBrowser操作は[Browserサンプル](../../../samples/Lumyte.UI.Browser.Sample/README.md)で確認します。ScreenshotHostへの委譲fixtureはGPU描画の検証とは分けて扱います。
