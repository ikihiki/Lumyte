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

## 親RenderGraph・Resourcesの確認

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --render-graph vulkan
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --render-graph wgpu
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --resources vulkan
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --resources wgpu
```

`--render-graph`はResourcesなし、`--resources`はDIで設定した既存Drawing2D pipelineを使う独立した経路です。同じCPU layoutをCanvas2DとRenderGraphCanvas2Dへ記録し、同じ形式・DPI (1.5, 2)・Clear/Load条件でreadbackを厳密比較します。直接描画用とgraph用のtargetをそれぞれ3 frame再利用し、UI移動・削除後の背景復元を確認します。Load時は親graphにdisplay-linearの青い背景passを置き、UI、readbackの順で実行します。3D rendererは使用せず、tone mapping済み背景との合成境界を確認します。

2026-10-29にVulkanとwgpu（Mesa lavapipeのソフトウェアGPU）で上記4経路を実行しました。HotReloadとdevice世代切り替えはResourcesの統合fixture、Append/Compile/Recordの失敗・取消とcleanup再試行はUI／Drawing2Dのfixtureで検証します。実機GPUのdeviceロスト・復旧後の再描画は未検証です。

## Locatorによる自動操作

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --automation record-only
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --automation vulkan
dotnet run --project samples/Lumyte.UI.Sample -c Release -- --automation wgpu
dotnet run --project samples/Lumyte.UI.Sample -c Release -- native --automation
```

role/nameで保存Buttonを探し、Click、成功indicatorの表示、Enterによる二回目の保存、focusを確認します。専用モード中はOS入力をUIへ渡さず、通常のUiSurface.Dispatchを自動操作のleaseから使います。record-onlyはGPU不要です。

GPUのoffscreen経路は新しい描画frameの実readbackを`ui-automation.png`へ保存します。Native Windowの経路は提示も進め、`ui-native-automation.png`を保存して終了します。PNGには対象surfaceだけを含みます。GPUのcold pipeline準備を含めサンプルの期限は30秒、ライブラリの既定は5秒です。

2026-10-29にheadless、Vulkan／wgpu（Mesa lavapipe）の自動操作とPNG保存を確認しました。NativeのWindow経路はビルドを確認し、Windows/X11 displayがないため実機提示・操作は未検証です。UIレベルの合成入力の確認と、OS入力取得の確認は区別します。
