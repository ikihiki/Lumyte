# ADR-GRAPHICS-0020: 色画像とcoverage付きメッシュの2D描画

- 状態: 採用
- 日付: 2026-10-26

## 背景

画像の一部を任意の矩形へ描き、外部で三角形化した図形も共通paintで塗りたい。画像の透明境界で色がにじまないこと、入力meshのcoverageを保持すること、外部GPU資源の借用と準備済みデータの所有を区別することが必要である。

[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md)の基本描画・pattern samplingと、[GRAPHICS-0019](GRAPHICS-0019-2d-paths-and-strokes.md)までの配送を拡張する。本ADRはDrawImageとDrawMeshを扱い、画像のロード・decode・atlas生成・meshのtessellationは利用側へ委ねる。

## 決定

### 命令と資源の境界

| 命令 | 入力 | 所有と初期方式 |
| --- | --- | --- |
| DrawImage | Image2D、Source、Destination | view・親texture・samplerを借用し、fragmentでRGBAを評価 |
| DrawMesh | 不変Mesh2D、PaintGraph2D | CPU配列をコピーし、fragmentで三角形coverageとpaintを評価 |

IDrawImageRenderer2DとIDrawMeshRenderer2Dを追加し、表のコンストラクター・CreateDefault・UseGraphics2D・型別命令保存・配送を更新する。この段階の表は8命令になる。画像やmeshの都合で前後の命令を並べ替えず、同じclipを持つ連続範囲だけをまとめる。

Image2Dは生成済みの単一mip/layerのSampled D2 color viewとsamplerを借用する。画像のpixelデータはCPU命令へコピーせず、資源の所有権も移譲しない。同じdevice、viewと親textureとsamplerのGPU使用終了までの生存は利用側が保証する。ResourceManagerから画像を得る場合も、その画像のleaseは別途保持する。rendererの共有pipeline用leaseが任意の描画画像まで所有するとはみなさない。

Mesh2Dはverticesとindicesを検証してコピーし、読み取り専用のsnapshotを公開する。positionはfinite、coverageは[0,1]、index数は3の倍数、各indexはvertices内を要求する。記録後に呼び出し側の元配列を書き換えても描画を変えない。

### DrawImageの座標とsampling

Sourceは選択mip内のtexel座標、DestinationはCanvasのlocal矩形とする。DestinationからSourceへの対応を作り、Canvas変換とPixelScaleを合わせてsampleする。Sourceはview内に収めるが、ImagePattern2Dとは異なり整数限定ではない。幅・高さ0のSourceまたはDestinationは透明として評価し、除算を行わない。

DrawImageのSourceは画像を切り出して伸縮する座標対応であり、各tapのtile境界をSourceへ変更する設定ではない。標準実装はview全体をsamplingの境界に使い、samplerのAddressU/VをClamp/Repeat/Mirrorへ対応付ける。atlasのSourceごとのtile境界や透明なDecalを必要とする場合は、基本描画のImagePattern2DをFillRectへ指定する。

標準実装はpixelからtexelへの逆変換の2軸の長さからfootprintを求め、1より大きければMinFilter、それ以外ならMagFilterを選ぶ。選択mipは一つなのでmipmap間補間は行わず、MaxAnisotropyが1以外なら準備時にNotSupportedExceptionを返す。

Nearest/LinearはImagePatternと同じtexel評価器へ正規化する。texel中心をnearestで読み、sRGB形式ならdecode済みRGBに対しStraight入力だけalphaを掛け、Linearなら4tapを補間する。`interpolate(rgb) * interpolate(alpha)`で代用しない。Premultipliedはdecode後に乗算済みという契約で、alphaを二度掛けない。geometryとCanvas clipのcoverageはその結果へ適用し、最終的にlinear premultiplied SrcOverでtargetへ合成する。

### DrawMeshのcoverage

MeshVertex2DはPositionとCoverageだけを持ち、頂点色やUVは持たない。色は共通のPaintGraph2Dから取得するため、gradient・画像pattern・変換を他の図形と同じように使える。triangle以外のprimitiveや独立した材質表は追加しない。

DrawingProgram2Dはindex列を展開し、triangleごとの3頂点をVector4(position.x,position.y,coverage,0)としてgeometry bufferへ格納する。標準shaderは逆変換したpixel中心で符号付き面積と重心座標を求め、内部なら頂点coverageを補間する。面積がほぼ0のtriangleは無視する。複数triangleが重なる点ではcoverageの最大値を採用し、一つのmesh内でpaintを何度もSrcOverしない。独立して色を重ねたいtriangle群は別のDrawMeshとして記録する。

meshのcoverageはpixel中心で固定し、外側のclipとpaintは4×4の共通subpixel位置で評価する。strokeの内部三角形unionとは評価点が異なる。triangleの境界へ自動的な16sample AAを付ける契約にはせず、境界AAが必要なら利用側がcoverage付きの境界頂点を供給する。geometry boundsは保守的scissorへ使い、coverageのない頂点から滑らかな輪郭を推測しない。

現行経路はhardware rasterizerへmeshの頂点・indexを直接渡さず、共通fragment評価器へ格納する。IDrawMeshRenderer2Dだけを差し替えてhardware描画へ移行できるが、coverage・paint・clip・描画順の契約は維持する。

### 準備・共有資源の宣言・失敗

