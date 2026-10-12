# ADR-GRAPHICS-0018: 描画順を保持するコマンド記録型2Dグラフィックエンジン

- 状態: 採用
- 日付: 2026-10-10
- 更新: 2026-10-12

## 背景

図形、画像、文字の描画形式を既存Graphics APIの上で扱いたい。描画順・クリッピング・色の合成を共通化しつつ、命令ごとのGPU描画方式を外部実装へ差し替えられる必要がある。レイヤーや背景へのエフェクト適用では、画像処理の前後で描画順とclipの意味を保持する。

本ADRは2D描画の構成と実行契約を定め、公開APIと詳細契約・利用例を末尾にまとめる。共通画像エフェクトの設計とAPIは [GRAPHICS-0017](GRAPHICS-0017-image-effects.md) に分離し、本ADRでは2D側の適用方法を定める。実装と検証の範囲はPRに記録し、設計上の受け入れ条件と実測結果を区別する。

## 決定

### 1. 構成と責務

```text
Canvas2D → DrawList2D → Renderer2D → Graphics command buffer

RenderGraphCanvas2D → RenderGraphDrawList2D → RenderGraphRenderer2D
  Effects(callback) → ImageEffectSubgraph ────────────┘
  親RenderGraph.Append2D(renderer) → 同じgraphのpass群 → Graphics
```

| 構成要素 | 責務と依存 |
| --- | --- |
| `Lumyte.Graphics.Drawing2D` | 通常のCPU命令記録、幾何・paint、直接GPU記録。Abstractionsだけへ実行時依存し、RenderGraph・ImageEffectsへ依存しない |
| `Lumyte.Graphics.Drawing2D.RenderGraph` | レイヤーCanvas、背景取得、Effectsコールバック、親graphへのAppend2D。Drawing2D・ImageEffects・RenderGraphへ依存 |
| `Lumyte.Graphics.ImageEffects` | レイヤー・背景の画像処理を委譲する接続先。[GRAPHICS-0017](GRAPHICS-0017-image-effects.md) の契約に従う |
| `Lumyte.Graphics.RenderGraph` | パス依存、barrier、不要パスの除去、論理一時資源の確保と解放 |
| 各 `DependencyInjection` パッケージ | 標準実装の任意登録。本体からDIコンテナーへ依存させない |
| アプリケーション | device、外部資源、Submit・完了確認・Present、DI scopeの所有 |

新規ライブラリは `src/Graphics/` に配置し、プロジェクト・NuGet・namespaceを上記名称に揃える。backend具象型、Window、Input、Compositionには依存せず、shaderは既存Slang基盤で用意する。

RenderGraphの接続契約はmain `81bba9e` に統合された [GRAPHICS-0015](./GRAPHICS-0015-render-graph.md) を参照する。通常のCanvas2DとRenderer2Dはgraphを生成せず、Graphics command bufferへ直接記録する。レイヤーと画像エフェクトはRenderGraphCanvas2Dに分離し、RenderGraphRenderer2Dを親graph.Append2D(renderer)で追加する。内部に別のgraphやcompiled planを作らない。

レイアウト、ヒットテスト、画像デコード、ファイル資産管理、フォント探索・shaping・行分割、必須のシーングラフは対象外とする。

### 2. 命令と描画方式

CanvasはCPU上に不変のDrawListを作る。描画順を固定し、連続する同型・同一状態の範囲だけをバッチ化する。レイヤー・clip・隔離合成の境界を越えてまとめず、textureや透明度による全体ソートは行わない。

初期標準実装は、描画先を覆うtriangleを保守的なboundsのscissorで制限し、fragment shaderで形状とpaintを評価する。通常描画はbatch.Recordでupload・内部barrier・描画を直接記録し、同一バッチのpipelineを共有する。graph経路は同じbatchを一つのopaque passとして追加し、共有資源の依存を宣言する。表はこの実装の方式を示す。

| 描画命令 | 初期標準実装の方式 |
| --- | --- |
| FillRect | fragmentで矩形内を判定し、共通paintを評価 |
| FillRoundedRect | fragmentで角丸矩形内を判定し、共通paintを評価 |
| FillCircle | 逆変換した座標で円内を判定し、共通paintを評価 |
| DrawLine | CPUでcapを含む輪郭を三角形へ展開し、fragmentで内外判定 |
| FillPath | CPUで曲線を分割し、fragmentでNonZero/EvenOddのwindingを評価 |
| StrokePath | CPUでcap・joinを含む輪郭を三角形へ展開し、fragmentで内外判定 |
| DrawImage | 描画矩形のRGBAをtexelごとにpremultiplyして補間・合成 |
| DrawCoverageMask | coverage textureと共通paintを評価 |
| DrawDistanceField | SDF/MSDF/MTSDFの輪郭を復元し、共通paintを評価 |
| DrawMesh | pixel中心でtriangleの内外・重心座標・頂点coverageを評価。輪郭AAは入力coverageに従う |
| DrawPaintGraph | gradient・画像pattern・path clip・隔離合成をshader内で評価 |

形状・clip・paint clipは同じ4×4 subpixel位置で評価し、相関を保つ。幾何coverageの精度は1/16刻みで、面積の厳密な解析積分ではない。DrawMeshの形状coverageだけはpixel中心の入力値とし、外側clipとpaintは同じsubpixel位置で評価する。曲線分割の画面上誤差は0.125 pixel以下とし、GPU準備はpaint深さ64、幾何・clipを含む展開命令256、データ262144個のVector4を上限とする。pathは65536点・edge、分割深さ24、round strokeの円弧分割4096を上限とする。上限超過とMaxAnisotropy > 1の指定は準備時に拒否し、黙って品質を落とさない。

