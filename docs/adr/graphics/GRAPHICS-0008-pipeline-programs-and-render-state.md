# ADR-GRAPHICS-0008: PSOのシェーダープログラムと描画状態の分離

- 状態: 置換済み（実行時検証の方針のみ。その他の決定は引き続き採用）
- 日付: 2026-10-09
- 変更日: 2026-10-10
- 置換範囲: shader／program／deviceの保持カウンターによる解放拒否とSubmit時の寿命再検証。利用者が寿命と同期を管理し、検証のための追跡費用を省く方針へ変更する。
- 後継: [ADR-GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)。以下の本文は判断時点の記録であり、上記の実行時検証には後継を適用する。

## 背景

PSOへshader・rasterizer・blend・depth/stencil・attachment formatを一緒に含めると、同じshaderを使う描画でも状態の組み合わせごとにPSOが増える。shader最適化に必要な情報と、描画状態を別に扱い、shaderを再利用しながら描画時に状態を設定できるようにしたい。

[NoGraphicsAPI](https://github.com/sebbbi/NoGraphicsAPI)はresource bindingとvertex layoutをpipelineから除き、viewport・scissor・depth/stencilを独立に設定する。ただし、そのGraphicsPSODescにはrasterization、blend、attachment formatが残っている。本設計では、その分離方針を参考にしてshader programと実行時の状態をさらに分ける。

DirectXの[Partial Graphics Programs仕様](https://github.com/microsoft/DirectX-Specs/blob/master/d3d/PartialGraphicsPrograms.md)は、shaderを含むpartial programを先にcompileし、他のstateを後からlinkする方向を示している。固定機能がshader codeとして実装される場合もあるため、「固定機能なら全てshader最適化と無関係」とは扱えない。特にprimitive topology、stage間のlinkage、alpha-to-coverageなどのcompile依存を残す必要がある。

関連する[shader成果物とmodule](GRAPHICS-0006-shader-compilation-and-modules.md)は、全targetのcode・reflection・entry・stage等を含むopaque binaryを扱う。本ADRはそのmoduleからGPU用programを作り、描画時の状態設定からbackendが必要なnative pipelineを解決する契約を定める。

## 決定

### 分離する責務

`IGraphicsPipeline`はshaderの組、stage間linkage、primitiveの分類、shader生成に影響するcompile設定を所有する不変なshader programとする。描画状態はrender encoderへ設定し、attachmentのformat・sample countは現在のrender passの実attachmentからbackendが取得する。

利用者はSetPipelineへIGraphicsPipelineを渡し、SetRenderState等で状態を設定してdrawを発行する。状態とattachmentを合わせた準備APIや別の公開binding objectは設けない。同じprogramを異なるblend、cull、depth/stencilとrender passで再利用できる。

backendの具象classがinterfaceを直接実装し、共通の所有wrapperやbackend contractを別に設けない。公開契約は`Lumyte.Graphics.Abstractions`へ配置し、device生成はbackend固有のままとする。

shader programに残すのはvertex／fragmentの組、stage間linkageとresource ABI、primitive topologyの分類、alpha-to-coverageである。entry・stage・workgroup size・resource layoutはshader binaryのmetadata／reflectionから取得し、Descで再指定しない。初期範囲はvertexと任意のfragment、およびcomputeとする。mesh・tessellation・geometry、view instancing、特殊なline rasterization、forced sample countは追加のcompile契約が必要なので別途設計する。

描画状態へ分けるのは正確なlist／strip topology、strip index format、culling・front face・depth bias、color targetごとのblendとwrite mask、depth/stencil、sample maskである。attachmentのformatとsample countもprogramから分け、現在のpassから取得する。vertex属性layoutは設けず、vertex indexからshader内でbufferを読むvertex pullingを基準にする。

この境界は共通APIの責務であり、全deviceでnative stateをdynamicに設定できるという保証ではない。backendがstateをnative PSOへ固定する必要があれば、draw時にその組み合わせのnative PSOをcacheから取得し、cache miss時には生成またはlinkする。API上のprogram再利用と、driver内でshader再compileが起きないことを区別する。

### 初期のresource境界

現在のtexture／pass契約で利用できる単一sampleのcolor attachmentを初期の実行範囲とする。depth/stencil texture format・attachment、MSAA／resolveはresourceとpassの拡張で追加する。depth／stencil testやwriteを有効にした状態は対応するattachmentがないdrawで拒否する。AlphaToCoverageEnableも実attachmentのsample count条件に従って拒否する。

初期のprogramはresource bindingやroot dataを必要としないshaderを受け付ける。binaryのreflectionにresource／push constantがあるprogramはNotSupportedExceptionとし、descriptorを暗黙に割り当てない。参照追跡とArgument Tableへの接続は、その契約とともに追加する。stage入出力のlocationと型をreflectionで検証し、vertex input layoutは設けない。computeのworkgroup各軸と積をdevice上限で検証する。

### 公開API

比較元はorigin/main（6717920）。説明・既定値・失敗条件をコメントとして示す。[CommandBuffer](GRAPHICS-0007-command-buffers-and-submission.md)のpass開始・終了、barrier、submitと完了待機の契約を使用し、既存のIRenderEncoder／IComputeEncoderへpipelineの接続点を追加する。direct draw／dispatchを本ADRで追加する。root data・resource binding、indexed／indirect命令は追加の契約で扱う。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public sealed record DeviceCaps
     {
+        public uint MaxComputeWorkgroupsPerDimension { get; init; }
+        public uint MaxComputeWorkgroupSizeX { get; init; }
+        public uint MaxComputeWorkgroupSizeY { get; init; }
+        public uint MaxComputeWorkgroupSizeZ { get; init; }
     }
     public interface IGraphicDevice
     {
+        // shader stage、同一device、stage間linkage、resource ABIを検証する。
+        // backendによっては状態とattachmentが確定するdraw時までnative生成を遅らせる。
+        IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);
+        IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc);
     }
