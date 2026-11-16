# ADR-GRAPHICS-0036: 同じ2D記録を三種類の描画方式で実行し、共通グラフで合成する

- 状態: 採用
- 日付: 2026-11-16
- 基準コミット: `b9c93f001f960221dc13eed5f6edc0bf04914c02`
- 変更関係: 一部変更
- 変更元: [基本2D描画](GRAPHICS-0018-2d-graphics-engine.md#決定)の標準GPU経路、[2Dレイヤー](GRAPHICS-0022-2d-render-graph-layers.md#決定)のCanvasに置いた合成入口
- 変更対象: 描画方式の選択、coverage生成、グラフへの画像出力とレイヤー合成の責務
- 継続する判断: Canvasの純粋な記録、命令順、paint内の隔離合成、CPU clip正規化、明示的なGPU所有権、3Dと共通のRenderGraph・ImageEffects

## 背景

2D描画を使うアプリは、矩形・文字主体のUIと、動的なパス主体の画面に異なる実行方式を使いながら、同じ記録と3Dの画像処理・合成機能を再利用したい。方式ごとにCanvasやレイヤー専用の実行器を作ると、描画内容とGPU戦略が結合し、同じ内容の比較や2Dと3Dの合成が難しくなる。

基準コミットの `src/Graphics/Lumyte.Graphics.Drawing2D/Model/Canvas2D.cs` と `Model/DrawList2D.cs` は不変の描画記録を作り、同プロジェクトの `Execution/Renderer2D.cs` が実行を担当する。一方 `src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/RenderGraphCanvas2D.cs` はlayer/effect命令を記録し、`RenderGraphRenderer2D.cs` が親graphへ展開する。`src/Graphics/Lumyte.Graphics.ImageEffects/EffectImage.cs` と `ImageEffectRenderGraphExtensions.cs` には2D・3D共通の画像、合成、blur、tone mappingの境界が既にある。

## 決定

CanvasとDrawList2Dを方式非依存の入力として維持し、IRenderer2Dを共通契約とし、標準方式のRenderer2D、CPU疎strip方式のSparseStripRenderer2D、GPU compute方式のComputeRenderer2Dを別の具象型として実装する。標準方式は既存の専用描画とcomputeによるパスcoverage生成を組み合わせ、他の二方式は描画領域全体をそれぞれのパイプラインで処理する。各方式の結果を共通のEffectImageへ出力し、レイヤー・画像効果・3Dとの合成は親RenderGraphとImageEffectsで行う。

### 利用者の変更前・変更後

Canvasに図形・paint・clipを記録するコードは変えない。方式名をCanvas、DrawList2D、DrawingScene2Dの型や記録命令へ入れない。変更前は、レイヤーを扱うために別のCanvasと記録型を使う。基準コミットのAPIを使った抜粋は次のとおりで、device、target、bindingsの生成とGPU実行を省略している。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;

var canvas = new RenderGraphCanvas2D();
canvas.Begin();
canvas.BeginLayer(new LayerOptions2D { ContentBounds = new Rect2D(0, 0, 512, 256) });
canvas.FillCircle(new System.Numerics.Vector2(100, 100), 40, paint);
canvas.EndLayer();
RenderGraphDrawList2D layered = canvas.End();
using var renderer = new RenderGraphRenderer2D(
    DrawingCommandRenderers2D.CreateDefault(), layered, target, bindings);
using PreparedDrawingPasses2D prepared = await parent.Append2D(renderer);
```

変更後の提案では、同じ通常Canvasで作ったDrawList2Dを、実行時に選んだrendererへ渡す。次の `paint`、`device`、同一親graph内のDisplayLinearな `world` は生成済みとする。worldがSceneLinearなら、3D側が共通のtone mappingを済ませてから渡す。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using FrameGraph = Lumyte.Graphics.RenderGraph.RenderGraph;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillCircle(new System.Numerics.Vector2(100, 100), 40, paint);
DrawList2D content = canvas.End();
using IRenderer2D renderer = Renderer2D.CreateDefault(device);
// parentはworldを生成した、まだ構築中のFrameGraph。
EffectImage layer = await parent.Append2D(renderer, content, new DrawingImageOptions2D
{
    Bounds = new EffectPixelRect(0, 0, 512, 256),
});
EffectImage faded = parent.AppendOpacity(layer, 0.8f);
EffectImage output = parent.AppendComposite(faded, world, EffectCompositeMode.SrcOver);
parent.MarkOutput(output.Texture);
```

生成する型を `new SparseStripRenderer2D(device)` または `new ComputeRenderer2D(device)` へ変えても、受け手はIRenderer2Dのままで、contentの再記録は不要である。例のlayer内の円は従来と同じ内容であり、レイヤー操作を記録からgraph構築側へ移す。独立した三つの描画内容を別方式で描き、そのEffectImageを同じparentで順に合成することもできる。

呼出元が一つのparentをCompileし、そのplanをRecord、SubmitしてGPU完了を確認する。command、plan、借用renderer・入力資源の順に解放する。Appendはgraphへ固定資源tokenを移すため、rendererのDisposeは新規準備を閉じても追加済みpassを失効させない。送信結果不明や待機取消を完了と扱わない。独立したgraph、内部Submit、内部Wait、2D専用の画像合成実行器は作らない。

### 共通入力と方式選択の境界

方式は生成する具象型で固定する。方式選択enumや全方式を内包する切替factoryは設けない。適応的な速度判定や、入力を見て別方式へ自動的に切り替える仕組みは入れない。メモリ不足や未対応入力でも方式を変更しない。同じ命令列を比較できるよう、backend、数値規則、対象範囲を準備結果の内部keyへ含める。

三方式の実装は `Lumyte.Graphics.Drawing2D` assembly内の実行モジュールに置く。internalのDrawList2D.Commands、保持ページ、DrawRecordPage2Dへ同じassemblyからアクセスする。DrawList2Dを公開配列へ変換したり、backendごとのCanvasや別の公開描画モデルを作ったりしない。Graph接続は既存のfriend assembly `Lumyte.Graphics.Drawing2D.RenderGraph` に置き、Drawing2DからRenderGraphやImageEffectsへの逆依存を追加しない。

直接描画のPrepareAsyncと保持描画の二つのPrepareAsyncは三方式とも維持する。Renderer2Dの既存constructorは標準方式として既存のDrawingResources2Dや型別custom rendererの設定を尊重する。Renderer2D.CreateDefaultと他二型のconstructorは埋込みの組込み資源を使い、外部shaderやcustom戦略の自動変換は行わない。custom命令や外部shaderの独自ABIを使うアプリは既存constructorの標準方式を使う。

### 標準方式：専用描画とcompute coverage

単純矩形・角丸・円は既存の解析的な形状評価とinstance描画、画像・mesh・文字atlasは既存の専用描画を優先する。残るFillPath、StrokePath、DrawLineの形状coverageをcomputeで生成し、fragment側で既存のpaintとclipを評価して、固定機能のSrcOverへ渡す。computeへ色評価や描画間合成を移さない。

CPUは既存の0.125画素の平坦化・stroke展開を行う。computeは既存のshape点評価と同じ4×4位置を検査し、画素ごとの16 bit membershipをuint32のbufferへ格納する。fragmentはbitごとにpaintと正規化clipを評価する。平均coverage一個へ縮約してからpaint・clipへ乗算しない。shape maskは整数格子の保守的boundsだけに確保し、target全域を一律にmask化しない。

形状・transform・pixel origin・DPR・誤差規則・device generationが一致する完成済みmaskは再利用できる。paintだけの変更はshape maskを失効させない。直接描画の保持cacheへ登録するのは完了確認済みの資産だけである。外部shaderがこのmask ABIを持たない場合は既存fragment評価のままとし、shader契約を推測しない。

この標準方式では、computeは形状生成の担当であり、元の描画順、paint、既存batch条件とattachment丸めを維持する。小さいパスや予算外maskを実行時に別方式へ切り替えることはせず、mask予算超過は準備を拒否する。専用経路の適用は入力と登録済み実装による決定的な分類である。

### CPU疎strip方式：CPUで形状をまとめ、GPUで塗る

CPUが平坦化、タイル分類、4×4サンプルの内外判定を行い、描画命令ごとの疎な水平stripへ変換する。stripは連続する完全被覆領域と、境界の16 bit membership列からなる。空領域は出力せず、完全内側に境界maskを持たせない。各stripに元の描画ordinalとpaint・clipの参照を保持する。

GPUはstripをinstance描画し、各サンプルのpaintとCPU正規化clipを評価して、既存の固定機能で描画順に合成する。単純図形も同じstrip列へ取り込めるが、既知の完全被覆矩形を境界maskへ展開しない。画像・gradient・文字のpaint表現は共通のものを使う。image/mask/distance fieldは保守的な矩形stripと既存descriptorを保持し、samplingはGPUで行う。DrawMeshの画素中心での補間coverageは二値membershipへ変換せず、保守的strip内で既存の中心評価を実行し、各サンプルのpaint・clipへ乗算する。純粋なCPU rendererではなく、CPUで疎な描画表現を作ってGPUへ渡す方式である。

同一描画内のstripを重複させず、別描画をpaint・texture順に並べ替えない。coverageの平均だけをstripへ保存せず、shapeとpaintとclipのサンプル相関を維持する。CPUのサンプル判定は[CPU clipの数値規則](GRAPHICS-0033-drawing2d-normalized-clips.md#数値規則と-16-サンプル相関)と同じbinary64の順序付きFMA、半開Y遷移と交差積の符号を使い、strokeは展開した三角形の和集合で判定する。CPUのサンプル判定を正とするため、従来GPUの境界とのbit完全一致は保証しない。独立したCPU oracleで新しい境界規則を確認する。

### GPU compute方式：単純図形も含めてタイルで合成する

CPUは命令・曲線・変換・paintを符号化し、有限な保守的bounds、分割数上限、buffer容量を検証する。GPUが曲線の線分生成、辺のタイル分類とbackdrop伝播、タイル別の描画参照列の生成、coverage・paint評価、順序付き合成を担当する。CPU側の役割を曲線ごとの入力検査・容量計算に限定し、全辺への展開や各画素のshape評価をCPUへ戻さない。Canvasの保存済み状態は符号化時に解決するため、Canvasの可変stackをGPUで直接走査しない。

矩形・円・角丸なども同じ命令列へ取り込み、タイル内の専用処理で評価する。単色矩形のために区間を分割して通常描画へ戻さない。画像・gradient・coverage mask・distance field・meshも共通のpaint/descriptorから評価し、旧fragmentの暗黙微分を前提とせず、samplingに必要な勾配を明示的に与える。DrawMeshの画素中心coverageなど命令固有の既存規則は維持する。

一つの描画内の線分は順不同に集計できるが、異なる描画のordinalは保つ。タイル別の候補から境界を横切る辺だけを扱い、辺のない内側タイルはbackdropの巻数で判定する。nonzero/evenodd、穴、strokeのcap/joinを符号化して処理し、tile boundsだけで内包を推定しない。CPU正規化clipは三方式共通の入力として維持し、そのbinary64判定をGPUで再計算しない。

画素のサンプルは `s=(x+(i+0.5)/4,y+(j+0.5)/4)`、i,j=0..3とする。描画kの色 `Ck=sum(shape_k(s)*paint_k(s)*clip_k(s))/16` を求めてから、透明黒A0へ `Ak=Ck+Ak-1*(1-Ck.a)` をfloat32で順に合成する。シーン全体をサンプルごとに先に合成して最後に平均するAAへ変更しない。

合成中のattachment丸めを省くため、StandardやCpuSparseStripsの各描画後のRgba16Float丸めとは結果が異なり得る。GPUでの曲線分割も既存CPU経路と異なる頂点列になり得る。三方式は同じ描画モデルを受けるが、丸め・輪郭の完全な画素同一性は保証せず、誤差規則とbackend別oracleを使う。既存GPUテストのtoleranceを広げて差を隠さない。

### GPU前処理の有限容量

GPUの可変長出力を無制限なappendへ任せない。CPUは曲線ごとの分割数と、生成線分が触れ得る保守的タイル範囲から出力上限を計算し、固定範囲を予約する。二次・三次Bezierのdevice空間の二階微分上限Mから、各区間の弦誤差が `M/(8*N*N)<=0.125` となるNを選び、線分出力の開始位置をprefix sumで固定する。三次は二つの二階差分のnormの最大値の6倍、二次は二階差分のnormの2倍をMとする。Nは `max(1,ceil(sqrt(M/(8*0.125))))` とし、直線・Close・塗りの暗黙閉辺を含む線分合計は既存の一path 65536辺上限以下にする。計算と上への丸めはbinary64で行い、非finiteや容量超過をPrepareで拒否する。この誤差は幾何的な弦近似の上限であり、GPU座標計算の丸め誤差は別に検証する。

strokeは基準コミットのStrokeStyle2Dにあるwidth、cap、join、miter limitを扱い、GPUは予約済みの範囲にだけ出力する。CPUは同じPathFlattening2D.Strokeの式でroundの分割数r（8以上4096以下）を決めるが、全接続の三角形は展開しない。平坦化辺上限Eとcontour数Cから、bodyは2E、joinはE+C箇所、capと孤立点は合わせて3C箇所を上限とし、一箇所max(2,r)三角形を予約する。roundの必要分割が4096を超える場合、または三角形予約合計が既存の65536を超える場合は準備を拒否する。geometry・paint・clipを含む既存の累積262144 Vector4予算と総作業bytesにも準備時に課金し、上限へclampして誤差条件を破ることはしない。

辺参照の予約は各曲線のcontrol hullが触れる全タイル数とNの積を上限とし、strokeではwidth・square cap・round cap・miter limitを含む保守的なdevice外接領域へ拡張し、参照上限を「展開後の全三角形辺数上限 × 拡張後boundsのタイル数」とする。単なる元曲線のNをstroke辺数に流用しない。描画参照は描画boundsのタイル数を上限にする。backdropの行・列とprefix scan領域も描画boundsから固定長を予約し、輪郭がない内側タイルへの巻数伝播を辺参照容量から落とさない。実出力はGPUのcount/scanで詰めるが、容量を増やすためのreadback・再submitは行わない。

保守的上限が大きいだけで実際は小さな入力も拒否され得る。この制約を隠して全シーン対応とはしない。GPUでの範囲検査とcapacity assertionを入れ、不正入力やoverflowで別領域へ書かない。入力検査・上限計算とGPU出力数の整合は受け入れ条件であり、予約上限を破る実装は描画の近似や黙った切捨てで扱わない。

### 画像出力と3Dとの共通合成

Graphの新しいAppend2Dは、boundsに対応する単一mip/layerのRgba16Float、linear premultiplied RGBA、DisplayLinearのEffectImageを返す。空の描画内容でも指定された正のbounds全体を透明に初期化する。PixelScaleは正の有限値、Boundsは有限な整数の半開区間で、左上がPixelOriginとなる。非整数座標の描画内容を丸めず、画像出力のextentだけを整数にする。

StandardとCpuSparseStripsはgraphのRenderAttachment | Sampled textureへ描く。GpuComputeはfloat32 RGBAの書込可能bufferへ結果を作り、通常のresolve passで同じtexture形式へ出力する。基準APIにはstorage textureがないため、新しいtexture usage/state/bindingを暗黙に仮定しない。Graph画像へのresolveは透明の出力先を上書きし、背景を読み込まず、buffer値の二重premultiplyやsRGB再decodeを行わない。

既存の直接描画PrepareAsyncは別の出口契約を持つ。GpuComputeは透明から作った結果を、Loadした既存attachmentへ固定機能のpremultiplied SrcOverで合成し、上書きしない。Clearでは既存どおりclear色をsRGBからlinearへ変換・premultiplyして先頭で一回clearし、その後に同じ合成を行う。targetは入口・出口ともColorAttachmentで、Sampled/Storage/CopySourceを追加要求しない。空描画のClearも実行する。直接描画でも中間丸めを省く差は生じるが、既存背景を消す変更ではない。

出力を3Dへ重ねる、2Dレイヤーを重ねる、opacity、blur、backdropの生成・置換はImageEffectsと親graphのpassで表現する。既存のAppendCompositeは同じColorSemanticの二画像を要求する。SceneLinearな3D画像へDisplayLinearな2Dをそのまま合成せず、3Dのtone mapping後に重ねる。3D面への貼付けも同じSampled画像を使えるが、色semanticを3D側で明示的に合わせる。depthを必要とする配置やpresentationは3D・host側の責務である。

方式の選択単位は独立した描画内容またはレイヤーであり、一つの命令列を図形種別ごとに三方式へ分割しない。各画像は隔離された群として扱われる。A、B、Cの描画順を保持したいとき、AとCだけを同じ画像へ先にまとめてBへ重ねてはならない。group opacityは個々の描画のopacityとは別に画像へ一度適用する。

### 親graphへのpass追加と資源寿命

Append2Dは同じdeviceのparentがIsBuildingである間に呼ぶ。内部の共通準備処理はtargetのdescriptorとpixel originからpass用データを作り、実viewはCompile後のcallbackで解決する。直接描画のPrepareAsyncへ架空のviewを渡したり、async準備をRecord callback内で実行したりしない。

追加するcopy・compute・raster・resolveは独立したgraph passとして宣言する。UseBuffer/UseTextureのscopeとstateから、copy→compute、compute→fragment、raster→後続samplingの依存を親graphが作る。内部に大きな不透明な実行器を置いてgraphの依存解析を迂回しない。CPUで用意する入力のuploadとGPUで生成する出力を区別し、culled producerを読ませない。

compute出力bufferのReadWrite登録とfragmentのReadOnly登録はpassごとの別表へ置く。選択表全体がbindingへ投影される基準コミットの `src/Graphics/Lumyte.Graphics.Shared/ShaderBindingData.cs` の制約に従い、同じ範囲のRO/RWを一表へ同時登録しない。表のbinding admissionとGPU barrierは両方必要である。GPU scanは複数dispatchとgraph依存で実装し、workgroup間をローカルbarrierだけで同期しない。

画像の一時資源はgraphがCreateTextureで作る。shader dataのABI登録と非同期mapをCompileより前に完了するため、bufferは準備ownerが作り、親graphへ準備専用の共有資源として登録する。この内部登録は複数passの依存・未初期化read検査を有効にし、外部importへのwriteのようなculling rootにはしない。単一pass専用のUseLocalBufferへ複数passのbufferを登録しない。固定pipeline、upload、rendererの資源tokenはIRenderGraphLifetimeへまとめて準備開始前にRetainする。成功したCompileでplanへ移し、未Compileの放棄、Append失敗、Compile失敗でも同じownerがcleanupを保持する。Append失敗後はparentを捨てて再構築し、部分的な追加を成功扱いしない。

直接・保持描画では既存の一回Record、AttachSubmission、完了とcommand破棄後のRetireを維持する。immutableなCPU記録・形状・strip・paintのcacheと、frameごとに書くmask・tile・scan・色bufferを分け、in-flight frame間で可変領域を共有しない。cache keyにはbackend、入力identity、transform、clip、origin、DPR、extent、誤差規則、device/resource generationを含める。paint変更だけで再利用できるshape cacheと、paint込みの画像cacheを混同しない。

Graph経路は公開APIに送信結果の通知を持たないため、RecordしただけでGPU residencyをrendererへ公開しない。Graphの一時GPU出力はplan内だけで使い、GPU完了後のplan解放で廃棄する。フレーム間ではCPUの不変データを再利用できるが、Graphをまたいだ完成済みGPU atlasや画像の再利用を自動で保証しない。呼出元が明示的に保持・importする画像は既存の外部資源寿命契約に従う。

### 上限、失敗と既存APIの互換性

MaxWorkingBytesは一つのPrepareまたはAppendで到達するCPU配列・upload・GPU入力・一時出力・登録表の保守的な所有bytes上限で、既定256 MiB、正数とする。cache hitでも使用するallocationを一度課金し、同一ownerを重複計上しない。既存のretained idle予算も別に維持し、driver全体のVRAM上限を保証する値とはしない。計数とbyte計算はcheckedで、CPU配列の確保前にも上限を検査する。

mask/stripの境界データ、線分、tile参照を含め、既存のgeometry・paint・clip上限を維持する。tile参照は一回の準備で最大4,194,304、scan・mask・色bufferも総予算へ含める。buffer/binding/dispatch/workgroupはDeviceCapsと実際の引数表admissionで検証する。256 invocationの16×16workgroupを基本とし、対応artifactとpipelineの生成成功を必要とする。

Standardの専用経路にcompute不要な入力しかなければcompute pipelineを作らない。compute対象を含む場合のcapability不足、予算超過、未対応入力はNotSupportedException、optionsやboundsの不正はArgumentOutOfRangeException/ArgumentException、取消はOperationCanceledExceptionとする。backendの生成・確保失敗は元の例外を保持する。失敗による方式変更、AA精度低下、入力の切捨ては行わない。

RenderGraphCanvas2D、RenderGraphDrawList2D、既存のAppend2D(RenderGraphRenderer2D)とレイヤーAPIは互換入口として残す。新経路はそれらを要求せず、通常のDrawList2Dを直接画像化する。既存のlayer/effect命令を通常DrawList2Dへ混ぜたり、既存の直接描画がlayerを受理するように変えたりしない。既存APIの廃止・削除とResources/DIの方式別選択は今回の判断に含めない。

### 契約とAPI差分

以下は基準コミットの公開APIとの差分である。Renderer2Dは標準方式の具象型として残し、既存の5引数・6引数constructorと直接・保持描画のメソッドを削除しない。IRenderer2Dに既存の準備・保持統計の契約を抽出し、借用deviceの照会を追加する。他二型は同じ契約を実装し、それぞれが自分のpipeline・cache・所有者を管理する。共有処理は内部の委譲で再利用し、publicな継承階層や方式switchへまとめない。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    public interface IRenderer2D : IDisposable
+    {
+        // 同一deviceの事前検査用。借用であり、rendererはdeviceを破棄しない。
+        IGraphicDevice Device { get; }
+        // 発行した準備・再利用と所有bytes。driver全体の統計ではない。
+        DrawingRetentionStatistics2D RetentionStatistics { get; }
+        // 一回Recordするowner。内部Submit/Waitなし。完了後の解放は共通契約。
+        ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
+            CancellationToken cancellationToken = default);
+        // sourceはawait完了まで保持。AttachSubmissionとRetireの契約を維持。
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+    }
+    public sealed record DrawingRendererOptions2D
+    {
+        // CPU・GPUの所有bytes上限。正数。方式を選ぶ設定は持たない。
+        public long MaxWorkingBytes { get; init; } = 256L * 1024 * 1024;
+    }
-    public sealed partial class Renderer2D : IDisposable
+    public sealed partial class Renderer2D : IRenderer2D
     {
+        public IGraphicDevice Device { get; }
+        // 標準方式だけを生成。optionsは検証・snapshot化、GPU資源は遅延生成。
+        public static Renderer2D CreateDefault(IGraphicDevice device,
+            DrawingRendererOptions2D? options = null,
+            DrawingRetentionOptions2D? retentionOptions = null);
     }
+    public sealed class SparseStripRenderer2D : IRenderer2D
+    {
+        // deviceは借用。optionsを検証・snapshot化し、GPU資源は準備時に生成。
+        public SparseStripRenderer2D(IGraphicDevice device,
+            DrawingRendererOptions2D? options = null,
+            DrawingRetentionOptions2D? retentionOptions = null);
+        // Device、RetentionStatistics、三つのPrepareAsync、Disposeを共通契約どおり実装。
+    }
+    public sealed class ComputeRenderer2D : IRenderer2D
+    {
+        // deviceは借用。optionsを検証・snapshot化し、GPU資源は準備時に生成。
+        public ComputeRenderer2D(IGraphicDevice device,
+            DrawingRendererOptions2D? options = null,
+            DrawingRetentionOptions2D? retentionOptions = null);
+        // Device、RetentionStatistics、三つのPrepareAsync、Disposeを共通契約どおり実装。
+    }
 }
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
+    public sealed record DrawingImageOptions2D
+    {
+        // 正の寸法とcheckedな整数端点。原点を共通device座標へ保持する。
+        public required EffectPixelRect Bounds { get; init; }
+        public float PixelScale { get; init; } = 1;
+    }
     public static class Drawing2DRenderGraphExtensions
     {
+        // 同じdeviceの構築中graphへ追加。固定資源はgraph/planが所有する。
+        // Rgba16Float・DisplayLinear・premultipliedの透明初期化済み画像。
+        public static ValueTask<EffectImage> Append2D(this RenderGraphBuilder graph,
+            IRenderer2D renderer, DrawList2D content, DrawingImageOptions2D options,
+            CancellationToken cancellationToken = default);
+        // sourceはawait完了まで保持。frameへ可変sceneの借用を持ち越さない。
+        public static ValueTask<EffectImage> Append2D(this RenderGraphBuilder graph,
+            IRenderer2D renderer, DrawingSceneReadLease2D source, DrawingImageOptions2D options,
+            CancellationToken cancellationToken = default);
+        public static ValueTask<EffectImage> Append2D(this RenderGraphBuilder graph,
+            IRenderer2D renderer, DrawingSnapshot2D source, DrawingImageOptions2D options,
+            CancellationToken cancellationToken = default);
     }
 }