退化gradientのうち、3点linearの投影分母が0、同心かつ同半径のradial、startAngle = endAngleのsweep、先頭・末尾stopのoffsetが等しいRepeat/Reflectは初期標準実装ではNotSupportedExceptionとする。通常のgradientと全28合成modeは同じpaint評価器で扱い、未対応の入力を別方式へ近似しない。

instancing、GPUでのtile化、三角形rasterizerを使うpath/mesh描画、computeでの動的path処理は今後の最適化とする。固定interfaceを差し替えて導入でき、命令やpaintの契約は維持する。

各命令は指定されたpaintとclipを含む処理全体を担当し、必要なら複数GPUパスを宣言する。FillPathだけを差し替えても、StrokePathの輪郭処理やDrawPaintGraph内部のpath処理には波及しない。

Save/Restore、変換、ClipRect/ClipPathはCPU状態操作である。BeginLayer/EndLayerとDrawBackdropEffectはホストが展開する構造命令とし、上記11命令の専用interfaceを増やさない。Canvas2D.Endは未完了のSave、RenderGraphCanvas2D.Endは未完了のSave・レイヤーがあれば失敗し、Cancelは記録全体を破棄する。

### 3. 固定DIと外部実装

11命令それぞれに非genericの専用interfaceを設ける。`DrawingCommandRenderers2D` は全11実装をコンストラクターで受け取り、nullを拒否して型付きreadonly fieldへ保持する。DrawListの型付き領域と描画順の列からバッチを取り出し、専用interfaceを直接呼ぶ。

実装の選択は構築時に固定する。CanHandle、候補探索、自動fallback、描画中のDI解決・Type辞書検索は設けない。入力やGPU機能に対応できない場合は準備時に明示的に失敗する。interface呼び出し自体は残るため、インライン化や性能向上は実測前に保証しない。

DrawingCommandRenderers2D.CreateDefault()は登録表と11標準実装を毎回newし、DI・device・GPU資源を必要としない。UseGraphics2Dは11標準描画実装と描画登録表をTryAddScopedする。エフェクトはDIへ登録しない。RenderGraphCanvas2DのEffectsコールバックへImageEffectSubgraphを渡し、画像とパラメーターからパスを追加する。

外部実装は後続AddScopedの単一解決が優先され、先行登録と再Useも保持する。Graphicsへは解決済み登録表を通常のRenderer2Dコンストラクターへ渡す。

DI経路ではCreateDefaultを呼ばず、各interfaceをコンストラクターへ注入する。登録はprovider構築前に完了させ、既存provider・rendererの設定は後から変更しない。device・renderer・providerは描画登録拡張で生成・解決しない。画像処理はEffectsコールバックからRenderGraphの部分ビューへ追加する。scopeはdevice単位とし、GPU使用、準備結果、rendererの解放後に破棄する。

### 4. paint・色・外部文字描画

FillRect、FillRoundedRect、FillCircle、DrawLine、FillPath、StrokePathはColor2DとPaintGraph2Dの両方を受け取る。Color2D版はSolidへ正規化し、描画命令の種類は変えない。paintは変換前の論理座標で評価し、形状boundsへ自動伸縮しない。

PaintGraph2DはSolid、linear/radial/sweep gradient、ImagePattern、Opacity、Clip、Transform、Layers、Compositeを持つ不変DAGとする。命令・path・nodeのsnapshotを公開し、外部実装がbackendのprivate型へ依存せず評価できるようにする。

| 項目 | 共通契約 |
| --- | --- |
| 座標 | 左上原点、X右・Y下。`targetPixel = Transform(local, CanvasTransform) * PixelScale - PixelOrigin` |
| 色 | sRGBをdecodeし、Straight入力はalphaを掛けてから線形光で補間・合成する |
| 中間画像 | linear premultipliedのRgba16Float。暗黙の低精度化は行わない |
| 2D出力 | 単一mip/layerのRgba8Srgbまたはdisplay-linearのRgba16Float。通常合成はSrcOver |
| coverage | 同一幾何段階のshape・Canvas clip・paint clipの相関を保ち、独立AAの重複乗算を避ける |
| group opacity | 子を合成したpremultiplied RGBAへ一度適用し、各子へ分配しない |
| 隔離合成 | Compositeの子を透明面へ独立評価する。backdropはgraphの子で、既存Canvas背景ではない |

Clear・Src・DestIn等の透明領域の意味を保持し、評価範囲を輪郭のunionだけへ縮めない。有限のtarget・clip・外側形状を基準に評価する。paint内の外側coverageをLayersの各子へ分配しない。

ImagePatternは整数texelのSource、座標変換、軸別Clamp/Repeat/Mirror/Decal、Nearest/Linearを指定する。外部samplerを持たず、Linearの各tapへSource相対のtile規則を適用する。atlasの隣接画像を読まない。DrawImageもtexelごとのdecode・premultiply後に補間し、補間後のpremultiplyで代用しない。具体式と距離場のrange・padding契約は末尾の詳細契約で定める。

DrawGlyphRunと文字の描画形式選択は外部文字ライブラリが担当する。パス・mesh・coverage・距離場・RGBA・COLRv1をそれぞれ汎用命令へ渡す。COLRv1の3点linear、2円radial、sweep、重複stopの順序、参照glyph、変換・clip、全28合成modeを保持する。variation・CPAL・foreground・font unitsとY反転・cacheは文字側で解決する。

