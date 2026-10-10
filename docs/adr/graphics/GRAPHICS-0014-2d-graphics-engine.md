# ADR-GRAPHICS-0014: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 提案
- 日付: 2026-10-10

## 背景

ゲーム内のスプライト、UI、図形、文字を共通の2D描画APIで扱いたい。利用者が図形ごとにGPU buffer、shader、描画状態を組み立てると、backend間の違い、クリッピング、半透明の描画順、GPU使用中のリソース寿命を各機能で重複して管理することになる。

Lumyteには既に [Graphics device](GRAPHICS-0001-graphics-device.md)、[CommandBufferとSubmission](GRAPHICS-0007-command-buffers-and-submission.md)、[描画状態](GRAPHICS-0008-pipeline-programs-and-render-state.md)、[shader引数](GRAPHICS-0009-shader-argument-binding.md)、[indexed draw](GRAPHICS-0011-indexed-and-indirect-commands.md) の契約がある。2D描画はこれらの上に構築し、独立したGPU backend抽象化は追加しない。

本ADRの主要な判断は、即時型のCanvas APIでCPUコマンド列を生成し、描画順を保持して既存Graphics APIへ変換することである。公開境界と、この判断に必要な状態・所有権の契約を定める。実装済みの機能や性能を表すものではない。

## 決定

### 適用範囲と責務

`src/Graphics/Lumyte.Graphics.Drawing2D` にC#ライブラリを配置する。プロジェクト名、NuGet名、名前空間は `Lumyte.Graphics.Drawing2D` とする。実行時にはBCLと `Lumyte.Graphics.Abstractions` を参照し、backendの具象型、Window、Input、Compositionへ依存しない。shader成果物は既存のSlang・shader生成基盤で用意する。

構成は「アプリケーション → Canvas2D → DrawList2D → Renderer2D → IGraphicDevice」とする。CanvasはCPU側の変換・クリップ・描画コマンドを記録し、Rendererは三角形化、連続コマンドのバッチ化、転送用リソースとGPU描画命令を組み立てる。deviceの生成・破棄、queue送信、完了待機、画面への表示はアプリケーションが所有する。

