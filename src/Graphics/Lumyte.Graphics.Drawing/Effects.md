# エフェクト

## モデルのライティングとフォグ

`ModelDrawArguments` でカメラ・方向ライト・ambient・フォグを指定します。
`LightDirection` は表面からライトへ向かう world-space の方向です。
`FogDensity = 0` でフォグを無効にし、正値でカメラからの距離に対する exponential fog を適用します。
計算と出力は linear color、HDR 値を保持します。詳細は [静的モデル描画](Models.md) を参照してください。

## 画面全体の色調整

`DrawingShaders.LoadPostProcessVertex()`／`LoadPostProcessFragment()` で作った pipeline は、
`Draw(3)` の fullscreen triangle で scene texture に次の処理を行います。

1. Exposure：stop 単位。`0` が元の明るさ、`1` が 2 倍、`-1` が半分。
2. Saturation：`1` が元の彩度、`0` が grayscale。
3. Vignette：`0` が無効、`1` が最大強度。AspectRatio で出力の縦横比を指定。
4. ToneMap：`1` が Reinhard、`0` が無効。元の alpha を保持。

値は有限とし、Saturation は非負、Vignette は `[0, 1]`、AspectRatio は正値を指定してください。
HDR scene は `Rgba16Float` の sampled/render attachment に描画します。
pass を終了してから texture を `ColorAttachment` → `Sampled` に明示遷移し、
scene texture と通常の clamp-to-edge sampler を argument table に登録します。

```csharp
var effects = new PostProcessArguments(
    sceneTextureRef, samplerRef,
    Exposure: 0.5f,
    Saturation: 0.8f,
    Vignette: 0.3f,
    AspectRatio: (float)width / height,
    ToneMap: 1);
render.SetPipeline(effectPipeline);
render.SetRenderState(new GraphicsRenderStateDesc
{
    ColorTargets = [new ColorBlendStateDesc()],
});
render.SetArguments(in effects);
render.Draw(3);
```

output 用の別 texture／swapchain frame を render attachment にします。
sampled texture と同じ image subresource へ同時に書き込まないでください。
複数 effect pass は ping-pong の texture と state transition を利用側で組み合わせます。
viewport／scissor／blend constant／stencil reference と submit／wait は明示してください。

output attachment が sRGB format なら linear → sRGB は attachment の変換に任せます。
shader 内で gamma を重ねて適用しません。linear format を選ぶ場合は次の表示経路で色空間を扱います。