+    public sealed record GraphicsPipelineDesc
+    {
+        public string? Label { get; init; }
+        public required IGraphicsShader VertexShader { get; init; }
+        // nullはfragmentなし。color targetを持たないdepth-only描画に使う。
+        public IGraphicsShader? FragmentShader { get; init; }
+        // 正確なlist／stripは描画状態側。分類はshader compilationの契約に残す。
+        public PrimitiveTopologyClass TopologyClass { get; init; } = PrimitiveTopologyClass.Triangle;
+        // shader生成へ影響するため描画状態へ移さない。
+        public bool AlphaToCoverageEnable { get; init; }
+        // native link／生成時のhint。時間やdriver再compileの有無は保証しない。
+        public PipelineOptimizationMode Optimization { get; init; } = PipelineOptimizationMode.PreferReuse;
+    }
+    public sealed record ComputePipelineDesc
+    {
+        public string? Label { get; init; }
+        public required IGraphicsShader ComputeShader { get; init; }
+    }
+    public interface IGraphicsPipeline : IDisposable
+    {
+        // 検証済みのsnapshot。shader moduleを所有権上保持する。
+        GraphicsPipelineDesc Desc { get; }
+    }
+    public interface IGraphicsComputePipeline : IDisposable
+    {
+        // computeはgraphicsの描画状態・attachmentとの結合を必要としない。
+        ComputePipelineDesc Desc { get; }
+    }
+    public enum PipelineOptimizationMode { PreferReuse, FullSpecialization }
+    public enum PrimitiveTopologyClass { Point, Line, Triangle }
+    public enum PrimitiveTopology { PointList, LineList, LineStrip, TriangleList, TriangleStrip }
+    public enum IndexFormat { Uint16, Uint32 }
+    public sealed record GraphicsRenderStateDesc
+    {
+        public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;
+        // indexed stripのformatとrestart値を固定。list／非indexed stripではnull。
+        public IndexFormat? StripIndexFormat { get; init; }
+        public RasterizationStateDesc Rasterization { get; init; } = new();
+        public DepthStencilStateDesc DepthStencil { get; init; } = new();
+        // 現在のpassのcolor slotと同じ数。blendなしでも各slotを明示する。
+        public IReadOnlyList<ColorBlendStateDesc> ColorTargets { get; init; } = [];
+        public uint SampleMask { get; init; } = uint.MaxValue;
+    }
+    public enum CullMode { None, Front, Back }
+    public enum FrontFace { CounterClockwise, Clockwise }
+    public sealed record RasterizationStateDesc
+    {
+        public CullMode Cull { get; init; } = CullMode.None;
+        public FrontFace FrontFace { get; init; } = FrontFace.CounterClockwise;
+        public bool DepthClipEnable { get; init; } = true;
+        // 整数bias。全backendで正確に表せる範囲は[-16777216, 16777216]。
+        public int DepthBiasConstant { get; init; }
+        // finiteのみ。対応featureがなければnonzero clampはNotSupportedException。
+        public float DepthBiasSlope { get; init; }
+        public float DepthBiasClamp { get; init; }
+    }
+    public enum BlendFactor
+    {
+        Zero, One, SourceColor, OneMinusSourceColor,
+        SourceAlpha, OneMinusSourceAlpha, DestinationColor, OneMinusDestinationColor,
+        DestinationAlpha, OneMinusDestinationAlpha, SourceAlphaSaturated,
+        Constant, OneMinusConstant,
+    }
+    public enum BlendOperation { Add, Subtract, ReverseSubtract, Min, Max }
+    public sealed record BlendComponentDesc
+    {
+        public BlendFactor Source { get; init; } = BlendFactor.One;
+        public BlendFactor Destination { get; init; } = BlendFactor.Zero;
+        // Min／MaxではSourceとDestinationをともにOneにする。
+        public BlendOperation Operation { get; init; } = BlendOperation.Add;
+    }
+    [Flags]
+    public enum ColorWriteMask { None = 0, Red = 1, Green = 2, Blue = 4, Alpha = 8, All = 15 }
+    public sealed record ColorBlendStateDesc
+    {
+        public bool BlendEnable { get; init; }
+        public BlendComponentDesc Color { get; init; } = new();
+        public BlendComponentDesc Alpha { get; init; } = new();
+        public ColorWriteMask WriteMask { get; init; } = ColorWriteMask.All;
+    }
+    public enum StencilOperation
+    {
+        Keep, Zero, Replace, IncrementClamp, DecrementClamp, Invert, IncrementWrap, DecrementWrap,
+    }
+    public sealed record StencilFaceDesc
+    {
+        public CompareFunction Compare { get; init; } = CompareFunction.Always;
+        public StencilOperation Fail { get; init; } = StencilOperation.Keep;
+        public StencilOperation DepthFail { get; init; } = StencilOperation.Keep;
+        public StencilOperation Pass { get; init; } = StencilOperation.Keep;
+    }
+    public sealed record DepthStencilStateDesc
+    {
+        public bool DepthTestEnable { get; init; }
+        public bool DepthWriteEnable { get; init; }
+        // depth test無効ならeffective比較はAlways。depth writeは独立に扱う。
+        public CompareFunction DepthCompare { get; init; } = CompareFunction.LessOrEqual;
+        public bool StencilTestEnable { get; init; }
+        public StencilFaceDesc Front { get; init; } = new();
+        public StencilFaceDesc Back { get; init; } = new();
+        public uint StencilReadMask { get; init; } = 0xff;
+        public uint StencilWriteMask { get; init; } = 0xff;
+    }
+    public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth, float MaxDepth);
+    public readonly record struct ScissorRect(uint X, uint Y, uint Width, uint Height);
+    public readonly record struct BlendConstant(float Red, float Green, float Blue, float Alpha);
     public interface IRenderEncoder
     {
+        // shader programを選ぶ。描画状態は変更しない。
+        void SetPipeline(IGraphicsPipeline pipeline);
+        // encoder状態のsnapshotを置き換える。attachment指定やprepareは不要。
+        void SetRenderState(GraphicsRenderStateDesc state);
+        // drawごとの値。native PSOのidentityへ含めない。
+        void SetViewport(Viewport viewport);
+        void SetScissor(ScissorRect scissor);
+        void SetBlendConstant(BlendConstant value);
+        void SetStencilReference(uint reference);
+        // vertex pulling。count=0は状態検証後に命令を省く。加算はchecked。
+        void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
         void End();
     }
     public interface IComputeEncoder
     {
+        void SetPipeline(IGraphicsComputePipeline pipeline);
+        // 正数かつdeviceの各軸共通実効上限以下。workgroup sizeはbinary内の情報を使用。
+        void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1);
         void End();
     }
 }