矩形、角丸矩形、円、線、ベジェ曲線を含むパス、画像、パスとpaint graphで表す整形済みグリフ、オフスクリーン描画を対象とする。最初に矩形・画像・変換・矩形クリップを実装し、その後にパスとグリフ描画を追加する。シーングラフは必要な利用者がCanvasの上に構築し、必須にしない。レイアウト、ヒットテスト、画像デコード、フォント探索、文字列のシェーピング、行分割は本ライブラリの責務に含めない。

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
+    public sealed class Path2D { }
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
+    // 単一mip/layerのSampled D2 viewとsamplerを借用。生成時に同一deviceを検証。
+    // Rgba8UnormSrgbの画像はRGB=sRGB、alpha=linearのstraight alphaに限定する。
+    public sealed class Image2D
+    {
+        public Image2D(IGraphicsTextureView view, IGraphicsSampler sampler);
+        public IGraphicsTextureView View { get; }
+        public IGraphicsSampler Sampler { get; }
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
+    public enum PaintColorSpace2D { LinearSrgb, Srgb }
+    // offsetはfinite。入力順は非減少、同一offsetのstopも順序を維持する。
+    public readonly record struct ColorStop2D(float Offset, Color2D Color);
+    // パスの集合とpaintのDAG。生成後は不変、子列とstop列はコピーする。
+    // constructorを非公開とし、factoryのみで生成して循環を防ぐ。
+    public sealed class PaintGraph2D
+    {
+        // 無限平面のpaint。実際の評価範囲はpath、clip、targetで制限する。
+        public static PaintGraph2D Solid(Color2D color);
+        // p0/p1/p2はCOLRv1の3点定義。p2を省略して2点へ縮約しない。
+        public static PaintGraph2D LinearGradient(Vector2 p0, Vector2 p1, Vector2 p2,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 2円定義。radiusはfiniteかつ0以上。単一中心へ縮約しない。
+        public static PaintGraph2D RadialGradient(Vector2 c0, float r0, Vector2 c1, float r1,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // angleはradian、+X基準、Y下向きで時計回り。入力adapterで変換する。
+        public static PaintGraph2D SweepGradient(Vector2 center, float startAngle,
+            float endAngle, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
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
+    // shaping済みグリフのgeometryとpaintを配置する。advanceは上位層が解決済み。
+    public readonly record struct GlyphPaint2D(PaintGraph2D Graph, Matrix3x2 Transform);
+    public sealed class GlyphRun2D
+    {
+        // glyphsをコピー。GPU atlasやフォントオブジェクトを借用しない。
+        // COLRv1はSrgb、通常の線形描画はLinearSrgbを明示する。
+        public GlyphRun2D(ReadOnlySpan<GlyphPaint2D> glyphs, PaintColorSpace2D colorSpace);
+    }
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
+        public void FillRect(Rect2D rect, Color2D color);
+        // radiusはfiniteかつ0以上。描画時に短辺の半分まで縮める。
+        public void FillRoundedRect(Rect2D rect, float radius, Color2D color);
+        public void FillCircle(Vector2 center, float radius, Color2D color);
+        public void DrawLine(Vector2 start, Vector2 end, Color2D color, StrokeStyle2D style);
+        public void FillPath(Path2D path, Color2D color, FillRule2D rule = FillRule2D.NonZero);
+        public void StrokePath(Path2D path, Color2D color, StrokeStyle2D style);
+        // sourceはview内のtexel座標、destinationは論理座標。
+        public void DrawImage(Image2D image, Rect2D source, Rect2D destination);
+        // 不変graphを記録。内部の合成はcolorSpaceで評価し、CanvasへSrcOver合成。
+        public void DrawPaintGraph(PaintGraph2D graph, PaintColorSpace2D colorSpace);
+        // colorはgraph内で解決済み。glyph変換→origin平行移動→Canvas変換の順。
+        public void DrawGlyphRun(GlyphRun2D run, Vector2 origin);
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
+        public Renderer2D(IGraphicDevice device);
+        // CPU三角形化・バッチ化と内部upload bufferへの書き込みのみ。
+        // submit/waitは行わない。キャンセル時は内部確保を解放する。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 生存するPreparedDraw2Dがあれば拒否。暗黙のGPU完了待機なし。
+        public void Dispose();
+    }
+    public sealed class PreparedDraw2D : IDisposable
+    {
+        // 一度だけ。Recordingかつactive passなしの同一device commandに記録する。
+        // 内部upload/copy/barrierと専用render passを記録し、passをEndする。
+        public void Record(IGraphicsCommandBuffer commands);
+        // Record先を含むSubmitの直後に呼ぶ。submissionは借用、一度だけ。
+        public void TrackSubmission(IGraphicsSubmission submission);
+        // 下記の寿命契約に従い内部GPUリソースを解放。idempotent。
+        public void Dispose();
+    }
+}
```

### コマンド、描画順、クリッピング

CanvasはBegin中だけ操作できる。Endは未対応のSaveがある場合に失敗し、記録を維持する。入力不備はコマンドを追加せず、状態を変更しない。End成功またはCancel後は新しい記録を開始できる。描画コマンドは変換、clip、色、stroke設定をsnapshotにし、後続の変更の影響を受けない。PathBuilderはMoveTo前の線・曲線・Closeを拒否する。

描画順は記録順で固定する。バッチ化は連続し、pipeline、texture、sampler、clip、合成設定が一致するコマンドに限る。透明度やtextureを理由に並べ替えない。画像はquad、図形とグリフのパスは共通のcoverage処理へ変換し、既存のtyped bufferとshader引数で渡す。上限を超えるバッチは順序を維持して分割する。

空の図形、空のclip、変換後に面積を持たない図形は描画しない。矩形clipは変換後も軸に平行な場合にscissorへ変換し、回転・せん断を含む場合はパスclipとして扱う。scissorは描画先の範囲内に収め、空範囲にはGPU命令を発行しない。複雑なclipはcoverage maskの積集合で処理する。maskとpaint graphの合成用中間面はrenderer内部で所有し、生成と利用を別passとbarrierで区切る。パスclip非対応の実装段階ではNotSupportedExceptionを返し、境界矩形への置き換えは行わない。

曲線の分割は変換とPixelScaleを適用した画面上の誤差に基づく。基本のアンチエイリアスは境界coverageで行い、clip maskのcoverageも乗算する。現行texture契約にないMSAAやresolveを必須にしない。最大誤差、分割数、maskサイズの具体的な上限は実装前の後続判断で定め、超過時に黙って品質を下げない。

### 座標、色、合成

左上原点、右がX正方向、下がY正方向の論理座標とする。Canvasの行列適用後にPixelScaleを掛け、targetの実寸法でclip spaceへ変換する。回転はこの座標系で正の値が時計回りとなる。非整数の座標も保持し、pixelへの丸めはscissor範囲の計算に限定する。

色はAPIのsRGB入力を線形へ変換し、alphaとcoverageを掛けたプリマルチプライド値としてshaderから出力する。画像のRGBはsRGB viewでdecodeした後にalphaを掛ける。通常合成はsource-overで、RGBとalphaともsource factor=One、destination factor=OneMinusSourceAlphaを使用する。sRGB targetへのencodeはattachmentに任せる。clear値も線形RGBへ変換して渡す。

初期出力は透明またはプリマルチプライドの線形合成結果をsRGB格納したtargetとする。Loadを使う場合、既存内容も同じ表現を満たす必要がある。自身のオフスクリーン出力を画像として再利用する際はstraight alpha画像と区別する必要があるため、その入力型・変換契約は後続ADRで追加する。COLRv1用のグラデーションとgraph内部の合成は下記で規定する。HDR、広色域、Canvas全体の追加blend modeは別途設計する。

### GPU実行、所有権、エラー

アプリケーションは入力画像をupload済みかつSampled stateにし、targetをColorAttachment stateへ遷移させてからRecordを呼ぶ。外部リソースの前後のbarrierとsubmission間の同期はアプリケーションが管理する。rendererは自分のupload・描画buffer・mask・合成用中間面に必要なcopyとbarrierだけを記録する。同じtarget subresourceを同時に画像として読む操作は拒否する。

利用順は「Canvas.End → PrepareAsync → 外部barrier → Record → 必要な後続barrier → commands.Finish → Queue.Submit → TrackSubmission → 完了確認 → commands.Dispose → PreparedDraw2D.Dispose」である。RecordはtargetをStoreして終了する。queue送信やPresentを内部で呼ばず、既存のオフスクリーンtargetで使用できる。未採用のSurface設計を前提にしない。

PreparedDraw2Dは内部リソースを所有する。Record前は破棄可能。Record後に送信を取りやめる場合は、記録先command bufferを先に破棄してから解放する。送信した場合はそのsubmissionを必ずTrackSubmissionで関連付け、Pending中のDisposeを拒否する。関連付け前でも記録先が生存する間はDisposeを拒否する。完了後も記録先を先に破棄してから内部リソースを解放する。submissionはこの確認まで生存させる。FailedではGPU使用終了が確認できる既存backendの契約に従い、状態名だけで安全な解放を推測しない。

画像・targetは借用し、Prepareから記録、送信、GPU完了まで呼び出し側が生存させる。DrawListの保持だけではリソースのDisposeを防がない。グローバルなResourceManager、世代管理、ファイル読み込みは追加せず、その仕組みが必要なアプリケーションから供給する。

Canvas、Renderer、PreparedDrawの同時呼び出しは保証しない。CPUコマンド列は不変として共有できるが、GPUリソースのアクセスと解放は利用者が同期する。asyncの内部upload処理が終わる前のrenderer破棄は拒否する。

数値・範囲違反はArgumentExceptionまたはArgumentOutOfRangeException、呼び出し順違反はInvalidOperationException、破棄済みリソースはObjectDisposedException、非対応format・機能はNotSupportedExceptionとする。未知のenum値も拒否する。通常の入力不備はRecord前に全体検証する。native記録中の失敗は例外で報告し、部分記録されたcommand bufferとPreparedDrawは再使用せず破棄する。暗黙のsubmit、wait、失敗した描画のスキップは行わない。

### 文字をパスとpaint graphとして描画する

文字の基本表現はパスの集合と、それを塗る不変のpaint DAGとする。単色グリフは `Clip(outline, Solid(color))`、カラーグリフは複数のpath clip、gradient、transform、layers、compositeから構成する。GlyphRun2Dは配置済みgraph列を保持し、Canvasの通常のパス描画と同じcoverage処理を使う。atlas quadを必須の入力としない。ラスターcacheは最適化として追加できるが、拡大・変形時も元のパスとpaintへ戻れる必要がある。

文字列は上位の文字処理層で「フォントfallback → shaping → 行配置 → outline/COLR paint解決 → GlyphRun2D」に変換する。日本語、合字、右から左への表記、advanceとoffsetはその層で解決する。OpenTypeの輪郭はfont unitsから論理座標へ変換し、Y上向きからY下向きへの反転もadapterで行う。paintの座標、clip box、回転・skewの角度も同じ変換を適用する。輪郭の穴はNonZero fillと元のwindingで保持する。

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

ColorLineのstop順、重複位置、Pad/Repeat/Reflect、端点と退化条件はOpenType COLR仕様に従う。offsetを一律[0,1]へ丸めたり、stopを重複除去したりしない。3点linearの投影、中心の異なる2円radialの解と描画有効領域、sweepの角度境界を保持する。特異な変換・退化gradientは仕様に定義された結果を実装し、未対応段階ではPrepareでNotSupportedExceptionを返す。別のgradientへの近似で成功扱いにしない。

COLRv1のgraph内部はSrgbモードで評価する。stopの補間はsRGB成分とalphaをプリマルチプライドにした空間で行い、CompositeModeの演算もCOLR仕様が参照するcompositing/blending規則に従う。LinearSrgbモードは通常の図形向けに線形成分で評価する。非分離HSL modeでは指定された非プリマルチプライド成分の演算と再乗算を行い、単なるRGB成分ごとの演算へ置き換えない。alpha=0での色成分とPlus等の範囲処理も参照規則に合わせる。

Srgb graphの評価面はsRGB成分をそのまま保持する線形formatの内部textureを使い、hardwareのsRGB decode/encodeを途中に挿入しない。最終graphの結果だけをunpremultiply → sRGB decode → premultiplyして通常のCanvasの線形合成へ渡す。alpha=0ではRGB=0とする。中間面にはRgba16Floatを優先し、必要なattachment/sample用途が未対応の場合はPrepareで拒否する。Rgba8への暗黙の精度低下は行わない。

Compositeのsourceとbackdropはそれぞれ独立した透明面に評価する。backdropはgraphの子であり、Canvasに既に描かれた内容ではない。子同士を合成した結果を一つの描画として外側へSrcOver合成する。Layersもgraph内部の順序で評価し、nested compositeを外側の描画列へ平坦化しない。graphと中間面境界を越えたバッチ化を禁止する。

Clear、Src、DestIn等では透明領域も結果へ影響するため、単なる輪郭のunionを評価領域にしない。現在のclipとtargetで定まる有限領域を正しい評価範囲とし、boundsを狭める最適化は各演算の透明領域の意味を保存できる場合だけ行う。clip maskのcoverageはgraphの指定箇所で適用し、graph全体の再合成時に二重乗算しない。

内部面の生成、render pass分割、copy、Sampled/ColorAttachment遷移はRecordへ組み込む。アプリケーションへの追加submitやGPU待機は要求しない。中間面・maskの寿命はPreparedDraw2Dに属する。最大面積、深さ、評価数、総メモリの上限をPrepareで検証し、超過時に単色化・レイヤー省略で代用しない。

参考: [OpenType COLR — Color Table](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)、[CPAL — Color Palette Table](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)、[Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)。

## 検討した代替案

- 描画ごとにGPUへ送信する: 小さな描画で送信回数が増え、アプリケーションのcopyや他のrender passとの同期を組み立てにくいため採用しない。
- textureごとにコマンドを並べ替える: 半透明の重なりとUIの前後関係が変わるため採用しない。連続コマンドだけをまとめる。
- グリフを単色atlasまたは色付きpathの平坦な列に限定する: COLRv1のgradient、参照、隔離合成を保持できないため採用しない。パスにpaint DAGを組み合わせる。
- シーングラフを必須にする: 単純なデバッグ描画にもノード管理が必要になり、UIやゲーム側の既存階層と重複するため採用しない。
- 2D専用のGPU backendを作る: device、shader、buffer、同期の既存契約を重複させるため採用しない。
- 文字列処理・ファイル資産管理をCanvasへ統合する: 描画記録と非同期I/O、フォントfallback、キャッシュ世代の責務が混在するため、配置済み描画データを受け取る。

## 結果と影響

ゲーム、UI、図形描画はbackendに依存せず同じCanvasを使用できる。描画順を維持するため結果を予測でき、GPU送信は他の描画処理とまとめられる。一方、texture切り替えの多い描画では全体ソートよりバッチ数が増え、複雑なclipとCOLRv1ではmask・隔離合成のpassとメモリが必要になる。文字は輪郭を共有して拡大に対応できる一方、単色atlas方式よりパス評価と合成のコストが増える。

CPUコマンド記録、GPU準備、記録、完了の境界を持つため、即時描画より呼び出し手順が増える。上位アプリケーションはframeごとの所有者にその手順を集約できる。既存Graphicsの契約は変更せず、新規ライブラリとそのshader・サンプル・テストを追加する。

## 検証方針

実装時は共通APIを使うsample/testを用意し、backend固有device生成はbootstrapへ置く。CPUテストでは変換順、Save/Restore、snapshot、不正入力後の状態、fill rule、stroke join、バッチ境界を確認する。

GPU画像比較では半透明図形の前後入れ替え、sRGBとalpha合成、ネストしたclip、回転矩形clip、パスの穴、曲線拡大、高DPI、パス文字、Load/Clearを確認する。三角形の共有境界に二重合成や隙間がないことを含め、色空間と誤差許容値を固定する。画像転送・readbackは既存の明示的なcopyと完了待機を使用する。

COLRv1の検証では固定したfont fixtureと軸・palette・foregroundで参照描画を比較する。単色とCOLRv0相当のlayers、全gradientとextend、重複stop、3点linear、偏心radial、sweep境界、nested transform、PaintColrGlyph、variable paint/clipを含める。全28の合成modeを半透明・透明入力と隔離backdropで検証し、sRGBでの補間・合成と外側Canvasへの線形合成を別々に確認する。空のsource、Clear/Src、HSL、clip境界、共有DAG、循環参照拒否、コスト上限超過、複数拡大率での輪郭再評価を受け入れ条件にする。

Recordがsubmit/waitを呼ばないこと、入力不備による部分記録がないこと、Pending中の解放拒否、未送信時の破棄、複数frameをGPU完了前に準備した場合のbuffer非上書きを確認する。代表的なスプライト、UI、パス描画でCPU準備時間、GPU時間、draw call数、転送byte数、割り当て量、mask使用量を測定し、backendごとの対応範囲とともに記録する。測定前に特定のframe rateを保証しない。

## 別途決定する事項

- パスの三角形化アルゴリズム、誤差・複雑度上限、mask cache戦略。
- text shaping、フォントfallback、OpenType outline/COLRv1 adapterのライブラリと公開API。
- プリマルチプライド画像入力、オフスクリーン再利用、HDRとCanvas全体の追加合成。
- シーングラフ、パス/paintの描画cacheと任意のatlas最適化、複数threadでの準備、frame pool。

これらを具体化し、対応する判断を採用してから実装を進める。
