# UIの直接描画サンプル

`using static Lumyte.UI.Compose;`でPanel、Stack、RectangleとButtonの背景を組み立て、二つのviewportで再配置します。Window・RenderGraphを使わないoffscreenサンプルです。

GPUなしで構築・layout・Canvasへの記録を実行できます。

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- record-only
```

GPUなしのpointer-onlyはDown→領域外Move→内側Move→Upを配送し、callbackが一回だけ実行され、押下が解除されることを確認します。

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- pointer-only
```

Vulkanまたはwgpuで描画し、作業ディレクトリに`ui-240x160.ppm`と`ui-320x220.ppm`を出力します。

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- vulkan
dotnet run --project samples/Lumyte.UI.Sample -c Release -- wgpu
```

ソースビルドにはリポジトリのSlang環境、GPU実行には各backendが利用できるdriverが必要です。PPMはRGBのP6形式で、ImageMagick等で変換できます。

サンプルがdevice、Renderer2D、target/view、command buffer、submission、readbackを所有します。CanvasのBegin/Endを呼び、UiSurface.RecordのsnapshotをRenderer2D.PrepareAsyncへ渡します。targetの状態遷移とClearはホスト側で行い、Submit後のWaitAsyncが完了してからcommand buffer、prepared batch、targetを解放します。

viewportごとに描画先全体をClearしてUIを描きます。連続frameでも前frameのUIに重ね続けず、そのframeの背景を再構成してください。同じtargetへ複数surfaceを重ねる場合は、最初だけClearし、残りは同じCanvasへ追記するかLoadで描画します。Record自身はClearしません。

[UIの利用方法](../../src/UI/Lumyte.UI/README.md)／[描画のADR](../../docs/adr/ui/UI-0004-drawing-and-clipping.md)

## キーボードだけの確認

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- keyboard-only
```

GPU・Window入力sourceを使わず、論理viewportのUpdateLayout後にTabでButtonへfocusを移し、SpaceのDown/Upを配送します。Clickが一回、Down中だけIsPressed=true、Up後にfalseになることを確認します。実WindowのfocusやOSキーの取得は後続host接続PRの範囲です。

## 実Windowの入力とDPI

```sh
dotnet run --project samples/Lumyte.UI.Sample -- native
```

Windows（STA）または64-bit Linux/X11で、Wgpuの表示先へUIを提示します。色付きButtonのクリック、Tab／Shift-TabとSpace／Enterを扱い、クリック数をタイトルへ反映します。Window外の解放は取消です。標準sourceはWindow付きの取得列を使い、InputSystemのRecorded通知をqueueへ保存してUpdate終了後に配送します。

viewportはWindowの論理ClientSize、CanvasのscaleはWindow.State.Scale、Drawing2D targetのPixelScaleは1です。resizeは次frameでswapchainを再構成します。描画・入力に失敗した場合は通常更新を止め、Detach・GPUの完了と解放・surfaceのDispose・Window終了の順で清掃します。

2026-10-29はNativeサンプルのビルドとheadlessの統合fixtureを確認しました。Windows/X11 displayがないため、Native実機でのcapture・DPI変更・GPU提示は未確認です。BrowserのHosted例と確認範囲は[Browserサンプル](../Lumyte.UI.Browser.Sample/README.md)を参照してください。