### 5. RenderGraph用Canvasとエフェクト部分グラフ

通常のCanvas2DにはBeginLayer・EndLayer・DrawBackdropEffectを持たせない。共通のCPU描画・変換・clip操作はDrawingCanvas2Dにまとめ、RenderGraphCanvas2Dだけがレイヤー操作を追加する。通常のEndはDrawList2D、graph用Endは別のRenderGraphDrawList2Dを返し、通常Renderer2Dへレイヤーを渡せない型にする。

LayerOptions2D.EffectsとBackdropEffectOptions2D.Effectsは `Func<ImageEffectSubgraph, EffectImage>` とする。レイヤーのEffectsはnullならidentityで、背景効果は必須とする。記録時はdelegateを保存するだけで、効果型のnewやImageEffectGraphの組立を利用者へ要求しない。設定値はコールバック内でgraph.AppendGaussianBlur(graph.Source, 3, 3)のように直接渡す。

Append2Dの準備中に内容を画像化し、一回だけEffectsを呼び出す。ImageEffectSubgraph.Sourceはそのレイヤーまたは背景の入力画像で、Graphは親RenderGraphの借用参照である。独立したgraph・import・Compile・Record・Submitは行わない。拡張メソッドは同じ親graphへpassを追加し、EffectImageを返す。途中結果の固定資源は部分ビューが所有し、2Dの準備ownerがGPU完了まで保持する。delegateのclosureには不変の設定を保持し、別Appendごとに新しい部分ビューと出力を作る。

BeginLayerは有限のContentBounds、Effects、Opacity、任意のOutputClipをsnapshotする。適用順は「内容描画 → Effects → group opacity → 最終clip → 親targetへSrcOver」。ContentBoundsは効果前の幾何clipであり、親clipとOutputClipは効果後に適用する。入力を最終targetへ先に切らず、効果で拡張した出力をContentBoundsで再度切らない。親clipのcoverageは一度だけ適用する。

DrawBackdropEffectは直前までの現在レイヤーをsnapshotし、結果を `effect × coverage + original × (1 − coverage)` で置換する。任意のパス追加コールバックから必要入力ROIを逆算する契約はないため、初期実装は現在targetの有限領域全体を取得する。ぼかし・移動・独自効果の入力を取りこぼさず、コールバックを計画用に再実行しない。必要ROIを明示する最適化は別途設計する。

2Dから渡す画像はDisplayLinearのlinear premultiplied Rgba16Floatとし、原点と有限範囲を保持する。コールバックの出力は非空画像で同じ色semanticとする。効果前の入力原点と、拡張後の出力原点を保持する。coverageや距離場は先に色画像へ描画する。別の実装は新しい効果と拡張メソッドで追加し、効果レンダラーの差し替え機構は持たない。

### 6. 所有権・完了・失敗

| 対象 | 所有者 |
| --- | --- |
| importしたtexture・view・sampler等 | アプリケーション。viewだけでなく親textureもGPU使用終了まで保持 |
| 一時allocationとcontext生成view | CompiledRenderGraph |
| CPU snapshot、pipeline、argument table、upload、私有shader-dataとエフェクト準備owner | PreparedDrawingPasses2D |
| 通常描画のCPU snapshotと固定GPU資源 | PreparedDraw2D。graphやplanは生成・所有しない |

準備ownerのcallback集合は、一つのcompiled planの一回のRecordにだけ使う。再実行にはAppendから新しく準備し、先行GPUが使うtableやstagingを上書きしない。owner解放後のgraphをCompile/Recordしない。

正常系は「準備・Compile → Record → Finish → Submit → 明示完了確認 → command → plan → 準備owner → submissionの解放」とする。最後にrendererとDI scopeを解放する。通常描画のPreparedDraw2Dは固定資源だけを所有し、graphやplanを所有しない。plan出力を後続submissionでも使う場合は、その完了まで保持する。

| 失敗位置 | 処理 |
| --- | --- |
| Appendの失敗・キャンセル | 内部の新規準備資源をcleanupし、変更途中のgraph全体を捨てて再構築。既存の成功ownerは利用者が解放 |
| Compileの失敗 | GPU記録前なので準備ownerを解放できる |
| Recordの失敗・未送信の記録を破棄 | commandを先に破棄してからplan・準備ownerを解放。再記録しない |
| Submit後の待機キャンセル・Failed | GPU使用終了とはみなさず、明示完了またはbackendの終了保証に従う |

[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)に従い、DisposeはGPU完了照会・waitを行わず、寿命違反の検出を保証しない。同一device、mapping排他、宣言と実アクセスの一致、外部資源の生存は利用者の事前条件である。数値・範囲・未知enum、自身の局所状態、graphの容量・展開上限は準備時に検査し、nativeの失敗も伝達する。単色化・レイヤー省略・自動fallbackで成功扱いにしない。

初期実行は一つの一般queueとcommand bufferを使い、内部Submit・Wait・Presentは行わない。Surfaceの取得・semaphore・提示解放、複数command間の転送metadata公開はアプリ側の既存Graphics契約に従う。Canvas・renderer・共有graphへの同時操作は保証しない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| paintとレイヤーの画像エフェクトを同じgraphで表す | 塗りのcoverageと画像化前後のclip・描画順の責務が混在する |
| CanHandle・優先順位による方式探索 | 命令ごとに指定した実装が固定されない |
| 単色専用命令とpaint種別ごとのDI登録 | 図形とpaintの組み合わせでAPI・登録が増える |
| graph経路で外部共有資源を宣言しない | 前後の描画やエフェクトとの依存を解析できない |
| 独自のGPU backend・パス実行器 | 既存Graphics・RenderGraphの契約と重複する |
| texture順の全体ソート・描画ごとのSubmit | 半透明の順序とアプリ側のGPU実行管理を崩す |
| 文字を単色atlasや平坦なpath列に限定 | 外部の形式選択とCOLRv1の隔離合成を保持できない |

