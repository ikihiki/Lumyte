# ADR-GRAPHICS-0016: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 提案
- 日付: 2026-10-10
- 更新: 2026-10-11 — 共通画像エフェクト、2Dレイヤー、宣言的GPUパス計画と将来のレンダーグラフ接続を追加

## 背景

ゲーム内のスプライト、UI、図形、文字を共通の2D描画APIで扱いたい。利用者が図形ごとにGPU buffer、shader、描画状態を組み立てると、backend間の違い、クリッピング、半透明の描画順、GPU使用中のリソース寿命を各機能で重複して管理することになる。

Lumyteには既に [Graphics device](GRAPHICS-0001-graphics-device.md)、[CommandBufferとSubmission](GRAPHICS-0007-command-buffers-and-submission.md)、[描画状態](GRAPHICS-0008-pipeline-programs-and-render-state.md)、[shader引数](GRAPHICS-0009-shader-argument-binding.md)、[indexed draw](GRAPHICS-0011-indexed-and-indirect-commands.md) の契約がある。2D描画はこれらの上に構築し、独立したGPU backend抽象化は追加しない。

本ADRの主要な判断は、即時型のCanvas APIでCPUコマンド列を生成し、描画順を保持して既存Graphics APIへ変換することである。公開境界と、この判断に必要な状態・所有権の契約を定める。実装済みの機能や性能を表すものではない。

## 決定

### 適用範囲と責務

`src/Graphics/Lumyte.Graphics.Drawing2D` にC#ライブラリを配置する。プロジェクト名、NuGet名、名前空間は `Lumyte.Graphics.Drawing2D` とする。実行時にはBCL、`Lumyte.Graphics.Abstractions`、`Lumyte.Graphics.RenderGraph` と後述する `Lumyte.Graphics.ImageEffects` を参照し、backendの具象型、Window、Input、Compositionへ依存しない。shader成果物は既存のSlang・shader生成基盤で用意する。

DI登録は任意の別パッケージ `Lumyte.Graphics.Drawing2D.DependencyInjection` に置き、同名のプロジェクト・名前空間を使う。このパッケージは本体、`Lumyte.Graphics.ImageEffects.DependencyInjection` と `Microsoft.Extensions.DependencyInjection.Abstractions` を参照する。UseGraphics2DはIServiceCollectionの拡張とし、描画・エフェクトの標準登録を補完する。DIコンテナーの生成はアプリケーションが行う。本体のコンストラクターは通常の引数を受け取るため、DIを使わない手動構築もできる。

構成は「アプリケーション → Canvas2D → DrawList2D → Renderer2D → RenderGraph → CompiledRenderGraph → IGraphicDevice」とする。CanvasはCPU側の変換・クリップ・描画コマンドとレイヤー構造を記録し、Rendererは命令ごとに構築時に指定された実装へ処理を渡す。連続コマンドのバッチ化後、描画・エフェクトの各passと資源アクセスを宣言する。deviceの生成・破棄、queue送信、完了待機、画面への表示はアプリケーションが所有する。

接続先は並行開発の [RenderGraph PR #35](https://github.com/ikihiki/Lumyte/pull/35)（GRAPHICS-0015、確認revision `b1721647f3fcf0276d3a0b0d165e948e3ece40e5`）とする。これはmainへ取り込み済みの機能とは扱わず、実装時にその採用・公開契約を確認する。単独2Dの便利APIも私有graphを使うため、別のPassPlanningライブラリや重複した順次実行器は追加しない。2DのGPU実装開始にはRenderGraphを必要とするが、Canvas・paint・effect DAGのCPU側設計は独立して進められる。

矩形、角丸矩形、円、線、ベジェ曲線を含むパス、画像、coverage画像、距離場、三角形mesh、paint graph、オフスクリーン描画を対象とする。最初に矩形・画像・変換・矩形クリップを実装し、その後にパスとpaint graphを追加する。文字の描画形式の選択とDrawGlyphRunは別の文字ライブラリが担当し、Drawing2Dには汎用描画命令を渡す。シーングラフは必要な利用者がCanvasの上に構築し、必須にしない。レイアウト、ヒットテスト、画像デコード、フォント探索、文字列のシェーピング、行分割は本ライブラリの責務に含めない。

### 公開APIの境界

比較元: main `c1f7c1d`。対象APIは比較元に存在せず、以下は追加する主要メンバーの宣言である。Graphicsの型には `Lumyte.Graphics.Abstractions`、行列・ベクトルには `System.Numerics` を使用する。RenderGraphの型はPR #35の公開APIを参照し、以下の `RenderGraphBuilder` は `using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;` による型aliasで、新しい型ではない。既存GraphicsのAPIを置き換えず、その上に構築する提案である。

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
+    // 単一mip/layerのSampled D2 viewとsamplerを借用。同一deviceを利用者が保証。
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
+        // 単一mip/layerのSampled D2 viewを借用。描画deviceとの一致は利用者が保証。
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
+        // 同一device、親textureも単一sample/mip/layerのD2、RenderAttachment view。
+        // 背景エフェクトを含む場合は親textureのSampled用途も必須。
+        // 初期対応はRgba8SrgbとRgba16Float。寸法はviewから取得する。
+        // Rgba16Floatも初期2Dではdisplay-linearのpremultiplied値を保持する。
+        public required IGraphicsTextureView View { get; init; }
+        // 論理座標から物理pixelへの倍率。finiteかつ正数、既定値1。
+        public float PixelScale { get; init; } = 1;
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    public sealed record RenderTargetBinding2D
+    {
+        // 同じgraphのtexture。Descriptionはallocationの実際の宣言と一致させる。
+        public required RenderGraphTexture Texture { get; init; }
+        public required TextureDesc Description { get; init; }
+        // 初期targetはallocation全体の単一mip/layer。このrange以外は未対応。
+        public TextureSubresourceRange Range { get; init; } = new(0, 1, 0, 1);
+        public float PixelScale { get; init; } = 1;
+        // この画像の左上が、ルート描画先の物理pixel座標のどこにあるか。finiteな整数。
+        public Vector2 PixelOrigin { get; init; }
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    // 外部画像は呼び出し側が一度だけimportし、同じallocationのhandleを共有する。
+    public sealed class DrawingGraphBindings2D
+    {
+        public DrawingGraphBindings2D(RenderGraphBuilder graph);
+        // 同じtexture/handleの再指定は許可、既存対応と異なる再指定は拒否。
+        // 正しいallocationとhandleの対応・同一device・寿命は利用者が保証する。
+        public void BindTexture(IGraphicsTexture texture, RenderGraphTexture resource);
+        // 未登録は準備時に拒否。viewは親textureからこの対応を引く。
+        public RenderGraphTexture GetTexture(IGraphicsTexture texture);
+    }
+    public sealed class Renderer2D : IDisposable
+    {
+        // deviceは借用。2引数版は標準ImageEffectRenderersを生成して使用する。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers);
+        // 明示的なエフェクト設定も借用。DI利用時は解決した両登録表を渡す。
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
+            ImageEffectRenderers effects);
+        // 単独実行の便利API。私有graphへimport→AppendPassesAsync→Compileを行う。
+        // submit/waitは行わない。キャンセル時は内部確保を解放する。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 共通graphへ追加。物理target viewは不要。返却ownerをGPU完了まで保持する。
+        // 失敗時はgraph全体を再構築。同時追加・追加中のCompileは禁止。
+        public ValueTask<PreparedDrawingPasses2D> AppendPassesAsync(RenderGraphBuilder graph,
+            DrawList2D list, RenderTargetBinding2D target, DrawingGraphBindings2D bindings,
+            CancellationToken cancellationToken = default);
+        // 利用者は全準備・GPU使用・準備結果の解放を先に完了させる。暗黙のwaitなし。
+        public void Dispose();
+    }
+    public sealed class PreparedDrawingPasses2D : IDisposable
+    {
+        // callbackのCPU snapshot・pipeline・table・upload等を所有。graphは所有しない。
+        // 一つのcompiled planで一回だけ実行。GPU使用終了と記録先の破棄後に呼ぶ。
+        public void Dispose();
+    }
+    public sealed class PreparedDraw2D : IDisposable
+    {
+        // 一度だけ。Recordingかつactive passなしの同一device commandに記録する。
+        // 内部copy/compute/barrier/render passを記録し、active passを残さない。
+        public void Record(IGraphicsCommandBuffer commands);
+        // GPU完了→commands破棄後に呼ぶ。CompiledRenderGraphと準備ownerを解放する。
+        // Disposeから完了照会・waitを行わず、寿命違反の検出を保証しない。idempotent。
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

Sourceはviewが選択したmip内の整数texel矩形で、幅・高さは正数かつ全体がview内に収まるものとする。source左上をpattern原点(0,0)とし、texel中心は(i+0.5,j+0.5)、周期はsourceの幅・高さである。PatternToLocalはその座標をpaintローカル座標へ変換する。samplingでは逆変換を使い、外側のgraph変換、Canvas変換、PixelScaleとの関係を維持する。factoryはnull view、範囲、未知のenum、非finite値、逆変換不能または逆行列に非finite値を生むPatternToLocalを拒否する。Prepareではsample可能な色format、Sampled用途を検証する。同一deviceであることと外部resourceの生存は利用者が保証する。

Clampは端のtexelへ固定し、Repeatは周期反復、Mirrorは2周期で往復反復する。負の座標でも同じ規則を使う。Decalは領域外のtexelを透明として扱う。X/Yは独立に指定できる。

各軸のsource寸法をN、整数tap座標をiとすると、Repeatの参照先はpositiveMod(i,N)、Mirrorはr=positiveMod(i,2*N)としてr<Nならr、そうでなければ2*N-1-rとする。positiveModは結果が0以上となる剰余であり、負の座標も同じ端処理になる。

Nearestはpattern座標の各成分をfloorしたtexelを選ぶ。Linearはtexel中心を基準とする4tapの双線形補間とし、各tapの整数座標に対してSource相対のtile規則を適用する。Repeatの端をまたぐtapは同じSourceの反対側、Mirrorは反射先、Clampは端、Decalの範囲外tapは透明になる。座標の中心だけをwrapしてatlas全体へhardware linear samplingする方法は使わない。指定した領域の外側にある隣接画像を読まず、この画像paintにはatlas paddingを要求しない。

色付き画像の補間は線形RGBのプリマルチプライド値に対して行う。各texelをsRGBならdecodeし、Straight入力ならalphaを乗算してから補間する。Premultiplied入力へ再乗算しない。透明texelのRGBが境界へ混入しないよう、hardware sampling、tap単位の処理、内部面への事前変換はこの結果と等価な場合だけ使う。初期の画像パターンは単一mipのNearest/Linearであり、縮小時のmipmap・異方性filterは別途設計する。

ImagePatternを含むgraphは画像の参照を保持するが、viewやtextureのGPU所有権は持たない。graph内の全画像をPrepareからGPU完了まで生存させる。単独Prepare経路は全parent allocationがSampledで必要なshader stageへ可視化済み、共有graphではアクセス宣言に基づいてgraphが遷移する。初期RenderGraphはallocation全体で追跡するため、targetと同一allocationを読むpaintは別subresourceであっても、どの深さに参照があっても拒否する。

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
+        // いずれも借用。pass追加はbatch.AppendPassesで行う。
+        public IGraphicDevice Device { get; }
+        public RenderGraphBuilder Graph { get; }
+        public DrawingGraphBindings2D Bindings { get; }
+        // Loadへ正規化。ホストが必要な初回clearのpassを先に宣言する。
+        public RenderTargetBinding2D Target { get; }
+    }
+    // 以下の全PrepareAsyncに共通する契約:
+    // 同型・同一clip/合成境界の連続命令。並べ替え不可。snapshotは準備結果の寿命まで有効。
+    // CPU処理とpass追加に必要なsnapshot・固定資源を準備する。submit/waitは禁止。
+    // 一時texture/bufferはAppendPassesで論理宣言し、物理確保は実行器へ任せる。
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
+        // 準備時と同じgraph/targetへ一度だけpassとアクセスを宣言する。
+        // target更新順序を保つ。barrier・一時資源はgraph、Submitはアプリが担当。
+        void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target);
+        // PreparedDrawingPasses2Dが所有。graphの一時textureを直接Disposeしない。
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

