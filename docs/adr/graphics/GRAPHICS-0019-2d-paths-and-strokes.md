# ADR-GRAPHICS-0019: 線・パスの塗りとstrokeのCPU準備・GPU評価

- 状態: 採用
- 日付: 2026-10-26

## 背景

曲線を含む輪郭の塗りと、cap・joinを持つ線を描きたい。[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md)のclip用pathとpaint・変換・coverageを再利用し、自己交差や穴を保持する必要がある。また、動的パスを将来computeへ移したり、外部のtessellation実装を選んだりしても記録APIを変更したくない。

本ADRはDrawLine、FillPath、StrokePathの追加を扱う。基本段階で導入したPath2DとPathBuilder2Dのモデルは変更せず、曲線分割とstroke展開を命令rendererの準備へ接続する。文字のshaping、dash、path boolean演算、GPUのtile化は対象外とする。

## 決定

### 三つの命令を独立して配送する

| 命令 | CPU準備 | fragment評価 |
| --- | --- | --- |
| DrawLine | 2点のopen pathを作り、共通stroke展開を実行 | stroke三角形のunion内かを判定 |
| FillPath | contourごとに曲線をedge列へ分割 | edgeに対するwindingを計算 |
| StrokePath | 曲線を分割し、線分・cap・joinを三角形へ展開 | stroke三角形のunion内かを判定 |

IDrawLineRenderer2D、IFillPathRenderer2D、IStrokePathRenderer2Dを追加し、DrawingCommandRenderers2Dのコンストラクター・readonly参照・CreateDefault、UseGraphics2D、型別保存領域、Renderer2Dのswitchを拡張する。この段階では基本3命令と合わせて6命令になる。型付きReadOnlyMemoryで連続する命令を渡し、順序とclip境界を保持する。

各実装はpaintとclipを含む描画全体を担当する。IFillPathRenderer2Dを差し替えても、StrokePathの展開やPaintGraph2D.Clipのpath評価は変わらない。CanHandleや方式の自動選択は追加せず、指定されたrendererが未対応条件を準備時に明示的に拒否する。

### Path2Dとcontourの意味

Path2DはMove、Line、Quadratic、Cubic、Closeからなる不変snapshotで、制御点を保持する。Curveを記録時に画面座標の折れ線へ固定しない。複数のMoveは別contourを開始し、Closeは現在contourを始点へ閉じる。Close後に線・曲線が続く場合は閉じた始点から新しいcontourを開始する。

FillPathはopen contourも最終点から始点へ暗黙に閉じて塗る。StrokePathは明示的なCloseの有無で終端処理を変え、closed contourにはcapを付けない。NonZeroはwindingが0以外、EvenOddは交差回数の奇偶で内部を決める。contourの向きと自己交差を保持し、穴を輪郭の外接矩形で置き換えない。

### 曲線を画面上の誤差で分割する

PathFlattening2D.Flattenはlocal座標の点列を作り、Canvas変換とPixelScaleを合わせた線形変換の保守的な最大倍率を使ってlocal許容誤差を求める。二次・三次Bezierはde Casteljauの中点分割を行い、制御点から有限の端点線分までの距離が許容値以内になるまで再帰する。無限直線への距離だけを使わず、同一直線上にある制御点が端点外へ大きく張り出す曲線も残す。

目標の画面誤差は0.125 pixel以下とする。最大倍率には線形成分のFrobenius normを用い、非一様拡大・せん断でも過小評価を避ける。深さ24、追加点65536を上限とし、上限では品質を落とさずNotSupportedExceptionを返す。分割後のedgeはVector4(a.x,a.y,b.x,b.y)としてgeometryデータへ格納する。極端な座標で非finiteになったデータはGPUへ送らない。

fragmentのpathCoverageはsampleから水平rayに対する上向き・下向き交差を半開区間で数え、頂点を二重計数しない。逆変換したlocal sampleに対して計算するため、反転や回転を含むCanvas変換も同じ輪郭の意味を保つ。shape・clip・paintは共通の4×4 sample位置で評価する。

### strokeを輪郭のunionとして評価する

StrokeStyle2D.Widthは正のlocal幅で、変換前に半幅の線分四辺形を作る。Canvas変換はその輪郭全体へ作用するため、非一様拡大では幅も異方的に変わる。画面pixel幅へ自動補正するhairline APIは設けない。

| 設定 | 展開 |
| --- | --- |
| Butt cap | 端点で終了 |
| Square cap | 接線方向に半幅延長 |
| Round cap | 端点中心のdiscを追加 |
| Bevel join | 外側の2点と中心を結ぶ三角形 |
| Miter join | offset線の交点まで延長。距離がMiterLimit×半幅を超えたらbevel |
| Round join | 頂点中心のdiscを追加 |

連続する重複点を取り除き、閉じたcontourの重複終点を整理する。点だけのpathや長さ0のDrawLineは、Roundならdisc、Squareなら半幅の正方形、Buttなら無面積となる。roundの分割数は画面上の弦誤差0.125 pixelから求め、最低8・最大4096とする。stroke全体の三角形数は65536を上限とし、超過を拒否する。

生成した三角形をhardware rasterizerへ個別に描いて重ねる方式にはせず、fragmentで三角形の内部coverageの最大値を取る。cap・join・線分の重なりによって半透明のstrokeを何重にも合成しない。DrawLineとStrokePathはこの処理を共有し、最終coverageとpaintを一度合成する。

