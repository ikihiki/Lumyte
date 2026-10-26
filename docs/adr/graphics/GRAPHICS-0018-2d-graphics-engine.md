# ADR-GRAPHICS-0018: 2Dコマンド記録・基本図形・paintと描画資源の所有権

- 状態: 採用
- 日付: 2026-10-10
- 更新: 2026-10-26

## 背景

矩形・角丸矩形・円を、gradientや画像patternで塗り、既存Graphics APIへ描画したい。CPU記録の再使用、半透明の描画順、clipとpaintのcoverageの相関を保ちつつ、命令ごとにGPU実装を選べる必要がある。shaderだけでなくpipelineとsamplerも汎用リソース定義で設定し、ResourceManagerを使わないアプリにも同じ描画実装を提供する。

本ADRは基本描画と共有基盤を扱う。線・パス、画像・mesh、文字向け形式、RenderGraphレイヤーは、それぞれの追加実装と同じPRのADRで扱う。画像patternとclip用pathは基本図形にも必要なため本ADRへ含める。Resourcesの汎用定義・世代管理は[RESOURCES-0008](../resources/RESOURCES-0008-graphics-generations.md)、GPUの寿命契約は[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)に従う。

## 決定

### CPU記録とGPU実行を分ける

```text
Canvas2D.Begin → 描画と状態操作 → End → DrawList2D
                                                ↓ 再使用可能なCPU snapshot
Renderer2D.PrepareAsync → PreparedDraw2D.Record → Graphics command buffer
                                                ↓ Finish / Submit / 完了確認はアプリ
```

`Lumyte.Graphics.Drawing2D`を`src/Graphics/`に配置し、実行時の依存をGraphics.Abstractionsへ限定する。Canvasはdevice、RenderGraph、ImageEffects、DIを参照せず、通常のRenderer2DもRenderGraphを作らない。Window、入力、レイアウト、画像デコード、ファイル取得、shapingは担当しない。

DrawingCanvas2Dは状態操作を持つ基底で、Canvas2D.Endが不変のDrawList2Dを返す。Beginは状態と記録を初期化し、ネスト開始を拒否する。Saveは現在の変換とclipを保存し、Restoreは対応する状態へ戻す。入力の検証後にだけ記録・状態を変更する。Endは未完了Saveを拒否して記録を維持し、End成功またはCancelで再Begin可能な状態へ戻す。

DrawList2Dは、描画順の命令列、型別の配列、順序から型別offsetへの対応を保持する。TakeBatchは連続する同型命令かつ同じClipStack2D参照の範囲を取り、型付きReadOnlyMemoryを専用rendererへ渡す。型ごとの全体ソートは行わず、透明描画の順序を保つ。バッチは配送と資源所有の単位であり、一つのバッチを一回のdrawへ融合する保証ではない。標準実装は各命令にargument tableとshader-dataを準備し、それぞれdrawする。

### 座標・clip・色を共通化する

左上原点、X右・Y下、正の回転は時計回りとする。変換は `targetPixel = Transform(local, CanvasTransform) * PixelScale - PixelOrigin`。通常描画のPixelOriginは0、PixelScaleは正数とする。Canvas変換は形状とpaintへ適用し、paintを形状boundsへ自動伸縮しない。非整数座標を保持し、整数化はscissorの保守的な範囲計算に限定する。

clipは不変のpath列の積集合で、ClipRectも矩形pathへ正規化する。各clipは追加時の変換を保存し、その後のCanvas変換で動かさない。scissorは描画領域を保守的に狭める最適化であり、回転・せん断・曲線の正確なclipはfragment側で評価する。PathBuilder2DはMove前の線・曲線・Closeを拒否し、Buildで配列をコピーする。

Color2Dはstraight alphaのsRGB入力で各成分を[0,1]に限定する。CPUでsRGBをdecodeし、alphaを掛けたlinear premultiplied RGBAをshaderへ渡す。gradientの補間と描画の合成もこの表現で行う。通常合成はRGB・alphaとも `source + destination * (1 - sourceAlpha)`。Rgba8Srgbのencodeはattachmentに任せ、Rgba16Floatもdisplay-linear premultipliedとして使う。Loadする既存内容にも同じ表現を要求する。clear色も同じ変換を通す。

### 基本図形とpaintの標準GPU経路