命令のdispatchにType辞書、reflection、dynamic、DI解決、候補列挙を使わない。共通のIPreparedCommandBatch2DはCPU準備とpass宣言の境界として維持し、AppendPassesをバッチ単位で呼ぶ。ホストはbatchをPreparedDrawingPasses2Dの所有対象に加えてから宣言し、失敗時には途中までの固定資源を解放する。RenderGraphにはrollback APIがないため、失敗したgraph全体を捨てて再構築し、追加済みcallbackを実行しない。専用interfaceでもinterface呼び出しは残る。探索と呼び出し回数を抑える構成を採用し、JIT/Native AOTでのインライン化や性能向上を測定前に保証しない。

### UseGraphics2Dによる標準登録と後勝ちの上書き

本体は各専用interfaceの標準実装を公開し、登録用パッケージや手動構築から生成できるようにする。すべてstatelessで、準備結果の固定資源はPreparedDrawingPasses2D、一時texture/bufferはCompiledRenderGraphが所有する。次はコンストラクターの宣言であり、PrepareAsyncは対応する専用interfaceを明示的に実装する。標準実装の描画方式は後述の対応表で定める。

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
+        // UseImageEffectsで標準effectを補完し、全11描画interfaceと登録表をTryAddScopedで登録。
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
    services.UseImageEffects();
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
var effects = scope.ServiceProvider.GetRequiredService<ImageEffectRenderers>();
// Graphicsへの接続は通常のconstructor呼び出し。deviceのDI登録は不要。
using var renderer = new Renderer2D(device, renderers, effects);
```

この設定ではすべてのFillPathがExternalComputeFillPathRendererへ渡る。パスのサイズ、静的・動的、GPU機能によって別の登録へ切り替えない。StrokePath、DrawPaintGraph、画像系の命令はそれぞれの登録のままである。同じ経路へ変更したい命令は、その専用interfaceも個別に登録する。

UseGraphics2Dは未登録のサービス型だけを補完するので、事前に登録された外部実装も保持する。再呼び出しても後から追加した上書きは失われない。外部実装を複数Addした場合は最後の登録が使われる。すべての登録はprovider構築前に完了させる。構築後のIServiceCollection変更は既存provider・登録表・Rendererへ反映せず、設定変更には新しいproviderとRendererを使う。

標準のサービス寿命はScopedとし、scopeをdevice・Renderer単位で作る。DrawingCommandRenderers2D、ImageEffectRenderersとRendererは注入された実装を借用し、異なるdevice間で状態を持つ外部実装を共有しない。全PreparedDraw2Dと共有plan・準備ownerを解放し、Rendererを破棄してからscopeを破棄する。外部実装をfactoryやinstance登録で供給する場合も、その登録方式の所有権に従ってこの寿命を維持する。標準のstateless実装のみ手動共有できる。

### Graphicsシステムとの直接接続

UseGraphics2DはIGraphicDevice、Renderer2D、実行器を登録しない。アプリケーションはDIまたはCreateDefaultで組み立てた登録表と、既存Graphicsのdeviceを通常の引数として渡す。命令実装はRenderPreparationContext2D.Deviceからpipeline等の固定資源を準備でき、一時texture/bufferはplanへ宣言する。deviceをDIから取得する必要はない。以下はDIなしの標準構築であり、DIのeffect上書きを使う場合は前節の3引数constructorへ解決済みeffectsを渡す。

次は既存の描画先へ2Dを重ねる正常終了時の例である。drawListはCanvas.Endの結果、targetViewは同じdeviceの有効な既存内容を持つ単一mip/layerのparent textureのview、targetToAttachmentはallocation全体を直前の実使用状態からColorAttachmentへ遷移させるTextureBarrierDescとする。入力画像等はallocation全体をSampled stateにし、VertexShader | FragmentShader | ComputeShaderのShaderReadへ可視化しておく。

```csharp
// deviceはアプリケーション所有。DIへ登録せず直接接続する。
var renderers = DrawingCommandRenderers2D.CreateDefault();
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

Begin、Save/Restore、ConcatTransform、Translate/Rotate/Scale、BeginLayer/EndLayer、DrawBackdropEffect、End、CancelはCanvasの固定CPU処理とする。レイヤーと背景効果はホストが描画・画像処理passへ展開する構造命令であり、11描画interfaceへ追加しない。PrepareAsync、AppendPassesAsync、Record、Disposeはホストの固定ライフサイクルであり、命令実装へ所有権を移さない。PathBuilderの各操作もCPU上のデータ構築である。

### レンダーグラフとの接続