```

Graph宣言のRenderGraphBuilderは `Lumyte.Graphics.RenderGraph.RenderGraph` のalias、EffectImage/EffectPixelRectは `Lumyte.Graphics.ImageEffects`、CancellationTokenとValueTaskはSystemの既存型である。IRenderer2Dの利用側は具象型を調べず、constructor injectionでも同じ契約を受け取れる。利用側が生成した具象instanceをIRenderer2Dとして渡し、既存DI登録が自動的に方式を選ぶとはしない。Disposeは新規Prepareを閉じ、破棄後のPrepareはObjectDisposedExceptionとするが、既存の準備ownerやGraphへ移したtokenは完了まで保持する。同一instanceの並列Prepareは保証しない。公開CanvasへGPU資源やgraph handleを保存しない。

### インターフェースからGraph準備への接続

IRenderer2DはDrawing2D assemblyに置き、RenderGraphやImageEffectsの型を公開メンバーへ入れない。Graph adapterは、同じassemblyにある内部のIDrawingPassRenderer2Dを介してpass別の準備結果を取得する。組込みの三型が両方の契約を実装する。単なるRenderer2Dへのdowncast、型名判定、方式enumのswitchは使わない。

内部の準備はtargetのdescriptorとoriginだけで実行し、戻り値はcopy・compute・raster・resolveごとの資源宣言と記録callback、保持すべきownerを持つ。既存friendのGraph adapterがそれを親graphへ変換する。新しい公開命令配列やCanvas別型、Drawing2DからGraphへの逆依存は不要である。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    internal interface IDrawingPassRenderer2D
+    {
+        ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawList2D content,
+            DrawingTargetInfo2D target, CancellationToken cancellationToken);
+        ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawingSceneReadLease2D source,
+            DrawingTargetInfo2D target, CancellationToken cancellationToken);
+        ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawingSnapshot2D source,
+            DrawingTargetInfo2D target, CancellationToken cancellationToken);
+    }
+    // 内部準備結果。native targetを保持せず、pass列と固定ownerを所有する。
+    internal sealed class DrawingPassPlan2D : IDisposable
+    {
+        public void Dispose();
+    }
 }
```

