# UIの直接描画サンプル

`using static Lumyte.UI.Compose;`でPanel、Stack、RectangleとButtonの背景を組み立て、二つのviewportで再配置します。入力・Window・RenderGraphを使わないoffscreenサンプルです。

GPUなしで構築・layout・Canvasへの記録を実行できます。

```sh
dotnet run --project samples/Lumyte.UI.Sample -c Release -- record-only
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
