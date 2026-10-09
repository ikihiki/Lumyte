# ADR-GRAPHICS-0007: PSOのシェーダープログラムと描画状態の分離

- 状態: 提案
- 日付: 2026-10-09

## 背景

PSOへshader・rasterizer・blend・depth/stencil・attachment formatを一緒に含めると、同じshaderを使う描画でも状態の組み合わせごとにPSOが増える。shader最適化に必要な情報と、描画状態を別に扱い、shaderの再利用と描画開始前の準備を可能にしたい。

[NoGraphicsAPI](https://github.com/sebbbi/NoGraphicsAPI)はresource bindingとvertex layoutをpipelineから除き、viewport・scissor・depth/stencilを独立に設定する。ただし、そのGraphicsPSODescにはrasterization、blend、attachment formatが残っている。本設計では、その分離方針を参考にしてshader programと実行時の状態をさらに分ける。

DirectXの[Partial Graphics Programs仕様](https://github.com/microsoft/DirectX-Specs/blob/master/d3d/PartialGraphicsPrograms.md)は、shaderを含むpartial programを先にcompileし、他のstateを後からlinkする方向を示している。固定機能がshader codeとして実装される場合もあるため、「固定機能なら全てshader最適化と無関係」とは扱えない。特にprimitive topology、stage間のlinkage、alpha-to-coverageなどのcompile依存を残す必要がある。

関連する[shader成果物とmodule](GRAPHICS-0006-shader-compilation-and-modules.md)は、全targetのcode・reflection・entry・stage等を含むopaque binaryを扱う。本ADRはそのmoduleからGPU用programを作り、描画状態との結合を準備する契約を定める。

## 決定

### 分離する責務

GraphicsのPSOは二層に分ける。

1. `IGraphicsPipeline`: shaderの組、stage間linkage、primitiveの分類、shader生成に影響するcompile設定を所有する不変なshader program。
2. `IGraphicsPipelineBinding`: programと描画状態・attachment layoutを結合して実行可能にした不変な準備結果。

利用者は同じprogramを、異なるblend、cull、depth/stencilやattachment layoutで再利用する。backendの具象classが各interfaceを直接実装し、共通の所有wrapperやbackend contractを別に設けない。公開契約は`Lumyte.Graphics.Abstractions`へ配置し、device生成はbackend固有のままとする。

shader programに残すのはvertex／fragmentの組、stage間linkageとresource ABI、primitive topologyの分類、alpha-to-coverageである。entry・stage・workgroup size・resource layoutはshader binaryのmetadata／reflectionから取得し、Descで再指定しない。初期範囲はvertexと任意のfragment、およびcomputeとする。mesh・tessellation・geometry、view instancing、特殊なline rasterization、forced sample countは追加のcompile契約が必要なので別途設計する。

描画状態へ分けるのは正確なlist／strip topology、strip index format、culling・front face・depth bias、color targetごとのblendとwrite mask、depth/stencil、sample maskである。attachmentのformatとsample countもprogramから分け、prepare時に渡す。vertex属性layoutは設けず、vertex indexからshader内でbufferを読むvertex pullingを基準にする。

この境界は共通APIの責務であり、全deviceでnative stateをdynamicに設定できるという保証ではない。backendがstateをnative PSOへ固定する必要があれば、prepareでその組み合わせを実体化する。API上のprogram再利用と、driver内でshader再compileが起きないことを区別する。

### 公開API

比較元はorigin/main。説明・既定値・失敗条件をコメントとして示す。IRenderEncoder／IComputeEncoderの宣言はpipelineの接続点で、pass開始・draw・dispatch・barrierの全契約はcommandのADRで扱う。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // shader stage、同一device、stage間linkage、resource ABIを検証する。
+        // backendによってはnative shader最適化をprepareまで遅らせる。
+        IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);
+        IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc);
+        // native programをlinkするか、必要な完全native PSOを作る。
+        // GPU命令・submit・shader sourceの再compileを暗黙に行わない。
+        IGraphicsPipelineBinding PrepareGraphicsPipeline(GraphicsPipelinePreparationDesc desc);
     }