各命令のデータはPreparedGpuBatch2Dが所有するinstruction/geometry bufferへ準備し、Record時にupload・copy・barrier・drawを記録する。画像の親allocationはDrawingProgram2D.Texturesへ重複なく集め、DeclareResourcesでFragmentShader/ShaderReadとSampled状態を宣言する。active targetと同じtextureを同時にsampleする命令は拒否する。直接描画では利用側が外部producerからの可視化を行う。

準備済みbatchは一回実行で、GPU完了とcommand破棄まで保持する。外部画像は借用のままであり、batch破棄でDisposeしない。meshのgeometryデータはbatchが解放する。入力format/用途の不備と未対応samplingを準備時に拒否し、nativeの生成失敗は呼び出し側へ伝える。自動的な画像変換、別samplerへのfallback、meshの再tessellationは行わない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| hardware補間したstraight RGBを最後にpremultiply | 透明texelの色が境界へ混ざる |
| Image2Dがtextureのロードと所有も担当 | 既存Resourcesと責務が重複し、描画記録の借用境界が曖昧になる |
| mesh triangleを個別の透明drawとして合成 | 重複領域が濃くなり、ひとつの図形としてのcoverageが変わる |
| meshへ自動AAを常に付加 | 入力済みcoverageと二重に処理し、外部tessellatorの意図を変える |

## 結果と影響

画像・meshが図形と同じ順序、色、clip、paintを使える。画像の世代保持はResourceManager等へ委ね、2D内に画像ロードやGPU資源キャッシュを追加しない。

一方、DrawImageのSourceとImagePatternのSourceはsampling境界の意味が異なる。meshもtriangle数に比例するfragment処理を行うため、大きいmeshでは専用hardware経路の測定が必要になる。UV付き材質mesh、mipmap/anisotropic sampling、mesh cacheは別途設計する。

## 検証方針

ModelTestsでmesh入力のコピーとindex/coverage検証を確認する。配送・DIテストは8命令へ拡張する。GPUテストではSource/Destination、sRGB、straight/premultiplied、透明境界、nearest/linearの違い、mesh coverageとgradient・clipの画素結果をVulkan/wgpuで比較する。将来の専用rendererも同じfixtureで比較する。

## API・利用例

比較元はsplit/2d-paths。共通のImagePattern2D、AlphaRepresentation2DとPreparedDraw2Dの契約は基本描画ADRに記載する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed class Image2D
+    {
+        public Image2D(IGraphicsTextureView view, IGraphicsSampler sampler,
+            AlphaRepresentation2D alpha = AlphaRepresentation2D.Straight);
+        public AlphaRepresentation2D Alpha { get; }
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    public readonly record struct MeshVertex2D(Vector2 Position, float Coverage);
+    public sealed class Mesh2D
+    {
+        // 両配列をコピー。index数は3の倍数、各indexはvertices内。
+        public Mesh2D(ReadOnlySpan<MeshVertex2D> vertices, ReadOnlySpan<uint> indices);
+        public IReadOnlyList<MeshVertex2D> Vertices { get; }
+        public IReadOnlyList<uint> Indices { get; }
+    }
+    public sealed record DrawImageCommand2D(Image2D Image, Rect2D Source, Rect2D Destination,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawMeshCommand2D(Mesh2D Mesh, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public interface IDrawImageRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawMeshRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public sealed class DefaultDrawImageRenderer2D : IDrawImageRenderer2D
+    {
+        public DefaultDrawImageRenderer2D();
+    }
+    public sealed class DefaultDrawMeshRenderer2D : IDrawMeshRenderer2D
+    {
+        public DefaultDrawMeshRenderer2D();
+    }
+}
```

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
     public abstract class DrawingCanvas2D
     {
+        // Sourceはtexel、Destinationはlocal座標。
+        public void DrawImage(Image2D image, Rect2D source, Rect2D destination);
+        public void DrawMesh(Mesh2D mesh, PaintGraph2D paint);
     }
     public sealed class DrawingCommandRenderers2D
     {
-        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
-            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle,
-            IDrawLineRenderer2D drawLine, IFillPathRenderer2D fillPath, IStrokePathRenderer2D strokePath);
+        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle,
+            IDrawLineRenderer2D drawLine, IFillPathRenderer2D fillPath, IStrokePathRenderer2D strokePath,
+            IDrawImageRenderer2D drawImage, IDrawMeshRenderer2D drawMesh);
+        public IDrawImageRenderer2D DrawImage { get; }
+        public IDrawMeshRenderer2D DrawMesh { get; }
     }
 }
```

```csharp
// imageView・親texture・samplerは呼び出し側がGPU完了まで保持する。
var image = new Image2D(imageView, sampler, AlphaRepresentation2D.Straight);
var mesh = new Mesh2D(
    [new(new Vector2(10, 10), 1), new(new Vector2(80, 10), 1), new(new Vector2(40, 70), 0)],
    [0u, 1u, 2u]);
canvas.Begin();
canvas.DrawImage(image, new Rect2D(0, 0, 32, 32), new Rect2D(0, 0, 128, 128));
canvas.DrawMesh(mesh, PaintGraph2D.Solid(new Color2D(1, 1, 1, 1)));
DrawList2D list = canvas.End();
// 既存のRenderer2D.PrepareAsyncへ渡す。2命令の記録順を維持する。
```