この内部契約は組込み三型のGraph統合用である。公開IRenderer2Dを第三者が実装しても、内部契約を自動取得するわけではない。新Append2Dへ内部契約のない実装を渡した場合は、graphを変更する前にNotSupportedExceptionで拒否し、標準方式へ置き換えない。第三者のGraph backend plugin境界を新設する判断は含めない。既存の型別custom rendererは標準Renderer2D内で引き続き使える。

## 検討した代替案

- 一つのRenderer2Dと方式enumで切り替える: 生成入口は一つになるが、方式固有の依存・cache・制約が一型へ集中する。IRenderer2Dと別の具象型を使い、合成側は共通契約だけを受け取る。
- 方式ごとにCanvasを分ける: 型だけで実行を固定できるが、内容の再利用と比較、上位UIの方式非依存性を失う。同じ記録とrenderer選択へ分ける。
- 単純図形を常に既存描画へ戻す: 標準方式には適するが、compute方式の区間を分断する。方式内部の専用処理へ統合する。
- 全用途をcomputeへ統一する: 実行器は一種類になるが、既存の解析図形・文字atlas・固定機能合成を活かす通常経路を失う。用途に応じて三方式を明示選択する。
- 全coverageを平均alpha一個にする: maskとstripは小さくなるが、paint・clipとのサンプル相関が失われる。境界のmembershipを保持する。
- 各方式が独自のlayer/effect機構を持つ: 最適化は閉じるが、3Dと同じ処理・所有権を重複実装する。EffectImageと親RenderGraphを共通境界にする。
- computeがtextureへ直接書く: resolveを減らせるが、基準のusage/state/bindingにない機能を必要とする。既存のbuffer書込みと通常passで画像化する。
- GPU出力を読み戻して可変bufferを再確保する: 容量効率は改善し得るが、内部submit/waitとCPU往復が増える。保守的な上限の予約と事前拒否を選ぶ。