## 結果と影響

図形・画像・外部文字描画が同じpaintと描画順の契約を共有し、命令別の実装をDIでも手動でも差し替えられる。レイヤーと背景へ共通エフェクトを適用し、GPU処理をRenderGraphへまとめられる。

一方、順序保持によって全体ソートほどバッチを減らせない場合があり、複雑なpaint・clip・エフェクトでは中間画像と複数passが必要になる。準備ownerとcompiled planの寿命、全アクセスの宣言、追加失敗時のgraph再構築を利用側が管理する。描画命令の追加時にはinterface、登録表、標準実装、CreateDefault、DI登録を同時に更新する。

既存GraphicsのAPIは変更しない。GPU実装はRenderGraphの採用・契約に依存し、初期のwhole-allocation同期と単一queueの制約に従う。

## 検証方針

| 領域 | 受け入れ条件 |
| --- | --- |
| 命令・DI | 11描画命令の登録対応とエフェクトの直接構築、後勝ち・再Use・CreateDefault、配送順と独立した差し替え、描画登録表構築時のGPU生成と描画中のDI解決がないこと |
| paint・画像 | 6命令のColor/Solid等価性、gradientの退化・stop・extend、tap別tileと負座標、透明境界の補間、shape/clip相関、group opacity |
| 文字・COLRv1 | 全gradient・全28合成・参照glyph・variation・palette・foreground・clipを固定fontと参照画像で比較 |
| エフェクトへの接続・レイヤー | DisplayLinear画像の受け渡し、DPIとoffset、前後clip、group opacity、ネスト、画面外からの影、必要入力の背景取得、透明背景・重なるパネルの置換 |
| RenderGraph | 単独/共有経路の画像一致、全資源宣言、重複import防止、Loadと部分更新の保持、culling・export・snapshot順序 |
| 失敗・寿命 | Append/Compile/Recordの失敗、待機キャンセル、一回実行、GPU使用終了後の解放、複数frameでの非上書き |

CPU準備時間、GPU時間、draw数、転送量、割り当て量、中間面の使用量を実装後に測定する。画像比較では色空間・誤差許容値を固定し、backend共通のfixtureを使用する。

## 別途決定する事項

- RenderGraphのsubresource/byte範囲、pool/alias、パス統合、複数queue、GPU timestamp、compile cache。
- instancing、pathのtile化・三角形rasterizerの利用、mask/paintのcacheとatlas最適化。
- shaping、font fallback、OpenType outline/COLRv1 adapterのライブラリと公開API。
- HDR・広色域のCanvas出力、追加blend mode、シーングラフ、並列準備、frame pool。

## API・詳細契約・利用例

比較元は先行エフェクトブランチ `feat/image-effects` のrevision `dc4c8a9`。以下の差分は追加する公開メンバーの宣言であり、同じ型の宣言は一つの公開型へ統合する。Graphics型は `Lumyte.Graphics.Abstractions`、行列・ベクトルは `System.Numerics`、DI型は `Microsoft.Extensions.DependencyInjection` を使用する。

RenderGraph型は先行ブランチが基づくmain `81bba9e` の公開APIを使用する。`RenderGraphBuilder` は `using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;` のaliasであり、新規型ではない。相互参照するDrawing2D・ImageEffects・RenderGraphの型はそれぞれの名前空間を使用する。ImageEffectSubgraph・ImageEffectDefinition・Append拡張メソッドの宣言と契約は [GRAPHICS-0017](GRAPHICS-0017-image-effects.md) を参照する。

### 公開API

#### 図形・paint・Canvas・Renderer

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
+    public abstract class DrawingCanvas2D
+    {
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
+        // 記録中のCPUコマンドを破棄し、再びBeginできる状態へ戻す。
+        public void Cancel();
+        protected DrawList2D CompleteRecording();
+        protected void RequireRecording();
+        protected DrawingState2D State { get; set; }
+        protected int SaveDepth { get; }
+        protected virtual int SaveBoundary { get; }
+        protected void AddCommand(DrawingCommand2D command);
+        protected virtual void ValidateComplete();
+        protected virtual void ResetAdditionalState();
+    }
+    public sealed class Canvas2D : DrawingCanvas2D
+    {
+        public DrawList2D End();
+    }
+    // 再使用可能なCPU snapshot。参照する画像のGPU所有権は持たない。
+    public sealed class DrawList2D { }
+    public sealed record RenderTarget2D
+    {
+        // 同一device、親textureも単一sample/mip/layerのD2、RenderAttachment view。
+        // 通常描画はSampled用途をtargetへ要求しない。背景効果は別パッケージの契約に従う。
+        // 初期対応はRgba8SrgbとRgba16Float。寸法はviewから取得する。
+        // Rgba16Floatも初期2Dではdisplay-linearのpremultiplied値を保持する。
+        public required IGraphicsTextureView View { get; init; }
+        // 論理座標から物理pixelへの倍率。finiteかつ正数、既定値1。
+        public float PixelScale { get; init; } = 1;
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    public record DrawingTargetInfo2D
+    {
+        public required TextureDesc Description { get; init; }
+        public float PixelScale { get; init; } = 1;
+        public Vector2 PixelOrigin { get; init; }
+        public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;
+        public Color2D ClearColor { get; init; }
+    }
+    public sealed class Renderer2D : IDisposable
+    {
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers);
+        // graphを作らず、固定資源と直接記録するbatchを準備する。
+        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        public void Dispose();
+    }