| 命令 | 形状評価 | paint |
| --- | --- | --- |
| FillRect | 逆変換した点が矩形内かをfragmentで判定 | 共通のPaintGraph2D |
| FillRoundedRect | 角丸矩形の距離判定。radiusは短辺の半分まで縮める | 共通のPaintGraph2D |
| FillCircle | 逆変換した点と中心の距離を判定 | 共通のPaintGraph2D |

標準経路はviewportを覆うtriangleをdrawし、DrawingBounds2Dが算出したscissor内で評価する。shape・Canvas clip・paint Clipを同じ4×4のsubpixel位置で評価し、16点の色を平均する。独立に平均したcoverageを掛ける方式を避け、同じ輪郭のclipによるAAの二重減衰を防ぐ。coverageの精度は1/16刻みで、解析的な面積積分ではない。特異変換の図形は透明として評価する。

PaintGraph2Dは不変のnodeを共有するDAGであり、GPUのRenderGraphとは別の、点の色を定める式である。基本段階ではSolid、3点linear、2円radial、sweep、ImagePattern、Opacity、Clip、Transformを持つ。Color2Dの描画overloadはSolidへ正規化し、命令数を増やさない。Rootとnodeのsnapshotを公開し、外部rendererがbackend内部型を参照せずに解析できるようにする。公開nodeを直接作ってgraphへ挿入するAPIは設けず、factoryで数値・子・深さを検証する。

2点linearは `p0=start, p1=end, p2=start+perp(end-start)`、単一中心radialは `c0=c1=center, r0=0, r1=radius`へ正規化する。3点linearの投影、偏心した2円radialの二次方程式、sweepの角度をそのまま評価する。stopは非減少順を要求し、重複offsetと[0,1]外のoffsetも保持する。Padは端色、Repeatは周期反復、Reflectは往復とし、sweep角はY下向きのradianで指定する。

DrawingProgram2DはCPUの内部コンパイラで、子を先に評価するDrawingInstruction列とVector4データ列へ形状・paint・clipを展開する。各nodeにはpixelからlocalへの逆変換と参照indexを記録する。fragment shaderは固定長の評価配列を順に埋め、rootの色を返す。shader moduleやpipelineを生成するコンパイラではない。

paint深さ64、展開後命令256、データ262144個のVector4を上限とし、clip・shapeも命令上限へ含める。共有nodeも実際の展開分を数える。3点linearの投影分母0、同心同半径のradial、開始終了角が同じsweep、幅0のColorLineのRepeat/Reflectは標準実装でNotSupportedExceptionとする。未対応入力を別gradientへ近似せず、shaderへ渡す非finiteデータも準備時に拒否する。

### 画像patternのsampling契約

ImagePattern2Dはviewを借用し、整数texelのSource、PatternToLocal、X/Y別のClamp/Repeat/Mirror/Decal、Nearest/Linearを保持する。外部samplerは受け取らず、明示したFilterとTileを唯一の設定元にする。単一mip/layerのSampled D2 viewを要求し、Sourceは正の寸法でview内に収める。PatternToLocalとその逆行列はfiniteかつ逆変換可能でなければならない。

Source左上を原点、texel中心を `(i+0.5,j+0.5)`、周期をSource寸法とする。寸法Nに対する整数tap iは、RepeatならpositiveMod(i,N)、Mirrorならr=positiveMod(i,2*N)に対し `r<N ? r : 2*N-1-r`。Clampは端へ固定し、Decalは範囲外を透明とする。負座標にも同じ規則を適用する。

Nearestはfloorしたtexel、Linearは中心基準の4tapを使う。各tapをSource相対でtile処理し、texelごとにsRGB decode・必要なpremultiplyを済ませてから補間する。隣接atlas画像を混ぜないため、画像patternにpaddingを要求しない。標準の共有samplerはnearest-clampで、linear補間はshaderで行う。既にpremultipliedの入力にはalphaを再乗算しない。

### 命令別interfaceとDI

基本段階はIFillRectRenderer2D、IFillRoundedRectRenderer2D、IFillCircleRenderer2Dを持つ。DrawingCommandRenderers2Dは全実装をコンストラクターで借用し、nullを拒否して型付きreadonly参照へ保持する。CanHandle、候補探索、自動fallback、描画中のDI解決やType辞書検索は設けない。専用interfaceの呼び出しは残るため、インライン化や性能向上を実測なしに保証しない。