GPUパス計画と一時資源管理には、[PR #35](https://github.com/ikihiki/Lumyte/pull/35) の `Lumyte.Graphics.RenderGraph` / GRAPHICS-0015を利用する。参照時点では未取り込みの別PRであり、本ADRはその公開契約を前提とする設計提案である。独自のPassPlanningパッケージや二つ目の実行器は新設しない。2DとImageEffectsは同じgraphへパスを追加し、3D側も同じgraphへ接続できる。

エフェクトグラフは「入力画像にどの画像演算を適用するか」を表し、RenderGraphは「どのGPUパスでどの資源を読み書きするか」を扱う。Gaussian blur等は複数のrender passへ展開する。既存の互換命令のバッチ化を維持し、Canvas命令ごとに一つのgraph passを要求しない。

| 処理 | 担当 |
| --- | --- |
| 2Dの描画順、描画グループ、clip、背景取得位置、座標と効果範囲 | Drawing2D |
| 画像演算のグラフ、色・alpha契約、必要入力範囲と出力範囲、GPU方式の選択 | ImageEffects |
| texture / bufferの宣言、資源依存、barrier、出力からの不要パス除去、一時資源の生成 | RenderGraph |
| device、importの実状態、外部資源の生存、Submit・完了待機・Present | アプリケーション |

**入口と所有権。** `Renderer2D.AppendPassesAsync` は既存graphへ処理を追加し、`PreparedDrawingPasses2D` を返す。このownerがprepared batch、immutableなCPU描画情報、pipeline / argument table / shader data等の固定資源を所有する。graphのcallbackが参照し、さらにGPUが使用する期間もownerを保持する。graphにCPU準備結果の所有権を自動移譲したとは扱わない。

PreparedDrawingPasses2DとPreparedImageEffectのcallback集合は、一つのcompiled planの一回のRecordでのみ使用する（記録失敗時も消費済み）。PR #35自体の再Compile機能とは区別し、同じ準備ownerで複数planを実行しない。別frameや再試行ではAppendから新しいownerを準備する。これによりargument table・shader-data・stagingを先行GPU処理と共有して上書きすることを避ける。owner解放後は、そのcallbackを保持したgraphをCompile/Recordしない。

既存の `Renderer2D.PrepareAsync` は便宜APIとして維持し、私有graphの作成、入力のimport、`AppendPassesAsync`、`Compile` を行う。返す `PreparedDraw2D` が `CompiledRenderGraph` と `PreparedDrawingPasses2D` を所有し、`Record` はcompiled graphへ委譲する。いずれの入口もSubmit・Present・CPU待機を内包しない。DIで差し替える命令別インターフェースと `DrawingCommandRenderers2D.CreateDefault()` は共通である。

**資源の結び付け。** `RenderTargetBinding2D` は `RenderGraphTexture`、allocationの `TextureDesc Description`、対象viewのrange、`PixelScale`・`PixelOrigin`・LoadOp / ClearColorを持つ。PR #35のhandleにはdescriptorを取得する公開APIがないため、呼び出し側がhandleの作成/import時と一致する記述を渡す。CPU準備時に実texture viewを要求せず、callback時に `RenderGraphContext.CreateTextureView` で解決する。

外部入力画像は呼び出し側が同じgraphへ一度だけimportし、`DrawingGraphBindings2D.BindTexture` で既存handleへ対応付ける。2D内のImage / ImagePattern / mask等は、元のviewのparent textureから `GetTexture` でhandleを取得し、元のviewのmip/layerを維持する。同じparent textureの複数viewを別々にimportしない。既に3Dや別の2D呼び出しがimportしたtextureも同じhandleを登録する。graph生成のeffect中間画像はhandleのまま接続し、物理画像へ変換して再importしない。

`DrawingGraphBindings2D` はgraph単位の構築用binding表であり、命令ごとの方式選択やDI解決には使用しない。`AppendPassesAsync` は必要なbindingを準備結果へ確定し、後から表を変更しても追加済みcallbackの入力を変えない。便宜 `PrepareAsync` は私有graph内で入力parent textureを重複排除して登録する。既存描画先をそのまま同時にsampleするfeedbackは作らず、背景取得は別画像への明示copyまたは描画パスとして追加する。

**宣言とbarrier。** 命令別のprepared batchは `AppendPasses` により `graph.AddPass(...).UseTexture(...)` 等を登録する。標準callbackは一つのrender / compute pass、またはpass外のcopy群を記録し、encoderを閉じて返す。複数dispatchの間に資源依存がある場合は複数graph passへ展開する。pass間barrierはRenderGraphが生成し、標準callbackは追加の状態追跡やboundary barrierを持たない。

shader引数から参照される画像、通常buffer、shader data、copy元/先、index / indirect bufferも宣言対象である。同じatlasなどを一つのpass内で複数回参照する場合は、resourceごとにstage/accessを合併してUseを一度だけ宣言する。通常bufferは `RenderGraphBuffer<T>` と `UseBuffer<T>`、shader dataは `RenderGraphShaderDataBuffer<T>` と `UseShaderDataBuffer<T>` を使用し、既存のtyped storageとshader ABIを維持する。shader dataの一時生成はPR #35にないため、必要なallocationは準備ownerが作成・所有してgraphへimportする。画像へ直接書き込むstorage texture契約は現Abstractionsにないため、初期画像エフェクトはfragment shaderで一時render attachmentへ描画する。

**描画順と内容の保持。** graphは追加順の資源hazardから依存を導出する。2DのLoad・部分更新・source-overは `ColorRead | ColorWrite` として前の描画結果への依存を残す。全体をClearするパスのみ `ColorWrite` とする。背景ぼかしは「直前までの背景描画 → 別画像へ取得 → blur → パネル合成 → 前面UI」を明示する。資源だけで表せない順序は `DependsOn`、資源宣言で表せない副作用は `KeepAlive` を使う。`KeepAlive` は順序を指定するものではない。

PR #35はallocation全体を同期し、mip/layer/byte部分範囲の依存を追跡しない。2DとImageEffectsが生成する中間textureは単一mip・単一layerとし、未初期化画像の最初の描画はClearする。部分更新をwrite-onlyな全面上書きとして宣言すると、必要な古いproducerが除去され得るため避ける。複数mip/layerの外部画像を使う場合もallocation全体のstateが一つである前提に従い、部分更新で残す内容がある場合はread/writeとして保守的に宣言する。初期化前の他領域を後続で読まないこと、StoreOp.Discard後の内容を読まないことは描画側の契約とする。

`MarkOutput` は必要な最終画像を保持し、`ExportTexture` 等はさらに外側で必要なfinal state/scopeを指定する。graphはexportしない画像を元のimport状態へ自動で戻さない。続く処理は `GetFinalTextureState` / `GetFinalScope` の正常実行後の予定値を使う。これはGPU状態の照会でもGPU完了の保証でもない。履歴画像はアプリ所有のping-pong資源としてimport/exportし、camera切り替えやresizeによる無効化は呼び出し側が決める。

次の例は既存RenderGraph APIを利用する接続例であり、`RenderGraphBuilder` は型aliasである。新しいgraph実装を定義しない。例のtargetは内容が初期化された単一mip・layerのtextureで、入力はSampled状態の別allocationとする。引数のdevice・renderer・外部textureはアプリケーションが所有する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

var graph = new RenderGraphBuilder(device, "2d-and-effects");
var bindings = new DrawingGraphBindings2D(graph);
var colorScope = new BarrierScope(PipelineStage.ColorOutput,
    ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
var sampleScope = new BarrierScope(PipelineStage.FragmentShader,
    ResourceAccess.ShaderRead);
var targetHandle = graph.ImportTexture(target, TextureState.ColorAttachment,
    colorScope, "canvas");
bindings.BindTexture(target, targetHandle);
// sampledInputsはparent textureで重複排除済み。target自身は含めない。
foreach (IGraphicsTexture input in sampledInputs)
{
    var handle = graph.ImportTexture(input, TextureState.Sampled, sampleScope);
    bindings.BindTexture(input, handle);
}
var targetBinding = new RenderTargetBinding2D
{
    Texture = targetHandle,
    Description = new TextureDesc
    {
        Width = target.Width,
        Height = target.Height,
        Format = target.Format,
        Usage = target.Usage,
        MipLevels = target.MipLevels,
        ArrayLayers = target.ArrayLayers,
    },
    Range = new TextureSubresourceRange(0, 1, 0, 1),
    PixelScale = 1,
    LoadOp = AttachmentLoadOp.Load,
};
var drawingOwner = await renderer.AppendPassesAsync(graph, drawList,
    targetBinding, bindings);
// 同じgraphへ3Dや追加の画像処理も登録できる。
graph.ExportTexture(targetHandle, TextureState.ColorAttachment, colorScope);
var plan = graph.Compile();
var commands = device.CreateCommandBuffer(new CommandBufferDesc());
plan.Record(commands);
commands.Finish();
var submission = device.Queue.Submit(new[] { commands });
await submission.WaitAsync();
// 正常完了時の解放順。外部target/inputは引き続きアプリが所有する。
commands.Dispose();
plan.Dispose();
drawingOwner.Dispose();
submission.Dispose();
```

**失敗と解放。** 例は正常系の順序を示す。実装時には例外経路でも同じ所有権を守る。PR #35にtransaction/rollback APIはないため、`AppendPassesAsync` が例外・キャンセルで失敗したgraphはCompile/Recordせず破棄して再構築する。途中で追加されたcallbackが解放済みの準備情報を参照し得るため、そのgraphを部分成功として使わない。失敗した呼び出しで新規作成したownerはRenderer2D内部でcleanupし、以前の成功で返したownerは利用者が解放する。

`Compile` 失敗時はGPU命令がなく、準備ownerを解放できる。`Record` 失敗時は記録先command bufferを破棄してからcompiled plan、準備ownerの順に解放する。Submit後は利用者がGPU使用完了を明示的に待ち、command buffer → compiled plan → 準備owner → submissionの順に解放する。WaitのキャンセルはGPU完了ではなく、FailedもbackendがGPU使用終了を保証するまで再利用条件としない。2D APIはDispose時のGPU完了照会やlive-child counterによる誤用検出を保証せず、GRAPHICS-0014の利用者管理契約に従う。

一時allocationとcontext生成viewはcompiled planが所有し、prepare時の固定GPU資源は準備owner、importした外部資源はアプリが所有する。後続submissionでもplanの出力を使う場合は、そのGPU使用も終了するまで元planと必要な準備ownerを保持する。pipeline/cacheのevictionも同じ所有権を守る。参照の所有は実資源を生存させるためのもので、利用者の全resourceの生存を毎draw/Submitで再検証する追跡は追加しない。

**外部実装の複数パス。** 標準経路は内部のcopy/compute/renderを複数のgraph passとして公開する。外部実装が既存の複数パス記録を一つのcallbackに包む場合は、すべての外部読み書きと専用資源を宣言し、専用資源の所有者も準備ownerへ含める。その内部barrierは外部実装が担当し、境界では宣言したstateへ必ず戻す。PR #35には別のexit stateを宣言するAPIがないため、入口と異なるstateを残す実装はこの方法では接続しない。

opaqueなcallbackのscopeには内部の全stage/accessを保守的に含める必要がある。ただし一つのtextureにcopyとsampleなど異なるstateが必要なら、PR #35の一つの `UseTexture` ではその全アクセスを正しく宣言できない。この場合は複数のgraph passへ分割する。内部記録を公開できない方式は現契約では未対応として明示的に失敗させ、barrierを推測しない。命令ごとの外部方式は既存DIで明示的に登録し、`CanHandle`・探索・自動fallbackを追加しない。

今後のsubresource/byte範囲追跡、pool / alias、パス統合、複数queue、GPU timestamp、compile cacheはRenderGraph側の拡張とする。2Dのclip順序・領域・描画順とImageEffectsの演算契約はその最適化から独立させる。RenderGraphのdiagnostic snapshotは観測用でcallbackを保持しないため、それを別executorへの実行計画として利用しない。

### 2D・3Dで共有する画像エフェクト

画像処理は `src/Graphics/Lumyte.Graphics.ImageEffects` に独立させ、プロジェクト・NuGet・名前空間も同名とする。依存先はBCL、Graphics.Abstractions、Graphics.RenderGraphとし、Drawing2Dや将来の3Dライブラリへ依存しない。2Dのグループ・clip・背景取得、3Dのカメラ・投影・深度・履歴・適用タイミングは各描画システムが管理する。画像処理のgraphは処理の意味を表し、RenderGraphの実行計画はGPUパス・リソース・依存関係を表す。

`ImageEffectGraph` は生成後不変のDAGとする。`PaintGraph2D` は形状へ塗るpaintを記述し、ImageEffectGraphは画像化後の入力へ作用するため、別の型にする。例えばgradientをぼかすことと、塗られた矩形の輪郭をぼかすことを混同しない。入力のSource葉と、ColorMatrix・GaussianBlur・Opacity・Compositeの4演算nodeを初期契約とし、factoryだけでgraphを構築する。公開nodeは外部実装が読み取れるsnapshotであり、任意の派生nodeを挿入する入口は設けない。

以下は比較元mainに対する追加公開APIの宣言である。RenderGraphTexture、RenderGraphBuilderは前述のRenderGraphの型とaliasで、GPU資源の割り当てと送信をこのライブラリの型へ埋め込まない。

```diff
+namespace Lumyte.Graphics.ImageEffects
+{
+    // 同じ出力pixel grid上の半開区間。Width/Heightは0以上。加減算overflowは拒否。
+    public readonly record struct EffectPixelRect(int X, int Y, int Width, int Height);
+    // いずれもlinear sRGB primaries。HDRと表示側の適用位置を区別する。
+    public enum EffectColorSemantic { DisplayLinear, SceneLinear }
+    // Bounds左上がtextureの(0,0)、寸法はBoundsのWidth/Heightと一致する単一mip/layer。
+    // 初期入力はRgba16Floatのlinear premultiplied RGBA。handleは同じgraphに属する。
+    // Width/Heightは正、allocationは単一mip/layerで寸法が一致する。
+    public readonly record struct EffectImage(RenderGraphTexture Texture,
+        EffectPixelRect Bounds, EffectColorSemantic ColorSemantic);
+    public sealed class ColorMatrix4x5
+    {
+        // 20個のfinite係数を行優先でコピー。各行はR,G,B,A,定数項の係数。
+        public ColorMatrix4x5(ReadOnlySpan<float> coefficients);
+        public IReadOnlyList<float> Coefficients { get; }
+    }
+    public enum EffectCompositeMode { SrcOver, Plus }
+    public sealed class ImageEffectGraph
+    {
+        public ImageEffectNode Root { get; }
+        public static ImageEffectGraph Source();
+        public static ImageEffectGraph ColorMatrix(ColorMatrix4x5 matrix, ImageEffectGraph input);
+        // sigmaはfull-resolutionのdevice pixel単位、finiteかつ0以上。
+        public static ImageEffectGraph GaussianBlur(float sigmaX, float sigmaY, ImageEffectGraph input);
+        public static ImageEffectGraph Opacity(float opacity, ImageEffectGraph input);
+        // sourceだけを指定整数pixel分移動してから合成する。backdropはgraph内の子。
+        public static ImageEffectGraph Composite(ImageEffectGraph source, ImageEffectGraph backdrop,
+            EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0);
+    }
+    public abstract record ImageEffectNode;
+    public sealed record SourceEffectNode : ImageEffectNode;
+    public sealed record ColorMatrixEffectNode(ColorMatrix4x5 Matrix, ImageEffectNode Input) : ImageEffectNode;
+    public sealed record GaussianBlurEffectNode(float SigmaX, float SigmaY, ImageEffectNode Input) : ImageEffectNode;
+    public sealed record OpacityEffectNode(float Opacity, ImageEffectNode Input) : ImageEffectNode;
+    public sealed record CompositeEffectNode(ImageEffectNode Source, ImageEffectNode Backdrop,
+        EffectCompositeMode Mode, int SourceOffsetX, int SourceOffsetY) : ImageEffectNode;
+    public sealed class EffectPreparationContext
+    {
+        public IGraphicDevice Device { get; }
+        // 固定資源を準備ownerへ移す。一時textureはRenderGraphへ宣言する。
+        public void Own(IDisposable resource);
+    }
+    public interface IColorMatrixEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            ColorMatrixEffectNode effect, EffectImage input);
+    }
+    public interface IGaussianBlurEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            GaussianBlurEffectNode effect, EffectImage input);
+    }
+    public interface IOpacityEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            OpacityEffectNode effect, EffectImage input);
+    }
+    public interface ICompositeEffectRenderer
+    {
+        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan,
+            CompositeEffectNode effect, EffectImage source, EffectImage backdrop);
+    }
+    public sealed class ImageEffectRenderers
+    {
+        // 表と4標準実装を毎回newする。device・GPU資源・DIは不要。
+        public static ImageEffectRenderers CreateDefault();
+        public ImageEffectRenderers(IColorMatrixEffectRenderer colorMatrix,
+            IGaussianBlurEffectRenderer gaussianBlur, IOpacityEffectRenderer opacity,
+            ICompositeEffectRenderer composite);
+        public IColorMatrixEffectRenderer ColorMatrix { get; }
+        public IGaussianBlurEffectRenderer GaussianBlur { get; }
+        public IOpacityEffectRenderer Opacity { get; }
+        public ICompositeEffectRenderer Composite { get; }
+    }
+    public sealed class DefaultColorMatrixEffectRenderer : IColorMatrixEffectRenderer
+    {
+        public DefaultColorMatrixEffectRenderer();
+    }
+    public sealed class DefaultGaussianBlurEffectRenderer : IGaussianBlurEffectRenderer
+    {
+        public DefaultGaussianBlurEffectRenderer();
+    }
+    public sealed class DefaultOpacityEffectRenderer : IOpacityEffectRenderer
+    {
+        public DefaultOpacityEffectRenderer();
+    }
+    public sealed class DefaultCompositeEffectRenderer : ICompositeEffectRenderer
+    {
+        public DefaultCompositeEffectRenderer();
+    }
+    public sealed class PreparedImageEffect : IDisposable
+    {
+        public EffectImage Output { get; }
+        // graph callbackの固定資源を所有。関連GPU処理とplanの解放後に呼ぶ。
+        public void Dispose();
+    }
+    public sealed class ImageEffectCompiler
+    {
+        // deviceとrenderersは借用する。生成だけではGPU資源を確保しない。
+        public ImageEffectCompiler(IGraphicDevice device, ImageEffectRenderers renderers);
+        // Source葉をsourceへ束縛し、同一planへパスを追加して出力画像を返す。
+        public PreparedImageEffect AppendPasses(RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source);
+        // 所望の出力を得るために必要なSource領域を逆向きに計算する。資源確保なし。
+        public EffectPixelRect GetRequiredSourceBounds(ImageEffectGraph graph, EffectPixelRect output);
+    }
+}
+namespace Lumyte.Graphics.ImageEffects.DependencyInjection
+{
+    public static class ImageEffectsServiceCollectionExtensions
+    {
+        public static IServiceCollection UseImageEffects(this IServiceCollection services);
+    }
+}
```

Compilerは子から順に処理し、Source葉を入力画像に束縛する。同一nodeの共有は一回のAppendPasses内でmemoizeできるが、別の入力・plan・frameへ結果を流用しない。演算の種類ごとに上記の専用interfaceを固定呼び出しし、実装候補の探索、CanHandle、自動fallbackを行わない。各実装は宣言したパスを追加し、単一パスまたは複数パスの結果を返せる。入力・出力の領域と意味はnodeの契約に一致させ、対応できなければ準備中に明示的に失敗させる。

標準実装のAppendPassesは対応するinterfaceを明示実装する。pipeline等の固定資源をDeviceから準備した場合はEffectPreparationContext.Ownへ所有権を渡し、PreparedImageEffectが所有する。一時texture・bufferは論理リソースとして宣言し、実物の確保、barrier、破棄、Submit、Waitは担当しない。追加中の失敗ではcompilerが未送信の固定資源を解放し、呼び出し側は変更途中のRenderGraph全体を捨てて再構築する。正常時はPreparedImageEffectをgraphのCompile・Record・GPU使用・plan破棄まで保持する。Drawing2D経由ではPreparedDrawingPasses2Dが内部のPreparedImageEffectも所有する。記録callbackで新たな依存先や一時資源を追加しない。

専用DIパッケージ `Lumyte.Graphics.ImageEffects.DependencyInjection` が本体とMicrosoft.Extensions.DependencyInjection.Abstractionsへ依存する。UseImageEffectsは4専用interfaceへ各Default実装をTryAddScopedし、ImageEffectRenderersもTryAddScopedする。nullは拒否し、同じIServiceCollectionを返す。device、compiler、providerを生成・登録・解決しない。UseGraphics2DはUseImageEffectsを呼び、未登録の標準実装を補完する。利用者はその後のAddScopedで演算別に上書きでき、繰り返し登録で既存overrideを消さない。

UseImageEffectsの標準登録は次の対応を持つ。登録時に実装を生成・解決せず、constructor injectionで後勝ちの設定を受け取る。

```csharp
services.TryAddScoped<IColorMatrixEffectRenderer, DefaultColorMatrixEffectRenderer>();
services.TryAddScoped<IGaussianBlurEffectRenderer, DefaultGaussianBlurEffectRenderer>();
services.TryAddScoped<IOpacityEffectRenderer, DefaultOpacityEffectRenderer>();
services.TryAddScoped<ICompositeEffectRenderer, DefaultCompositeEffectRenderer>();
services.TryAddScoped<ImageEffectRenderers>();
```

DIなしではImageEffectRenderers.CreateDefault、DIありでは単一解決したImageEffectRenderersを用いる。登録表は4実装を型付きreadonly fieldで借用し、全引数のnullを拒否する。サービスの寿命はDrawing2Dと同じdevice単位のscopeとし、すべてのGPU使用と準備結果の破棄が完了するまで保持する。Renderer2Dの既存2引数constructorは標準effect表を生成し、明示的な3引数constructorは渡されたeffect表からcompilerを構築する。

3D側からも同じcompilerを利用する。次のsceneImageは同じgraphで生成済みのRgba16Float画像を表すEffectImageで、linear premultiplied RGBAとSceneLinearの契約を満たす。公開されていない3D rendererのAPIを前提にせず、その出力画像を接続する。

```csharp
var effectCompiler = new ImageEffectCompiler(device, ImageEffectRenderers.CreateDefault());
var effectOwner = effectCompiler.AppendPasses(graph,
    ImageEffectGraph.GaussianBlur(3, 3, ImageEffectGraph.Source()), sceneImage);