+    public sealed class PreparedDraw2D : IDisposable
+    {
+        // 一度だけ。Recordingかつactive passなしの同一device commandに記録する。
+        // 内部copy/compute/barrier/render passを記録し、active passを残さない。
+        public void Record(IGraphicsCommandBuffer commands);
+        // GPU完了→commands破棄後に呼ぶ。固定GPU資源を解放する。
+        // Disposeから完了照会・waitを行わず、寿命違反の検出を保証しない。idempotent。
+        public void Dispose();
+    }
+}
```

#### 命令snapshotと固定レンダラー

全11命令は専用interfaceへ配送し、登録表の構築後に方式を探索しない。命令内のpaint評価も、その命令へ指定した実装が担当する。

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
+        public IGraphicDevice Device { get; }
+        public DrawingTargetInfo2D Target { get; }
+    }
+    // 以下の全PrepareAsyncに共通する契約:
+    // 同型・同一clip/合成境界の連続命令。並べ替え不可。snapshotは準備結果の寿命まで有効。
+    // CPU処理とpass追加に必要なsnapshot・固定資源を準備する。submit/waitは禁止。
+    // batch私有texture/bufferは準備ownerが所有し、Record内でupload・barrier・描画を記録する。
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
+        // 外部共有資源を宣言する。batch私有資源はRecordで内部同期する。
+        void DeclareResources(IDrawingResourceDeclarations2D resources);
+        void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target);
+    }
+    public interface IDrawingResourceDeclarations2D
+    {
+        void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope);
+        void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
+        void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
+            where T : struct, IShaderData;
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

#### 標準描画実装とDI登録

標準クラスのPrepareAsyncは対応interfaceの明示実装とする。UseGraphics2Dは全11描画組とDrawingCommandRenderers2DだけをTryAddScopedで登録する。IGraphicDevice・Renderer2D・providerは登録・生成・解決しない。

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
+        // 全11描画interfaceと描画登録表だけをTryAddScopedで登録。effectは登録しない。
+        // 未登録分だけを補完し、再呼び出しで既存の上書きを消さない。
+        // servicesのnullは拒否。provider生成やサービス解決は行わず、同じservicesを返す。
+        public static IServiceCollection UseGraphics2D(this IServiceCollection services);
+    }
+}
```

#### RenderGraph用Canvas・レイヤー・親graphへの接続

以下は別パッケージの公開APIである。通常Canvas2DにはレイヤーAPIを追加しない。

```diff
+namespace Lumyte.Graphics.Drawing2D.RenderGraph
+{
+    public sealed class RenderGraphCanvas2D : DrawingCanvas2D
+    {
+        public void BeginLayer(LayerOptions2D options);
+        public void EndLayer();
+        public void DrawBackdropEffect(BackdropEffectOptions2D options);
+        public RenderGraphDrawList2D End();
+    }
+    public sealed class RenderGraphDrawList2D;
+    public sealed record LayerOptions2D
+    {
+        public required Rect2D ContentBounds { get; init; }
+        public Func<ImageEffectSubgraph, EffectImage>? Effects { get; init; }
+        public float Opacity { get; init; } = 1;
+        public Rect2D? OutputClip { get; init; }
+    }
+    public sealed record BackdropEffectOptions2D
+    {
+        public required Rect2D OutputBounds { get; init; }
+        public required Func<ImageEffectSubgraph, EffectImage> Effects { get; init; }
+    }
+    public sealed record RenderTargetBinding2D : DrawingTargetInfo2D
+    {
+        public required RenderGraphTexture Texture { get; init; }
+        public TextureSubresourceRange Range { get; init; } = new(0, 1, 0, 1);
+    }
+    public sealed class DrawingGraphBindings2D
+    {
+        public DrawingGraphBindings2D(RenderGraphBuilder graph);
+        public void BindTexture(IGraphicsTexture texture, RenderGraphTexture resource);
+        public RenderGraphTexture GetTexture(IGraphicsTexture texture);
+        public void BindBuffer<T>(IGraphicsBuffer<T> buffer, RenderGraphBuffer<T> resource) where T : unmanaged;
+        public RenderGraphBuffer<T> GetBuffer<T>(IGraphicsBuffer<T> buffer) where T : unmanaged;
+        public void BindShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer,
+            RenderGraphShaderDataBuffer<T> resource) where T : struct, IShaderData;
+        public RenderGraphShaderDataBuffer<T> GetShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer)
+            where T : struct, IShaderData;
+    }
+    public sealed class RenderGraphRenderer2D : IDisposable
+    {
+        public RenderGraphRenderer2D(DrawingCommandRenderers2D renderers, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings);
+        public void Dispose();
+    }
+    public static class Drawing2DRenderGraphExtensions
+    {
+        public static ValueTask<PreparedDrawingPasses2D> Append2D(this RenderGraphBuilder graph,
+            RenderGraphRenderer2D renderer, CancellationToken cancellationToken = default);
+    }
+    public sealed class PreparedDrawingPasses2D : IDisposable
+    {
+        // batch・effect部分ビュー・compositorの固定資源を所有。親graphは所有しない。
+        public void Dispose();
+    }
+}
```

### 詳細契約

#### 座標・塗り・coverage