CreateDefaultは毎回新しい登録表と3標準実装を生成し、GPU資源を作らない。UseGraphics2Dは別DependencyInjectionパッケージで、この3組と登録表をTryAddScopedする。先行するアプリ登録を維持し、後続AddScopedは単一解決のあと勝ちで上書きできる。再Useで既存実装を置き換えない。後続の描画命令追加ではinterface・登録表・CreateDefault・DI・配送を同時に拡張する。

deviceとrendererはアプリが生成し、DIから解決した表を通常のコンストラクターへ渡す。scopeはGPU使用・準備結果・rendererの解放後に破棄する。DIを使わない場合も同じ表を直接newする。外部rendererは命令全体のpaint・clipを担当し、IPreparedCommandBatch2D.Recordで複数のcopy・compute・barrier・render passを記録できる。内部Submit・Wait・Presentは行わず、active passを残さない。

### 汎用pipeline・samplerとの接続

```text
DI: GraphicsPipelineDefinition → shader依存キー
    GraphicsSamplerDefinition → sampler設定
           ↓ ResourceManagerが生成・世代管理
GraphicsResource<IGraphicsPipeline> + GraphicsResource<IGraphicsSampler>
           ↓ Renderer2DResourceStage.RequireAsync
DrawingResources2D(device, pipeline.Value, sampler.Value)
           ↓ Renderer2DResourceとして公開
reference.CreateRenderer() → Renderer2D
```

DrawingResources2Dはnative参照をまとめる不変の値で、所有権、ResourceKey、Managerへの参照を持たない。汎用定義との接続をResources.Graphics.Drawing2Dへ分離する。AddGraphics2Dは公開キーDrawingGraphicsResources2D.VertexShader、FragmentShader、Pipeline、Samplerへ既定定義をTryAdd登録する。アプリは同じキーへ汎用定義を明示登録でき、明示登録同士はあと勝ちになる。定義はStage構築時に固定するのでManager構築前に設定する。

Renderer2DResourceStageはpipelineとsamplerをResident依存として取得し、同一の物理device、Triangle topology、alpha-to-coverage無効、fragmentありを検査して参照群へまとめる。shaderはpipeline定義のResident依存で、fragment artifactはpipeline.Desc.FragmentShader.Artifactから取得する。2D専用のprovider、pipelineキャッシュ、public leaseによる再取得経路は追加しない。独自shaderもpaint・coverage ABIを、共有samplerもsampling契約を満たす必要がある。

AcquireRenderer2DAsyncで非同期に準備済みReferenceを取得し、CreateRendererはその正確な世代のResourceLeaseを一つ取得するだけとする。成功時にleaseをresourceOwnerへ移譲し、失敗時はleaseを返却する。CreateRenderer中にはI/OやGPU生成を行わない。ResourceSnapshotを渡せば他の資源と同じpublicationに揃えられる。UseGraphicsShaders<Renderer2DResource>は既定shader定義の入力artifactキーを選ぶ簡略設定で、明示した公開キーの定義を優先する。

ResourceManagerなしでは利用側が生成したpipelineと任意のsamplerをDrawingResources2Dへ渡す。GPU完了まで自分で保持するか、まとめたIDisposableをresourceOwnerへ渡す。resources省略時だけ、DrawingShaders2Dの埋め込みCPU artifactから最初の標準batch準備時にshaderとpipelineを遅延生成し、renderer内で共有する。sampler省略時はbatchが既定samplerを所有する。カスタム実装がResourcesを要求しなければ既定GPU資源を生成しない。

### 準備・記録・所有権と復旧

PreparedGpuBatch2Dは共有資源の寿命tokenを先に取得し、各命令のargument table、instruction buffer、geometry buffer、upload stagingと1×1の補助textureを準備する。Recordはupload→copy→fragment-read barrier→renderを同じcommand bufferへ記録する。入力の外部textureはSampled、targetはColorAttachmentへ可視化済みであることを利用側に要求する。batchのDeclareResourcesは外部共有texture/bufferアクセスを宣言し、私有資源の内部同期はbatchが担当する。

