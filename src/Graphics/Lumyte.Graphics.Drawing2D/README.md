# Lumyte.Graphics.Drawing2D

Canvasに記録した図形・画像・paint graphを、共通Graphics APIとRenderGraphで描画する。文字組みは外部ライブラリが担当し、パス・mesh・coverage・SDF/MSDF/MTSDF・画像のいずれでも描画を依頼できる。

## DIを使わない接続

`device` はアプリケーションが作成した `IGraphicDevice`、`targetView` は同じdeviceの `Rgba8Srgb` または `Rgba16Float` のRenderAttachment viewとする。device・描画先・入力画像の所有権はアプリケーションに残る。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRoundedRect(
    new Rect2D(20, 20, 240, 80),
    12,
    PaintGraph2D.LinearGradient(
        new(20, 20), new(260, 20),
        [new(0, new(0.1f, 0.4f, 1, 1)), new(1, new(0.6f, 0.2f, 0.9f, 1))]));
DrawList2D list = canvas.End();

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new()
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Clear,
    ClearColor = new(0, 0, 0, 0),
});
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    prepared.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
```

`PrepareAsync` の入口と出口で描画先は `ColorAttachment`、入力画像は `Sampled` とする。初回の描画先状態遷移やPresentへの遷移はアプリケーションで記録する。`AppendPassesAsync` を使う場合は、外部画像を共通graphへ一度だけimportして `DrawingGraphBindings2D` へ登録する。

準備結果は一回の記録に使う。再描画は同じ `DrawList2D` から改めて準備する。GPU使用完了後にcommand buffer、compiled graph、準備結果の順で解放する。待機のキャンセルはGPU完了を意味しない。

## 描画方式の差し替え

11命令にはそれぞれ専用interfaceがある。例えば `IFillPathRenderer2D` を差し替えると `FillPath` を独自のcompute経路にできる。準備結果の `AppendPasses` でcopy・compute・renderの複数passを宣言し、各resourceへのアクセスをRenderGraphへ渡す。

`DrawingCommandRenderers2D` のconstructorには11個の実装を直接渡す。DIを使う場合は別packageの `UseGraphics2D` が標準実装を登録し、その後の `AddScoped<IFillPathRenderer2D, CustomFillPathRenderer>()` で上書きできる。

## Paint・エフェクト

塗りと線は単色、linear/radial/sweep gradient、image pattern、opacity、path clip、transform、layer、COLRv1の全28合成modeを受け付ける。色はstraight sRGBで入力し、描画時はlinear premultiplied RGBAとして評価する。

`BeginLayer` / `EndLayer` はグループ全体へのエフェクトとopacity、`DrawBackdropEffect` はそこまでに描いた背景へのエフェクトを表す。色行列・Gaussian blur・opacity・合成は、`Lumyte.Graphics.ImageEffects` が提供するRenderGraphの拡張メソッドで同じgraphへパスを追加する。

2D描画とエフェクト適用時の契約、API一覧、共通graphへの接続例は [GRAPHICS-0017](../../../docs/adr/graphics/GRAPHICS-0017-2d-graphics-engine.md) を参照する。画像エフェクト自体の設計とAPIは [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) に記載する。

すべてのエフェクトは共通ライブラリの `ImageEffectDefinition` に領域計算と固定のAppendPassesを実装し、
明示的に生成してImageEffectGraph.Createで組み立てます。他の効果と組み合わせた同じgraphを、記録中のCanvasへ渡せます。

```csharp
var effect = ImageEffectGraph.Create(definition, ImageEffectGraph.Source());
var blur = new GaussianBlurEffect(3, 3);
var effects = ImageEffectGraph.Create(blur, effect);

canvas.BeginLayer(new LayerOptions2D
{
    ContentBounds = new Rect2D(10, 10, 100, 50),
    Effects = effects,
});
canvas.FillRect(new Rect2D(10, 10, 100, 50), new Color2D(1, 0, 0, 1));
canvas.EndLayer();

canvas.DrawBackdropEffect(new BackdropEffectOptions2D
{
    OutputBounds = new Rect2D(150, 10, 100, 50),
    Effects = effects,
});
```

背景取得は独自定義の必要入力範囲を含めて行い、独自効果の出力にも通常の親clip・opacity・合成を適用します。
エフェクトとAppend拡張メソッドの実装例は [GRAPHICS-0016](../../../docs/adr/graphics/GRAPHICS-0016-image-effects.md) を参照してください。

エフェクトはDIへ登録せず、直接生成します。エフェクトレンダラーの差し替えはなく、新しい処理は別の効果型を追加します。UseGraphics2Dが登録するのは描画命令の実装と描画登録表だけです。
