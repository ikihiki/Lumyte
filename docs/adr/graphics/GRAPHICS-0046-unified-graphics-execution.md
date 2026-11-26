# ADR-GRAPHICS-0046: 描画・型付き処理・画像効果を一つのGPU実行計画へ統合する

- 状態: 採用
- 日付: 2026-11-27
- 基準コミット: `fcde70b3c84a620ab776ca19982ff678fe142315`
- 変更関係: 一部変更
- 変更元: 下記「過去の判断との関係」に対象節と継続範囲を示す。
- 変更対象: GPU処理の展開、資源と引数登録表の所有、準備時期、送信と退役を共通の計画へ集約する。
- 継続する判断: 不変の描画記録、描画順、画素と色の意味、C#転送配置、明示的な送信、GPU完了までの資源保持。

## 背景

2D・3Dの描画と画像効果を組み合わせる作者は、処理の入出力に加え、資源宣言・引数登録・準備・解放の接続を管理している。処理方式を型付き部品へ分割しても、それぞれがGPU資源と登録表を所有すると、効果の実装と寿命管理が重複する。呼出し口の違いを実行機構の違いにせず、一つの計画で処理と所有権を検証する必要がある。

基準コミットの `src/Graphics/Lumyte.Graphics.ImageEffects/ImageEffectDefinition.cs` の `AppendPasses` はRenderGraphを受け取る。`EffectGpuPass.cs` は一時画像とpassを作り、`EffectPreparationContext.cs` は固定資源を保持する。一方、`src/Graphics/Lumyte.Graphics.Drawing2D/Execution/DrawingArgumentPlan2D.cs` は描画の引数確定とuploadを管理し、`src/Graphics/Lumyte.Graphics.RenderGraph/GraphShaderResourceOwner.cs` はlive passの登録表を準備する。共通の `ArgumentTablePlan` は存在するが、その利用だけでは全処理の資源identity・生存期間・完了判定は一つにならない。

## 決定

RenderGraph、型付きパイプライン、画像効果を、共通のGPU実行中間表現を構築する入口にする。効果は領域と色を解析した後、一つの `Lower` 実装から共通passを宣言する。資源、引数登録表、barrier、送信と退役は同じ実行基盤へ集約し、既存実装と利用側を直接移行する。

既存APIとのソース・バイナリ互換は維持しない。旧実行器、旧効果実装、互換facade、旧callbackを囲う不透明な実行経路は残さない。低水準のGraphics抽象とbackendは共通実行基盤の実装に使用するが、管理対象のcomponentが別のallocator、登録表、送信経路を作ることは認めない。

### 利用者の変更前・変更後

次は同じ画像へぼかしを適用する操作である。変更前は基準コードの実在API、変更後は本ADRで追加・変更する提案APIを使う。初期化済みのdevice、入力画像、shader、予算と資源snapshotを例から省略する。

変更前はRenderGraphへ効果を展開し、callback記録中に一時資源と参照を準備する。送信と完了待機は利用側が行う。

~~~csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;

EffectImage output = parent.AppendGaussianBlur(source, 3, 3);
parent.MarkOutput(output.Texture);
using var plan = parent.Compile();
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
plan.Record(commands);
commands.Finish();
using IGraphicsSubmission submitted = device.Queue.Submit([commands]);
await submitted.WaitAsync();
// GPU完了後、commands、planの順で解放する。
~~~

変更後は全componentを同じframe scopeへ宣言し、一回の共通compileと実行区間ごとのprepareを行う。`Record` では資源確保と登録をしない。明示的な `ExecuteAsync` が記録・送信・完了待機を引き受ける。

~~~csharp
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.ImageEffects;

using var session = new GraphicsDeviceSession(execution, device, artifacts, policy);
using var frame = session.CreateFrame(resourceSnapshot, frameBudget);
var graph = frame.AsRenderGraph(); // 同じframe.Planを使う上位APIのview。
PlanImage input = graph.ImportImage(sourceView, sourceUseLease, sourceDescription);
PlanImage output = graph.AppendEffect(new GaussianBlurEffect(3, 3), input);
graph.Export(output);
using var compiled = await frame.CompileAsync(ct);
using var prepared = await compiled.PrepareAsync(ct);
var use = session.CreateExecution(prepared);
await use.ExecuteAsync(ct);
use.Dispose(); // 実完了とcommand退役を確認した後だけ成功する。
~~~

`sourceUseLease` は借用画像の独立した使用権であり、画像allocation自体の所有権ではない。実行失敗・待機取消し時はsessionが実行と未回収ownerを保持し、利用側は状態を確認して `DrainAsync` または信頼されたdevice世代の退役後の回収へ進む。scopeやsessionの `Dispose` は暗黙待機せず、使用中や隔離中の解放を拒否する。