DrawingResourceState2Dはrendererの一つの所有者を、renderer自身と準備済みbatchの内部tokenで共有する。batchごとのManager leaseやpipeline再取得は行わない。rendererをDisposeすると新規Prepareと新規token取得を閉じるが、既存batchは記録できる。最後の使用者を返すと所有者をDisposeする。native所有者とbatchの個別cleanupは、成功した末尾要素だけを所有リストから除き、失敗した解放を再試行できるようにする。この内部機構はGPU完了を検出しない。

PreparedDraw2Dは一回だけRecordでき、記録途中の失敗も消費済みになる。DrawList2Dは再利用できるが、次の描画は新しいPrepareを行い、先行GPUが使うtableやstagingを書き換えない。準備失敗・取消では生成済みbatchを逆順に解放する。Record失敗時は未送信commandを先に破棄する。Submit後のWait取消やFailedという状態名だけをGPU完了とみなさない。正常終了はGPU完了確認→command破棄→prepared破棄→rendererとReference返却→DI scope破棄とする。DisposeはWaitしない。

validateResourcesは準備時の資源アクセスと記録直前に呼び、Manager経路では元lease.Valueの失効検査へ接続する。HotReload後も既存rendererの参照を差し替えず、次のCreateRendererで新世代を選ぶ。deviceロスト時は使用停止・退役確認・旧prepared解放・device復旧・RebuildRuntimeAsyncをアプリのcoordinatorが進め、同じReferenceから新rendererを作る。CPU artifactは再利用できるが、DrawListが借用するGPU viewは再解決する。失効時に埋め込みへfallbackしない。

数値・範囲の不備はArgumentException/ArgumentOutOfRangeException、局所的な順序違反はInvalidOperationException、破棄後操作はObjectDisposedException、標準経路の対応外はNotSupportedExceptionとする。同一deviceや借用資源の生存を常に検出する保証は設けない。Canvas・rendererへの同時操作と並列Prepareは保証しない。

## 検討した代替案

| 代替案 | 採用しない理由 |
| --- | --- |
| 型・texture順の全体ソート | 半透明の描画順が変わる |
| CanHandleでrendererを探索する | 構築時の選択と失敗条件が不明確になる |
| paintの種類ごとに図形命令を増やす | 図形と塗りの組み合わせでAPIが増える |
| 通常描画にもRenderGraphを必須とする | 直接記録だけを使うアプリへgraphとEffectsの依存を要求する |
| 専用provider・pipeline leaseで資源を再管理する | 汎用定義の生成・世代・所有権管理と重複する |
| hardware linear sampling後にpremultiplyする | 透明texelのRGBが補間へ混入する |

## 結果と影響

CPU記録・実装選択・共有資源・GPU実行の境界が明確になり、DIの有無とResourceManagerの有無を独立して選べる。通常描画はgraphなしで動き、後続の命令も同じpaint・clip・所有権を使える。

一方、現行のfragment評価はpixelごとの命令数とpath edge数に比例し、4×4評価の負荷もある。バッチ内のdraw融合、instancing、tile化、analytic AA、cacheは別の最適化とする。CPU準備時間、GPU時間、draw数、転送量、割り当てを測定して選ぶ。pipelineのblend/cull等はGraphicsの動的描画状態、attachment formatはpassの責務であり、汎用定義へ重複させない。

## 検証方針

- ModelTestsでBegin/End/Cancel、Save/Restore、入力拒否時の状態維持、snapshot、gradientとpattern設定を確認する。
- ExtensionDispatchTestsとDependencyInjectionTestsで順序、同型・同clipの配送、後勝ち、再Use、CreateDefaultでのGPU生成なしを確認する。
- DrawingResourceLifetimeTestsでrenderer破棄後のprepared保持、失効検査、準備失敗・取消、部分生成のcleanup、所有者解放の再試行を確認する。
- RendererResourceTestsで汎用依存、共有、HotReload、新しいdevice世代、ReferenceとLeaseの寿命を確認する。
- Drawing2DTestsのVulkan/wgpu経路で基本図形、gradient、clip相関、patternのtap処理と透明境界を画素比較する。実行結果はPRへ記録し、設計上の受け入れ条件とは区別する。

## API・利用例

比較元はmain `2c86e1d`。以下は本PRで追加する主要宣言である。Graphics型はLumyte.Graphics.Abstractions、行列・ベクトルはSystem.Numericsを使う。