+    public sealed record GraphicsPipelineDesc
+    {
+        public string? Label { get; init; }
+        public required IGraphicsShader VertexShader { get; init; }
+        // nullはfragmentなし。color targetを持たないdepth-only描画に使う。
+        public IGraphicsShader? FragmentShader { get; init; }
+        // 正確なlist／stripは描画状態側。分類はshader compilationの契約に残す。
+        public PrimitiveTopologyClass TopologyClass { get; init; } = PrimitiveTopologyClass.Triangle;
+        // shader生成へ影響するためBlendStateDescへ移さない。
+        public bool AlphaToCoverageEnable { get; init; }
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
+    public sealed record GraphicsPipelinePreparationDesc
+    {
+        public string? Label { get; init; }
+        public required IGraphicsPipeline Pipeline { get; init; }
+        public required GraphicsRenderStateDesc State { get; init; }
+        public required RenderTargetLayoutDesc Targets { get; init; }
+        // driverへの最適化方針のhint。時間やshader再compileの有無を保証しない。
+        public PipelineOptimizationMode Optimization { get; init; } = PipelineOptimizationMode.PreferReuse;
+    }
+    public enum PipelineOptimizationMode { PreferReuse, FullSpecialization }
+    public interface IGraphicsPipelineBinding : IDisposable
+    {
+        // listを含むsnapshot。native準備結果を所有し、Pipelineを保持する。
+        GraphicsPipelinePreparationDesc Desc { get; }
+    }
+    public sealed record RenderTargetLayoutDesc
+    {
+        // slot順。emptyはcolor attachmentなし。null要素／未定義formatは許可しない。
+        public IReadOnlyList<TextureFormat> ColorFormats { get; init; } = [];
+        // nullはdepth/stencil attachmentなし。color formatは指定できない。
+        public TextureFormat? DepthStencilFormat { get; init; }
+        // 0は不正。formatとsample countの組み合わせをdeviceが検証する。
+        public uint SampleCount { get; init; } = 1;
+    }
     public enum TextureFormat
     {
+        // depth/stencil attachment用に追加。対応usage・sample countはdeviceで検証。
+        Depth16Unorm,
+        Depth24Stencil8,
+        Depth32Float,
+        Depth32FloatStencil8,
     }
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
+        // ColorFormatsと同じslot数。blendなしでも明示的に各slotを設定する。
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
+        public CompareFunction DepthCompare { get; init; } = CompareFunction.LessEqual;
+        public bool StencilTestEnable { get; init; }
+        public StencilFaceDesc Front { get; init; } = new();
+        public StencilFaceDesc Back { get; init; } = new();
+        public uint StencilReadMask { get; init; } = 0xff;
+        public uint StencilWriteMask { get; init; } = 0xff;
+    }
+    public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth, float MaxDepth);
+    public readonly record struct ScissorRect(uint X, uint Y, uint Width, uint Height);
+    public readonly record struct BlendConstant(float Red, float Green, float Blue, float Alpha);
+    public interface IRenderEncoder
+    {
+        // prepared bindingを選ぶだけ。PSOの生成・linkはこの呼び出しに隠さない。
+        void SetPipeline(IGraphicsPipelineBinding binding);
+        // drawごとの値。PSO／prepared bindingのidentityへ含めない。
+        void SetViewport(Viewport viewport);
+        void SetScissor(ScissorRect scissor);
+        void SetBlendConstant(BlendConstant value);
+        void SetStencilReference(uint reference);
+    }
+    public interface IComputeEncoder
+    {
+        void SetPipeline(IGraphicsComputePipeline pipeline);
+    }
 }
