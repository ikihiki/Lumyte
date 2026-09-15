# 準備済み文字の描画入力

この assembly は font の読込み、shaping、fallback、段落 layout を行わない。それらを済ませた `Lumyte.Resources` 等から、直接描画できる不変の配置と glyph を受け取る。

`TextDrawData` は全体の Size、Lines と GlyphRuns を所有する。`PositionedGlyphData` は glyph、transform、advance、UTF-16 cluster をそのまま保持し、描画時に再配置しない。`GlyphUploadData` は輪郭と任意の color paint tree を直接持つ。解決用 font ID や font file bytes を再取得しない。

```csharp
using System.Numerics;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;

var outline = new PathBuilder().MoveTo(new(0, 12)).LineTo(new(5, 0))
    .LineTo(new(10, 12)).Close().Build();
var glyph = new GlyphUploadData(new("triangle glyph", 1), new(0, 0, 10, 12),
    new GlyphOutline(outline));
var text = new TextDrawData(new("label", 1), new(12, 12),
    [new(12, 12, 0, new(0, 0, 12, 12), new(0, 0, 10, 12))],
    [new([new(glyph, Matrix3x2.Identity, new(12, 0), 0)])]);
using var draw = new Draw2DSceneBuilder();
draw.DrawText(text, new(8, 8), Brush.Solid(Color.White));
```

color glyph は `GlyphPaintNode` の Outline、Solid、Foreground、Gradient、Bitmap、Transform、Clip、Composite、Layers で表す。`GlyphPaintNode.LinearGradient`／`RadialGradient`／`SweepGradient` は `GlyphGradientStop.Foreground` を含む stop も受ける。foreground を特定色に固定せず、描画時の brush に gradient の alpha を掛ける意味を既存の不変 paint tree で表す。

`DistanceFieldUploadData` は画素、Coverage／Sdf／Msdf、元のサイズと distance range を持つ。Coverage は R の値、Sdf は R、Msdf は RGB の median を使い、距離は `(value - 0.5) * DistanceRange`、内側が正。Coverage の range は 0、距離場の range は正とする。画素は線形または Data encoding で渡す。

配列はコピーし、glyph と paint の子 upload 所有集合を作成時に記録する。scene はこれを保持するので、供給元の cache 回収で描画入力が無効にならない。atlas と GPU cache は本体の責務。現在の renderer は輪郭と供給済み距離場を描画し、glyph atlas や距離場の自動生成は後続の最適化である。

[ADR 0036](../../../docs/adr/0036-2d-render-passes.md) に API と描画契約をまとめている。