### 基本モデルと実行契約

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public readonly record struct Rect2D(float X, float Y, float Width, float Height);
+    public readonly record struct Color2D(float R, float G, float B, float A);
+    public enum FillRule2D { NonZero, EvenOdd }
+    public enum PathVerb2D { Move, Line, Quadratic, Cubic, Close }
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
+    public enum ImageTileMode2D { Clamp, Repeat, Mirror, Decal }
+    public enum ImageFilter2D { Nearest, Linear }
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
+    public enum ExtendMode2D { Pad, Repeat, Reflect }
+    public readonly record struct ColorStop2D(float Offset, Color2D Color);
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
+    }
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
+    public sealed class Canvas2D : DrawingCanvas2D
+    {
+        public DrawList2D End();
+    }
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
+    public sealed class DrawingResources2D
+    {
+        public DrawingResources2D(IGraphicDevice device, IGraphicsPipeline pipeline, IGraphicsSampler? sampler = null);
+        public IGraphicDevice Device { get; }
+        public IGraphicsPipeline Pipeline { get; }
+        public IGraphicsSampler? Sampler { get; }
+    }
+    public static class DrawingShaders2D
+    {
+        public static ShaderArtifact Vertex { get; }
+        public static ShaderArtifact Fragment { get; }
+    }
+    public sealed class Renderer2D : IDisposable
+    {
+        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers, DrawingResources2D? resources = null, IDisposable? resourceOwner = null, Action? validateResources = null);
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
+    public sealed record ClipElement2D(Path2D Path, Matrix3x2 Transform, FillRule2D Rule);
+    public sealed class ClipStack2D
+    {
+        // 論理的な積集合を表す不変列。各要素の変換は独立して保持する。
+        public IReadOnlyList<ClipElement2D> Elements { get; }
+    }
+    public sealed record DrawingState2D(Matrix3x2 Transform, ClipStack2D Clip);
+    public abstract record DrawingCommand2D(DrawingState2D State);
+    public sealed record FillRectCommand2D(Rect2D Rect, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillRoundedRectCommand2D(Rect2D Rect, float Radius, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed record FillCircleCommand2D(Vector2 Center, float Radius, PaintGraph2D Paint,
+        DrawingState2D State) : DrawingCommand2D(State);
+    public sealed class RenderPreparationContext2D
+    {
+        public IGraphicDevice Device { get; }
+        public DrawingTargetInfo2D Target { get; }
+        public DrawingResources2D Resources { get; }
+        public IDisposable RetainResources();
+        public void ValidateResources();
+    }
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
+}
```

### 記録・paint・配送

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public abstract class DrawingCanvas2D
+    {
+        public void Begin();
+        public void Save();
+        public void Restore();
+        public void ConcatTransform(Matrix3x2 transform);
+        public void Translate(Vector2 offset);
+        public void Rotate(float radians);
+        public void Scale(Vector2 scale);
+        public void ClipRect(Rect2D rect);
+        public void ClipPath(Path2D path, FillRule2D rule = FillRule2D.NonZero);
+        public void FillRect(Rect2D rect, Color2D color);
+        public void FillRect(Rect2D rect, PaintGraph2D paint);
+        public void FillRoundedRect(Rect2D rect, float radius, Color2D color);
+        public void FillRoundedRect(Rect2D rect, float radius, PaintGraph2D paint);
+        public void FillCircle(Vector2 center, float radius, Color2D color);
+        public void FillCircle(Vector2 center, float radius, PaintGraph2D paint);
+        public void Cancel();
+    }
+    public sealed class DrawingCommandRenderers2D
+    {
+        // 全て借用。nullを拒否し、配送先を構築時に固定する。
+        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle);
+        public static DrawingCommandRenderers2D CreateDefault();
+        public IFillRectRenderer2D FillRect { get; }
+        public IFillRoundedRectRenderer2D FillRoundedRect { get; }
+        public IFillCircleRenderer2D FillCircle { get; }
+    }
+}
+namespace Lumyte.Graphics.Drawing2D.DependencyInjection
+{
+    public static class Graphics2DServiceCollectionExtensions
+    {
+        // 3標準実装と登録表をTryAddScoped。deviceやrendererは生成しない。
+        public static IServiceCollection UseGraphics2D(this IServiceCollection services);
+    }
+}
```

### ResourceManager接続

```diff
+namespace Lumyte.Resources.Graphics.Drawing2D
+{
+    public static class DrawingGraphicsResources2D
+    {
+        public static ResourceKey<GraphicsResource<IGraphicsShader>> VertexShader { get; }
+        public static ResourceKey<GraphicsResource<IGraphicsShader>> FragmentShader { get; }
+        public static ResourceKey<GraphicsResource<IGraphicsPipeline>> Pipeline { get; }
+        public static ResourceKey<GraphicsResource<IGraphicsSampler>> Sampler { get; }
+    }
+    public sealed class Renderer2DResource
+    {
+        public IGraphicDevice Device { get; }
+        // native資源の借用参照。利用中は取得元のleaseを維持する。
+        public DrawingResources2D Resources { get; }
+    }
+    public static class ResourceGraphics2DRegistrationExtensions
+    {
+        public static ResourceRegistrationBuilder AddGraphics2D(this ResourceRegistrationBuilder builder);
+    }
+    public static class ResourceGraphics2DExtensions
+    {
+        public static ValueTask<ResourceReference<Renderer2DResource>> AcquireRenderer2DAsync(
+            this ResourceManager manager, CancellationToken cancellationToken = default);
+        // 一つのleaseをrendererへ移譲。GPU資源の生成なし。
+        public static Renderer2D CreateRenderer(this ResourceReference<Renderer2DResource> reference,
+            DrawingCommandRenderers2D? renderers = null, ResourceSnapshot? snapshot = null);
+    }
+}
```

### 直接描画

```csharp
var canvas = new Canvas2D();
canvas.Begin();
var paint = PaintGraph2D.LinearGradient(new Vector2(20, 20), new Vector2(120, 20),
    [new(0, new Color2D(1, 0, 0, 1)), new(1, new Color2D(0, 0, 1, 1))]);
canvas.FillRect(new Rect2D(20, 20, 100, 50), paint);
DrawList2D list = canvas.End();
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using var prepared = await renderer.PrepareAsync(list,
    new RenderTarget2D { View = targetView, LoadOp = AttachmentLoadOp.Clear });
using (var commands = device.CreateCommandBuffer(new()))
{
    // targetはColorAttachment、入力はSampledへ可視化済み。
    prepared.Record(commands);
    commands.Finish();
    using var submission = device.Queue.Submit([commands]);
    await submission.WaitAsync();
}
// GPU完了・command破棄後にpreparedを返す。Wait取消時は別途完了を保証する。
```

### 汎用定義を設定してrendererを取得する

```csharp
// Manager構築前。省略すれば既定の埋め込みartifactを使う。
services.UseGraphicsShaders<Renderer2DResource>(new("shaders/2d-v"), new("shaders/2d-f"));
services.AddGraphicsResource<IGraphicsPipeline>(DrawingGraphicsResources2D.Pipeline,
    new GraphicsPipelineDefinition
    {
        VertexShader = DrawingGraphicsResources2D.VertexShader,
        FragmentShader = DrawingGraphicsResources2D.FragmentShader,
        Label = "app-drawing-2d",
        Optimization = PipelineOptimizationMode.PreferReuse,
    });
services.AddGraphicsResource<IGraphicsSampler>(DrawingGraphicsResources2D.Sampler,
    new GraphicsSamplerDefinition(new SamplerDesc
    {
        MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest,
        MipmapFilter = FilterMode.Nearest,
        AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge,
        LodMaxClamp = 0,
    }));
services.AddResources(resources => resources.UseSource<AppSource>()
    .AddShaderPipeline().AddGraphics2D());
services.AddResourceGraphics();

// deviceとExecutionの起動後。DrawingResources2Dは接続Stageが組み立てる。
using var reference = await manager.AcquireRenderer2DAsync();
using var renderer = reference.CreateRenderer(commandRenderers);
// Prepare・Record・Submit後、GPU完了までpreparedを保持する。
```

### ResourceManagerなしで外部資源を渡す

```csharp
using var vertex = device.CreateShader(vertexArtifact);
using var fragment = device.CreateShader(fragmentArtifact);
using var pipeline = device.CreateGraphicsPipeline(new()
{
    VertexShader = vertex,
    FragmentShader = fragment,
});
var resources = new DrawingResources2D(device, pipeline);
using var renderer = new Renderer2D(device, commandRenderers, resources);
using var prepared = await renderer.PrepareAsync(list, target);
// GPU完了とcommand破棄後にprepared・renderer・pipeline・shaderを解放する。
```