CanvasはBegin中だけ操作でき、入力不備では命令・状態を変更しない。Endの対応関係検証に失敗した場合は記録を維持する。End成功またはCancel後は新たにBeginできる。PathBuilderはMoveTo前の線・曲線・Closeを拒否する。

左上原点で右がX正、下がY正、正の回転は時計回りとする。`targetPixel = Transform(local, CanvasTransform) * PixelScale - PixelOrigin`。中間面もルート描画先のpixel原点を保持し、単独ルートのPixelOriginは0とする。paintは変換前の論理座標で評価し、形状boundsへ自動伸縮しない。Canvas変換は形状とpaintへ、PaintGraph.Transformは子paintのgeometryを含む全体へ作用する。

2点linearは `p0=start, p1=end, p2=start+perp(end-start)`、`perp(x,y)=(-y,x)` へ正規化する。単一中心radialは `c0=c1=center, r0=0, r1=radius` とする。便利overloadは非退化入力を要求し、一般の3点linear・2円radialの退化規則を変えない。

形状はpaintの外側の幾何clipとし、Canvas clipとgraph Clipとの相関を維持する。同じ幾何評価段階では、独立に平均化したAA coverageの乗算を避け、共通sampleまたは幾何的交差を評価する。外側coverageをLayersの各子へ分配しない。PaintGraph.Opacityは子全体の合成後のpremultiplied RGBAへ一度適用し、Opacity(0.5)を掛けた不透明Layersの重なりもalpha=0.5とする。

DrawCoverageMaskはsample後の[0,1]値をcoverageとする。SDFはR、MSDFはRGBの中央値を輪郭距離とし、MTSDFはRGBの輪郭距離とAのtrue signed distanceを保持する。range・符号・paddingを供給側と一致させ、画面上のpixel幅でAAを評価する。mask・距離場のatlas paddingとsource範囲は供給側が確保する。DrawMeshは頂点Coverageを補間し、境界用頂点のないmeshへ自動AAを仮定しない。

非整数座標を保持し、pixelへの丸めはscissor範囲の計算に限定する。ClipRectは変換後に軸平行かつ整数pixel境界の場合だけscissorを使い、小数・回転・せん断を含むclipはcoverageを評価する。scissorをtarget内へ収め、空の形状・clip・変換後の無面積形状は描画しない。未対応clipを境界矩形へ置き換えずNotSupportedExceptionとする。曲線分割はCanvas変換とPixelScale適用後の画面上誤差を基準とし、現GraphicsにないMSAA/resolveを必須にしない。

通常合成はpremultiplied source-overで、RGB・alphaともsource factor=One、destination factor=OneMinusSourceAlphaとする。clear値も線形RGBへ変換し、sRGB targetへのencodeはattachmentに任せる。Loadする既存内容もdisplay-linearのpremultiplied表現を満たす。色画像とcoverage・距離場のデータtextureを混同しない。

#### 画像パターンのsampling

ImagePattern2Dは外部samplerを持たず、FilterとTileX/TileYを唯一の設定元とする。Image2DからはViewとAlphaを渡す。Sourceは選択mip内の整数texel矩形で、正の寸法を持ちview内に収まる。source左上を原点(0,0)、texel中心を(i+0.5,j+0.5)、周期をsource寸法とし、PatternToLocalの逆変換でsampleする。factoryは未知のenum、非finite値、逆変換不能・逆行列が非finiteとなる行列を拒否する。

各軸の寸法をN、整数tap座標をiとする。Repeatは `positiveMod(i,N)`。Mirrorは `r=positiveMod(i,2*N)` として `r<N ? r : 2*N-1-r`。positiveModは0以上の剰余とし負座標にも適用する。Clampは端のtexelへ固定し、Decalは範囲外tapを透明とする。X/Yは独立して指定する。

Nearestは各成分をfloorしたtexelを選ぶ。Linearはtexel中心基準の4tap双線形補間で、各tapへSource相対のtile規則を適用する。中心座標だけをwrapしてatlas全体をhardware linear samplingする方式は使用しない。Source外の隣接画像を読まないため、この画像paintにはatlas paddingを要求しない。初期対応は単一mipのNearest/Linearとする。

DrawImageとImagePatternの補間は、texelごとにsRGBをdecodeし、Straightならalphaを掛けた後の線形premultiplied RGBで行う。補間後のpremultiplyで代用せず、Premultiplied入力へ再乗算しない。hardware sampling・tap処理・事前変換はこの結果と等価な場合に使う。Prepareは色formatとSampled用途を確認し、同一deviceとview・親texture・samplerの生存は利用者がGPU使用終了まで保証する。

#### COLRv1と隔離合成

文字のfallback・shaping・行配置・描画形式の選択・cacheは外部文字ライブラリが担当する。DrawGlyphRunはDrawing2Dへ追加せず、パス・mesh・coverage・距離場・RGBA・paint graphを対応する汎用命令へ渡す。adapterはfont unitsとY上向き座標を変換し、paint・clip box・角度へ同じ変換を適用する。輪郭の穴はNonZeroと元のwindingで保持する。

| COLRv1要素 | 写像 |
| --- | --- |
| PaintSolid / PaintVarSolid | palette・foreground・alphaを解決したSolid |
| PaintLinearGradient / Var版 | 3点LinearGradientとColorLine |
| PaintRadialGradient / Var版 | 2円RadialGradientとColorLine |
| PaintSweepGradient / Var版 | 座標・角度を変換したSweepGradientとColorLine |
| PaintGlyph | outlineをClipとして子paintへ適用 |
| PaintColrGlyph | 参照先paint graphを解決・共有し、仕様のclip適用範囲を保持 |
| PaintColrLayers | 順序を保持したLayers |
| Transform / Translate / Scale / Rotate / Skew、各Var・center版 | centerを含むアフィン行列のTransform |
| PaintComposite | source・backdropと全28種のCompositeModeを保持 |
| ClipList / ClipBox / variable clip box | 仕様の範囲へ矩形pathのClipを適用 |