graph.ExportTexture(effectOwner.Output.Texture, TextureState.Sampled,
    new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
// この後にgraph.Compile→Record→Submit。GPU完了、commandsとplanの破棄後に
// effectOwner.Dispose()を呼ぶ。出力画像の所有者はcompiled plan。
```

#### エフェクト画像の色・領域・演算契約

画像の中間形式はRgba16Float、linear premultiplied RGBAとする。DisplayLinearとSceneLinearを形式だけから推測せず、入力時に指定する。SceneLinearはRGBが1を超えるHDR値を保持する。異なるsemanticの画像をCompositeへ渡すと拒否し、暗黙の露出調整・トーンマッピング・色域変換をしない。通常の2DレイヤーはDisplayLinearで、HDR中間画像に2Dを挿入する拡張では適用位置とsemanticを呼び出し側が指定する。

ColorMatrixはalphaが正ならRGBをunpremultiplyし、alphaが0ならRGBを0としたstraight linear RGBAへ4×5行列を適用する。結果のalphaを[0,1]へ制限してからRGBへ乗算する。RGBは[0,1]へclampせず、HDR値と負値を浮動小数点中間画像に保持する。表示先へ変換する際のclamp・出力変換は上位の責務とする。行列は入力領域を拡大せず、定数項で透明pixelに色やalphaを生成する場合もその有限領域内だけに作用する。

Opacityはfiniteかつ[0,1]で、入力画像のpremultiplied RGBAを一度だけ乗算する。SrcOverはpremultipliedの標準式、PlusはRGBを加算し、alphaだけを上限1へ制限する。Compositeはsourceを指定整数pixel分だけ移動した領域とbackdrop領域の和集合を出力領域とし、各画像の領域外は透明とする。画像位置を失って原点へ揃えることはしない。EffectPixelRectで表す出力は両領域の和集合を含む最小の外接矩形とし、離れた画像間の透明な隙間も含む。後続ColorMatrixの定数項は、その隙間を含む外接矩形全域に作用する。

GaussianBlurは各軸で半径ceil(3×sigma)までの離散Gaussian kernelを正規化し、premultiplied RGBAへ適用する。sigma=0の軸はidentityとする。出力領域は入力領域を各半径分だけ広げ、入力範囲外は透明をsampleする。標準実装は横・縦の分離filterをfragment shaderで実行し、同じsubresourceを同時にsample・attachmentとして使わず、一時画像を往復して処理する。初期Graphics APIにstorage textureの契約がないため、画像へのcompute書き込みを前提にしない。

初期版は近似的な縮小blurを行わない。将来の品質別実装でもsigmaはfull-resolution device pixelを意味し、内部縮小率を利用者の効果サイズへ漏らさない。巨大な領域・sigma・資源制限超過はPrepareで拒否し、黙ってradiusを縮めない。GetRequiredSourceBoundsはblur半径、Compositeの移動と子の必要領域の和集合を逆算する。これは保守的な必要入力領域で、任意の無限領域を割り当てる要求ではない。

DropShadowはSourceのalphaをColorMatrixで着色し、GaussianBlurを通し、元のSourceを前面としてCompositeする組み立てで表す。影の移動はCompositeのsourceOffsetを使い、影をsource、Opacity(0, Source())を透明backdropとする合成を挟んでから、元画像をその前へ合成する。Glowは移動なしの同様の組み立てで表す。3Dの深度・投影行列・履歴などを基本4演算へ強制的に追加しない。TAA等の履歴画像は将来3D側が別frameの入力としてimportし、ビュー単位で無効化する。

並行開発の [Drawing PR #34](https://github.com/ikihiki/Lumyte/pull/34)（確認revision `c6553d80d8929debe3a14fe489fd51841a393874`）はモデル描画とfullscreenの露出・彩度・vignette・Reinhard処理を持つ。本ADRはその公開APIを変更せず、共通化する画像処理と適用位置を上記境界で整理する。取り込み後のadapter・shader共用は別途行い、opaque/straight-alphaの3D出力をpremultiplied画像として誤って渡さない。SceneLinearのトーンマッピング前とDisplayLinearの後を明示し、深度・カメラ・履歴を基本4演算へ埋め込まない。

### 任意の2D描画グループと背景エフェクト

複数のCanvas命令を一つの透明画像へ描いてからエフェクトを適用するレイヤーを追加する。Save/Restoreは状態だけを保存し、画像の分離やgroup opacityを暗黙に発生させない。BeginLayer/EndLayerとDrawBackdropEffectはホストが扱う構造命令で、既存11描画命令の専用interfaceは維持する。構造境界をまたいでバッチを結合しない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed record LayerOptions2D
+    {
+        // BeginLayer時のローカル座標。効果前の内容をこの有限矩形にclipする。
+        public required Rect2D ContentBounds { get; init; }
+        public ImageEffectGraph Effects { get; init; } = ImageEffectGraph.Source();
+        // エフェクトの後にグループ全体へ一度適用。finiteかつ[0,1]。
+        public float Opacity { get; init; } = 1;
+        // 指定時は効果後の出力だけをclip。BeginLayer時のローカル座標で固定。
+        public Rect2D? OutputClip { get; init; }
+    }
+    public sealed record BackdropEffectOptions2D
+    {
+        // 背景への効果を表示する矩形。必要入力のsnapshot領域とは別。
+        public required Rect2D OutputBounds { get; init; }
+        public required ImageEffectGraph Effects { get; init; }
+    }
+    public sealed class Canvas2D
+    {
+        public void BeginLayer(LayerOptions2D options);
+        public void EndLayer();
+        public void DrawBackdropEffect(BackdropEffectOptions2D options);
+    }
+}
```

BeginLayerはoptionsを検証・snapshot化し、変換と親clipを保存する。ContentBoundsは必要な内容を明示する有限の入力領域であり、単なるcullingヒントではない。内容はこの矩形で幾何clipするため、領域外から入る影も必要なら、その元になる描画を含む十分な領域を指定する。変換後のdevice pixel上の外接矩形を外向きに丸めて中間画像の範囲とし、元の矩形による幾何clipも維持する。画面外の内容でも効果が画面内へ届く可能性があるため、入力を最終targetの範囲へ先に切り詰めない。

レイヤー内容はBeginLayer時の変換を引き継ぐ。一方、その時点の親clipは効果後の最終合成へ適用し、入力画像を先に切らない。レイヤー内で追加したClipRect/ClipPathは入力描画へ作用する。したがって、ぼかし前に切りたい形状はレイヤー内部でclipし、効果後に切りたい場合は外側のclipまたはOutputClipを使う。ContentBounds、追加clip、描画形状のAAは既存のcoverage相関契約に従う。ただし相関を保存するのは一つの幾何評価段階内である。入力をRGBA画像へ確定した後の親clip・OutputClipは独立した出力clipであり、効果前後に同じ境界を明示しても一回の幾何clipと同じ結果を保証しない。例えば入力coverage=0.5へ出力coverage=0.5を掛けたalpha=0.25は、この二段階の操作の結果である。

EndLayerは内容の描画、Effects、Opacity、最終clip、親描画先へのSrcOverの順で計画を追加し、BeginLayer時の状態を復元する。最終clipは保存された親clipとOutputClipと描画先範囲の積集合であり、そのcoverageを一度適用する。エフェクトが計算した拡張後の領域をContentBoundsで再度切り取らない。例えば重なる二つの不透明図形へレイヤーOpacity=0.5を適用した場合、重なりもalpha=0.5となる。

Save/Restoreは各レイヤー内で対応を完結させる。内側のRestoreで外側のSaveを消費すること、未完了Saveを残したEndLayer、対応するBeginLayerのないEndLayerは拒否する。Canvas.Endは未完了レイヤー・Saveがあれば拒否し、Cancelは全構造を破棄する。ネストしたレイヤーも同じ規則を使う。空のContentBoundsは出力なしとし、0寸法のGPU画像を確保しない。レイヤーはCPU記録上の木として保持し、実際の入力画像とエフェクトのGPUパスはPrepareで展開する。

EffectsのsigmaとComposite移動はdevice pixel単位で、Canvasの拡大・縮小・PixelScaleから独立する。例えばsigma=4はDPIやCanvas変換にかかわらず出力pixel上で4となる。論理座標で一定幅にしたいアプリはdevice pixelへ明示変換する。非一様拡大でも、初期版のGaussianは出力pixel gridのX/Y軸に沿う。暗黙に回転した楕円kernelへ変換しない。

DrawBackdropEffectは呼び出し位置より前に描かれた、現在のレイヤーの描画先だけを取得する。標準の背景取得はfragment shaderを使用し、対象targetにSampled | RenderAttachment用途を必須とする。準備時に用途を確認し、読出しできないtargetではNotSupportedExceptionとする。アプリは必要な用途を持つオフスクリーン面へ描いてから表示用targetへ合成できる。Rgba8Srgbの背景はsamplingでsRGBをdecodeし、既にpremultipliedのRGBへalphaを再乗算せずRgba16Floatのsnapshotへ描く。異なる形式へのraw copyで色変換を代用しない。Rgba16Floatのレイヤー背景も同じsnapshot形式へ取得する。祖先レイヤー・後続命令・自分自身を読み込まない。OutputBoundsと現在のclipを出力範囲とし、変換後のpixel領域からGetRequiredSourceBoundsでsample領域を逆算する。必要入力領域と置換に使う元背景の出力領域の和集合を別textureへsnapshotしてから処理する。sample領域をOutputBoundsで先に切らず、取得元の有効な描画先範囲外だけを透明として埋める。現在の描画先に有効な内容がなければ取得できないため、初期Clearまたは有効内容のLoadを必須とする。

背景エフェクトの結果は、元の背景へもう一度SrcOverせず、OutputBoundsとclipのcoverageをcとして `result = effect × c + original × (1 − c)` で置き換える。元背景が半透明でもalphaを二重合成しない。読み込みと書き込みの同時使用を避けるため、originalもsnapshot側から読み、attachmentにはblend無効で結果を書く。clip外は保持する。ガラス板の色などはその後に通常の描画命令を重ねる。描画先更新→snapshot→effect→置換→後続描画の依存関係をplanへ宣言し、opaqueかどうかを理由に順番を消さない。

レイヤーの内容を描く際は既存命令別rendererを使い、塗りや外部文字ライブラリの選んだcoverage・mesh・path等をそのまま利用できる。幾何clipや距離場は適切に画像化してから画像filterへ渡し、SDF値自体を色画像としてぼかさない。構造命令の効果はImageEffectRenderersを使うため、例えばGaussianBlurの登録を差し替えるとレイヤーと背景の両方へ反映される。

```csharp
var source = ImageEffectGraph.Source();
var blur = ImageEffectGraph.GaussianBlur(4, 4, source);
var canvas = new Canvas2D();
canvas.Begin();
canvas.BeginLayer(new LayerOptions2D
{
    ContentBounds = new Rect2D(20, 20, 160, 80),
    Effects = blur,
    Opacity = 0.5f
});
canvas.FillRect(new Rect2D(20, 20, 100, 80), new Color2D(1, 0, 0, 1));
canvas.FillCircle(new Vector2(130, 60), 40, new Color2D(0, 0, 1, 1));
canvas.EndLayer(); // 二つの形状を合成→blur→全体opacity→親へ合成。
var drawList = canvas.End();
```

#### 段階的な機能追加と検証

難度はAIが実装する場合のコード生成・統合・画質検証を含む設計上の見積もりで、性能測定値ではない。1は局所的shader、3は複数パス・領域・寿命、5は履歴や描画システム全体の整合を要する規模を表す。

| 機能 | 担当・導入段階 | AI実装難度 | 主な検証点 |
| --- | --- | ---: | --- |
| ColorMatrix・Opacity・SrcOver/Plus | 共通画像処理・初期 | 2 | alpha=0、HDR値、透明領域、group適用 |
| GaussianBlur | 共通画像処理・初期 | 3 | kernel正規化、透明端、出力outset、sigma単位 |
| DropShadow・Glow | 初期4演算の組み立て | 3 | 影の位置・領域、元画像と影の順序 |
| 任意の2D命令のレイヤー化 | Drawing2D・初期 | 3–4 | clip前後、ネスト、AA、opacityの一度適用 |
| 背景ぼかし・背景色調整 | Drawing2D・初期 | 4 | snapshot位置、入力余白、半透明背景、重なるパネル |
| LUT・gamma・畳み込み・画像変形 | 共通画像処理・後続 | 2–3 | 色の意味、sample半径、入力境界 |
| 膨張・収縮・画像輪郭・inner shadow | 共通画像処理・後続 | 3–4 | alpha処理、幾何strokeとの違い、収縮と拡張 |
| Bloom | 共通処理＋上位の適用制御・後続 | 3–4 | HDR輝度、露出・トーンマッピングとの順序 |
| SSAO・被写界深度・TAA | 3D側で構成・対象外 | 4–5 | 投影、深度、速度、履歴、リサイズ・camera切替 |
| パスの破線・trim・線の形状効果 | Drawing2Dの幾何処理・後続 | 3 | 曲線長、cap/join、閉じた輪郭 |

実装時は、重なった図形のgroup opacity、親clipと内部clipのblur差、変換/DPI下のsigma、画面外から入る影、透明背景での背景置換、隣接・重複する背景パネル、scene/display混合の拒否、GPU完了までの一時資源保持を検証する。今のADR更新ではAPIと責務を定め、画質・性能を実測済みとは扱わない。

### 外部描画形式の契約

DrawImageとImagePatternはalpha表現を尊重し、sRGB texelをdecodeして線形RGBのプリマルチプライド値で補間する。Straight画像を補間してからalphaを乗算する処理で代用しない。必要ならtapごとの処理や内部変換を使う。Premultiplied入力へ再度alphaを掛けない。入力画像は表示用の色であり、coverage・距離場textureとは区別する。

DrawCoverageMaskのcoverageはsampling後の[0,1]値を使う。DrawDistanceFieldではSDFはR、MSDFはRGBの中央値を輪郭距離とし、MTSDFはRGBを輪郭、Aをtrue signed distanceの情報として保持する。距離range・符号・paddingの規約を供給側と一致させ、画面上のpixel幅でAAを評価する。sampling kernelが隣のatlas領域を読まないpaddingとsource範囲は供給側が確保する。

DrawMeshは入力のCoverageを補間する。境界用頂点がないmeshに自動でAA済みの品質を仮定しない。paintは変換前の論理座標で評価し、変換とclipは他の描画命令と同じ意味を持つ。

単色、gradient、画像patternを含むgraphの結果を、形状とclipとの相関を保ってcoverageで覆い、Canvasへsource-over合成する。一般のCompositeModeではcoverageとopacityを混同せず、Clear/Src/DestIn等の境界で必要なdestination側の処理も仕様に従う。

### コマンド、描画順、クリッピング

CanvasはBegin中だけ操作できる。Endは未対応のSaveまたはBeginLayerがある場合に失敗し、記録を維持する。入力不備はコマンドを追加せず、状態を変更しない。End成功またはCancel後は新しい記録を開始できる。描画コマンドは変換、clip、不変paintへの参照、stroke設定をsnapshotにし、後続の設定変更の影響を受けない。paintが借用する画像の内容とGPU寿命は外部リソースの契約に従う。PathBuilderはMoveTo前の線・曲線・Closeを拒否する。

描画順は記録順で固定する。バッチ化は連続し、pipeline、texture、sampler、clip、合成設定が一致するコマンドに限る。透明度やtextureを理由に並べ替えない。標準実装では画像はquad、図形のパスは共通のcoverage処理へ変換し、既存のtyped bufferとshader引数で渡す。上限を超えるバッチは順序を維持して分割する。

空の図形、空のclip、変換後に面積を持たない図形は描画しない。矩形clipは変換後に軸平行で整数pixel境界となる場合にscissorを使用できる。小数境界、回転・せん断を含む場合はそのclipを使用する描画命令の登録実装がcoverageを評価する。別の命令実装へ自動で切り替えない。scissorは描画先の範囲内に収め、空範囲にはGPU命令を発行しない。複雑なclipは形状の積集合として評価する。同一の幾何評価段階では、独立に平均化したcoverageの単純乗算で同じ境界へAAを二重適用しない。画像化を挟んだエフェクト前後のclipは後述の独立した演算契約に従う。maskとpaint graphの合成用中間面はgraphへ論理宣言し、生成と利用を別passに分けてアクセスを宣言する。パスclip非対応の実装段階ではNotSupportedExceptionを返し、境界矩形への置き換えは行わない。

曲線を分割する実装では、変換とPixelScaleを適用した画面上の誤差に基づいて分割する。アンチエイリアスは登録実装が契約の精度を満たすcoverageを生成する。形状とclipの相関を保つサンプル評価または幾何的な交差処理を使う。現行texture契約にないMSAAやresolveを必須にしない。最大誤差、分割数、maskサイズの具体的な上限は実装前の後続判断で定め、超過時に黙って品質を下げない。

### 座標、色、合成

左上原点、右がX正方向、下がY正方向の論理座標とする。Canvasの行列適用後にPixelScaleを掛け、RenderTargetBinding2D.PixelOriginを引いてtarget内pixel座標にし、その実寸法でclip spaceへ変換する。すなわちtargetPixel = Transform(local, CanvasTransform) * PixelScale - PixelOriginである。単独ルートtargetのPixelOriginは0で、ネストした中間面もルート座標の原点を保持する。回転はこの座標系で正の値が時計回りとなる。非整数の座標も保持し、pixelへの丸めはscissor範囲の計算に限定する。

色はAPIのsRGB入力を線形へ変換し、プリマルチプライド値でpaintを評価する。画像はtexelごとにdecodeし、Straight入力の場合だけalphaを掛けてから補間する。最終出力は形状・clipのcoverageを反映し、alphaを二重に乗算しない。通常合成はsource-overで、RGBとalphaともsource factor=One、destination factor=OneMinusSourceAlphaを使用する。sRGB targetへのencodeはattachmentに任せる。clear値も線形RGBへ変換して渡す。

初期出力は透明またはプリマルチプライドのdisplay-linear合成結果を、Rgba8SrgbへencodeするかRgba16Floatへ線形格納したtargetとする。Loadを使う場合、既存内容も同じ表現を満たす必要がある。自身のオフスクリーン出力を画像として再利用する際はImage2Dのalpha表現を明示する。COLRv1用のグラデーションとgraph内部の合成は下記で規定する。HDR、広色域、Canvas全体の追加blend modeは別途設計する。

### GPU実行、所有権、エラー

単独のPrepareAsync経路では、アプリケーションが全入力画像・mask・距離場をupload済みのSampled state、targetをColorAttachment stateにしてからRecordを呼ぶ。私有graphはその入口状態をimportし、targetをColorAttachment、入力をSampledとしてexportする。入力の入口scopeはVertexShader | FragmentShader | ComputeShaderとShaderRead、targetはColorOutputとColorRead | ColorWriteとする。利用者は外部producerからこれらのstageまで可視化しておく。単に最後のread stageだけを宣言して、以前のwriteの可視性まで補えるとは扱わない。共有graphへのAppendPassesAsync経路では、利用者が実際の入口状態・scopeで一度だけimportし、DrawingGraphBindings2Dで対応を渡す。途中の遷移と依存関係はgraphが管理し、外部の後続用途へのexportは利用者が指定する。既にread-only状態でimportする場合は、graphの最初の利用stageまでの外部producerとの依存を利用者が満たす。submission間の同期、Finish、Submit、Presentは常にアプリケーションが担当する。

各命令実装はupload・描画buffer・mask・合成面のcopy/compute/render passを事前宣言する。通常のpass間barrierはRenderGraphが生成する。ホストは最初のLoadOp/ClearColorを一度だけ適用するpassを宣言し、以後のtarget合成はLoad/StoreとColorRead | ColorWriteを使用する。callbackは開いたpassを残さない。PR #35の追跡単位はallocation全体なので、同一allocationを同一pass内でsampleとattachmentに使わない。背景効果には別のsnapshotを使う。

単独利用順は「Canvas.End → PrepareAsync → 外部barrier → Record → 必要な後続barrier → commands.Finish → Queue.Submit → 明示完了確認 → commands.Dispose → PreparedDraw2D.Dispose → submission.Dispose」とする。PreparedDraw2DはCompiledRenderGraphとPreparedDrawingPasses2Dを所有し、前者が論理一時texture/bufferと生成view、後者がbatchのCPU snapshot、pipeline、argument table、upload、importした私有shader-data等を所有する。共有graphでは利用者が両ownerを保持する。graphの一時資源をbatch側から破棄しない。

Record前は準備結果を破棄できる。記録後に送信を取りやめる場合やRecordが失敗した場合は、command bufferを先に破棄してからplanと準備ownerを解放する。送信後は利用者が全使用submissionのGPU完了を確認する。WaitAsyncのキャンセルはGPU処理のキャンセルを意味しない。Failedという状態名だけでGPU使用終了を推測せず、backendの終了保証に従う。外部処理でplanの出力を再利用した場合は、その使用完了までplanも保持する。

[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)に従い、DisposeはGPU完了照会・waitを行わず、Pendingや外部resourceの寿命違反を必ず拒否する保証を設けない。利用者がcommand→plan→準備owner→renderer→DI scopeの順序と各GPU使用終了を保証する。明示的なStatus/WaitAsyncはアプリケーションのframe管理で使える。検証専用のlive childカウンターやSubmit時の参照先再走査は追加しない。

画像・mask・距離場・targetは借用であり、利用者はviewだけでなく親texture、sampler、登録先tableもPrepareからGPU使用終了まで維持する。viewの保持は親textureのGPU寿命を保証せず、viewを先に解放する。SurfaceのAcquire・Submitのsemaphore・Present・frameの提示解放完了はアプリケーションが管理し、2D本体はSurfaceへ依存しない。GPU実行の完了と提示側の解放完了を混同しない。

Canvas、Renderer、準備owner、共有graphへの同時呼び出しは保証しない。CPUコマンド列は不変として共有できるが、async準備が完了するまでRendererを維持し、GPUアクセスと解放は利用者が同期する。初期実行は一つの一般queueとcommand bufferを使い、内部Submitや別queueを必要としない。shader-data copyのCPU metadataも同じcommand内のoverlayを利用する。複数commandへ分ける場合は、転送のSubmit成功後に後続のshader-data参照を記録するという既存Graphics契約を守る。

数値・範囲違反はArgumentExceptionまたはArgumentOutOfRangeException、自身の局所的な呼び出し順違反はInvalidOperationException、自身の破棄後操作はObjectDisposedException、非対応format・機能はNotSupportedExceptionとする。未知のenum、CPU snapshot、graph生成に必要な範囲・容量は準備時に検査する。外部resourceの生存、同一device、mapping排他、宣言と実アクセスの一致、実際の入口stateは利用者の事前条件で、検出保証とは分ける。native確保・記録中の失敗は伝達し、部分記録されたcommandとplanは再使用しない。暗黙のsubmit、wait、失敗した描画のスキップは行わない。

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

内部面、render/compute pass分割、copyと資源アクセスをRenderGraphへ宣言する。アプリケーションへの追加submitやGPU待機は要求しない。中間面・maskはCompiledRenderGraphが所有する。最大面積、深さ、評価数、総メモリの上限をPrepareで検証し、超過時に単色化・レイヤー省略で代用しない。

参考: [OpenType COLR — Color Table](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)、[CPAL — Color Palette Table](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)、[Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)。

## 検討した代替案

- 2Dと3Dで画像エフェクトを別実装にする: blur・色変換・画像合成の画質と資源管理が重複するため、画像処理をImageEffectsへまとめ、適用対象・位置は各描画システムに残す。
- paint DAGへ任意のCanvas命令やカメラ・履歴を統合する: 塗り、描画グループ、画像処理の意味が混在するため、PaintGraph2D、レイヤー構造、ImageEffectGraphを分ける。
- GPU処理を自由なRecord callbackだけで隠す: RenderGraphが入出力・依存・一時資源を解析できないため、標準実装はpassとアクセスを公開する。限定したopaque経路でも全アクセス宣言を必須にする。
- RenderGraphとは別の汎用PassPlanningと実行器を作る: PR #35の公開契約と機能が重複するため、直接接続する。単独描画の便利APIは私有graphで実現する。
- 初期版からsubresource最適化・memory alias・async computeを必須にする: 現RenderGraphのwhole-allocation・単一queueで要求を満たせるため、後続の最適化として分離する。
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

2Dと3Dが共通の画像演算とRenderGraphを使用でき、CPU前処理からcomputeへの命令実装の差し替えも、pass宣言を変えることで行える。任意の2D描画グループと背景効果を扱える一方、領域・alpha・clip順序、追加失敗時のgraph全体再構築、prepared ownerとcompiled planの二つの寿命を管理する必要がある。

GPU実装は未取り込みのRenderGraph PR #35の採用・契約に依存する。現時点のwhole-allocation同期では一部の並列性とsubresource再利用を制限し、shader-dataは準備側の確保・importが必要となる。既存Drawing PR #34の移行やRenderGraph自体の実装・変更は、この文書更新に含めない。

図形の塗り、線、mesh、文字ライブラリから渡すcoverage・距離場で同じpaintを共有できる。画像patternとgroup opacityによりUIの背景や装飾も描画できる。一方、単純な矩形実装も指定されたpaintの評価が必要になり、複雑なgraphでは中間面と複数passを要する。画像のtap別tile処理と正しいalpha補間は単純なtexture samplingより実装・検証コストが増える。

ゲーム、UI、図形描画はbackendに依存せず同じCanvasを使用できる。描画順を維持するため結果を予測でき、GPU送信は他の描画処理とまとめられる。一方、texture切り替えの多い描画では全体ソートよりバッチ数が増え、複雑なclipとCOLRv1ではmask・隔離合成のpassとメモリが必要になる。文字は輪郭を共有して拡大に対応できる一方、単色atlas方式よりパス評価と合成のコストが増える。

CPUコマンド記録、GPU準備、記録、完了の境界を持つため、即時描画より呼び出し手順が増える。上位アプリケーションはframeごとの所有者にその手順を集約できる。既存Graphicsの契約は変更せず、新規ライブラリとそのshader・サンプル・テストを追加する。

専用interfaceと型付き登録表により、描画中のDI解決と型の辞書検索を避けられる。一方、命令追加時は専用interface、コンストラクター、dispatch、標準実装、CreateDefaultとUseGraphics2Dの登録を同時に追加する必要があり、登録表のコンストラクターも変更になる。登録用パッケージを分離するため、本体と併せたバージョン管理が必要になる。

## 検証方針

RenderGraph接続では、同じDrawListの単独Prepare経路と共有graphへのAppend経路の画像一致、全資源アクセス宣言、Loadによる前内容の保持、部分更新producerのculling防止、同allocationの重複import防止、入力bindingのsnapshot、snapshot前後の描画順を確認する。純粋な未使用中間出力は除去でき、Export/KeepAlive/DependsOnの役割を混同しないことも検証する。Append・Compile・Recordの各失敗、キャンセル、GPU完了後のcommand→plan→owner解放を確認し、寿命違反の検出を前提にした試験にはしない。

画像エフェクトでは、4専用interfaceと標準登録・CreateDefaultの対応、UseGraphics2DによるUseImageEffectsの補完、前後のAddScopedと再登録、DIなしの手動構築、2D/3Dが同じGaussian実装を使うことを確認する。共有DAGの評価、影のoffset、blurの入力・出力余白、ColorMatrixの有限領域とHDR値、scene/displayの区別、レイヤーのSave/Restore、透明背景での置換をCPU参照画像と比較する。初期fragment経路は将来compute版と同じ画質契約で比較する。

塗りの検証では6種類の図形・線命令でColor2D版とSolid版が同じ結果になり、全paint種別を同じ専用interfaceへ渡せることを確認する。2点/3点linearと単一中心/2円radialの等価条件、複数stop、Repeat/Reflect、変換前のpaint座標、形状boundsへ自動伸縮しないことを確認する。shape・Canvas clip・graph Clipに同じ境界を使ってもAAが二重に薄くならないこと、Opacity(0.5)を適用した不透明Layersの重なりもalphaが0.5になること、全体opacityと子ごとのalpha指定が区別されることを画像比較する。

画像patternはatlas隣接領域に強い別色を置き、全tile mode、X/Y別指定、負座標、1texel幅、切り出し、回転・拡大縮小、Nearest/Linear、Decal境界を検証する。透明texelに任意のRGBを置いても色が漏れないこと、Straightと等価なPremultiplied入力の結果が一致すること、sRGBと線形formatを正しく扱うことを確認する。既存DrawImageのalpha補間も含める。source範囲違反、特異行列、graph内部のtarget自己参照をRecord前に拒否し、borrowed viewと内部変換面をGPU完了まで維持することを確認する。

CreateDefaultはDIパッケージとdeviceなしで呼び出せること、全11propertyの実装型がUseGraphics2Dの標準登録と一致すること、複数回の呼び出しで表と各実装が別instanceになること、生成時にGPU資源を確保しないことを確認する。DI経路の外部登録がCreateDefaultで無視されず、コンストラクターへ届くことも検証する。

DIの検証ではUseGraphics2Dだけで11標準実装とDrawingCommandRenderers2Dを解決できること、後続のAddScopedで指定した最後の実装が使われること、UseGraphics2Dの再呼び出しや先行する外部登録で意図した実装が保持されることを確認する。provider構築後の登録変更が既存provider・Rendererへ波及しないこと、null引数の拒否、IGraphicDeviceの登録なしで登録表を解決できることも確認する。

11専用interfaceへの配送をそれぞれ確認し、FillPathだけを外部実装へ置換した場合にStrokePathやDrawPaintGraphへ影響しないことを検証する。FillPath → FillRect → FillPathの順序とバッチ境界、描画中のDI解決・型の辞書検索がないこと、未対応入力・device機能不足で別実装を呼ばないこと、公開snapshotだけで外部実装できることを確認する。CPU経路とcompute経路は同じfixtureで比較し、順序、clip境界、合成、許容誤差を一致させる。小さなバッチが多い条件でもCPU準備時間と割り当て量を測定し、専用interface化の効果を評価する。

実装時は共通APIを使うsample/testを用意し、backend固有device生成はbootstrapへ置く。CPUテストでは変換順、Save/Restore、snapshot、不正入力後の状態、fill rule、stroke join、バッチ境界を確認する。

GPU画像比較では半透明図形の前後入れ替え、sRGBとalpha合成、ネストしたclip、回転矩形clip、パスの穴、曲線拡大、高DPI、パス文字、Load/Clearを確認する。三角形の共有境界に二重合成や隙間がないことを含め、色空間と誤差許容値を固定する。画像転送・readbackは既存の明示的なcopyと完了待機を使用する。

COLRv1の検証では固定したfont fixtureと軸・palette・foregroundで参照描画を比較する。単色とCOLRv0相当のlayers、全gradientとextend、重複stop、3点linear、偏心radial、sweep境界、nested transform、PaintColrGlyph、variable paint/clipを含める。全28の合成modeを半透明・透明入力と隔離backdropで検証し、sRGB入力からの線形化、線形光RGBでの補間・合成、外側Canvasへの合成を別々に確認する。空のsource、Clear/Src、HSL、clip境界、共有DAG、循環参照拒否、コスト上限超過、複数拡大率での輪郭再評価を受け入れ条件にする。

Recordがsubmit/waitを呼ばないこと、入力不備による部分記録がないこと、明示完了後の解放順、未送信時の破棄、複数frameをGPU完了前に準備した場合のbuffer非上書きを確認する。代表的なスプライト、UI、パス描画でCPU準備時間、GPU時間、draw call数、転送byte数、割り当て量、mask使用量を測定し、backendごとの対応範囲とともに記録する。測定前に特定のframe rateを保証しない。

## 別途決定する事項

- RenderGraph側のsubresource/byte範囲、pool/alias、パス統合、複数queue、GPU timestampとcompile cache。storage textureを使うcompute画像処理にはGraphics APIの追加設計が必要。
- LUT、畳み込み、画像変形、morphology、Bloom、履歴効果の具体APIと品質契約、Drawing PR #34とのadapter・shader共用。初期4演算と2Dレイヤー・背景効果の契約は本ADRで定める。
- パスの三角形化アルゴリズム、誤差・複雑度上限、mask cache戦略。
- text shaping、フォントfallback、OpenType outline/COLRv1 adapterのライブラリと公開API。
- HDR、広色域とCanvas全体の追加合成。
- シーングラフ、パス/paintの描画cacheと任意のatlas最適化、複数threadでの準備、frame pool。

これらを具体化し、対応する判断を採用してから実装を進める。