```

### 作成・状態設定・drawの検証

CreateGraphicsPipelineではvertexがVertex、任意のfragmentがFragmentであることをbinaryから確認する。computeはComputeのみを受け付ける。全shaderは同一deviceの生存resourceでなければならない。stage間の型・location・補間方式・system valueのlinkageとresource ABIをreflectionおよび生成codeに基づいて検証する。backendのshader validationで後から確定する失敗は例外として伝える。

SetRenderStateはenum・値・内部の整合性を検証してsnapshotを保存する。drawの直前にpipeline・状態・実attachmentを合わせて以下を検証する。

- topologyの分類がprogramのTopologyClassと一致する。StripIndexFormatはLineStrip／TriangleStripだけで指定でき、indexed stripのindex formatと一致する。restart indexはそのformatの最大値に固定する。
- 現在のpassのcolor slot数とColorTargetsの数が一致し、deviceのMaxColorAttachments以内。formatのattachment usage、blend可否、sample count、shaderのfragment outputが一致する。fragmentなしの場合はcolor attachmentがなく、AlphaToCoverageEnableはfalse。
- depth test／writeはdepth format、stencil testはstencil aspectを必要とする。depth/stencilなしなら両testとdepth writeが無効でなければ拒否する。stencil mask／referenceは初期formatの8-bit範囲内とする。Testが無効な場合のstateをnativeへ変換しても、公開Descの値を暗黙に書き換えない。
- AlphaToCoverageEnableにはfragmentとSampleCount > 1が必要。sample countを補正しない。shaderのsample関連system valueとoutputの互換性も確認する。
- enum・flag・finite値・bias範囲を検証する。Min／Max blendの両factorはOne、SourceAlphaSaturatedはcolorのSourceのみで使用する。initial scopeではFill rasterizationのみ。非対応のdepth clip無効化、bias clampやattachment組み合わせは拒否する。

nullはArgumentNullException、Descの不整合・異なるdevice・stage不一致はArgumentException、非対応feature／format／sample countはNotSupportedException、解放済みresourceはObjectDisposedException。native作成／linkの失敗は診断を含むInvalidOperationException。setterの失敗では直前のencoder状態を維持する。draw時の検証またはnative生成／linkに失敗した場合は、そのdrawのnative命令を発行せず、取得途中のnative resourceを回収する。不完全な生成結果をcacheへ登録しない。

### 描画時に扱う状態

SetPipelineとSetRenderStateは独立したencoder状態を更新する。SetPipelineは描画状態を初期化せず、SetRenderStateは選択したprogramを変更しない。pass開始時はpipelineと描画状態を未設定とし、最初のdraw前に両方を設定する。passをまたいでencoder状態を引き継がない。

SetRenderStateのcollectionはcopyして不変のsnapshotを保持する。呼び出し後に利用者が元listを変更しても記録済みの状態に影響しない。複数回設定した場合は次のdrawに最後の設定が適用され、以前に記録したdrawには影響しない。drawはその時点のpipeline・状態・attachment metadataを消費する。

viewport、scissor、blend constant、stencil referenceはencoder状態とし、native PSOのkeyには含めない。最初のdrawより前に設定する。viewportはattachment内のfiniteな位置・正の幅と高さ、`0 <= MinDepth <= MaxDepth <= 1`、scissorはattachment範囲内の正の幅と高さ、blend constantはfiniteな値とする。暗黙のdefaultやclampは挿入しない。現在のportable契約ではfront／backのstencil referenceは共通の一値を使う。

attachment情報を利用者が別のlayoutとして再指定するAPIは設けない。backendはpassが保持するtexture viewからformatとsample countを取得する。resolve targetを含むrender passの整合性はcommandの設計で扱う。drawに必要な情報は実resourceとshader binaryのmetadataを使う。

SetPipelineは同一deviceとresourceの生存を検証する。topology、shader output、sample countなど設定同士の互換性はdraw時に検証する。stateの変更はSetRenderStateを呼び、indexed stripではdrawに渡すindex formatも一致させる。Slang sourceの再compileやshader binaryの書き換えは状態設定・drawに含めない。

Argument Tableの論理slotやresource参照の値はprogramやnative PSOのidentityではない。reflectionから導く物理resource ABI／layout signatureはprogramのidentityに含む。[Argument Table](GRAPHICS-0005-argument-tables-and-gpu-references.md)の内容やbuffer要素を変更するだけでshader programを作り直さない。root data、参照追跡、resource binding命令との接続はcommand設計で扱う。

### 利用例

```csharp
// passは実際のattachmentを既に保持している。
encoder.SetPipeline(pipeline);
encoder.SetRenderState(opaqueState);
// viewport等とroot dataを設定し、command APIのdrawを発行する。