型付き部品として構成する場合も、ぼかしのGPU実装は同じである。型付きdevice値は配置とproducer依存を表し、CPUから画素を読めるという意味ではない。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.ImageEffects;

var g = new PipelineGraph<Device<ImageSchema>>();
var blurred = g.Input.Then(Effects.Stage(new GaussianBlurEffect(3, 3)));
using var template = session.Build(g.Complete(blurred));
var value = graph.Invoke(template, graph.ImageValue(input));
PlanImage output = graph.Image(value);
// Export、CompileAsync、PrepareAsync、明示ExecuteAsyncは上と同じ。
~~~

二つの変更後例は同じ目的の別構築であり、同じ入力を二回実行する指定ではない。`AppendEffect` と `Effects.Stage` は同じ効果定義を宣言する。入口ごとに `AppendPasses` と `Encode` を実装しない。

効果作者は生deviceと独立ownerを受け取る `AppendPasses` を、解析と宣言だけを行う次の実装へ置き換える。kernelとfield tokenはartifactの型付きABIに結び付く。sampler、render state、color形式はkernelの不変descriptorで明示する。

~~~csharp
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.Planning;

public sealed record TintEffect(Vector4 Scale,
    RasterKernel<TintArguments> Kernel, SamplerHandle Sampler) : ImageEffectDefinition
{
    public override int InputCount => 1;
    public override EffectAnalysis Analyze(EffectInputs inputs, ImageRegion requested)
        => EffectAnalysis.Preserve(inputs.Single, requested);

    public override PlanImage Lower(EffectLoweringContext context,
        ReadOnlySpan<PlanImage> inputs, EffectAnalysis analysis)
    {
        var pass = context.Raster("tint", Kernel);
        var arguments = pass.Arguments()
            .Bind(TintFields.Source, pass.Sample(inputs[0]))
            .Bind(TintFields.Sampler, pass.Sampler(Sampler))
            .Set(TintFields.Scale, Scale);
        PlanImage output = pass.ColorOutput(analysis.Output, AttachmentLoadOp.Clear);
        pass.Draw(arguments, vertexCount: 3);
        return output;
    }
}
~~~

`TintArguments`、`TintFields`、kernelとsamplerの初期登録は省略している。効果の `Lower` はnative table、資源allocation、GPU参照、送信を操作せず、この一実装を全ての入口から使用する。

### 過去の判断との関係

主要判断はGPU実行基盤の統合である。次の一部を変更し、既存ADRの判断本文は保持する。