```

### 作成・prepareの検証

CreateGraphicsPipelineではvertexがVertex、任意のfragmentがFragmentであることをbinaryから確認する。computeはComputeのみを受け付ける。全shaderは同一deviceの生存resourceでなければならない。stage間の型・location・補間方式・system valueのlinkageとresource ABIをreflectionおよび生成codeに基づいて検証する。backendのshader validationで後から確定する失敗は例外として伝える。

PrepareGraphicsPipelineでは以下を検証する。

- topologyの分類がprogramのTopologyClassと一致する。StripIndexFormatはLineStrip／TriangleStripだけで指定でき、indexed stripのindex formatと一致する。restart indexはそのformatの最大値に固定する。
- ColorFormatsとColorTargetsの数が一致し、deviceのMaxColorAttachments以内。formatのattachment usage、blend可否、sample count、shaderのfragment outputが一致する。fragmentなしの場合はColorFormatsがemptyで、AlphaToCoverageEnableはfalse。
- depth test／writeはdepth format、stencil testはstencil aspectを必要とする。depth/stencilなしなら両testとdepth writeを無効にする。stencil mask／referenceは初期formatの8-bit範囲内とする。Testが無効な場合のstateをnativeへ変換しても、公開Descの値を暗黙に書き換えない。
- AlphaToCoverageEnableにはfragmentとSampleCount > 1が必要。sample countを補正しない。shaderのsample関連system valueとoutputの互換性も確認する。
- enum・flag・finite値・bias範囲を検証する。Min／Max blendの両factorはOne、SourceAlphaSaturatedはcolorのSourceのみで使用する。initial scopeではFill rasterizationのみ。非対応のdepth clip無効化、bias clampやattachment組み合わせは拒否する。

nullはArgumentNullException、Descの不整合・異なるdevice・stage不一致はArgumentException、非対応feature／format／sample countはNotSupportedException、解放済みresourceはObjectDisposedException。native作成／linkの失敗は診断を含むInvalidOperationException。新しいbindingの返却前に失敗した場合は取得済みのnative resourceを回収し、所有関係を変更しない。

### 描画時に扱う状態

viewport、scissor、blend constant、stencil referenceはencoder状態とし、prepareのkeyには含めない。最初のdrawより前に設定する。viewportはfiniteな位置・正の幅と高さ、`0 <= MinDepth <= MaxDepth <= 1`、scissorはattachment範囲内の正の幅と高さ、blend constantはfiniteな値とする。暗黙のdefaultやclampは挿入しない。現在のportable契約ではfront／backのstencil referenceは共通の一値を使う。

SetPipeline時にはdeviceと現在のpassのattachment format・sample countを準備結果と照合する。stateを変更する場合は別のprepared bindingを選ぶ。indexed stripではdrawに渡すindex formatも一致させる。SetPipelineはpipelineの生成、Slang compile、shader binaryの書き換えやlinkを呼び出さない。

Argument Tableの論理slotやresource参照の値はprogramやbindingのidentityではない。reflectionから導く物理resource ABI／layout signatureはprogramのidentityに含む。[Argument Table](GRAPHICS-0005-argument-tables-and-gpu-references.md)の内容やbuffer要素を変更するだけでshader programを作り直さない。root data、参照追跡、resource binding命令との接続はcommand設計で扱う。

### 最適化とcache

shader programのkeyはshaderのtarget codeとreflectionに由来するABI・linkage、TopologyClass、AlphaToCoverageEnableとする。Label、material内容、登録slot、実resource identity、viewport等は除外する。

native準備結果のkeyはprogram identity、描画状態、attachment layout、backendが固定するABIとOptimizationを含む。描画状態とlayoutのcollectionはcopyして変更できないcollectionで公開する不変のsnapshotとし、呼び出し後の元list変更がkeyやDescに影響しないようにする。cacheは等価な準備結果を再利用できるが、寿命管理とmemory消費の責任はbackend側にある。初期契約はcacheの有無や容量を保証せず、利用者はprepared bindingを保持して繰り返し使用できる。

PreferReuseは既存のshader compile結果の再利用を優先するhint、FullSpecializationは完全な組み合わせの最適化を求めるhint。どちらもsourceからのSlang再compileを必要とせず、driverが内部shader codeを生成し直す可能性はある。部分programを再利用できないdeviceでもprepare時に完全なnative PSOを作り、同じ共通APIを提供する。driverのbackground specializationが可能でも、本ライブラリからbackground taskや非同期のresource変更を起動する契約は設けない。

prepareはnative生成を含み、時間がかかる可能性がある。利用者がscene／frameの準備段階で必要な組み合わせを作る。draw中の初回cache missを理由にnative PSOを生成する実装は採用しない。computeはCreateComputePipelineで実行可能なnative programを準備する。

### 所有と同期

programはdeviceとshader moduleの子resource、prepared bindingはdeviceとprogramの子resourceとする。shader moduleがprogramから保持されている間、programがbindingから保持されている間、それぞれのDisposeを拒否する。deviceも子resourceが残るDisposeを拒否する。binding → program → shader module → deviceの順で解放し、各Disposeはidempotent。commandが参照しているresourceのGPU完了までの寿命は利用者が管理する。

CPU／GPUの同期は利用者が管理する。内部lock、atomic counter、同期を挿入するwrapper、GPU完了待機を設けない。cacheを実装する場合もこの契約を変更しない。並列呼び出しへの保証をこのAPIに追加しない。

### Backend文書の責務

native partial program／pipeline library／完全PSOへの対応、必要なfeatureとpreviewのversion、最適化hintの扱い、native stateの固定範囲とcacheの実装は各backend projectのREADMEへ記載する。共通ADRにはbackend固有の構造体・binding番号・feature交渉手順を持ち込まない。previewの存在を全deviceの対応保証として扱わない。

## 検討した代替案

- 全stateをGraphicsPipelineDescへ含める: shader programの再利用単位が状態の組み合わせと一体になるため採用しない。
- shaderだけを残し、全ての固定機能をdynamicと保証する: topology分類やalpha-to-coverageにもcompile依存があり、native APIの制約を満たせないため採用しない。
- draw時に必要なPSOを自動生成する: cache missとlink／compile時間が描画処理へ混入するため採用しない。
- vertex／fragmentを別々の公開partial programにする: 初期のvertex pullingと固定したstageの組ではcombined programでも分離の目的を達成できる。stage間linkageをapplicationへ重複して指定させず、stage別のnative再利用はbackendで行う。mesh等の公開単位は追加要件とともに検討する。

## 結果と影響

shader programと描画状態を独立に再利用でき、利用者がnative準備の発生時点を選べる。部分programを活用できるdeviceではshader compileの重複を減らせる可能性がある。完全なnative PSOが必要なdeviceではstate組み合わせごとの準備結果とmemory消費が残るため、性能改善を共通APIだけで保証しない。

depth/stencil用TextureFormatを追加するため、texture allocation・view・sampler comparisonとの対応をbackend実装時に整合させる。programの寿命管理とread-only snapshotの実装も必要になる。

## 検証方針

共通APIのみを使うsample／testで、一つのprogramからblend・cull・depth state・attachment format／sample countを変えたbindingを作り、正しい描画とprogram再利用を確認する。stage／device不一致、非対応state、shader metadata／linkage不整合、変更後の元list、解放順、strip index formatを検証する。

native shader compile回数、prepare時間、cache hit、native PSO数をbackendの計測で分けて確認する。異なるcolor write mask等を変えた場合にdriverが再compileしないかは実測し、APIの保証と混同しない。command記録中にshader compile／native PSO生成が呼ばれないことも確認する。既存CIでnativeとWasmのテストを実行する。

## 参考資料

- [NoGraphicsAPI公開API](https://github.com/sebbbi/NoGraphicsAPI/blob/main/include/NoGraphicsAPI/NoGraphicsAPI.hpp)
- [NoGraphicsAPIのAPI比較](https://github.com/sebbbi/NoGraphicsAPI/blob/main/docs/no-graphics-api-comparison.md)
- [Partial Graphics Programs](https://github.com/microsoft/DirectX-Specs/blob/master/d3d/PartialGraphicsPrograms.md): partial program、late linkとcompile依存の境界。
- [State Object Compilation Flags](https://github.com/microsoft/DirectX-Specs/blob/master/d3d/PartialGraphicsPrograms.md#state-object-compilation-flags): 再利用とspecializationのhint。
