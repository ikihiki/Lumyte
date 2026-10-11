# ADR-GRAPHICS-0014: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 提案
- 日付: 2026-10-10
- 更新: 2026-10-11 — 専用interfaceと固定DIを維持し、共通paintによる図形の塗り・線、画像パターン、group opacityを具体化

## 背景

ゲーム内のスプライト、UI、図形、文字を共通の2D描画APIで扱いたい。利用者が図形ごとにGPU buffer、shader、描画状態を組み立てると、backend間の違い、クリッピング、半透明の描画順、GPU使用中のリソース寿命を各機能で重複して管理することになる。

Lumyteには既に [Graphics device](GRAPHICS-0001-graphics-device.md)、[CommandBufferとSubmission](GRAPHICS-0007-command-buffers-and-submission.md)、[描画状態](GRAPHICS-0008-pipeline-programs-and-render-state.md)、[shader引数](GRAPHICS-0009-shader-argument-binding.md)、[indexed draw](GRAPHICS-0011-indexed-and-indirect-commands.md) の契約がある。2D描画はこれらの上に構築し、独立したGPU backend抽象化は追加しない。

本ADRの主要な判断は、即時型のCanvas APIでCPUコマンド列を生成し、描画順を保持して既存Graphics APIへ変換することである。公開境界と、この判断に必要な状態・所有権の契約を定める。実装済みの機能や性能を表すものではない。

## 決定

### 適用範囲と責務

`src/Graphics/Lumyte.Graphics.Drawing2D` にC#ライブラリを配置する。プロジェクト名、NuGet名、名前空間は `Lumyte.Graphics.Drawing2D` とする。実行時にはBCLと `Lumyte.Graphics.Abstractions` を参照し、backendの具象型、Window、Input、Compositionへ依存しない。shader成果物は既存のSlang・shader生成基盤で用意する。

DI登録は任意の別パッケージ `Lumyte.Graphics.Drawing2D.DependencyInjection` に置き、同名のプロジェクト・名前空間を使う。このパッケージだけが本体と `Microsoft.Extensions.DependencyInjection.Abstractions` を参照する。UseGraphics2DはIServiceCollectionの拡張とし、DIコンテナーの生成はアプリケーションが行う。本体のコンストラクターは通常の引数を受け取るため、DIを使わない手動構築もできる。

構成は「アプリケーション → Canvas2D → DrawList2D → Renderer2D → IGraphicDevice」とする。CanvasはCPU側の変換・クリップ・描画コマンドを記録し、Rendererは命令ごとに構築時に指定された実装へ処理を渡し、連続コマンドのバッチ化、転送用リソースとGPU描画命令を組み立てる。deviceの生成・破棄、queue送信、完了待機、画面への表示はアプリケーションが所有する。

矩形、角丸矩形、円、線、ベジェ曲線を含むパス、画像、coverage画像、距離場、三角形mesh、paint graph、オフスクリーン描画を対象とする。最初に矩形・画像・変換・矩形クリップを実装し、その後にパスとpaint graphを追加する。文字の描画形式の選択とDrawGlyphRunは別の文字ライブラリが担当し、Drawing2Dには汎用描画命令を渡す。シーングラフは必要な利用者がCanvasの上に構築し、必須にしない。レイアウト、ヒットテスト、画像デコード、フォント探索、文字列のシェーピング、行分割は本ライブラリの責務に含めない。

### 公開APIの境界