adapterは正規化軸とvariation store、CPAL palette、foreground（0xFFFF）、stop alphaをgraph生成前に解決する。ColorLineはvariation適用後にoffset順へ安定ソートし、重複stopのfont内順序を保持する。offsetの一律[0,1]制限・重複除去は行わない。異なる軸・palette・foregroundのrunでgraphを誤共有せず、PaintColrGlyphを輪郭参照へ縮約しない。参照循環、不正offset/glyph、深さ・node数超過は拒否し、共有DAGの実展開コストも評価上限へ数える。

3点linearの投影、偏心2円radialの解と有効領域、sweep角度境界、Pad/Repeat/Reflect、退化・特異変換はOpenType COLRの規則を保持する。未対応段階はPrepareでNotSupportedExceptionとし、別gradientへの近似で成功扱いにしない。補間・合成はsRGBを線形化しpremultiplyした値で行い、HSL modeは仕様のunpremultiplied演算後に再乗算する。alpha=0やPlus等の範囲処理も参照規則に従う。

中間面はlinear premultiplied Rgba16Floatとし、必要なattachment/sample用途がなければPrepareで拒否する。sRGBのままの演算やRgba8への暗黙の精度低下を設けない。Compositeのsource/backdropは独立した透明面へ評価し、backdropを既存Canvas背景と混同しない。結果を一つの描画として外へSrcOverし、nested compositeを平坦化せず中間面境界をまたぐbatch化を禁止する。

Clear/Src/DestInは透明領域も結果へ影響するため、子の輪郭unionだけへ評価範囲を狭めない。target・Canvas clip・外側形状の有限範囲で評価し、透明領域の意味を保持できる場合だけ縮小する。clip coverageを指定位置で適用し、再合成時に二重乗算しない。ImagePatternとOpacityは一般描画用の追加nodeであり、新しいOpenType形式を要求しない。

参照: [OpenType COLR](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)、[CPAL](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)、[Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)。

#### レイヤーのclip順序と背景置換

BeginLayerはoptions・変換・親clipをsnapshot化する。ContentBoundsは入力を実際に幾何clipする有限矩形で、cullingヒントではない。変換後のpixel外接矩形を外向きに丸めて中間画像を作り、元矩形の幾何clipも保持する。画面外の入力が画面内へ影響し得るため、最終target範囲へ先に切り詰めない。空のContentBoundsは出力なしとし、0寸法画像を作らない。

親clipは入力へ適用せず、レイヤー内で追加したclipを入力描画へ適用する。EndLayerは内容→Effects→Opacity→保存した親clipとOutputClipと描画先範囲の積集合→親へのSrcOverの順で処理し、BeginLayer時の状態を復元する。拡張された効果領域をContentBoundsで再度切らない。最終clipのcoverageは一度だけ適用する。

coverage相関を保つのは同じ幾何評価段階内であり、画像化を挟む入力clipと出力clipは別演算とする。同じ境界でも入力0.5×出力0.5=alpha 0.25となり得る。group opacityは合成済み画像へ掛け、Opacity=0.5なら不透明形状同士の重なりもalpha=0.5とする。Save/Restoreは各レイヤー内で完結し、外側Saveの消費・未完了Saveを残すEndLayer・対応のないEndLayerを拒否する。Endは未完了構造を拒否し、Cancelは全構造を破棄する。

エフェクトのsigmaとComposite移動はdevice pixel単位で、Canvas変換・PixelScaleに依存しない。論理座標で一定幅にする場合はアプリが明示変換する。非一様拡大や回転でもGaussianは出力pixel gridのX/Y軸に沿い、暗黙に回転楕円kernelへ変換しない。

DrawBackdropEffectはその位置より前の現在レイヤーの描画先だけを取得し、祖先・後続・自分自身を読まない。targetはSampled | RenderAttachment用途と有効内容を要求する。初期Clearまたは有効なLoadが必要で、読出し不能ならPrepareで拒否する。Rgba8Srgbはsample時にdecodeし、既にpremultipliedの値を再乗算せずRgba16Floatへ描く。異形式のraw copyを色変換の代用にしない。

変換後のOutputBoundsと現在clipから出力pixel領域を求め、任意のEffectsコールバックのため現在targetの有限領域全体を別textureへsnapshotし、取得元の有効範囲外を透明とする。sample領域をOutputBoundsへ先に切らない。元背景が半透明でも二重合成しないよう、coverageをcとして `result = effect*c + original*(1-c)` で置換する。originalもsnapshotから読み、attachmentはblend無効で書き、clip外は保持する。

#### 準備・RenderGraph・解放

準備ownerはcallbackのCPU snapshot・固定GPU資源を、compiled planは一時allocationと生成viewを、アプリはimport資源を所有する。Drawing2D経由のPreparedImageEffectはPreparedDrawingPasses2Dが所有し、同じGPU使用期間を通じて保持する。エフェクト内部の資源管理は [GRAPHICS-0017](GRAPHICS-0017-image-effects.md) に従う。callbackは宣言済み資源だけを使い、内部Submit/Waitや記録中の追加依存・一時資源生成を行わない。