| 変更元と対象節 | 変更する契約 | 継続する判断 |
| --- | --- | --- |
| [GRAPHICS-0015](GRAPHICS-0015-render-graph.md#決定)の「責務と実装範囲」「実用版の所有権とculling」 | RenderGraph独自のcompile・一時資源・記録ownerを共通実行計画へ移す | 宣言に基づく依存・barrier・生存判定、明示送信 |
| [GRAPHICS-0017](GRAPHICS-0017-image-effects.md#決定)の「定義とRenderGraphへの変換」「エフェクト自身に固定のパス実装を持たせる」「資源宣言・所有権・失敗」 | Graph依存AppendPassesとEffectPreparationContextを解析・単一Lowerへ置換する | 画像効果の再利用、領域・色・入力数、不変パラメーター |
| [GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md#決定)の「命令別interfaceとDI」「準備・記録・所有権と復旧」 | 固定命令配送とrenderer固有の資源管理を型付き部品と共通scopeへ移す | CPU記録、painter順、形状・paint・clipの意味 |
| [GRAPHICS-0022](GRAPHICS-0022-2d-render-graph-layers.md#決定)の「Effectsを親RenderGraphの部分ビューとして渡す」「通常batchとgraph compositorの二つの接続」 | layer効果を共通計画の意味論nodeへ記録し、実行経路を一本化する | layer/effect/opacity/clipの順序、背景の取得と置換 |
| [GRAPHICS-0032](GRAPHICS-0032-drawing2d-shared-argument-tables.md#決定)の「Pass 宣言、容量、参照の契約」「Materialize、module、所有期間」 | pass全体固定の容量単位とRecord時materializeを、宣言済draw/dispatch単位とepoch Prepareへ変更する | 不可分実行の容量検査、描画順、間接参照のtable coherence、GPU完了まで保持 |
| [GRAPHICS-0035](GRAPHICS-0035-csharp-authoritative-shader-transfer.md#決定)の「共通の参照情報とbackend固有の所有」「グラフの参照準備と実行時照合」 | symbolic参照recipeと共通の登録・後確定を追加し、component別所有を除く | C#転送配置、数値copy、既存wire参照ABI、whole-table binding、低水準の明示操作 |

### 共通計画と所有者

意味論scene、型付き値依存、GPU資源依存は別概念として保持し、同じ実行基盤で照合する。sceneは何を描くか、パイプラインはどの算法を接続するか、RenderGraph入口はどのGPU資源とpassを宣言するかを表す。一意味論nodeを必ず一GPU passにする規則は設けない。

`GraphicsDeviceSession` はdevice世代、artifact/pipeline/sampler cache、資源identity、予算、退役台帳を所有する。global singletonにはしない。`GraphicsFrameScope` は一frameの宣言、資源version、使用lease、実行区間と登録要求を所有する。subscopeはその制限viewであり、独立したallocator、登録表plan、送信権を持たない。

型付きDAGの一般構築とCPU値処理はCoreへ置き、既存の `src/Core/Lumyte.Execution/JobGraphBuilder.cs` と `ExecutionRuntime` を利用する。Coreに画像・device型を逆依存させない。Graphics部品は共通builderへ宣言し、初期CPU計算、GPU処理、明示Upload/Downloadを同じ依存計画へ接続する。独立runのleaseを再帰的なPrepareで継ぎ足さない。

公開の `IStage` は不変なfactoryを持ち、factoryが要求を申告して、検証後にprocessorを作る。CPU processorはscopeに属する入力leaseを読み、不変出力を公開する。Graphics processorは宣言scopeへkernel、資源use、argumentsを追加する。外部作者も同じ公開契約を使い、既知型switchによる組込み専用経路を設けない。

`Stage` と単項の `Then` は同じnode追加であり、呼出しごとに別nodeを作る。同じPort参照は同producerを共有する。多入力Stageで分岐と合流を表し、foreign graph、cycle、freeze後の変更を拒否する。templateのBuildは算法・schema・artifactを固定し、frame内のInvokeは同一scopeへ有限個のinstanceを作る。

### 効果の解析と一回のLower

`Complete` は意味論宣言をfreezeする。`CompileAsync` は初期CPU producerを実行し、出力要求を集約してbounds、必要入力ROI、色、sample、resolveを解析する。同contextの共有nodeは要求ROIをunionし、異contextや別ROIへ分ける場合はinstanceと予算を先に固定する。その後に各instanceを一回Lowerし、GPU実行構造をfreezeする。

`Analyze` は副作用と資源確保を持たず、要求の収束まで繰返せる。効果実装が一箇所であることと、一invocationにつき一回Lowerすることは区別する。`Lower` 後にROIを変更して再展開する通常経路は作らない。

PlanImageは全論理bounds、格納ROI、pixel原点と写像、format/view、color/alpha、内容versionを保持する。Unknown領域をEmptyとせず、有限target全域へ保守化する。blurのhalo、filterの領域、maskのalpha/輝度を区別する。0入力generatorはcontextに明示された色semanticを使い、入口による暗黙の違いを持たない。

効果の出力領域、必要入力領域、入力damageからの出力damage、色変換と予算を解析結果へ含める。固定shaderとpass構成は効果が選び、runtimeが暗黙のshader融合や算法fallbackを行わない。

### 資源versionと物理identity

共通中間表現は論理資源ID、内容version、view/range、producerとread/write use、kernel、argument recipe、観測可能な出力を持つ。device世代とphysical allocation identityへimportを正規化し、同じ実体を別名でimportしても独立snapshotにしない。

内容versionは不変である。writeは入力version、範囲、full discardまたはpreserveを宣言し、新versionを返す。旧handleを最新内容へ暗黙更新しない。partial preserveは未変更領域も入力versionから読むRMWである。partial discardの範囲外はUndefinedとし、読み出しを拒否する。

同じversionへの複数writerを禁止する。独立した非重複更新を合流する場合は、範囲を検査する明示mergeで後継versionを作る。重複writeはversionを直列化する。同passのtexture sample/write feedbackは初期契約で禁止し、buffer RMWは能力に合う一つのReadWrite useへまとめる。

version追加は自動copyではない。旧readerを全てwrite前へ順序付けできれば同allocationを使える。上書き後にも旧内容が必要なら別配置または明示copyで保存し、保存不能・cycle・予算超過ならcompileを失敗させる。

sessionはframeを跨ぐimportの競合も追跡する。受理済submissionへの依存と、未送信frameのticket順を実行hostが守る。session外の利用はacquire/releaseと実completionを持つinterop leaseを要求する。追跡不能な利用と競合する場合は排他的貸出に制限する。

資源をimported、transient、persistent、exported、artifactに分類し、保持と再利用可否を分ける。借用allocationは破棄せず使用leaseだけを保持する。export leaseが残る画像と、GPU使用中のcache資源はalias・evictionしない。同allocationのbytesはowner単位一回、仕事量はinstance単位で課金する。

### 引数登録と物理binding

同じBindingPlannerが全live taskのshader ABI、参照closure、view/range、実行kind、attachment、backend上限を受け取る。共通管理はnative table一枚を意味しない。不可分な一draw/dispatchを単位とし、安全に共有できる単位だけを同table groupへまとめる。groupの範囲を変えても描画順は変えない。

既存の `ArgumentTablePlan` の予約、重複除去、partitionと一括公開を再利用する。`ShaderResources` は一draw/dispatchの制限viewにする。componentはtableを作成・所有・直接登録しない。tableは資源ownerや同期器ではなく、共通台帳と同じidentityを使う登録機構である。

C#転送配置と既存wire ABIを維持し、symbolic typed field token、resource use、argument recipe、relocation情報を生成器へ加える。数値は既存配置へcopyし、物理参照だけを後確定する。reflectionとの一致を検査するが、数値の別配置をreflectionから再構築しない。

shader fieldをuse tokenへBindすると、型、scope、version、view/range、shader stage/accessを照合する。これを依存とbinding双方の権威にする。attachment、copy、vertex/index、indirectのようなshader field外の使用は別に宣言する。定数Setによるresource参照の偽装を拒否する。

動的resource配列は全到達先集合、索引範囲、最大要素数とupload容量を事前宣言する。shader-data中の推移的参照もcodegenとrelocation metadataから列挙する。集合外参照と無制限の探索は拒否する。同じpatched payloadを読む単位にはtable domainのcoherenceを要求し、domainが変わる場合はcanonical内容から別payloadを作ってpatchする。

logical capacityとphysical descriptor数は別に検査する。slot span、RO/RW、root/remap/helper、stage/combined/attachment、range size/alignment、実ABIを含める。一draw/dispatchが単独で上限超過ならtableの分割だけでは解消しない。宣言済みの合法な別kernel構成を明示選択するか失敗とし、精度や処理を黙って変えない。

現行backendのwhole-table契約を維持する。選択tableの書込み済み全登録が物理bindされ、使わないdescriptorも個数とalias検査から除外できない。attachmentを別passがsampleする場合など、共有tableが不正な参照を露出させる組合せは別tableにする。pass-local subsetへの自動投影を仮定しない。

semantic ResourceUseとは別に、table全登録からPhysicalBindingUseを作る。allocatorは両者の保持区間のunionでallocation/view/registrationをpinし、state、usage、alias、予算を検査する。未使用RW descriptorの物理要件を実shader writeと混同して内容versionを作らない。cullingはsemantic依存と明示rootで行い、物理露出だけでdead producerを復活させない。

GPU参照を含むpersistent payloadは、canonical内容から再patchするか、参照先allocation/table generationをまとめた独立leaseとして保持する。tableだけを交換してbufferを使い回したり、in-flightのpayloadやslotを再番号付けしたりしない。immutable tableを再利用するなら全登録のownerをpinし、保持量も計上する。

### compile・prepare・実行区間

処理順は次で固定する。

1. 確保前に予約し、意味論DAGとtemplate invocationを不変captureする。
2. 初期CPU producerを実行し、要求ROIを解析・合流してinstanceを確定する。
3. 一回Lowerし、出力、外部write、present、KeepAlive、明示CacheCommitをrootとしてcullする。
4. 型付き依存、資源RAW/WAR/WAW、painter順を整列し、readbackで実行区間を切る。
5. provisionalなallocation生存期間とtable groupを計画し、PhysicalBindingUseを加えて再検証する。不成立なら合法なgroup分割か非alias配置へ有限回で保守化する。
6. 現区間のallocation/view/rangeをmaterializeし、native table登録、実GpuRef/projection、argument payloadのpatchをこの順に行う。
7. Lower時に予約したupload/copy taskを埋め、barrier、load/store、記録台帳を確定する。
8. 現区間の全準備成功後だけrecord-readyにし、明示的な実行hostが記録・送信する。

payload領域とupload taskはLower時に最大容量を宣言する。materialize後に未知の資源・edge・taskを追加しない。Recordは登録・allocation・I/Oをせず、自分で開くencoderを例外時も閉じる。

全体compileと区間ごとのprepareを区別する。GPU→Download→CPU→Upload→GPUでは、実完了、map/copy、producerの必要退役を確認してHost値を公開し、そのCPU処理後に次区間をprepareする。GPUから得た個数は事前宣言したExtentExprと最大値の範囲でallocation/dispatchへ適用する。新しい効果や未知資源を発生させる構造変更は拒否する。

後続prepareの失敗は先行GPUの書込みをrollbackしない。全体atomicな外部出力が必要ならprivate targetへ処理し、最後のcommit passで反映する明示policyを使って追加予算を予約する。単一区間のprepare失敗保証を、複数区間全体の保証へ広げない。

### 完了・cache公開・退役

準備、記録、送信受理、GPU完了、command退役を別状態にする。実行ticketはplan、device世代、正確なcommand identityとproducer集合へ結び付く。stage、effect、RenderGraph入口は独自送信しない。待機取消しはGPU取消しではない。

Submit受理不明なら依存owner、table、payloadと予約を隔離する。既知submissionのDrainと、信頼されたdevice世代退役証明による回収を区別する。証明がなければ安全な有限時間回収を保証しない。cleanup失敗は同owner台帳へ残し、成功済み返却を繰返さず再試行する。

cache候補は最終ResourceVersion/view、必要producer集合、実行区間、allocation/payload/registration lease、意味論とartifactのkeyを持つ。候補化だけで処理を生存させず、cache生成目的の実行はCacheCommitを明示rootにする。途中producerだけliveの場合と、後段が最終allocationをalias上書きした場合は公開しない。

必要な全producerの成功、実GPU完了、command退役、最終内容とregistration保持が揃ったときだけ一回のtransactionでResidentへ公開する。部分record失敗、受理不明、cleanup失敗では公開しない。複数区間の一部成功によるcache公開は、独立候補を明示したpolicyだけに許し、既定は全体成功を要求する。

CPU内容cache、配置依存geometry、sample coverage、paint plan、GPU常駐資産、完成画像を区別する。keyには結果が依存するtransform/DPI/sample/color、palette/foreground、背景と資源publication、artifact/device世代だけを含める。資源保持量と実行回数、同時peakを別計上し、全in-flight/cacheを跨ぐ共通ledgerで追加・確保前に予約する。

### 2D・画像効果・地図・文字の境界

CanvasのCPU記録はGraphics実行から分離し、描画順と不変内容を維持する。shape/clipは同sample位置で評価し、未resolveのmembership/paintと、ResolvedImage、raw MaskImageを別schemaにする。同じ半pixelを覆う不透明描画二回は、drawごとresolveしてsource-overするprofileではalpha 0.75となり、全scene末尾の一回resolveへ移して0.5へ変えない。

paint内の隔離Composite、背景とのlayer blend、group完成結果へのopacity、背景snapshotのeffect置換を別演算にする。背景効果の境界はeffect*c + original*(1-c)であり、単純なsource-overへ置換しない。SVG用paint/filterとCOLR用paintは補間空間、alpha、座標、ClipBoxと有限領域を保持し、全て同じ画像演算へ無条件に潰さない。

usvgの正規化結果はgroup/paint/clip/mask/filter/patternの意味をadapterで保持する。文字のpaint DAGとforeground座標、同内容の別配置を区別する。Mapsは全tileのcasingから全tileの内線へのglobal順、camera-relative精度、ownership clipとAA clipの区別、dash距離とpattern位相を渡す。地図のstyle評価・label衝突回避やfont解析は共通実行基盤に持ち込まない。

処理の融合は所有・計画の統合であり、自動shader融合ではない。明示的なfused実装を選ぶ場合も、分離実装と同じtyped入出力、sample/resolve、color、precisionと資源契約を要求する。

### 契約と API 差分

以下は基準コミットに対する主要宣言の抜粋である。新しいPlanningおよびCoreの型付きAPIも本ADRの追加であり、実装済み型として参照しない。descriptor/field tokenの全accessorと低水準backend APIは省略する。旧APIの委譲実装は残さず、呼出し側も移行する。

~~~diff
 namespace Lumyte.Graphics.ImageEffects
 {
     public abstract record ImageEffectDefinition
     {
         public abstract int InputCount { get; }
-        public abstract EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
-        public abstract EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
-        public virtual EffectColorSemantic GetOutputColorSemantic(EffectColorSemantic input);
-        public abstract EffectImage AppendPasses(EffectPreparationContext context,
-            RenderGraph plan, ReadOnlySpan<EffectImage> inputs);
+        // 副作用/確保なし。入力数・色・領域不一致を拒否し、必要ROIと予算を返す。
+        public abstract EffectAnalysis Analyze(EffectInputs inputs, ImageRegion requested);
+        // 確定解析後に一回だけ宣言する。独立owner、table作成、送信は禁止。
+        public abstract PlanImage Lower(EffectLoweringContext context,
+            ReadOnlySpan<PlanImage> inputs, EffectAnalysis analysis);
     }
-    public sealed class EffectPreparationContext : IRenderGraphLifetime
-    {
-        public IGraphicDevice Device { get; }
-        public void Own(IDisposable resource);
-    }
-    public readonly record struct EffectImage(RenderGraphTexture Texture,
-        EffectPixelRect Bounds, EffectColorSemantic ColorSemantic);
+    public sealed class EffectLoweringContext
+    {
+        // 共通builderの制限view。native資源や独立ownerを公開しない。
+        public RasterPlanPass<TArguments> Raster<TArguments>(string name, RasterKernel<TArguments> kernel)
+            where TArguments : struct;
+    }
+    public static class Effects
+    {
+        // 単項画像用。多入力/0入力も同じ基盤で入力数を検証する。
+        public static IStage<Device<ImageSchema>, Device<ImageSchema>> Stage(ImageEffectDefinition effect);
+    }
 }
 namespace Lumyte.Pipelines
 {
+    public interface IStage<A, O> { IStageFactory<A, O> Factory { get; } }
+    public interface IStageFactory<A, O>
+    {
+        StageRequirements Describe(StageInputDescriptions inputs);
+        IStageProcessor<A, O> Create(StageBuildContext context);
+    }
+    public interface IStageProcessor<A, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> input, CancellationToken cancellationToken);
+    }
+    public sealed class PipelineGraph<TInput>
+    {
+        public Port<TInput> Input { get; }
+        public Port<O> Stage<A, O>(IStage<A, O> stage, Port<A> input);
+        public Port<O> Stage<A, B, O>(IStage<A, B, O> stage, Port<A> a, Port<B> b);
+        public PipelineDefinition<TInput, O> Complete<O>(Port<O> output);
+    }
+    public sealed class Port<T>
+    {
+        public Port<O> Then<O>(IStage<T, O> stage);
+    }
 }
 namespace Lumyte.Graphics.Planning
 {
+    // いずれも不変descriptor/所有scopeへ結び付き、利用者がnative参照を捏造しない。
+    public sealed class PlanImage;
+    public sealed class PlanValue<T>;
+    public sealed class ResourceVersion<T>;
+    public sealed class PlanSampledImage;
+    public sealed class PlanSampler;
+    public sealed class ArgumentField<TArguments, TField>;
+    public sealed class ArgumentRecipe<TArguments> where TArguments : struct
+    {
+        public ArgumentRecipe<TArguments> Bind<TField>(ArgumentField<TArguments, TField> field, TField reference);
+        public ArgumentRecipe<TArguments> Set<T>(ArgumentField<TArguments, T> field, T constant) where T : unmanaged;
+        // 全到達先・最大要素・索引範囲を事前宣言し、集合外を拒否する。
+        public ArgumentRecipe<TArguments> BindArray<T>(ArgumentField<TArguments, PlanResourceArray<T>> field,
+            PlanResourceArray<T> references, int maximumCount, IndexRange reachable);
+    }
+    public sealed class RasterPlanPass<TArguments> where TArguments : struct
+    {
+        public PlanSampledImage Sample(PlanImage image);
+        public PlanSampler Sampler(SamplerHandle sampler);
+        public ArgumentRecipe<TArguments> Arguments();
+        public PlanImage ColorOutput(ImageDescription description, AttachmentLoadOp load);
+        public void Draw(ArgumentRecipe<TArguments> arguments, uint vertexCount);
+    }
+    public interface IGraphicsPlanSource<A, O>
+    {
+        // 意味論node/templateを同scopeへ宣言。effectのGPU Lowerはここで先行実行しない。
+        PlanValue<O> Declare(GraphicsPlanBuilder builder, PlanValue<A> input);
+    }
+    public sealed class GraphicsDeviceSession : IDisposable
+    {
+        public GraphicsDeviceSession(ExecutionRuntime runtime, IGraphicDevice device,
+            IStageArtifactProvider artifacts, GraphicsExecutionPolicy policy);
+        public GraphicsFrameScope CreateFrame(ResourceSnapshot resources, GraphicsBudget budget);
+        public GraphicsPipelineTemplate<A, O> Build<A, O>(PipelineDefinition<A, O> definition);
+        public GraphicsExecution CreateExecution(PreparedGraphicsExecution prepared);
+        public ValueTask DrainAsync();
+        // 使用中/隔離中は拒否。暗黙wait/device破壊はしない。
+        public void Dispose();
+    }
+    public sealed class GraphicsFrameScope : IDisposable
+    {
+        // 上位のRenderGraph/効果型へ逆依存しない共通builder。
+        public GraphicsPlanBuilder Plan { get; }
+        public ValueTask<CompiledGraphicsPlan> CompileAsync(CancellationToken cancellationToken);
+    }
+    public sealed class CompiledGraphicsPlan : IDisposable
+    {
+        // 現実行区間のallocation→registration→payload patchを完了して公開する。
+        public ValueTask<PreparedGraphicsExecution> PrepareAsync(CancellationToken cancellationToken);
+    }
+    public sealed class GraphicsExecution : IDisposable
+    {
+        // 明示的にrecord/submitする。取消しでも受理済仕事とownerはsessionに残る。
+        public ValueTask ExecuteAsync(CancellationToken cancellationToken);
+        public GraphicsRecoveryState Recovery { get; }
+        public void Recover(GraphicsDeviceRetirementProof proof);
+        public void Dispose();
+    }
 }
 namespace Lumyte.Graphics.RenderGraph
 {
+    public static class RenderGraphFrameExtensions
+    {
+        // 新rootを作らずframe.Planを参照するviewを返す。
+        public static RenderGraph AsRenderGraph(this GraphicsFrameScope frame);
+    }
     public sealed class RenderGraph
     {
-        public RenderGraph(IGraphicDevice? device = null, string name = "render-graph",
-            IRenderGraphObserver? observer = null, RenderGraphExtensions? extensions = null);
+        // frame.AsRenderGraph()で取得し、同scopeを受ける内部constructorで生成する。
-        public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record);
-        public CompiledRenderGraph Compile(long frameId = 0, RenderGraphCaptureOptions? capture = null);
+        // 同frame scopeの宣言view。生command callbackの拡張口は置換する。
+        public PlanImage ImportImage(IGraphicsTextureView view, IDisposable useLease, ImageDescription description);
+        public PlanImage AppendEffect(ImageEffectDefinition effect, PlanImage input);
+        public PlanValue<O> Invoke<A, O>(IGraphicsPlanSource<A, O> source, PlanValue<A> input);
+        public PlanValue<Device<ImageSchema>> ImageValue(PlanImage image);
+        public PlanImage Image(PlanValue<Device<ImageSchema>> value);
+        public void Export(PlanImage image);
     }
-    public sealed class CompiledRenderGraph : IDisposable
-    {
-        // Record中に一時資源と登録表を準備する。
-        public void Record(IGraphicsCommandBuffer commands);
-    }
-    public sealed class RenderGraphContext;
 }
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public sealed record LayerOptions2D
     {
-        public Func<ImageEffectSubgraph, EffectImage>? Effects { get; init; }
+        // 不変な意味論DAGを記録する。即時Graph callbackは受け取らない。
+        public ImageEffectGraph? Effects { get; init; }
     }
     public sealed record BackdropEffectOptions2D
     {
-        public required Func<ImageEffectSubgraph, EffectImage> Effects { get; init; }
+        public required ImageEffectGraph Effects { get; init; }
     }
-    public sealed class RenderGraphRenderer2D : IDisposable;
-    public sealed class PreparedDrawingPasses2D : IDisposable;
 }
 namespace Lumyte.Graphics.Drawing2D
 {
-    public sealed class DrawingArgumentPlan2D : IDisposable;
-    public sealed class DrawingCommandRenderers2D;
-    public sealed class PreparedDraw2D : IDisposable
-    {
-        public void Record(IGraphicsCommandBuffer commands);
-        public void Dispose();
-    }
-    public sealed class PreparedDrawingFrame2D : IDisposable
-    {
-        public void Record(IGraphicsCommandBuffer commands);
-        public void AttachSubmission(IGraphicsSubmission actualSubmit);
-        public void Retire();
-        public void AbortUnsubmitted();
-        public void Dispose();
-    }
     public sealed partial class Renderer2D
     {
-        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
-            DrawingResources2D? resources = null, IDisposable? resourceOwner = null,
-            Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null);
-        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
-            DrawingResources2D? resources, IDisposable? resourceOwner, Action? validateResources);
-        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
-            CancellationToken cancellationToken = default);
-        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
-            RenderTarget2D target, CancellationToken cancellationToken = default);
-        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
-            RenderTarget2D target, CancellationToken cancellationToken = default);
-        public DrawingArgumentTableMode ArgumentTableMode { get; init; }
-        public DrawingRetentionStatistics2D RetentionStatistics { get; }
+        // 構築済みの型付き処理を直接借用。資源・table・実行ownerはsessionへ集約する。
+        public Renderer2D(GraphicsDeviceSession session, GraphicsPipelineTemplate<DrawRequest2D, Device<ImageSchema>> pipeline);
+        public PlanImage Declare(GraphicsFrameScope frame, DrawingSceneSnapshot2D scene);
     }
 }
