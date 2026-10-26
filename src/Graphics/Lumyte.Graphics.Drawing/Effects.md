# エフェクト

## モデルのライティングとフォグ

`ModelDrawArguments` でカメラ・方向ライト・ambient・フォグを指定します。
`LightDirection` は表面からライトへ向かう world-space の方向です。
`FogDensity = 0` でフォグを無効にし、正値でカメラからの距離に対する exponential fog を適用します。
モデルの出力は scene-linear HDR です。透明材質の source RGB は straight alpha とし、
`ModelDrawing.CreateRenderState` の blend が中間画像へ premultiplied RGB を書き込みます。
透明背景は RGBA zero で clear してください。詳細は [静的モデル描画](Models.md) を参照してください。

## RenderGraph と共有 ImageEffects

画面全体の処理には [Lumyte.Graphics.ImageEffects](../Lumyte.Graphics.ImageEffects/README.md) を使います。
Drawing 専用の postprocess shader／argument 型は持ちません。
単一 mip／layer の `Rgba16Float` scene を RenderGraph の `EffectImage` へ渡します。

```csharp
using Lumyte.Graphics.ImageEffects;

var scene = new EffectImage(sceneHandle, new(0, 0, width, height), EffectColorSemantic.SceneLinear);
EffectImage output = graph.AppendPostProcess(scene,
    exposure: 0.5f, saturation: 0.8f, vignette: 0.3f, toneMap: true);
graph.MarkOutput(output.Texture);
using var plan = graph.Compile();
plan.Record(commands);
// commands.Finish、Submit、実際の GPU 完了後に plan を Dispose。
```

`sceneHandle` は同じ graph へ import、または graph 内で生成・描画した texture です。
`width`／`height` は pixel 単位の int。露出は stop、彩度は非負、ビネットは `[0, 1]` です。
縦横比は画像の範囲から求めます。処理中は RGB を unpremultiply し、結果を alpha で再び乗算します。
alpha は保持し、alpha zero の RGB は zero にします。

`toneMap: true` は SceneLinear 入力を Reinhard mapping し、DisplayLinear 出力へ明示変換します。
DisplayLinear の入力への重複 tone mapping は拒否します。false なら semantic を保持します。
再利用する DAG には `PostProcessEffect` を使えます。他の効果との合成・barrier・中間画像・
固定 GPU 資源の寿命は既存の ImageEffects／RenderGraph に委ねます。

標準の埋め込み shader でも、`AddImageEffects(manager)` で取得した共有 GPU 資源でも同じ Append を使います。
共有 shader をカスタマイズする場合は、更新された effect shader の operation 6 にも対応してください。
output を sRGB attachment へ描画する場合は、その変換を attachment に任せ、gamma を重ねて適用しません。