外部textureはparent allocationごとに一度だけimportし、DrawingGraphBindings2Dへ同じhandleを登録する。準備時にbindingを確定し、後の表変更で追加済みcallbackの入力を変えない。target Descriptionは実際の宣言と一致させる。graph adapterは全外部共有アクセスを宣言し、同じ資源のstage/accessを合併する。通常batchの私有資源はbatchが所有して内部barrierを記録する。初期graphはallocation全体で同期し、同一allocationのsampleとattachmentを同時使用しない。外部共有bufferとshader-dataはbindingsで既存typed handleへ対応付ける。graph compositorの一時資源はRenderGraphへ宣言する。

Load・部分更新・source-overはColorRead | ColorWrite、全体ClearのみColorWriteとして前内容の依存を保持する。未初期化領域・Discard後の内容を読まない。背景は更新→snapshot→effect→置換→後続描画の依存を宣言する。MarkOutputは出力を保持し、Exportはさらにfinal state/scopeを指定する。DependsOnは順序、KeepAliveは副作用による保持であり代替しない。exportしない画像は自動でimport状態へ戻らない。

準備ownerは一つのcompiled planで一回のRecordだけに使い、記録失敗も消費済みとする。Append失敗・キャンセルでは途中graphを破棄して再構築し、その呼び出しの新規ownerは内部cleanup、先行する成功ownerは利用者が解放する。Compile失敗ならownerを解放でき、Record失敗・送信取消ならcommandを先に破棄する。Submit後は全GPU使用終了を待ち、command→plan→準備owner→submissionの順で解放する。後続submissionが出力を使う間も元planを保持する。

WaitのキャンセルやFailedという状態名だけをGPU終了とみなさない。Disposeは完了照会・waitを行わず、寿命違反の検出も保証しない。利用者は準備結果→renderer→DI scopeの寿命を保証する。通常Prepareはgraphを作らず、入力をSampled、targetをColorAttachmentへ可視化済みとしてRecordする。共有graphは実際の入口state/scopeを渡し、read-only importも最初の利用stageまで外部producerとの依存を満たす。

opaque callbackは全外部アクセスを宣言し、内部barrierと私有資源を実装側で管理する。終了時は宣言した入口stateへ戻す。現RenderGraphには別exit stateの宣言がなく、単一Useで表せない複数stateはpassへ分割する。宣言できない経路を推測で実行しない。

数値・範囲違反はArgumentException/ArgumentOutOfRangeException、自身の局所的な呼び出し順違反はInvalidOperationException、自身の破棄後操作はObjectDisposedException、非対応形式・機能はNotSupportedExceptionとする。外部資源の寿命・実state・同一deviceの違反を必ず検出する保証とは区別し、nativeの失敗は伝達する。

### 利用例

#### RenderGraphを使わない通常描画

deviceとtargetViewはアプリ所有とする。targetはColorAttachment、入力画像はSampledへ可視化済みとし、通常描画はRenderGraphを生成しない。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(new Rect2D(20, 20, 100, 50), new Color2D(1, 0, 0, 1));
DrawList2D list = canvas.End();

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new RenderTarget2D
{
    View = targetView,
    LoadOp = AttachmentLoadOp.Clear,
});
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    prepared.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
```

#### Effectsコールバックと親graph.Append2D

Effectsには、その時点のレイヤー入力Sourceと親RenderGraphを持つ部分ビューが渡る。値を直接指定し、効果オブジェクトのnewや効果DAGの組立は行わない。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

var canvas = new RenderGraphCanvas2D();
canvas.Begin();
canvas.BeginLayer(new LayerOptions2D
{
    ContentBounds = new Rect2D(20, 20, 200, 100),
    Effects = graph => graph.AppendGaussianBlur(graph.Source, 3, 3),
    Opacity = 0.8f,
});
canvas.FillRoundedRect(new Rect2D(40, 40, 100, 50), 8, new Color2D(1, 0, 0, 1));
canvas.EndLayer();
RenderGraphDrawList2D list = canvas.End();

var parent = new RenderGraphBuilder(device, "frame");
var description = new TextureDesc
{
    Width = 512,
    Height = 256,
    Format = TextureFormat.Rgba16Float,
    Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
};
var target = new RenderTargetBinding2D
{
    Texture = parent.CreateTexture(description, "2d-output"),
    Description = description,
    LoadOp = AttachmentLoadOp.Clear,
};
using var renderer = new RenderGraphRenderer2D(
    DrawingCommandRenderers2D.CreateDefault(), list, target,
    new DrawingGraphBindings2D(parent));
using PreparedDrawingPasses2D drawing = await parent.Append2D(renderer);
parent.MarkOutput(target.Texture);
// 同じparentへ3Dや後処理のpassを追加できる。
using (var plan = parent.Compile())
using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
{
    plan.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
```

背景効果はRenderGraphCanvas2Dの記録中に追加する。複数の効果を連結しても、部分ビューがすべての固定資源を保持する。

```csharp
canvas.DrawBackdropEffect(new BackdropEffectOptions2D
{
    OutputBounds = new Rect2D(250, 20, 200, 100),
    Effects = graph =>
    {
        var source = graph.Source;
        var blurred = graph.AppendGaussianBlur(source, 3, 3);
        return graph.AppendOpacity(blurred, 0.8f);
    },
});
```

通常描画とgraph描画のどちらにも、DIで解決したDrawingCommandRenderers2DまたはCreateDefault()を渡す。UseGraphics2Dが登録するのは11描画実装と命令登録表だけであり、レイヤーやEffectsはDIへ登録しない。