encoder.SetRenderState(transparentState);
// 同じpipelineで次のdrawを発行する。利用側のprepareは不要。
```

### 最適化とcache

shader programのkeyはshaderのtarget codeとreflectionに由来するABI・linkage、TopologyClass、AlphaToCoverageEnableとOptimizationとする。Label、material内容、登録slot、実resource identity、viewport等は除外する。

draw時に必要なnative PSO／link結果はbackend内部で管理する。keyはprogram identity、backendが固定する描画状態、現在のpassのattachment format・sample countと固定ABIを含む。Optimizationはprogramの方針としてnative生成に反映する。texture viewの実instance、passのclear値、resourceの内容、encoderでdynamicに適用する値はnative PSOのkeyに含めない。

backendは状態の変更を追跡し、drawの直前に必要なnative pipelineとdynamic stateを解決する。完全なnative PSOや高コストのshader compile／link結果を状態の組み合わせごとに必要とするbackendはcacheを実装し、等価なkeyで生成済みの結果を再利用する。cache missではその場で生成／linkしてからdrawを記録する。利用者に準備objectやcache miss処理を要求しない。

cacheを一律に要求しない。shader programを直接使える経路や、軽量なstate object／link結果の生成だけで済む経路では、cacheの検索・key生成・保持と毎回生成する方法を比較して採用方法を決める。比較はbackend全体ではなくnative PSO、depth/stencil object、partial programのlink結果など実際の生成単位ごとに行う。native objectの生成が不要なdynamic stateの設定は、そのままencoderへ適用する。

状態が変わらないdrawではencoderが既に解決した結果を使える。この再利用と複数state間のcacheを区別する。生成が必要な経路の評価では「各drawで毎回生成」「状態が変わったときだけ生成」「keyでcacheする」を同じ描画結果・所有規則で比較する。cacheを使わない場合もSlang sourceのcompileはdrawへ挿入しない。

PreferReuseは既存のshader compile結果の再利用を優先するhint、FullSpecializationは完全な組み合わせの最適化を求めるhint。どちらもsourceからのSlang再compileを必要とせず、driverが内部shader codeを生成し直す可能性はある。部分programを再利用できないdeviceでも完全なnative PSOを内部生成し、同じ共通APIを提供する。driverのbackground specializationが可能でも、本ライブラリからbackground taskや非同期のresource変更を起動する契約は設けない。

cacheを採用する場合、native cacheはprogramに属する内部resourceとして保持する。cache entryの公開handle・Dispose APIは設けない。初期契約ではprogramの生存中に使ったvariantを保持し、記録済みcommandが参照するentryを途中で破棄しない。program解放時にcacheも解放する。evictionを追加する場合はGPU lifetimeとcommand保持の契約を別途設計する。cacheを使わず生成する一時native objectも、native APIの保持規則と記録commandの寿命に従って保持・解放し、GPUが使用中のobjectを破棄しない。この経路の具体的なcommand所有契約はcommandのADRで扱う。computeはCreateComputePipelineで実行可能なnative programを生成する。

初回drawのcache missではnative生成／linkに時間がかかる可能性がある。APIを描画時の状態設定に統一する代わりに、この費用とvariantのmemory消費をbackendが引き受ける。共通APIはdriverの再compile回避やdraw記録時間の一定性を保証しない。

### 所有と同期

programはdeviceとshader moduleの子resourceとし、内部native variantを所有する。shader moduleがprogramから保持されている間、そのDisposeを拒否する。deviceも子resourceが残るDisposeを拒否する。program → shader module → deviceの順で解放し、各Disposeはidempotent。commandが参照しているprogramと内部variantのGPU完了までの寿命は利用者が管理する。SetPipelineを呼ぶだけで所有権を利用側へ移さない。

CPU／GPUの同期は利用者が管理する。内部lock、atomic counter、同期を挿入するwrapper、GPU完了待機を設けない。cacheを実装する場合もこの契約を変更しない。並列呼び出しへの保証をこのAPIに追加しない。

### Backend文書の責務

native partial program／pipeline library／完全PSOへの対応、必要なfeatureとpreviewのversion、最適化hintの扱い、native stateの固定範囲、cacheの要否と毎回生成との比較結果は各backend projectのREADMEへ記載する。共通ADRにはbackend固有の構造体・binding番号・feature交渉手順を持ち込まない。previewの存在を全deviceの対応保証として扱わない。

## 検討した代替案

- 全stateをGraphicsPipelineDescへ含める: shader programの再利用単位が状態の組み合わせと一体になるため採用しない。
- shaderだけを残し、全ての固定機能をdynamicと保証する: topology分類やalpha-to-coverageにもcompile依存があり、native APIの制約を満たせないため採用しない。
- 状態とattachmentを指定して事前prepareし、専用bindingをSetPipelineへ渡す: 利用側にnative state結合の準備単位を公開することになるため採用しない。描画時の状態設定だけで利用できる契約を優先する。
- 全backend・全state objectへ同じcacheを強制する: 軽量な生成やdynamic state設定では検索・保持費用が利益を上回る可能性があるため採用しない。必要な経路にはcacheを実装し、他の経路は生成単位ごとに比較する。
- vertex／fragmentを別々の公開partial programにする: 初期のvertex pullingと固定したstageの組ではcombined programでも分離の目的を達成できる。stage間linkageをapplicationへ重複して指定させず、stage別のnative再利用はbackendで行う。mesh等の公開単位は追加要件とともに検討する。

## 結果と影響

shader programを再利用し、利用者は描画時に状態を設定するだけで使える。attachment情報は実passから取得する。部分programを活用できるdeviceではshader compileの重複を減らせる可能性がある。完全なnative PSOが必要なdeviceではstate組み合わせごとのnative variantとmemory消費、初回drawでの生成費用が残るため、性能改善を共通APIだけで保証しない。

depth/stencil用TextureFormatの追加時に、texture allocation・view・sampler comparisonとの対応を整合させる。programの寿命管理とread-only snapshotの実装も必要になる。

## 検証方針

共通APIのみを使うsample／testで、一つのprogramをSetPipelineし、blend・write mask・sample maskを設定してRGBA／BGRAのpassでdrawし、正しい画素とprogram再利用を確認する。未設定state、元listの変更、shader解放拒否、stage／device不一致、compute dispatchも確認する。depth/stencilとMSAAのresource拡張後には、それらのattachmentとsample countを変えた検証を追加する。stage／device不一致、非対応state、shader metadata／linkage不整合、変更後の元list、解放順、strip index formatを検証する。

native shader compile回数、初回drawの生成／link時間、cache hit、native PSO数をbackendの計測で分けて確認する。異なるcolor write mask等を変えた場合にdriverが再compileしないかは実測し、APIの保証と混同しない。cache採用経路では同じkeyの再利用で生成を繰り返さないこと、cache missでは必要な生成／linkが行われることを検証する。非cache経路では想定した生成回数とnative objectの解放を検証する。いずれも失敗したdrawが記録されないことを確認し、既存CIでnativeとWasmのテストを実行する。

cacheの要否が自明でない経路は、以下の条件で毎回生成と比較する。

- 同じdevice・driver・shader・attachment・描画数で、同一stateの連続、少数stateの交互使用、多数の異なるstateを評価する。初回とwarm状態を分け、driver内部cacheの影響も区別する。
- key構築・検索を含むCPUのdraw記録時間、native生成／link回数と時間、allocation数、保持memory、GPU描画時間を計測する。平均だけでなくp95／p99とcache miss時の費用を確認する。
- native objectの保持・解放と描画結果の検証を同じ条件で含める。cache側だけ寿命を短縮したり、生成側だけGPU同期を追加したりしない。
- CPU時間とmemoryの実測から採用方式を決め、対象GPU／driver、workload、比較条件と結果を各backend READMEへ記録する。計測前に特定の方式が高速と断定しない。

完全なnative PSOを必要とする生成単位はcacheを実装する。dynamic stateの設定はnative objectを生成しない。軽量なstate object等を追加する際は、上記の比較結果を各backend READMEへ記録する。

## 参考資料

- [NoGraphicsAPI公開API](https://github.com/sebbbi/NoGraphicsAPI/blob/main/include/NoGraphicsAPI/NoGraphicsAPI.hpp)
- [NoGraphicsAPIのAPI比較](https://github.com/sebbbi/NoGraphicsAPI/blob/main/docs/no-graphics-api-comparison.md)
- [Partial Graphics Programs](https://github.com/microsoft/DirectX-Specs/blob/master/d3d/PartialGraphicsPrograms.md): partial program、late linkとcompile依存の境界。
- [State Object Compilation Flags](https://github.com/microsoft/DirectX-Specs/blob/master/d3d/PartialGraphicsPrograms.md#state-object-compilation-flags): 再利用とspecializationのhint。