~~~

`GraphicsPipelineTemplate` は `IGraphicsPlanSource` を実装する。template factory/processorは公開契約であり、外部作者も同じ宣言・検証を通す。CoreのprocessorがDevice値をCPU可読として扱うことはできず、Graphics能力の制限viewから宣言を追加する。Deviceを含むbundleは再帰的に非CPU可読として扱う。

`ImageEffectGraph` の再利用可能な不変DAGは維持できるが、実行器とownerにはしない。`ImageEffectSubgraph`、`EffectImage`、`AppendEffectsWithContext`/`AppendEffectWithContext`、旧 `RenderGraphContext` のGPU参照取得、`PreparedDraw2D` と `PreparedDrawingPasses2D` に分かれた実行所有は、新しいPlanImageと共通executionへ移す。全Renderer2D constructorと旧Prepare/Record入口を呼出し側も含めて変更し、旧overloadを残さない。

`IArgumentTable`、`IGpuRef`、`IShaderArguments` と低水準Graphicsのcommand APIは共通実行基盤が使用する。これらの既存wire ABIを変更する判断ではない。componentにそれらの独立所有を返さず、session外で使う場合は前述interop境界を守る。

## 検討した代替案

- 独立した実行器をadapterで接続する: 既存の実装境界を保てるが、効果実装、資源配置、table確定、完了判定の分散が残るため選ばない。
- allocatorだけ共通化する: 確保の重複を減らせるが、生存判定と登録表の世代、payload、送信証跡が別計画のままで不十分である。
- 全処理を一つのtableへ登録する: 管理は単純だが、whole-tableの物理露出、attachment競合、binding上限、不要な保持を解決できない。
- 効果へGraph用Appendとpipeline用Encodeを実装する: shaderを共有しても領域・引数・useと所有が二重になる。単一Lowerを選ぶ。
- typed処理をなくしRenderGraphだけにする: resource依存は表せるが、CPU値を含む型付き部品交換と非描画処理の再利用を失う。入口を分け、実行基盤だけを統合する。
- 全効果を自動でshader融合する: pass削減の余地はあるが、色・resolve・clip・filter順序と精度の別判断になる。本判断では明示構成を使う。