比較元: main `4b8902f5226c17ce86c337d09434a3ae4043bf2d`。対象APIは比較元に存在せず、以下は追加する主要メンバーの宣言である。Graphicsの型には `Lumyte.Graphics.Abstractions`、行列・ベクトルには `System.Numerics` を使用する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // 左上原点の論理座標。全成分finite、幅・高さは0以上。
+    public readonly record struct Rect2D(float X, float Y, float Width, float Height);
+    // API入力はstraight alphaのsRGB、各成分はfiniteかつ[0,1]。
+    public readonly record struct Color2D(float R, float G, float B, float A);
+    public enum FillRule2D { NonZero, EvenOdd }
+    public enum LineCap2D { Butt, Round, Square }
+    public enum LineJoin2D { Miter, Round, Bevel }
+    public sealed record StrokeStyle2D
+    {
+        // 論理座標での幅。finiteかつ正数、既定値1。
+        public float Width { get; init; } = 1;
+        public LineCap2D Cap { get; init; } = LineCap2D.Butt;
+        public LineJoin2D Join { get; init; } = LineJoin2D.Miter;
+        // finiteかつ1以上。超過したmiterはbevelへ切り替える。
+        public float MiterLimit { get; init; } = 4;
+    }
+    // 完了後は不変。入力列はBuild時にコピーする。
+    public enum PathVerb2D { Move, Line, Quadratic, Cubic, Close }
+    // Move/LineはP0、Quadraticはcontrol=P0/end=P1、Cubicはcontrols=P0/P1/end=P2。
+    public readonly record struct PathSegment2D(PathVerb2D Verb, Vector2 P0, Vector2 P1, Vector2 P2);
+    public sealed class Path2D
+    {
+        // 外部描画実装も読み取れる不変のsnapshot。変更可能な配列を公開しない。
+        public IReadOnlyList<PathSegment2D> Segments { get; }
+    }
+    public sealed class PathBuilder2D
+    {
+        public PathBuilder2D();
+        public void MoveTo(Vector2 point);
+        public void LineTo(Vector2 point);
+        public void QuadraticTo(Vector2 control, Vector2 end);
+        public void CubicTo(Vector2 control1, Vector2 control2, Vector2 end);
+        public void Close();
+        public Path2D Build();
+    }
+    public enum AlphaRepresentation2D { Straight, Premultiplied }
+    // 単一mip/layerのSampled D2 viewとsamplerを借用。生成時に同一deviceを検証。
+    // sRGB formatはsample時にdecode。Premultipliedはdecode後にRGBがalpha乗算済みとなる表現。
+    public sealed class Image2D
+    {
+        public Image2D(IGraphicsTextureView view, IGraphicsSampler sampler,
+            AlphaRepresentation2D alpha = AlphaRepresentation2D.Straight);
+        public AlphaRepresentation2D Alpha { get; }
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    // source領域を基準に各軸を独立して延長する。Decalの範囲外は透明。
+    public enum ImageTileMode2D { Clamp, Repeat, Mirror, Decal }
+    public enum ImageFilter2D { Nearest, Linear }
+    // samplerを持たない画像paint記述。factoryが値を検証・snapshot化する。
+    public sealed record ImagePattern2D
+    {
+        // 単一mip/layerのSampled D2 viewを借用。描画deviceとの一致はPrepareで検証。
+        public required IGraphicsTextureView View { get; init; }
+        // viewの選択mip内のtexel矩形。全成分整数、幅・高さは正、範囲外は拒否。
+        public required Rect2D Source { get; init; }
+        public AlphaRepresentation2D Alpha { get; init; } = AlphaRepresentation2D.Straight;
+        // source左上を原点とするtexel座標→paintローカル座標。逆行列も含めfiniteかつ逆変換可能。
+        public Matrix3x2 PatternToLocal { get; init; } = Matrix3x2.Identity;
+        public ImageTileMode2D TileX { get; init; } = ImageTileMode2D.Repeat;
+        public ImageTileMode2D TileY { get; init; } = ImageTileMode2D.Repeat;
+        public ImageFilter2D Filter { get; init; } = ImageFilter2D.Linear;
+    }
+    public sealed class CoverageImage2D
+    {
+        // R8UnormのRを[0,1]のcoverageとして読む。view/samplerは同一deviceから借用。
+        public CoverageImage2D(IGraphicsTextureView view, IGraphicsSampler sampler);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+    }
+    public enum DistanceFieldEncoding2D { Sdf, Msdf, Mtsdf }
+    public sealed class DistanceField2D
+    {
+        // データtexture。sRGB形式は拒否。SDFはR8、MSDF/MTSDFはRGBA8を使用。
+        // texel距離=(sample-0.5)*distanceRange、内側正。rangeはfiniteかつ正数。
+        public DistanceField2D(IGraphicsTextureView view, IGraphicsSampler sampler,
+            DistanceFieldEncoding2D encoding, float distanceRange);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
+        public DistanceFieldEncoding2D Encoding { get; }
+        public float DistanceRange { get; }
+    }
+    // Positionは論理座標、Coverageは[0,1]。境界AA済みなら必要な頂点を利用者が供給する。
+    public readonly record struct MeshVertex2D(Vector2 Position, float Coverage);
+    public sealed class Mesh2D
+    {
+        // 両配列をコピー。index数は3の倍数、各indexはvertices内。
+        public Mesh2D(ReadOnlySpan<MeshVertex2D> vertices, ReadOnlySpan<uint> indices);
+        public IReadOnlyList<MeshVertex2D> Vertices { get; }
+        public IReadOnlyList<uint> Indices { get; }
+    }
+    public enum ExtendMode2D { Pad, Repeat, Reflect }
+    // COLRv1の全CompositeModeを表す。通常のCanvas合成とは独立する。
+    public enum CompositeMode2D
+    {
+        Clear, Src, Dest, SrcOver, DestOver, SrcIn, DestIn, SrcOut, DestOut,
+        SrcAtop, DestAtop, Xor, Plus, Screen, Overlay, Darken, Lighten,
+        ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
+        Multiply, HslHue, HslSaturation, HslColor, HslLuminosity
+    }
+    // offsetはfinite。入力順は非減少、同一offsetのstopも順序を維持する。
+    public readonly record struct ColorStop2D(float Offset, Color2D Color);
+    // パスの集合とpaintのDAG。生成後は不変、子列とstop列はコピーする。
+    // constructorを非公開とし、factoryのみで生成して循環を防ぐ。
+    public sealed class PaintGraph2D
+    {
+        // 外部実装が解析できる不変の公開node tree。factoryが値と子を検証する。
+        public PaintNode2D Root { get; }
+        // 無限平面のpaint。実際の評価範囲はpath、clip、targetで制限する。
+        public static PaintGraph2D Solid(Color2D color);
+        // 一般的な2点linear。finiteかつ異なる2点。既存3点nodeへ正規化する。
+        public static PaintGraph2D LinearGradient(Vector2 start, Vector2 end,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend = ExtendMode2D.Pad);
+        // p0/p1/p2はCOLRv1の3点定義。p2を省略して2点へ縮約しない。
+        public static PaintGraph2D LinearGradient(Vector2 p0, Vector2 p1, Vector2 p2,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 2円定義。radiusはfiniteかつ0以上。単一中心へ縮約しない。
+        public static PaintGraph2D RadialGradient(Vector2 c0, float r0, Vector2 c1, float r1,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 中心から外周へのradial。radiusはfiniteかつ正数。同心の2円nodeへ正規化する。
+        public static PaintGraph2D RadialGradient(Vector2 center, float radius,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend = ExtendMode2D.Pad);
+        // angleはradian、+X基準、Y下向きで時計回り。入力adapterで変換する。
+        public static PaintGraph2D SweepGradient(Vector2 center, float startAngle,
+            float endAngle, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 設定をsnapshot化しviewを借用。GPU資源を確保しない。
+        public static PaintGraph2D ImagePattern(ImagePattern2D pattern);
+        // 子graphの合成後のpremultiplied RGBA全体を倍率変更。finiteかつ[0,1]。
+        public static PaintGraph2D Opacity(float opacity, PaintGraph2D child);
+        // paintをpath coverageで切り抜く。paintとpathは同じローカル座標系。
+        public static PaintGraph2D Clip(Path2D path, PaintGraph2D paint,
+            FillRule2D rule = FillRule2D.NonZero);
+        // 子graph全体（geometryとpaint）へ変換を適用する。
+        public static PaintGraph2D Transform(Matrix3x2 transform, PaintGraph2D child);
+        // 配列の先頭からSrcOverで合成。空列は透明。
+        public static PaintGraph2D Layers(ReadOnlySpan<PaintGraph2D> children);
+        // sourceとbackdropを独立した透明面へ評価してから、指定modeで合成。
+        public static PaintGraph2D Composite(PaintGraph2D source,
+            PaintGraph2D backdrop, CompositeMode2D mode);
+    }
+    // 公開nodeは読み取り用のsnapshot。直接生成したnodeをgraphへ挿入するAPIは持たない。
+    public abstract record PaintNode2D;
+    public sealed record SolidPaintNode2D(Color2D Color) : PaintNode2D;
+    public sealed record LinearPaintNode2D(Vector2 P0, Vector2 P1, Vector2 P2,
+        IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
+    public sealed record RadialPaintNode2D(Vector2 C0, float R0, Vector2 C1, float R1,
+        IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
+    public sealed record SweepPaintNode2D(Vector2 Center, float StartAngle, float EndAngle,
+        IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
+    public sealed record ImagePaintNode2D(ImagePattern2D Pattern) : PaintNode2D;
+    public sealed record OpacityPaintNode2D(float Opacity, PaintNode2D Child) : PaintNode2D;
+    public sealed record ClipPaintNode2D(Path2D Path, FillRule2D Rule, PaintNode2D Child) : PaintNode2D;
+    public sealed record TransformPaintNode2D(Matrix3x2 Transform, PaintNode2D Child) : PaintNode2D;
+    public sealed record LayersPaintNode2D(IReadOnlyList<PaintNode2D> Children) : PaintNode2D;
+    public sealed record CompositePaintNode2D(PaintNode2D Source, PaintNode2D Backdrop,
+        CompositeMode2D Mode) : PaintNode2D;
+    public sealed class Canvas2D
+    {
+        public Canvas2D();
+        // 記録なし・スタックなしの状態から開始する。ネスト開始は拒否。
+        public void Begin();
+        public void Save();
+        // Saveに対応しないRestoreは拒否。
+        public void Restore();
+        // finiteな行列のみ。点への適用はローカル変換→既存変換の順。
+        public void ConcatTransform(Matrix3x2 transform);
+        public void Translate(Vector2 offset);
+        public void Rotate(float radians);
+        public void Scale(Vector2 scale);
+        // 呼び出し時の変換で固定し、現在のclipとの積集合にする。
+        public void ClipRect(Rect2D rect);
+        public void ClipPath(Path2D path, FillRule2D rule = FillRule2D.NonZero);
+        // Color2D版はSolid(color)として同じ命令を記録する。以下の塗り・線も同様。
+        public void FillRect(Rect2D rect, Color2D color);
+        public void FillRect(Rect2D rect, PaintGraph2D paint);
+        // radiusはfiniteかつ0以上。描画時に短辺の半分まで縮める。
+        public void FillRoundedRect(Rect2D rect, float radius, Color2D color);
+        public void FillRoundedRect(Rect2D rect, float radius, PaintGraph2D paint);
+        public void FillCircle(Vector2 center, float radius, Color2D color);
+        public void FillCircle(Vector2 center, float radius, PaintGraph2D paint);
+        public void DrawLine(Vector2 start, Vector2 end, Color2D color, StrokeStyle2D style);
+        public void DrawLine(Vector2 start, Vector2 end, PaintGraph2D paint, StrokeStyle2D style);
+        public void FillPath(Path2D path, Color2D color, FillRule2D rule = FillRule2D.NonZero);
+        public void FillPath(Path2D path, PaintGraph2D paint, FillRule2D rule = FillRule2D.NonZero);
+        public void StrokePath(Path2D path, Color2D color, StrokeStyle2D style);
+        public void StrokePath(Path2D path, PaintGraph2D paint, StrokeStyle2D style);
+        // sourceはview内のtexel座標、destinationは論理座標。
+        public void DrawImage(Image2D image, Rect2D source, Rect2D destination);
+        // 不変graphを記録。補間・合成は線形光RGBで評価する。
+        public void DrawPaintGraph(PaintGraph2D graph);
+        // coverageは色空間変換しない。paintの座標はCanvasの論理座標。
+        public void DrawCoverageMask(CoverageImage2D mask, Rect2D source,
+            Rect2D destination, PaintGraph2D paint);
+        // SDF/MSDF/MTSDFをcoverageへ復元し、paintを適用する。
+        public void DrawDistanceField(DistanceField2D field, Rect2D source,
+            Rect2D destination, PaintGraph2D paint);
+        // 外部で三角形化済みのデータを描く。
+        public void DrawMesh(Mesh2D mesh, PaintGraph2D paint);
+        // Save/Restoreの対応を検証し、不変のCPUコマンド列を返す。GPU実行なし。
+        public DrawList2D End();
+        // 記録中のCPUコマンドを破棄し、再びBeginできる状態へ戻す。
+        public void Cancel();
+    }
+    // 再使用可能なCPU snapshot。参照する画像のGPU所有権は持たない。
+    public sealed class DrawList2D { }
+    public sealed record RenderTarget2D
+    {
+        // 同一device、単一sample/mip/layer、D2、RenderAttachment view。
+        // 初期対応はRgba8UnormSrgb。寸法はviewから取得する。
+        public required IGraphicsTextureView View { get; init; }
+        // 論理座標から物理pixelへの倍率。finiteかつ正数、既定値1。
+        public float PixelScale { get; init; } = 1;
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    public sealed class Renderer2D : IDisposable
+    {
+        // deviceは借用。backend固有APIを要求しない。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers);
+        // 登録された実装でCPU処理・バッチ化・uploadとGPU実行計画を準備する。
+        // submit/waitは行わない。キャンセル時は内部確保を解放する。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 生存するPreparedDraw2Dがあれば拒否。暗黙のGPU完了待機なし。
+        public void Dispose();
+    }
+    public sealed class PreparedDraw2D : IDisposable
+    {
+        // 一度だけ。Recordingかつactive passなしの同一device commandに記録する。
+        // 内部copy/compute/barrier/render passを記録し、active passを残さない。
+        public void Record(IGraphicsCommandBuffer commands);
+        // Record先を含むSubmitの直後に呼ぶ。submissionは借用、一度だけ。
+        public void TrackSubmission(IGraphicsSubmission submission);
+        // 下記の寿命契約に従い内部GPUリソースを解放。idempotent。
+        public void Dispose();
+    }
+}
```

### 共通paintによる塗りと線

FillRect、FillRoundedRect、FillCircle、FillPathはPaintGraph2Dを受け取り、単色・gradient・画像パターン・複数paintの合成を同じ形状へ適用する。DrawLineとStrokePathも同じpaintを受け取り、線の輪郭を塗る。Color2Dの便利overloadはSolidへ正規化し、対応する一つの命令を記録する。FillRectへgradientを渡してもFillRectCommand2Dのままであり、DrawPaintGraphCommand2Dへの書き換えや別のDI登録への転送は行わない。11個の専用interfaceとCreateDefault・UseGraphics2Dの対応を維持する。

paintは命令の変換前の論理座標で評価し、形状のboundsへ自動で引き伸ばさない。Canvasの変換は形状とpaintの両方へ適用する。paintだけを動かす場合はPaintGraph2D.Transformを使い、画像パターンの位置・拡大・回転にはPatternToLocalを使う。

| 塗りの機能 | 指定方法と意味 |
| --- | --- |
| 単色・半透明 | Color2DまたはSolid。色ごとのalphaを保持 |
| 線形gradient | 2点の便利overload、またはCOLRv1の3点定義。複数stopを保持 |
| 放射gradient | 中心と半径の便利overload、または中心の異なる2円定義 |
| 角度gradient | SweepGradient。開始・終了角度と複数stopを指定 |
| gradientの延長 | Pad・Repeat・Reflect |
| 画像パターン | 画像の切り出し、座標変換、軸別Clamp・Repeat・Mirror・Decal |
| paint全体の透明度 | Opacity。子graphを合成した結果のRGBAへ一度だけ適用 |
| paintの組み合わせ | Layers・Composite・Clip・Transform。既存の全28合成modeを利用 |

2点linearはp0=start、p1=end、p2=start+perp(end-start)へ正規化し、perp(x,y)=(-y,x)とする。中心・半径radialはc0=c1=center、r0=0、r1=radiusへ正規化する。便利overloadは非退化の入力を要求し、既存の3点・2円APIとCOLRv1の退化規則は保持する。

形状をpaintの外側の幾何clipとして扱い、Canvas clip、graph内Clipと相関を保って評価する。外側形状のcoverageはLayersの各子へ分配せず、graph全体を覆う位置で一度だけ適用する。同じ境界を含むshapeとClipを別々にAA済みmaskへ解決して乗算すると境界が薄くなるため、その方法は使わない。解析coverageだけで相関を保持できない組み合わせでは、登録実装内で共通サンプルまたは幾何的交差を評価する。

Opacityは子graphを透明背景上で評価・合成した結果のプリマルチプライドRGBとalphaを共に倍率変更する。各子のalphaへ分配しない。例えば重なる二つの不透明な図形をLayersにし、その全体へOpacity(0.5)を適用した場合、重なり部分のalphaも0.5となる。必要なら隔離中間面を使用し、数学的に同じ結果を保てる場合だけ省略する。

### 画像パターンの座標とsampling

ImagePattern2Dはviewとsampling設定を持ち、外部samplerは受け取らない。Image2Dから使う場合はViewとAlphaを明示的に渡す。FilterとTileX/TileYを唯一の設定元とし、DrawImageが使うImage2D.Samplerとの優先順位を設けない。描画実装が必要な内部samplerや変換面を所有する。

Sourceはviewが選択したmip内の整数texel矩形で、幅・高さは正数かつ全体がview内に収まるものとする。source左上をpattern原点(0,0)とし、texel中心は(i+0.5,j+0.5)、周期はsourceの幅・高さである。PatternToLocalはその座標をpaintローカル座標へ変換する。samplingでは逆変換を使い、外側のgraph変換、Canvas変換、PixelScaleとの関係を維持する。factoryはnull view、範囲、未知のenum、非finite値、逆変換不能または逆行列に非finite値を生むPatternToLocalを拒否する。Prepareではdevice、sample可能な色format、Sampled用途を検証する。

Clampは端のtexelへ固定し、Repeatは周期反復、Mirrorは2周期で往復反復する。負の座標でも同じ規則を使う。Decalは領域外のtexelを透明として扱う。X/Yは独立に指定できる。

各軸のsource寸法をN、整数tap座標をiとすると、Repeatの参照先はpositiveMod(i,N)、Mirrorはr=positiveMod(i,2*N)としてr<Nならr、そうでなければ2*N-1-rとする。positiveModは結果が0以上となる剰余であり、負の座標も同じ端処理になる。

Nearestはpattern座標の各成分をfloorしたtexelを選ぶ。Linearはtexel中心を基準とする4tapの双線形補間とし、各tapの整数座標に対してSource相対のtile規則を適用する。Repeatの端をまたぐtapは同じSourceの反対側、Mirrorは反射先、Clampは端、Decalの範囲外tapは透明になる。座標の中心だけをwrapしてatlas全体へhardware linear samplingする方法は使わない。指定した領域の外側にある隣接画像を読まず、この画像paintにはatlas paddingを要求しない。

色付き画像の補間は線形RGBのプリマルチプライド値に対して行う。各texelをsRGBならdecodeし、Straight入力ならalphaを乗算してから補間する。Premultiplied入力へ再乗算しない。透明texelのRGBが境界へ混入しないよう、hardware sampling、tap単位の処理、内部面への事前変換はこの結果と等価な場合だけ使う。初期の画像パターンは単一mipのNearest/Linearであり、縮小時のmipmap・異方性filterは別途設計する。

ImagePatternを含むgraphは画像の参照を保持するが、viewやtextureのGPU所有権は持たない。graph内の全画像をPrepareからGPU完了まで生存させ、Record前にSampled stateへ遷移させる。targetと同一subresourceを読むgraphは、どの深さに参照があっても拒否する。

次は利用例である。imageは少なくとも32×32 texelのImage2D、outlineは呼び出し側が用意したPath2Dとする。画像patternにはimage.Samplerを渡さない。

```csharp
var stops = new[]
{
    new ColorStop2D(0, new Color2D(1, 0.2f, 0, 1)),
    new ColorStop2D(1, new Color2D(0, 0.4f, 1, 1))
};
var linear = PaintGraph2D.LinearGradient(
    new Vector2(20, 20), new Vector2(220, 20), stops);
var radial = PaintGraph2D.RadialGradient(new Vector2(300, 70), 50, stops);
var sweep = PaintGraph2D.SweepGradient(
    new Vector2(100, 100), 0, MathF.Tau, stops, ExtendMode2D.Repeat);
var pattern = PaintGraph2D.ImagePattern(new ImagePattern2D
{
    View = image.View,
    Alpha = image.Alpha,
    Source = new Rect2D(0, 0, 32, 32),
    PatternToLocal = Matrix3x2.CreateScale(2)
        * Matrix3x2.CreateRotation(0.2f)
        * Matrix3x2.CreateTranslation(20, 140),
    TileX = ImageTileMode2D.Repeat,
    TileY = ImageTileMode2D.Mirror,
    Filter = ImageFilter2D.Linear
});

canvas.Begin();
canvas.FillRect(new Rect2D(20, 20, 200, 100), linear);
canvas.FillCircle(new Vector2(300, 70), 50, radial);
canvas.FillRoundedRect(new Rect2D(20, 140, 320, 120), 16,
    PaintGraph2D.Opacity(0.8f, pattern));
canvas.FillPath(outline, sweep, FillRule2D.EvenOdd);
canvas.StrokePath(outline, linear, new StrokeStyle2D { Width = 3 });
var drawList = canvas.End();
```

### 命令別の専用interfaceとコンストラクター注入

各描画命令型に実装を一つ割り当て、Renderer生成時に登録表を固定する。RendererはCanHandle、優先順位、性能推定による実装探索を行わない。指定された実装が入力やdevice機能に対応しない場合はNotSupportedExceptionを返し、別実装へのfallbackは行わない。通常の値検証とdevice機能の検証は必須だが、実装を選ぶための問い合わせには使わない。

各命令に専用の非generic interfaceを設け、DrawingCommandRenderers2Dの単一の公開コンストラクターで11実装を受け取る。各引数は必須でnullを拒否し、対応する型付きreadonly fieldに保持する。登録表は構築後に変更できない。DIは構築時に各interfaceを単一解決し、Rendererは描画中にサービスを解決しない。

比較元は上記の公開API差分と同じmain revision。以下の命令型はCanvasが作成する不変snapshotであり、外部実装が読み取れる。DrawList内部の配列やbackendのprivate型は公開しない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // ClipRectも矩形のPath2Dへ正規化して記録。呼び出し時の変換を固定する。
+    public sealed record ClipElement2D(Path2D Path, Matrix3x2 Transform, FillRule2D Rule);
+    public sealed class ClipStack2D
+    {
+        // 論理的な積集合を表す不変列。各要素の変換は独立して保持する。
+        public IReadOnlyList<ClipElement2D> Elements { get; }
+    }
+    public sealed record DrawingState2D(Matrix3x2 Transform, ClipStack2D Clip);
+    public abstract record DrawingCommand2D(DrawingState2D State);
+    // Color2D overloadもSolidのPaintへ正規化。paintの種類で命令型を分けない。
+    public sealed record FillRectCommand2D(Rect2D Rect, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillRoundedRectCommand2D(Rect2D Rect, float Radius, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillCircleCommand2D(Vector2 Center, float Radius, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawLineCommand2D(Vector2 Start, Vector2 End, PaintGraph2D Paint,
+        StrokeStyle2D Style, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillPathCommand2D(Path2D Path, PaintGraph2D Paint, FillRule2D Rule,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record StrokePathCommand2D(Path2D Path, PaintGraph2D Paint, StrokeStyle2D Style,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawImageCommand2D(Image2D Image, Rect2D Source, Rect2D Destination,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawCoverageMaskCommand2D(CoverageImage2D Mask, Rect2D Source,
+        Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawDistanceFieldCommand2D(DistanceField2D Field, Rect2D Source,
+        Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawMeshCommand2D(Mesh2D Mesh, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record DrawPaintGraphCommand2D(PaintGraph2D Graph,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed class RenderPreparationContext2D
+    {
+        // いずれも借用。TargetはLoadOp=Loadに正規化したsnapshot。初回clearはホストのRecordが行う。
+        public IGraphicDevice Device { get; }
+        public RenderTarget2D Target { get; }
+    }
+    // 以下の全PrepareAsyncに共通する契約:
+    // 同型・同一clip/合成境界の連続命令。並べ替え不可。snapshotは準備結果の寿命まで有効。
+    // CPU処理、資源確保、GPU実行計画を準備する。submit/waitは禁止。
+    public interface IFillRectRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IFillRoundedRectRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IFillCircleRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawLineRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IFillPathRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IStrokePathRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawImageRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawCoverageMaskRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawDistanceFieldRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawMeshRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IDrawPaintGraphRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default);
+    }
+    public interface IPreparedCommandBatch2D : IDisposable
+    {
+        // 同一device、Recording、active passなしで一度だけ呼ぶ。
+        // copy/compute/barrier/renderを記録し、active passなしで返す。
+        void Record(IGraphicsCommandBuffer commands);
+        // ホストがGPU使用終了と記録先の破棄を確認して呼ぶ。暗黙のwaitなし。
+        void Dispose();
+    }
+    public sealed class DrawingCommandRenderers2D
+    {
+        // DIなしで全11標準実装を生成し、下記constructorへ渡した新しい登録表を返す。
+        // 呼び出すたびに表と各実装を新規生成。device・GPU資源・DIサービスは生成しない。
+        public static DrawingCommandRenderers2D CreateDefault();
+        // 全実装を借用し、型付きreadonly fieldへ保持。nullは拒否。
+        public DrawingCommandRenderers2D(
+            IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect,
+            IFillCircleRenderer2D fillCircle,
+            IDrawLineRenderer2D drawLine,
+            IFillPathRenderer2D fillPath,
+            IStrokePathRenderer2D strokePath,
+            IDrawImageRenderer2D drawImage,
+            IDrawCoverageMaskRenderer2D drawCoverageMask,
+            IDrawDistanceFieldRenderer2D drawDistanceField,
+            IDrawMeshRenderer2D drawMesh,
+            IDrawPaintGraphRenderer2D drawPaintGraph);
+        // 構築時に指定された実装をそのまま返す。探索・再解決なし。
+        public IFillRectRenderer2D FillRect { get; }
+        public IFillRoundedRectRenderer2D FillRoundedRect { get; }
+        public IFillCircleRenderer2D FillCircle { get; }
+        public IDrawLineRenderer2D DrawLine { get; }
+        public IFillPathRenderer2D FillPath { get; }
+        public IStrokePathRenderer2D StrokePath { get; }
+        public IDrawImageRenderer2D DrawImage { get; }
+        public IDrawCoverageMaskRenderer2D DrawCoverageMask { get; }
+        public IDrawDistanceFieldRenderer2D DrawDistanceField { get; }
+        public IDrawMeshRenderer2D DrawMesh { get; }
+        public IDrawPaintGraphRenderer2D DrawPaintGraph { get; }
+    }
+}
```

### DIなしでの標準構成

DrawingCommandRenderers2D.CreateDefaultは本体パッケージに置く引数なしの静的メソッドとする。全11命令に対応するDefault*Renderer2Dを一つずつnewし、公開コンストラクターへ渡す。UseGraphics2Dの標準登録と同じ命令・実装の対応を使い、呼び出すたびに新しい登録表と11個の新しい標準実装を返す。

DIパッケージ、IServiceProvider、deviceを必要とせず、GPU資源も確保しない。標準実装はstatelessで破棄が必要な資源を持たないため、生成した登録表と実装にDisposeは不要である。GPU資源はPrepare以降の既存の寿命契約に従う。

```csharp
var renderers = DrawingCommandRenderers2D.CreateDefault();
using var renderer = new Renderer2D(device, renderers);
// 以後は同じPrepareAsync → Record → Submitの描画手順を使う。
```

DIなしで個別の外部実装を使う場合は、公開コンストラクターに各専用interfaceの実装を渡す。変更しない命令にはCreateDefaultの対応propertyを渡せる。生成済み登録表の変更や描画中の実装探索は行わない。

### 固定された呼び出し経路

DrawListは内部に命令別の型付き領域と、元の描画順を表すkind・範囲の列を持つ。Canvas.Endで不変snapshotにし、Prepareのたびに基底型配列から型別の配列を抽出・コピーしない。Rendererは元の順序を走査し、連続する同型・同一状態・同一clip/合成境界の範囲だけをバッチ化する。FillPath → FillRect → FillPathは3つのバッチを維持し、別の位置にある同型命令を結合しない。

Prepareでは内部の固定kindでバッチごとに一度分岐し、対応する専用interfaceを直接呼ぶ。次は内部実装の抜粋イメージであり、kindや型付き領域を公開APIにはしない。

```csharp
switch (batch.Kind)
{
    case DrawingCommandKind2D.FillRect:
        preparedBatches.Add(await renderers.FillRect.PrepareAsync(
            context, list.FillRects.Slice(batch.Offset, batch.Count), cancellationToken));
        break;
    case DrawingCommandKind2D.FillPath:
        preparedBatches.Add(await renderers.FillPath.PrepareAsync(
            context, list.FillPaths.Slice(batch.Offset, batch.Count), cancellationToken));
        break;
    // 残り9命令も、それぞれの型付き領域と専用interfaceを使う。
}
```

命令のdispatchにType辞書、reflection、dynamic、DI解決、候補列挙を使わない。共通のIPreparedCommandBatch2DはGPU実行計画と資源寿命の境界として維持し、Recordもバッチ単位で呼ぶ。専用interfaceでもinterface呼び出しは残る。探索と呼び出し回数を抑える構成を採用し、JIT/Native AOTでのインライン化や性能向上を測定前に保証しない。

### UseGraphics2Dによる標準登録と後勝ちの上書き

本体は各専用interfaceの標準実装を公開し、登録用パッケージや手動構築から生成できるようにする。すべてstatelessで、GPU資源は準備結果が所有する。次はコンストラクターの宣言であり、PrepareAsyncは対応する専用interfaceを明示的に実装する。標準実装の描画方式は後述の対応表で定める。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed class DefaultFillRectRenderer2D : IFillRectRenderer2D
+    {
+        public DefaultFillRectRenderer2D();
+    }
+    public sealed class DefaultFillRoundedRectRenderer2D : IFillRoundedRectRenderer2D
+    {
+        public DefaultFillRoundedRectRenderer2D();
+    }
+    public sealed class DefaultFillCircleRenderer2D : IFillCircleRenderer2D
+    {
+        public DefaultFillCircleRenderer2D();
+    }
+    public sealed class DefaultDrawLineRenderer2D : IDrawLineRenderer2D
+    {
+        public DefaultDrawLineRenderer2D();
+    }
+    public sealed class DefaultFillPathRenderer2D : IFillPathRenderer2D
+    {
+        public DefaultFillPathRenderer2D();
+    }
+    public sealed class DefaultStrokePathRenderer2D : IStrokePathRenderer2D
+    {
+        public DefaultStrokePathRenderer2D();
+    }
+    public sealed class DefaultDrawImageRenderer2D : IDrawImageRenderer2D
+    {
+        public DefaultDrawImageRenderer2D();
+    }
+    public sealed class DefaultDrawCoverageMaskRenderer2D : IDrawCoverageMaskRenderer2D
+    {
+        public DefaultDrawCoverageMaskRenderer2D();
+    }
+    public sealed class DefaultDrawDistanceFieldRenderer2D : IDrawDistanceFieldRenderer2D
+    {
+        public DefaultDrawDistanceFieldRenderer2D();
+    }
+    public sealed class DefaultDrawMeshRenderer2D : IDrawMeshRenderer2D
+    {
+        public DefaultDrawMeshRenderer2D();
+    }
+    public sealed class DefaultDrawPaintGraphRenderer2D : IDrawPaintGraphRenderer2D
+    {
+        public DefaultDrawPaintGraphRenderer2D();
+    }
+}
+namespace Lumyte.Graphics.Drawing2D.DependencyInjection
+{
+    public static class Graphics2DServiceCollectionExtensions
+    {
+        // 全11専用interfaceとDrawingCommandRenderers2DをTryAddScopedで登録。
+        // 未登録分だけを補完し、再呼び出しで既存の上書きを消さない。
+        // servicesのnullは拒否。provider生成やサービス解決は行わず、同じservicesを返す。
+        public static IServiceCollection UseGraphics2D(this IServiceCollection services);
+    }
+}
```

UseGraphics2Dの登録内容は次のとおり。IServiceCollectionはMicrosoft.Extensions.DependencyInjection、TryAddScopedは同.Extensions名前空間の拡張を使う。DI経路ではCreateDefaultを呼ばず、各専用interfaceを単一解決してDrawingCommandRenderers2Dのコンストラクターへ渡すことで、後勝ちの上書きを反映する。

```csharp
public static IServiceCollection UseGraphics2D(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);
    services.TryAddScoped<IFillRectRenderer2D, DefaultFillRectRenderer2D>();
    services.TryAddScoped<IFillRoundedRectRenderer2D, DefaultFillRoundedRectRenderer2D>();
    services.TryAddScoped<IFillCircleRenderer2D, DefaultFillCircleRenderer2D>();
    services.TryAddScoped<IDrawLineRenderer2D, DefaultDrawLineRenderer2D>();
    services.TryAddScoped<IFillPathRenderer2D, DefaultFillPathRenderer2D>();
    services.TryAddScoped<IStrokePathRenderer2D, DefaultStrokePathRenderer2D>();
    services.TryAddScoped<IDrawImageRenderer2D, DefaultDrawImageRenderer2D>();
    services.TryAddScoped<IDrawCoverageMaskRenderer2D, DefaultDrawCoverageMaskRenderer2D>();
    services.TryAddScoped<IDrawDistanceFieldRenderer2D, DefaultDrawDistanceFieldRenderer2D>();
    services.TryAddScoped<IDrawMeshRenderer2D, DefaultDrawMeshRenderer2D>();
    services.TryAddScoped<IDrawPaintGraphRenderer2D, DefaultDrawPaintGraphRenderer2D>();
    services.TryAddScoped<DrawingCommandRenderers2D>();
    return services;
}
```

アプリケーションはUseGraphics2Dの後で上書きしたいinterfaceをAddScopedする。Microsoft.Extensions.DependencyInjectionの単一サービス解決は同じサービス型の最後の登録を使うため、専用の置換APIは設けない。標準登録を削除する必要もなく、IEnumerableで候補を取得しない。ExternalComputeFillPathRendererはIFillPathRenderer2Dを実装する外部ライブラリの例で、組み込み型ではない。

```csharp
var services = new ServiceCollection();
services.UseGraphics2D();
services.AddScoped<IFillPathRenderer2D, ExternalComputeFillPathRenderer>();

using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = provider.CreateScope();

// constructorの11引数をDIが解決。FillPathだけ最後の登録を受け取る。
var renderers = scope.ServiceProvider
    .GetRequiredService<DrawingCommandRenderers2D>();
```

この設定ではすべてのFillPathがExternalComputeFillPathRendererへ渡る。パスのサイズ、静的・動的、GPU機能によって別の登録へ切り替えない。StrokePath、DrawPaintGraph、画像系の命令はそれぞれの登録のままである。同じ経路へ変更したい命令は、その専用interfaceも個別に登録する。

UseGraphics2Dは未登録のサービス型だけを補完するので、事前に登録された外部実装も保持する。再呼び出しても後から追加した上書きは失われない。外部実装を複数Addした場合は最後の登録が使われる。すべての登録はprovider構築前に完了させる。構築後のIServiceCollection変更は既存provider・登録表・Rendererへ反映せず、設定変更には新しいproviderとRendererを使う。

標準のサービス寿命はScopedとし、scopeをdevice・Renderer単位で作る。DrawingCommandRenderers2DとRendererは注入された実装を借用し、異なるdevice間で状態を持つ外部実装を共有しない。全PreparedDraw2Dを解放し、Rendererを破棄してからscopeを破棄する。外部実装をfactoryやinstance登録で供給する場合も、その登録方式の所有権に従ってこの寿命を維持する。標準のstateless実装のみ手動共有できる。

### Graphicsシステムとの直接接続

UseGraphics2DはIGraphicDeviceやRenderer2Dを登録しない。アプリケーションはDIまたはCreateDefaultで組み立てたDrawingCommandRenderers2Dと、既存Graphicsのdeviceを通常の引数として渡す。GPU資源の生成にはRenderPreparationContext2D.Deviceを使えるので、標準の命令実装はdeviceをDIから取得する必要がない。

次は既存の描画先へ2Dを重ねる正常終了時の例である。drawListはCanvas.Endの結果、targetViewは同じdeviceの有効な既存内容を持つview、targetToAttachmentはその範囲を直前の実使用状態からColorAttachmentへ遷移させるTextureBarrierDescとする。入力画像等はSampled stateにしておく。

```csharp
// deviceはアプリケーション所有。DIへ登録せず直接接続する。
using var renderer = new Renderer2D(device, renderers);
var prepared = await renderer.PrepareAsync(drawList, new RenderTarget2D
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Load
});

var commands = device.CreateCommandBuffer(new CommandBufferDesc());
commands.Barrier(targetToAttachment);
prepared.Record(commands); // active passの外から呼ぶ。内部のpassも閉じて返す。
// 必要なら、後続Graphics処理のbarrierと命令をここへ追加する。
commands.Finish();

var submission = device.Queue.Submit([commands]);
prepared.TrackSubmission(submission);
await submission.WaitAsync();

commands.Dispose();
prepared.Dispose();
submission.Dispose();
// DIを使う場合は、rendererの破棄後にscopeを破棄する。
```

Graphicsとの接続点はdevice、targetView、command buffer、submissionである。新規targetはClearで初期化する。毎frameの完了待ちは説明用で、実際にはアプリケーションのframe管理が完了確認と解放を遅延できる。DIを使わない場合はCreateDefault、または各実装を指定するコンストラクターで登録表を用意し、同じ描画手順を使う。

GPU機能不足は実装の生成時、またはPrepareAsyncの全体検証で報告する。Prepareは描画ごとの入力検証と資源確保を行うが、別実装の再試行を行わない。途中のPrepare失敗・キャンセルでは、それまでに作った全batchを破棄する。Record中の失敗では部分記録を再使用せず、未送信command bufferを先に破棄してからbatchを破棄する。

### 命令と登録実装の対応

以下は描画方式の割り当てであり、公開APIの宣言は上のdiffを参照する。難度はAIが自作し検証完了するまでの相対評価で、1は局所処理、2は境界条件、3は統合、4は幾何・画像比較、5はGPU並列化と性能調整が中心となる。実測の性能順位は表さない。

| 命令 | 標準登録の描画方式 | AI実装難度 |
| --- | --- | --- |
| FillRect | quadと解析的な境界coverage、共通paint評価 | 1〜2＋paint |
| FillRoundedRect | quadと角丸の解析coverage、共通paint評価 | 2〜3＋paint |
| FillCircle | quadと円・変換後の楕円の解析coverage、共通paint評価 | 2〜3＋paint |
| DrawLine | 線分を覆うquadと線分・capの解析coverage、共通paint評価 | 2〜3＋paint |
| FillPath | CPU前処理とGPUタイルcoverage、共通paint評価。別途compute実装を固定登録可能 | 4＋paint、compute実装は5＋paint |
| StrokePath | stroke輪郭の展開と汎用coverage、共通paint評価 | 4＋paint |
| DrawMesh | 入力頂点・indexによる三角形描画、共通paint評価 | 2〜3＋paint |
| DrawImage | RGBA texture付きquad、premultiplied補間とalpha合成 | 2〜3 |
| DrawCoverageMask | coverage texture付きquadとpaint評価 | 2〜3＋paint |
| DrawDistanceField | 距離場復元shaderとpaint評価 | 3＋paint |
| DrawPaintGraph | graph走査、パスcoverage、gradient・画像pattern、隔離合成 | 4 |

表の「＋paint」は下記graph評価の難度も必要という意味で、数値の加算ではない。単純なSolid・gradient・画像patternは形状のcoverageと同じfragment shaderで評価できる。複雑なclip、Layers、Composite、Opacityでは相関を保つcoverage処理や隔離中間面を用い、矩形でも一つのdrawに収まるとは限らない。GPUコマンド数は命令数から独立させる。

一つの登録実装は命令の意味、paint、clip、品質契約を満たす全処理を所有する。標準実装は共通のパス・paint処理を内部で再利用できるが、別の登録実装への自動委譲はしない。特にStrokePath内部の輪郭処理とDrawPaintGraph内部のパスは、それぞれに登録した実装の一部である。FillPathだけを置換しても両者の内部は変わらない。図形・線・mesh・coverage画像・距離場に渡されたpaintも、その命令実装の責務である。paint非対応の外部実装はPrepareでNotSupportedExceptionを返し、別命令へのfallbackは行わない。

ClipRect/ClipPathはCPU状態命令であり、ClipStack2Dへ元の形状と変換を記録する。clipのGPU評価は、そのclipを使用する描画命令の登録実装が担当する。これにより個別に平均化したmaskの乗算を必須とせず、形状とclipを同じサンプルで評価できる。異なる登録実装間でもclipの意味と共通の品質契約を満たす必要がある。

Solid、LinearGradient、RadialGradient、SweepGradient、ImagePattern、Opacity、Clip、Transform、Layers、Compositeは不変graphの構築操作であり、独立した描画命令ではない。DrawPaintGraphまたはpaintを引数に取る命令の実装が、下記の評価方式を持つ。

| graph構築操作 | 評価方式 | AI実装難度 |
| --- | --- | --- |
| Solid | 線形化した定数色 | 1 |
| LinearGradient | 3点定義の射影とstop補間 | 2〜3 |
| RadialGradient | 2円の解・描画有効領域とstop補間 | 4 |
| SweepGradient | 角度とstop補間 | 3 |
| ImagePattern | 座標逆変換、source相対のtap別tile処理、線形光のpremultiplied画像補間 | 3〜4 |
| Opacity | 子graphの合成後のRGBAを倍率変更。必要に応じて隔離中間面 | 3〜4 |
| Clip | 子graphへの幾何的clip | 4 |
| Transform | geometryとpaint座標系の変換 | 2 |
| Layers | 配列順のsource-over | 2〜3 |
| Composite | source/backdropの独立評価と指定modeによる隔離合成 | 4 |

Begin、Save/Restore、ConcatTransform、Translate/Rotate/Scale、End、CancelはCanvasの固定CPU処理とする。PrepareAsync、Record、TrackSubmission、Disposeはホストの固定ライフサイクルであり、命令実装へ所有権を移さない。PathBuilderの各操作もCPU上のデータ構築である。

### 外部描画形式の契約

DrawImageとImagePatternはalpha表現を尊重し、sRGB texelをdecodeして線形RGBのプリマルチプライド値で補間する。Straight画像を補間してからalphaを乗算する処理で代用しない。必要ならtapごとの処理や内部変換を使う。Premultiplied入力へ再度alphaを掛けない。入力画像は表示用の色であり、coverage・距離場textureとは区別する。

DrawCoverageMaskのcoverageはsampling後の[0,1]値を使う。DrawDistanceFieldではSDFはR、MSDFはRGBの中央値を輪郭距離とし、MTSDFはRGBを輪郭、Aをtrue signed distanceの情報として保持する。距離range・符号・paddingの規約を供給側と一致させ、画面上のpixel幅でAAを評価する。sampling kernelが隣のatlas領域を読まないpaddingとsource範囲は供給側が確保する。

DrawMeshは入力のCoverageを補間する。境界用頂点がないmeshに自動でAA済みの品質を仮定しない。paintは変換前の論理座標で評価し、変換とclipは他の描画命令と同じ意味を持つ。

単色、gradient、画像patternを含むgraphの結果を、形状とclipとの相関を保ってcoverageで覆い、Canvasへsource-over合成する。一般のCompositeModeではcoverageとopacityを混同せず、Clear/Src/DestIn等の境界で必要なdestination側の処理も仕様に従う。

### コマンド、描画順、クリッピング

CanvasはBegin中だけ操作できる。Endは未対応のSaveがある場合に失敗し、記録を維持する。入力不備はコマンドを追加せず、状態を変更しない。End成功またはCancel後は新しい記録を開始できる。描画コマンドは変換、clip、不変paintへの参照、stroke設定をsnapshotにし、後続の設定変更の影響を受けない。paintが借用する画像の内容とGPU寿命は外部リソースの契約に従う。PathBuilderはMoveTo前の線・曲線・Closeを拒否する。

描画順は記録順で固定する。バッチ化は連続し、pipeline、texture、sampler、clip、合成設定が一致するコマンドに限る。透明度やtextureを理由に並べ替えない。標準実装では画像はquad、図形のパスは共通のcoverage処理へ変換し、既存のtyped bufferとshader引数で渡す。上限を超えるバッチは順序を維持して分割する。

空の図形、空のclip、変換後に面積を持たない図形は描画しない。矩形clipは変換後に軸平行で整数pixel境界となる場合にscissorを使用できる。小数境界、回転・せん断を含む場合はそのclipを使用する描画命令の登録実装がcoverageを評価する。別の命令実装へ自動で切り替えない。scissorは描画先の範囲内に収め、空範囲にはGPU命令を発行しない。複雑なclipは形状の積集合として評価する。独立に平均化したcoverageの単純乗算で同じ境界へAAを二重適用しない。maskとpaint graphの合成用中間面はrenderer内部で所有し、生成と利用を別passとbarrierで区切る。パスclip非対応の実装段階ではNotSupportedExceptionを返し、境界矩形への置き換えは行わない。

曲線を分割する実装では、変換とPixelScaleを適用した画面上の誤差に基づいて分割する。アンチエイリアスは登録実装が契約の精度を満たすcoverageを生成する。形状とclipの相関を保つサンプル評価または幾何的な交差処理を使う。現行texture契約にないMSAAやresolveを必須にしない。最大誤差、分割数、maskサイズの具体的な上限は実装前の後続判断で定め、超過時に黙って品質を下げない。

### 座標、色、合成

左上原点、右がX正方向、下がY正方向の論理座標とする。Canvasの行列適用後にPixelScaleを掛け、targetの実寸法でclip spaceへ変換する。回転はこの座標系で正の値が時計回りとなる。非整数の座標も保持し、pixelへの丸めはscissor範囲の計算に限定する。

色はAPIのsRGB入力を線形へ変換し、プリマルチプライド値でpaintを評価する。画像はtexelごとにdecodeし、Straight入力の場合だけalphaを掛けてから補間する。最終出力は形状・clipのcoverageを反映し、alphaを二重に乗算しない。通常合成はsource-overで、RGBとalphaともsource factor=One、destination factor=OneMinusSourceAlphaを使用する。sRGB targetへのencodeはattachmentに任せる。clear値も線形RGBへ変換して渡す。

初期出力は透明またはプリマルチプライドの線形合成結果をsRGB格納したtargetとする。Loadを使う場合、既存内容も同じ表現を満たす必要がある。自身のオフスクリーン出力を画像として再利用する際はImage2Dのalpha表現を明示する。COLRv1用のグラデーションとgraph内部の合成は下記で規定する。HDR、広色域、Canvas全体の追加blend modeは別途設計する。

### GPU実行、所有権、エラー

アプリケーションはpaint graph内の画像を含む全入力画像・mask・距離場をupload済みかつSampled stateにし、targetをColorAttachment stateへ遷移させてからRecordを呼ぶ。外部リソースの前後のbarrierとsubmission間の同期はアプリケーションが管理する。各命令実装は自分のupload・描画buffer・mask・合成用中間面に必要なcopy/computeとbarrierを記録する。同じtarget subresourceを同時に画像として読む操作は拒否する。

利用順は「Canvas.End → PrepareAsync → 外部barrier → Record → 必要な後続barrier → commands.Finish → Queue.Submit → TrackSubmission → 完了確認 → commands.Dispose → PreparedDraw2D.Dispose」である。ホストは最初にTarget.LoadOp/ClearColorを一度だけ適用し、以後のbatchへLoad/Storeで使うtargetを渡す。各batchはtargetをColorAttachment stateへ戻してStoreし、開いたpassを残さない。内部textureは必要な最終stateへ遷移させ、外部入力のSampled stateを維持する。queue送信やPresentを内部で呼ばず、既存のオフスクリーンtargetで使用できる。未採用のSurface設計を前提にしない。

PreparedDraw2Dは全IPreparedCommandBatch2Dとその内部リソースを所有する。Record前は破棄可能。Record後に送信を取りやめる場合は、記録先command bufferを先に破棄してから解放する。送信した場合はそのsubmissionを必ずTrackSubmissionで関連付け、Pending中のDisposeを拒否する。関連付け前でも記録先が生存する間はDisposeを拒否する。完了後も記録先を先に破棄してから内部リソースを解放する。submissionはこの確認まで生存させる。FailedではGPU使用終了が確認できる既存backendの契約に従い、状態名だけで安全な解放を推測しない。

画像・mask・距離場・targetは借用し、Prepareから記録、送信、GPU完了まで呼び出し側が生存させる。DrawListの保持だけではリソースのDisposeを防がない。グローバルなResourceManager、世代管理、ファイル読み込みは追加せず、その仕組みが必要なアプリケーションから供給する。

Canvas、Renderer、PreparedDrawの同時呼び出しは保証しない。CPUコマンド列は不変として共有できるが、GPUリソースのアクセスと解放は利用者が同期する。asyncの内部upload処理が終わる前のrenderer破棄は拒否する。

数値・範囲違反はArgumentExceptionまたはArgumentOutOfRangeException、呼び出し順違反はInvalidOperationException、破棄済みリソースはObjectDisposedException、非対応format・機能はNotSupportedExceptionとする。未知のenum値も拒否する。通常の入力不備はRecord前に全体検証する。native記録中の失敗は例外で報告し、部分記録されたcommand bufferとPreparedDrawは再使用せず破棄する。暗黙のsubmit、wait、失敗した描画のスキップは行わない。

### 外部文字ライブラリから描画する

文字ライブラリは輪郭とpaintを保持し、文字サイズ、変換、再利用状況に応じて描画形式を決める。Drawing2DはDrawGlyphRun、GlyphRun2D、GlyphPaint2Dを公開しない。文字ライブラリがパスならFillPath、三角形ならDrawMesh、coverage画像ならDrawCoverageMask、距離場ならDrawDistanceField、RGBA画像ならDrawImage、COLRv1の構造を保持するならDrawPaintGraphを呼ぶ。Drawing2Dは受け取った命令を、その命令に登録された実装へ渡す。

文字列は上位の文字処理層で「フォントfallback → shaping → 行配置 → outline/COLR paint解決 → 描画形式の選択 → 汎用描画命令」に変換する。日本語、合字、右から左への表記、advanceとoffsetはその層で解決する。OpenTypeの輪郭はfont unitsから論理座標へ変換し、Y上向きからY下向きへの反転もadapterで行う。paintの座標、clip box、回転・skewの角度も同じ変換を適用する。輪郭の穴はNonZero fillと元のwindingで保持する。

単色の輪郭は `Clip(outline, Solid(color))`、カラー文字は複数のpath clip、gradient、transform、layers、compositeで表せる。輪郭cacheの可変軸、coverage cacheの倍率・subpixel位置・AA、完成画像cacheのpalette・foregroundを含むキーと失効は文字ライブラリの責務とする。

フォントparserやshaperは本ライブラリへ埋め込まず、上位のadapterがCOLRv1を次のように写像する。本ADRはCOLRv1を描画できる表現を必須にするもので、font parserの実装済み対応を宣言するものではない。

| COLRv1の要素 | Drawing2Dへの写像 |
| --- | --- |
| PaintSolid / PaintVarSolid | CPAL palette、foreground、alphaを解決したSolid |
| PaintLinearGradient / PaintVarLinearGradient | 3点のLinearGradientとColorLine |
| PaintRadialGradient / PaintVarRadialGradient | 2円のRadialGradientとColorLine |
| PaintSweepGradient / PaintVarSweepGradient | 角度と座標を変換したSweepGradientとColorLine |
| PaintGlyph | outlineをClipとして子paintへ適用 |
| PaintColrGlyph | 参照glyphのpaint graphを解決し共有。仕様のclip適用範囲を保持 |
| PaintColrLayers | 順序を保持したLayers |
| PaintTransform、Translate、Scale、Rotate、Skewと各Var/center版 | centerを含めたアフィン行列のTransform |
| PaintComposite | source、backdrop、全28種のCompositeModeを保持するComposite |
| ClipList / ClipBox / variable clip box | 仕様で指定される範囲に矩形pathのClipを適用 |

variationは正規化軸座標とfontのvariation storeに従って上位adapterで解決する。CPALのpalette選択、foreground（palette index 0xFFFF）、stop alphaを含む値もgraph生成前に解決する。異なる軸・palette・foregroundのrunでgraphを誤共有しない。PaintColrGlyphを単なる輪郭参照へ縮約しない。参照循環、不正offset、不正glyph、深さ・ノード数超過はadapterで拒否し、rendererも評価コスト上限を検証する。共有DAGは許可するが、参照回数による実際の展開コストも上限へ数える。

### グラデーションと隔離合成の契約

ColorLineはvariation適用後にoffset順へ安定ソートし、同じoffsetではfont内の順序を維持する。重複位置、Pad/Repeat/Reflect、端点と退化条件はOpenType COLR仕様に従う。offsetを一律[0,1]へ丸めたり、stopを重複除去したりしない。3点linearの投影、中心の異なる2円radialの解と描画有効領域、sweepの角度境界を保持する。特異な変換・退化gradientは仕様に定義された結果を実装し、未対応段階ではPrepareでNotSupportedExceptionを返す。別のgradientへの近似で成功扱いにしない。

COLRv1を含むgraphの補間・合成は線形光RGBで評価する。CPALやColor2DのsRGB成分に逆伝達関数を適用し、alphaを乗算してからstopを補間する。CompositeModeもCOLR仕様が参照するcompositing/blending規則に従い、線形光RGBで計算する。非分離HSL modeでは指定された非プリマルチプライド成分の演算と再乗算を行う。alpha=0での色成分とPlus等の範囲処理も参照規則に合わせる。

中間面は線形光・プリマルチプライドのRgba16Floatを基準とし、必要なattachment/sample用途が未対応の場合はPrepareで拒否する。非線形sRGBのまま計算する経路やRgba8への暗黙の精度低下は設けない。最終出力時にsRGB attachmentへencodeし、途中でencode/decodeを重ねない。

Compositeのsourceとbackdropはそれぞれ独立した透明面に評価する。backdropはgraphの子であり、Canvasに既に描かれた内容ではない。子同士を合成した結果を一つの描画として外側へSrcOver合成する。Layersもgraph内部の順序で評価し、nested compositeを外側の描画列へ平坦化しない。graphと中間面境界を越えたバッチ化を禁止する。

Clear、Src、DestIn等では透明領域も結果へ影響するため、単なる子paintの輪郭のunionを評価領域にしない。target、Canvas clip、図形命令では外側形状も含めた有限領域を評価範囲とし、boundsを狭める最適化は各演算の透明領域の意味を保存できる場合だけ行う。clip maskのcoverageはgraphの指定箇所で適用し、graph全体の再合成時に二重乗算しない。ImagePatternとOpacityは一般描画用の拡張nodeであり、COLRv1 adapterに追加のOpenType形式を要求するものではない。

内部面の生成、render/compute pass分割、copyと必要なresource state遷移はRecordへ組み込む。アプリケーションへの追加submitやGPU待機は要求しない。中間面・maskの寿命はPreparedDraw2Dに属する。最大面積、深さ、評価数、総メモリの上限をPrepareで検証し、超過時に単色化・レイヤー省略で代用しない。

参考: [OpenType COLR — Color Table](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)、[CPAL — Color Palette Table](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)、[Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)。

## 検討した代替案

- 図形の塗りを単色だけにし、gradient・画像をすべてDrawPaintGraphへ変換する: 矩形等の専用描画経路と命令単位のDIを活かすため、形状命令自体に共通paintを持たせる。
- paint種別ごとに描画命令とDI登録を増やす: 形状との組み合わせで登録数が増えるため採用しない。各形状の専用interfaceがpaint評価を含めて処理する。
- 画像patternにImage2D.Samplerと独立したtile/filter指定を同時に渡す: 設定の優先順位が生まれるため採用しない。画像paint用の記述にsampling設定を集約する。
- CanHandleと優先順位で命令ごとに実装を探索する: DIで設定した経路が入力や環境によって変わるため採用しない。命令別interfaceの単一解決と固定保持を使う。
- genericな描画interfaceと型の辞書で実装を取得する: 宣言量は減るが、今回の固定された11命令には専用interfaceと型付きfieldを使い、バッチごとの呼び出し経路を明確にする。
- DI解決後に独自の置換APIで登録表を編集する: DIの登録内容と別の登録表を編集する手順が増えるため採用しない。DIではUseGraphics2Dで標準を補完し、後続のDI登録を優先する。DIなしの標準構築にはCreateDefaultを提供する。
- 描画ごとにGPUへ送信する: 小さな描画で送信回数が増え、アプリケーションのcopyや他のrender passとの同期を組み立てにくいため採用しない。
- textureごとにコマンドを並べ替える: 半透明の重なりとUIの前後関係が変わるため採用しない。連続コマンドだけをまとめる。
- グリフを単色atlasまたは色付きpathの平坦な列に限定する: COLRv1のgradient、参照、隔離合成を保持できないため採用しない。パスにpaint DAGを組み合わせる。
- シーングラフを必須にする: 単純なデバッグ描画にもノード管理が必要になり、UIやゲーム側の既存階層と重複するため採用しない。
- 2D専用のGPU backendを作る: device、shader、buffer、同期の既存契約を重複させるため採用しない。
- 文字列処理・ファイル資産管理をCanvasへ統合する: 描画記録と非同期I/O、フォントfallback、キャッシュ世代の責務が混在するため、配置済み描画データを受け取る。

## 結果と影響

図形の塗り、線、mesh、文字ライブラリから渡すcoverage・距離場で同じpaintを共有できる。画像patternとgroup opacityによりUIの背景や装飾も描画できる。一方、単純な矩形実装も指定されたpaintの評価が必要になり、複雑なgraphでは中間面と複数passを要する。画像のtap別tile処理と正しいalpha補間は単純なtexture samplingより実装・検証コストが増える。

ゲーム、UI、図形描画はbackendに依存せず同じCanvasを使用できる。描画順を維持するため結果を予測でき、GPU送信は他の描画処理とまとめられる。一方、texture切り替えの多い描画では全体ソートよりバッチ数が増え、複雑なclipとCOLRv1ではmask・隔離合成のpassとメモリが必要になる。文字は輪郭を共有して拡大に対応できる一方、単色atlas方式よりパス評価と合成のコストが増える。

CPUコマンド記録、GPU準備、記録、完了の境界を持つため、即時描画より呼び出し手順が増える。上位アプリケーションはframeごとの所有者にその手順を集約できる。既存Graphicsの契約は変更せず、新規ライブラリとそのshader・サンプル・テストを追加する。

専用interfaceと型付き登録表により、描画中のDI解決と型の辞書検索を避けられる。一方、命令追加時は専用interface、コンストラクター、dispatch、標準実装、CreateDefaultとUseGraphics2Dの登録を同時に追加する必要があり、登録表のコンストラクターも変更になる。登録用パッケージを分離するため、本体と併せたバージョン管理が必要になる。

## 検証方針

塗りの検証では6種類の図形・線命令でColor2D版とSolid版が同じ結果になり、全paint種別を同じ専用interfaceへ渡せることを確認する。2点/3点linearと単一中心/2円radialの等価条件、複数stop、Repeat/Reflect、変換前のpaint座標、形状boundsへ自動伸縮しないことを確認する。shape・Canvas clip・graph Clipに同じ境界を使ってもAAが二重に薄くならないこと、Opacity(0.5)を適用した不透明Layersの重なりもalphaが0.5になること、全体opacityと子ごとのalpha指定が区別されることを画像比較する。

画像patternはatlas隣接領域に強い別色を置き、全tile mode、X/Y別指定、負座標、1texel幅、切り出し、回転・拡大縮小、Nearest/Linear、Decal境界を検証する。透明texelに任意のRGBを置いても色が漏れないこと、Straightと等価なPremultiplied入力の結果が一致すること、sRGBと線形formatを正しく扱うことを確認する。既存DrawImageのalpha補間も含める。source範囲違反、特異行列、画像viewのdevice不一致、graph内部のtarget自己参照をRecord前に拒否し、borrowed viewと内部変換面をGPU完了まで維持することを確認する。

CreateDefaultはDIパッケージとdeviceなしで呼び出せること、全11propertyの実装型がUseGraphics2Dの標準登録と一致すること、複数回の呼び出しで表と各実装が別instanceになること、生成時にGPU資源を確保しないことを確認する。DI経路の外部登録がCreateDefaultで無視されず、コンストラクターへ届くことも検証する。

DIの検証ではUseGraphics2Dだけで11標準実装とDrawingCommandRenderers2Dを解決できること、後続のAddScopedで指定した最後の実装が使われること、UseGraphics2Dの再呼び出しや先行する外部登録で意図した実装が保持されることを確認する。provider構築後の登録変更が既存provider・Rendererへ波及しないこと、null引数の拒否、IGraphicDeviceの登録なしで登録表を解決できることも確認する。

11専用interfaceへの配送をそれぞれ確認し、FillPathだけを外部実装へ置換した場合にStrokePathやDrawPaintGraphへ影響しないことを検証する。FillPath → FillRect → FillPathの順序とバッチ境界、描画中のDI解決・型の辞書検索がないこと、未対応入力・device機能不足で別実装を呼ばないこと、公開snapshotだけで外部実装できることを確認する。CPU経路とcompute経路は同じfixtureで比較し、順序、clip境界、合成、許容誤差を一致させる。小さなバッチが多い条件でもCPU準備時間と割り当て量を測定し、専用interface化の効果を評価する。

実装時は共通APIを使うsample/testを用意し、backend固有device生成はbootstrapへ置く。CPUテストでは変換順、Save/Restore、snapshot、不正入力後の状態、fill rule、stroke join、バッチ境界を確認する。

GPU画像比較では半透明図形の前後入れ替え、sRGBとalpha合成、ネストしたclip、回転矩形clip、パスの穴、曲線拡大、高DPI、パス文字、Load/Clearを確認する。三角形の共有境界に二重合成や隙間がないことを含め、色空間と誤差許容値を固定する。画像転送・readbackは既存の明示的なcopyと完了待機を使用する。

COLRv1の検証では固定したfont fixtureと軸・palette・foregroundで参照描画を比較する。単色とCOLRv0相当のlayers、全gradientとextend、重複stop、3点linear、偏心radial、sweep境界、nested transform、PaintColrGlyph、variable paint/clipを含める。全28の合成modeを半透明・透明入力と隔離backdropで検証し、sRGB入力からの線形化、線形光RGBでの補間・合成、外側Canvasへの合成を別々に確認する。空のsource、Clear/Src、HSL、clip境界、共有DAG、循環参照拒否、コスト上限超過、複数拡大率での輪郭再評価を受け入れ条件にする。

Recordがsubmit/waitを呼ばないこと、入力不備による部分記録がないこと、Pending中の解放拒否、未送信時の破棄、複数frameをGPU完了前に準備した場合のbuffer非上書きを確認する。代表的なスプライト、UI、パス描画でCPU準備時間、GPU時間、draw call数、転送byte数、割り当て量、mask使用量を測定し、backendごとの対応範囲とともに記録する。測定前に特定のframe rateを保証しない。

## 別途決定する事項

- パスの三角形化アルゴリズム、誤差・複雑度上限、mask cache戦略。
- text shaping、フォントfallback、OpenType outline/COLRv1 adapterのライブラリと公開API。
- HDR、広色域とCanvas全体の追加合成。
- シーングラフ、パス/paintの描画cacheと任意のatlas最適化、複数threadでの準備、frame pool。

これらを具体化し、対応する判断を採用してから実装を進める。