## 結果と影響

利用者は同じ描画内容を三方式へ渡し、方式ごとのCanvasを作らずに、3Dと同じ画像処理・合成へ接続できる。方式をレイヤー単位で選べる一方、画像化と合成にはallocationと帯域の費用があり、単一の直接描画より常に速いとは保証しない。

既存の96 B record、共有paint、CPU clip、明示的なGPU完了責任を共通基盤として使う。各方式の形状前処理と中間表現は異なり、同じ公開入力を受けることを理由に同じuploadや同じ画素結果を保証しない。旧ADRの本文は変更せず、採用・実装・性能検証を別の事実として扱う。

### 受け入れ条件

- IRenderer2D経由で同じDrawList2D、保持scene/snapshotを三つの具象型へ渡し、Canvas再記録やbackend固有命令が不要なことを確認する。方式別のcacheとgenerationを分離し、transform・paint・order・extentだけの変更を個別に検証する。
- Standardの単純図形・文字atlasの専用処理を維持し、mask生成の4×4 bitとpaint・clip相関を検証する。CpuSparseStripsは内側圧縮・境界bit・非重複と順序、GpuComputeは単純図形混在・辺分類・backdrop・scan・描画ordinalを検証する。
- Bezierの分割誤差と予約上限、strokeのcap/join/miter、NonZero/EvenOdd、穴、特異変換、退化gradient、全paint・画像・mesh・distance fieldをbackend別oracleで検査する。三方式の画素差を保存し、既存テストのtoleranceを緩和しない。
- bounds原点、DPR、空内容の透明初期化、group opacity、複数方式の重ね順、3D tone mapping後の合成を確認する。直接描画ではRenderAttachmentのみのLoad先の既存色を保持し、Clearの変換・空描画のclearも検証する。semantic不一致と別graphの画像は拒否する。
- 三つの組込み型をIRenderer2Dで受けたGraph追加が具象型判定なしで動くこと、内部Graph契約のない外部実装はgraphを変更する前に拒否されることを確認する。
- 親graphのcopy/compute/raster/resolve依存、pass別のRO/RW表、culling、未初期化read、Compile前放棄と失敗、renderer先行Dispose、GPU完了後のplan解放、待機取消とdevice lossを検証する。
- capability・予算・参照数の境界とoverflowを試し、動的fallback・切捨て・GPU範囲外書込みがないことを確認する。GraphのRecordだけでGPU cacheをresidentにしない。
- 専用矩形・文字、画像主体、静的保持、パス変形、単純図形混在、高overdraw、高解像度を分けて比較する。CPU準備、GPU前処理、画素評価、最終合成、転送・中間bytesを区別し、同じ出力範囲とGPU完了境界で測る。未測定の高速化を保証しない。