DrawingBounds2Dは制御点を含むpath boundsを使い、strokeの幅・miter limit・square capの対角方向も含めて拡張する。外向きに丸めたscissorは保守的なcullingであり、輪郭判定の代用ではない。特に回転・せん断したsquare capを切り落とさない。

### computeなどへの差し替えと記録契約

外部実装は準備時に独自のedge/tile buffer、compute pipeline、upload資源を確保し、IPreparedCommandBatch2D.Recordでupload→barrier→dispatch→barrier→renderを一つのcommand bufferへ記録できる。computeを使うだけで別queueやtimeline semaphoreを必須にしない。提出とGPU完了確認はアプリが担当する。

共有資源はDeclareResourcesで宣言し、batch私有の中間資源とbarrierは実装が管理する。現行のgraph接続はbatch全体をopaque passとして扱うため、graphへ見せる入口・出口の状態を一致させる。独立したgraph passとしてcomputeとrenderを最適化する経路は別のadapter設計が必要であり、現在のinterfaceが任意のRenderGraph pass構築を公開するわけではない。

DrawList2DはCPUデータとして再使用できる。動的pathを毎frame再BuildしてPrepareする経路も、同じ不変snapshotの契約に従う。標準経路は毎回CPUで分割・展開し、暗黙のpath cacheやcompute fallbackは持たない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 記録時に曲線を固定精度で折れ線化 | 実際のCanvas変換とDPIで必要な精度が変わる |
| path全体をCPUでtessellationしてhardware描画 | 初期実装のwinding・共通subpixel評価を共有しにくい。専用rendererとして後から導入できる |
| cap・joinの三角形をそれぞれ半透明合成 | 重なりが濃くなり、一つのstrokeの意味が変わる |
| 動的pathを必ずcomputeへ送る | 小さいpathや利用可能GPU機能を問わずその準備を要求する |

## 結果と影響

曲線、穴、自己交差、strokeのcap・joinを共通paintで描ける。local輪郭と画面誤差を分けるためDPIやせん断にも対応でき、命令単位で別の方式を選べる。

一方、初期実装はCPU分割のコストと、pixel・sampleごとのedge/triangle走査が必要になる。大量の動的pathではtile/binningやcomputeへの移行を測定して判断する。dash、path計測、GPU cacheは別途設計する。

## 検証方針

RenderingGeometryTestsで同一直線上のBezierの張り出し、Close後の始点、変換後の誤差、0長線、cap・join・miter limit、せん断したround cap、square capの保守的bounds、分割上限を確認する。配送・DIテストへ3命令を追加し、GPUテストではpathとstrokeの輪郭・paint・clipを画素比較する。性能目標は実測前に保証しない。

## API・利用例

比較元は基本描画PRのsplit/2d-primitives。本PRで追加する主要メンバーを示す。Path2DとPathBuilder2Dの宣言は[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md)を参照する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
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
+    public sealed record DrawLineCommand2D(Vector2 Start, Vector2 End, PaintGraph2D Paint,
+        StrokeStyle2D Style, DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillPathCommand2D(Path2D Path, PaintGraph2D Paint, FillRule2D Rule,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record StrokePathCommand2D(Path2D Path, PaintGraph2D Paint, StrokeStyle2D Style,
+        DrawingState2D State) : DrawingCommand2D(State);
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
+}
```

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
     public abstract class DrawingCanvas2D
     {
+        public void DrawLine(Vector2 start, Vector2 end, Color2D color, StrokeStyle2D style);
+        public void DrawLine(Vector2 start, Vector2 end, PaintGraph2D paint, StrokeStyle2D style);
+        public void FillPath(Path2D path, Color2D color, FillRule2D rule = FillRule2D.NonZero);
+        public void FillPath(Path2D path, PaintGraph2D paint, FillRule2D rule = FillRule2D.NonZero);
+        public void StrokePath(Path2D path, Color2D color, StrokeStyle2D style);
+        public void StrokePath(Path2D path, PaintGraph2D paint, StrokeStyle2D style);
     }
     public sealed class DrawingCommandRenderers2D
     {
-        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
-            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle);
+        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle,
+            IDrawLineRenderer2D drawLine, IFillPathRenderer2D fillPath,
+            IStrokePathRenderer2D strokePath);
+        public IDrawLineRenderer2D DrawLine { get; }
+        public IFillPathRenderer2D FillPath { get; }
+        public IStrokePathRenderer2D StrokePath { get; }
     }
 }
```

CreateDefaultとUseGraphics2Dは追加した3標準実装も登録する。batchの所有権とRecordの一回実行契約は基本描画と同じとする。

```csharp
var path = new PathBuilder2D();
path.MoveTo(new Vector2(10, 10));
path.CubicTo(new Vector2(80, 0), new Vector2(20, 90), new Vector2(90, 70));
var outline = path.Build();
canvas.Begin();
canvas.StrokePath(outline, new Color2D(1, 0, 0, 0.6f),
    new StrokeStyle2D { Width = 4, Cap = LineCap2D.Round, Join = LineJoin2D.Miter });
canvas.FillPath(outline, PaintGraph2D.Solid(new Color2D(0, 0, 1, 0.3f)), FillRule2D.EvenOdd);
DrawList2D list = canvas.End();
// 同じRenderer2D.PrepareAsyncへ渡す。Fillは暗黙に閉じ、strokeはopenのまま。
```