## 結果と影響

効果作者は一つの解析・Lowerを実装し、描画とGPU処理の作者は同じ資源と引数計画へ参加できる。table数やpass数を一律に減らす保証ではなく、重複した管理と不整合な所有境界をなくす判断である。実装と利用側を同じ変更で移行し、旧custom effect、独自batch、DI登録、shader payloadの接続も書き換える。互換adapterや二重実行経路は維持しない。

Graphicsの依存方向は、Coreの型付き構築とExecution、その上の共通Graphics計画、さらにRenderGraph・画像効果・Drawing2Dとする。RenderGraphとtyped Graphics処理が互いに実装を参照する循環を作らない。Resources連携は共通sessionへartifact leaseとpublication snapshotを供給する。

受入検証は次を含める。

- Graph、typed構築、2D、単独実行から同じ効果を呼び、同じLower/kernel recipeになる。effectごとの別record処理を検出する。
- 2D→effect→composite→3D/postprocessの全taskが共通計画に見え、dead taskはallocation/table/uploadを作らない。
- sharingと異context/ROI、旧version保持、partial RMW、frame間import競合、range/view違いを検証する。
- 全table登録のPhysicalBindingUse、RO/RW、attachment/combined制限、binding域を跨ぐshader-data、payload relocation、immutable tableの全owner保持を検証する。
- 二つの半coverage描画の0.75、shape/clipの相関、group opacity、mask、色補間差、blur halo、backdrop置換、地図の描画順と継ぎ目を独立期待値で検証する。
- 部分prepare/record失敗、後続区間失敗、待機取消し、送信受理不明、device世代退役、exportとcache eviction、cleanup再試行を検証する。

CPUの構造・所有権、native統合、GPU画像、Browser/AOTは別に受入確認する。文書の構造検査だけで実装成立、画素一致、速度改善を認定しない。数値予算、対応backendとfilter集合は構築時に明示し、不足時の黙った省略や別方式への切替を認めない。
