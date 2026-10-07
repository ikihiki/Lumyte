# ADR-0007: Graphics の Desc 型と生成・記録時の検証

- 状態: 提案
- 日付: 2026-10-07

## 背景

[ADR-0004](0004-graphics-library.md) では DeviceDesc、BufferDesc<T> などの名前と役割だけを示していた。利用者が実装を参照せずに生成条件を指定できるよう、フィールド、既定値、単位、検証条件を定める。[ADR-0006](0006-render-encoder.md) の描画パスと pipeline の互換性も同じ定義を使用する。

## 決定

### 型と公開契約

本 ADR の Desc 型は `Lumyte.Graphics` 名前空間に置く未実装の C# 公開 API 案である。基本形は `public sealed record XxxDesc` とし、フィールドは public の init-only property にする。宣言の必須項目には `required` を付け、生成・記録 API でも null、範囲、enum 値を検証する。`new XxxDesc { ... }` で組み立てる。Desc は GPU リソースの所有者ではなく、Dispose を持たない。

すべての Desc に診断用の `string? Label = null` を設ける。ラベルは動作や互換性を変えない。各 Desc の宣言にも Label を示す。既定のネストした Desc は宣言の既定値で生成したものを使い、null は受け付けない。配列は `IReadOnlyList<T>` とし、呼び出し時に値を snapshot する。呼び出し後の配列変更が GPU 記録に影響しないようにする。

一般の constructor には GPU 操作を持たせず、検証は CreateDeviceAsync／CreateBuffer／CreateTexture／CreateView／CreateSampler／CreateGraphicsPipeline／CreateComputePipeline／BeginRenderPass／Draw／DrawIndexed／RecordCopyBufferToTexture が行う。契約違反は引数例外、未対応機能・確保失敗は ADR-0004 の Result／GraphicsError を使用する。recording API での unsupported な入力も記録前の引数・状態エラーとして通知し、backend 記録失敗は ADR-0006 に従って Encoder を Faulted にする。Desc を黙って補正しない。

DeviceCaps は本 ADR が参照する `MaxBufferSize`、`MaxColorAttachments`、`MaxAnisotropy`、`CopyBufferOffsetAlignment`、`CopyBytesPerRowAlignment` と compute／texture／binding の上限を公開する。`FormatCapabilities DeviceCaps.GetFormatCapabilities(TextureFormat format)` は sampled／filterable／comparison-sampleable／storage-read／storage-write／renderable／blendable の可否、対応 sample count、許可する view format を返す。不明な format は引数例外、既知の未対応 format は空の capability とする。これらは候補選択・Desc 検証に使用し、参照の物理表現を公開しない。

GPU アドレス、Native handle、物理 binding slot を Desc のフィールドに含めない。shader の成果物と論理引数 layout は [ADR-0005](0005-shader-compilation-and-data-interop.md) の型を参照する。

### DeviceDesc

API は .NET の API review／API diff に倣い、namespace・型・メンバーを C# 宣言でまとめる。`+` は origin/main に対する追加 API、`-` は削除 API、無印は変更の文脈を表す。この PR の main には Graphics API がないため、掲載する宣言は追加として表示する。各ブロックは当該 ADR の対象メンバーの抜粋であり、実装コードではない。説明と検証条件は宣言の `//` コメントに記す。提案と実装済みの区別は ADR の状態と本文に従う。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record DeviceDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public DeviceDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // null は backend が選択
+        // ID はその backend の列挙で得た不透明値
+        // 別 backend の ID は拒否
+        public AdapterId? Adapter { get; init; } = null;
+
+        // Default／LowPower／HighPerformance
+        // 選択のヒントであり性能の保証ではない
+        public PowerPreference PowerPreference { get; init; } = PowerPreference.Default;
+
+        // 必須機能の flags
+        // 満たさない候補を使用しない
+        // 実 GPU アドレス方式の指定は含めない
+        public GraphicsFeatures RequiredFeatures { get; init; } = GraphicsFeatures.None;
+
+        // 未指定の上限は追加要求なし
+        // 各 nullable フィールドは必要な最小容量を表す
+        public DeviceLimitsRequest RequiredLimits { get; init; } = new();
+
+        // Default／Enabled／Disabled
+        // Enabled を実現できなければ生成を失敗させる
+        public ValidationMode Validation { get; init; } = ValidationMode.Default;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // GraphicsFeatures の初期 flags は None／MeshShader／IndirectDraw／AnisotropicFiltering／DepthBiasClamp とする
+    // feature の有無と実 API の提供範囲を区別し、未実装の拡張操作を利用可能として報告しない
+    // AdapterInfo は AdapterId Id、string Name、GraphicsFeatures Features、DeviceCaps Caps を持ち、名前や ID に実 GPU アドレスを含めない
+    public sealed record AdapterInfo
+    {
+        public AdapterId Id { get; }
+        public string Name { get; }
+        public GraphicsFeatures Features { get; }
+        public DeviceCaps Caps { get; }
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // DeviceLimitsRequest は ulong? MaxBufferSize、uint? MaxTextureDimension2D、uint? MaxColorAttachments、uint? MaxSampledTexturesPerStage、uint? MaxStorageBuffersPerStage、uint? MaxComputeInvocationsPerWorkgroup を持つ
+    // 名前は要求するデバイス上限に対応し、指定値は正数とする
+    // 満たした実値は DeviceCaps に返す
+    // キューは ADR-0004 の単一汎用キューを初期契約とし、任意の Native queue 指定は提供しない
+    public sealed record DeviceLimitsRequest
+    {
+        public ulong? MaxBufferSize { get; init; } = null;
+        public uint? MaxTextureDimension2D { get; init; } = null;
+        public uint? MaxColorAttachments { get; init; } = null;
+        public uint? MaxSampledTexturesPerStage { get; init; } = null;
+        public uint? MaxStorageBuffersPerStage { get; init; } = null;
+        public uint? MaxComputeInvocationsPerWorkgroup { get; init; } = null;
+    }
+}
```

### リソース Desc の正本

リソース固有の生成条件、利用 API、バックエンド実装契約は以下へ分離する。本 ADR は Device、RenderPass、Pipeline、Draw と共通の Desc 規約を扱う。定義の重複を避け、リソースの詳細は各 ADR を参照する。

| 型・詳細 | 正本 |
| --- | --- |
| BufferDesc<T>、BufferUsage、MemoryPreference、BufferSlice<T>、buffer alignment／typed metadata | [ADR-0009: バッファ](0009-buffer-resource-contract.md) |
| TextureDesc、TextureViewDesc、TextureCopyDesc、TextureRegion、format／mip／layer／copy pitch | [ADR-0010: テクスチャ・ビュー](0010-texture-resource-contract.md) |
| SamplerDesc、SamplerInfo、SamplerKind、filter／compare／anisotropy と texture の適合 | [ADR-0011: サンプラー](0011-sampler-resource-contract.md) |

Extent3D／Origin3D／TextureFormat／TextureAspect の定義は ADR-0010、CompareOp の値は ADR-0011 を参照する。DepthStencilDesc も同じ CompareOp を使う。FormatCapabilities の詳細と対象コピーの制約は ADR-0010 に従う。

### RenderPassDesc と attachment

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record RenderPassDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public RenderPassDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // 順序が出力 location に対応
+        // null の穴は許可しない
+        public IReadOnlyList<ColorAttachmentDesc> Colors { get; init; } = Array.Empty<ColorAttachmentDesc>();
+
+        // color が空の場合は必須
+        // attachment が一つ以上必要
+        public DepthStencilAttachmentDesc? DepthStencil { get; init; } = null;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record ColorAttachmentDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public ColorAttachmentDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // color の D2 単一 mip／layer、RenderAttachment 用途
+        public required TextureView View { get; init; }
+
+        // Clear／Load
+        public LoadOp Load { get; init; } = LoadOp.Clear;
+
+        // Store／Discard
+        public StoreOp Store { get; init; } = StoreOp.Store;
+
+        // Load = Clear 時に使用
+        // format の numeric category と一致すること
+        public ClearColor ClearValue { get; init; } = ClearColor.Float(0,0,0,0);
+
+        // source が 4 samples、target が 1 sample、同 format・同サイズ
+        // integer format は不可
+        public TextureView? ResolveTarget { get; init; } = null;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // ClearColor は Float(float r, float g, float b, float a)、Int(int r, int g, int b, int a)、UInt(uint r, uint g, uint b, uint a) の factory で作る tagged value とする
+    // Unorm／Srgb／Float format は Float、Sint は Int、Uint は UInt を要求する
+    // float 成分は有限値とする
+    // Srgb の clear 値は線形値として渡し、backend の render-target 書き込み規約に従って格納する
+    public readonly struct ClearColor
+    {
+        // format の numeric category に適合する値を作る。
+        public static ClearColor Float(float r, float g, float b, float a);
+        public static ClearColor Int(int r, int g, int b, int a);
+        public static ClearColor UInt(uint r, uint g, uint b, uint a);
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record DepthStencilAttachmentDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public DepthStencilAttachmentDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // depth／stencil の D2 単一 mip／layer、RenderAttachment 用途
+        public required TextureView View { get; init; }
+
+        // 使用する depth aspect の操作
+        // null はその aspect を使用しない
+        public DepthAttachmentOps? Depth { get; init; } = null;
+
+        // 使用する stencil aspect の操作
+        // null はその aspect を使用しない
+        public StencilAttachmentOps? Stencil { get; init; } = null;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // Depth／Stencil の少なくとも一方が必要で、View が含む aspect に限る
+    // DepthAttachmentOps は bool ReadOnly = false、LoadOp Load = Clear、StoreOp Store = Store、float ClearValue = 1 を持つ
+    // depth clear は有限の 0〜1
+    // StencilAttachmentOps は同じ ReadOnly／Load／Store と uint ClearValue = 0 を持ち、値は 0〜255
+    public sealed record DepthAttachmentOps
+    {
+        public bool ReadOnly { get; init; } = false;
+        public LoadOp Load { get; init; } = LoadOp.Clear;
+        public StoreOp Store { get; init; } = StoreOp.Store;
+        public float ClearValue { get; init; } = 1;
+    }
+
+    public sealed record StencilAttachmentOps
+    {
+        public bool ReadOnly { get; init; } = false;
+        public LoadOp Load { get; init; } = LoadOp.Clear;
+        public StoreOp Store { get; init; } = StoreOp.Store;
+        public uint ClearValue { get; init; } = 0;
+    }
+}
```

使用しない aspect は backend 内部で readonly として扱い、clear／discard／書き込みを行わない。ReadOnly = true の aspect は Load = Load、Store = Store を要求し、clear／discard と pipeline の書き込みを拒否する。WebGPU では readonly aspect の native load／store フィールドを設定せず、この契約を表現する。使用しない aspect の test／write も pipeline で無効であることを要求する。

すべての attachment の source サイズ・sample count は一致し、Color 数は DeviceCaps.MaxColorAttachments 以下とする。alias、load／store、resolve の意味と寿命は ADR-0006 に従う。render area は共通サイズの全域とし、部分描画は viewport／scissor で指定する。

### GraphicsPipelineDesc

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record GraphicsPipelineDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public GraphicsPipelineDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // vertex stage の compiled entry point
+        public required ShaderEntry Vertex { get; init; }
+
+        // color 出力がある場合は必須
+        // depth-only では省略可能
+        public ShaderEntry? Fragment { get; init; } = null;
+
+        // 各 stage と同じ linked program／schema／BindingPlan の layout
+        public required ShaderArgumentsLayout ArgumentsLayout { get; init; }
+
+        // PointList／LineList／LineStrip／TriangleList／TriangleStrip
+        public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;
+
+        // strip の index 描画では Uint16／Uint32 が必要
+        // その他は null
+        public IndexFormat? StripIndexFormat { get; init; } = null;
+
+        // RenderPass の color 順序・format と一致
+        public IReadOnlyList<ColorTargetDesc> ColorTargets { get; init; } = Array.Empty<ColorTargetDesc>();
+
+        // null は depth／stencil attachment のない pipeline
+        public DepthStencilDesc? DepthStencil { get; init; } = null;
+
+        // 下記
+        public RasterizerDesc Rasterizer { get; init; } = new();
+
+        // 下記
+        public MultisampleDesc Multisample { get; init; } = new();
+    }
+}
```

ColorTargets が空の場合は DepthStencil が必要で、出力先のない pipeline は初期契約では拒否する。

```diff
+namespace Lumyte.Graphics
+{
+    // ShaderEntry(ShaderModule Module, string EntryPoint) は immutable な stage 参照で、entry の存在・stage・device を検証する
+    // stage 間の組み合わせは同じ composition ID と互換 layout を要求する
+    // ShaderArgumentsLayout は ADR-0005 の非 generic な共通基底契約とし、ShaderArgumentsLayout<T> が型付き契約を加える
+    // GPU アドレスや binding の数値は保持しても利用者に公開しない
+    public readonly record struct ShaderEntry
+    {
+        public ShaderEntry(ShaderModule module, string entryPoint);
+        public ShaderModule Module { get; }
+        public string EntryPoint { get; }
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // ColorTargetDesc は TextureFormat Format 必須、ColorWriteMask WriteMask = All、BlendDesc? Blend = null を持つ
+    // WriteMask は Red／Green／Blue／Alpha の flags（None も可）
+    // Blend = null は blending 無効
+    // fragment の出力 location、numeric type と format が一致し、blend は format capability に従う
+    public sealed record ColorTargetDesc
+    {
+        public ColorTargetDesc();
+        // 診断専用。動作と互換性を変えない。
+        public string? Label { get; init; } = null;
+        public required TextureFormat Format { get; init; }
+        public ColorWriteMask WriteMask { get; init; } = ColorWriteMask.All;
+        public BlendDesc? Blend { get; init; } = null;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // BlendDesc は BlendComponentDesc Color = new() と BlendComponentDesc Alpha = new() を持つ
+    // BlendComponentDesc は BlendOperation Operation = Add、BlendFactor Source = One、BlendFactor Destination = Zero
+    // Operation は Add／Subtract／ReverseSubtract／Min／Max、Min／Max は factor が One／One の場合だけ受け付ける
+    // Factor は Zero／One／SourceColor／OneMinusSourceColor／SourceAlpha／OneMinusSourceAlpha／DestinationColor／OneMinusDestinationColor／DestinationAlpha／OneMinusDestinationAlpha／SourceAlphaSaturate／Constant／OneMinusConstant
+    // Alpha では color factor と SourceAlphaSaturate を拒否する
+    // dual-source blend は含めない
+    public sealed record BlendDesc
+    {
+        public BlendDesc();
+        // 診断専用。動作と互換性を変えない。
+        public string? Label { get; init; } = null;
+        public BlendComponentDesc Color { get; init; } = new();
+        public BlendComponentDesc Alpha { get; init; } = new();
+    }
+
+    public sealed record BlendComponentDesc
+    {
+        public BlendComponentDesc();
+        // 診断専用。動作と互換性を変えない。
+        public string? Label { get; init; } = null;
+        public BlendOperation Operation { get; init; } = BlendOperation.Add;
+        public BlendFactor Source { get; init; } = BlendFactor.One;
+        public BlendFactor Destination { get; init; } = BlendFactor.Zero;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // RasterizerDesc は CullMode Cull = None、FrontFace FrontFace = CounterClockwise、int DepthBias = 0、float SlopeScaledDepthBias = 0、float DepthBiasClamp = 0 を持つ
+    // Cull は None／Front／Back
+    // bias の float は有限、Clamp != 0 は対応 feature が必要
+    // fill は Solid、depth clip は有効を共通契約とし、wireframe／depth clamp は今回含めない
+    // FrontFace はライブラリの座標規約で判断し、backend の viewport 変換で反転する場合は内部で補正する
+    public sealed record RasterizerDesc
+    {
+        public RasterizerDesc();
+        // 診断専用。動作と互換性を変えない。
+        public string? Label { get; init; } = null;
+        public CullMode Cull { get; init; } = CullMode.None;
+        public FrontFace FrontFace { get; init; } = FrontFace.CounterClockwise;
+        public int DepthBias { get; init; } = 0;
+        public float SlopeScaledDepthBias { get; init; } = 0;
+        public float DepthBiasClamp { get; init; } = 0;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // MultisampleDesc は uint Count = 1、uint Mask = uint.MaxValue、bool AlphaToCoverage = false を持つ
+    // Count は 1 または 4、下位 Count bit だけを有効にする
+    // AlphaToCoverage は Count = 4、fragment が alpha を出力する location 0 の color target が必要
+    // Count とすべての attachment の source sample count が一致する
+    public sealed record MultisampleDesc
+    {
+        public MultisampleDesc();
+        // 診断専用。動作と互換性を変えない。
+        public string? Label { get; init; } = null;
+        public uint Count { get; init; } = 1;
+        public uint Mask { get; init; } = uint.MaxValue;
+        public bool AlphaToCoverage { get; init; } = false;
+    }
+}
```

### DepthStencilDesc

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record DepthStencilDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public DepthStencilDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // RenderPass の view format と一致
+        public required TextureFormat Format { get; init; }
+
+        // depth aspect がない場合は Always
+        public CompareOp DepthCompare { get; init; } = CompareOp.Always;
+
+        // depth aspect が必要
+        // readonly attachment では true を拒否
+        public bool DepthWrite { get; init; } = false;
+
+        // true は stencil aspect が必要
+        public bool StencilEnabled { get; init; } = false;
+
+        // front／back の compare と操作
+        public StencilFaceDesc Front { get; init; } = new();
+
+        // front／back の compare と操作
+        public StencilFaceDesc Back { get; init; } = new();
+
+        // 0〜255
+        public uint StencilReadMask { get; init; } = 255;
+
+        // 0〜255
+        // readonly stencil では 0 または全操作 Keep が必要
+        public uint StencilWriteMask { get; init; } = 255;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // StencilFaceDesc は CompareOp Compare = Always、StencilOp Fail = Keep、StencilOp DepthFail = Keep、StencilOp Pass = Keep を持つ
+    // StencilOp は Keep／Zero／Replace／IncrementClamp／DecrementClamp／Invert／IncrementWrap／DecrementWrap
+    // stencil reference の数値は Desc へ含めず RenderEncoder.SetStencilReference で指定する
+    public sealed record StencilFaceDesc
+    {
+        public StencilFaceDesc();
+        // 診断専用。動作と互換性を変えない。
+        public string? Label { get; init; } = null;
+        public CompareOp Compare { get; init; } = CompareOp.Always;
+        public StencilOp Fail { get; init; } = StencilOp.Keep;
+        public StencilOp DepthFail { get; init; } = StencilOp.Keep;
+        public StencilOp Pass { get; init; } = StencilOp.Keep;
+    }
+}
```

### ComputePipelineDesc

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record ComputePipelineDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public ComputePipelineDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // compute stage の entry point、同じ Device
+        public required ShaderEntry Compute { get; init; }
+
+        // compiled program の schema／BindingPlan と一致
+        public required ShaderArgumentsLayout ArgumentsLayout { get; init; }
+    }
+}
```

threadgroup size は compiled artifact の numthreads／特殊化結果から取得する。Desc で別の値を重複指定せず、各次元と積を device limits に照合する。dispatch の group count は CommandEncoder.Dispatch の引数で指定する。実行時に numthreads を変更するには別の compiled artifact を生成する。

### DrawDesc／IndexedDrawDesc

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record DrawDesc
+    {
+        // count = 0 は有効
+        // first と count の和は index の表現範囲内
+        public DrawDesc();
+        public string? Label { get; init; } = null;
+        public required uint VertexCount { get; init; }
+        public uint InstanceCount { get; init; } = 1;
+        public uint FirstVertex { get; init; } = 0;
+        public uint FirstInstance { get; init; } = 0;
+    }
+
+    public sealed record IndexedDrawDesc
+    {
+        // index 範囲が SetIndexBuffer した BufferSlice に収まること
+        // BaseVertex は符号付き
+        public IndexedDrawDesc();
+        public string? Label { get; init; } = null;
+        public required uint IndexCount { get; init; }
+        public uint InstanceCount { get; init; } = 1;
+        public uint FirstIndex { get; init; } = 0;
+        public int BaseVertex { get; init; } = 0;
+        public uint FirstInstance { get; init; } = 0;
+    }
+}
```

IndexFormat は Uint16／Uint32。primitive の不足する末尾は Native API の規約に従って描画されず、count を topology の単位へ丸めない。開始 index と instance の正規化、count = 0 の検証、index の内容による頂点アクセスは ADR-0006 に従う。

### 利用例

```csharp
var textureDesc = new TextureDesc {
    Size = new Extent3D(1280, 720, 1),
    Format = TextureFormat.Rgba8Unorm,
    Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
};

// view は MipLevelCount = 1、ArrayLayerCount = 1 で作成済みとする。
using var render = commands.BeginRenderPass(new RenderPassDesc {
    Colors = new[] { new ColorAttachmentDesc {
        View = view,
        ClearValue = ClearColor.Float(0, 0, 0, 1),
    } },
});
render.SetPipeline(pipeline); // color format と sample count が一致するもの
render.Draw(arguments, new DrawDesc { VertexCount = 3 });
```

## 検討した代替案

### Native API の Desc をそのまま公開する

実装との対応は容易だが、Native handle、layout、binding と platform 条件が利用側へ広がる。共通フィールドと検証条件を定め、各 backend が Native の表現へ変換する。

### 既定値で全フィールドを埋める

短い呼び出しになるが、format、size、用途や shader の未指定が意図せず有効になる。意味を推測できないフィールドは必須にし、固定状態の便利な既定値と区別する。

### 可変の Desc を記録後も参照する

コピーは省けるが、GPU 記録の途中で条件が変わり、非同期処理と寿命管理が複雑になる。入力は呼び出し時に snapshot する。

## 結果と影響

- 利用者はフィールド、単位、既定値と検証条件を参照してリソースと pipeline を構成できる。
- 共通 API に Native の物理表現を含めず、RenderEncoder の互換性検証を一貫して行える。
- 各 backend で format capability、alignment、上限、readonly aspect と shader layout の検証が必要になる。
- 初期契約の format、sample count とコピー用途を限定し、未対応機能は明示的に報告する。
- 公開 API の実装・各 backend の GPU 動作は未検証である。

## 検証方針

実装時は各 Desc の必須項目、既定値、enum の不明値、null、数値境界・overflow、配列 snapshot と device 不一致を検証する。texture の dimension／mip／layer、copy pitch と必要 bytes、sampler の比較型・anisotropy、shader stage と layout の不一致も確認する。

RenderPass／GraphicsPipeline の format・sample count・readonly aspect、integer clear／blend、resolve、strip index format を各 backend で確認する。今回の PR では ADR に出現する Desc 名の定義、相対リンク、必須節と既存契約との整合性を確認する。

## 別途決定する事項

- Swapchain／surface の Desc、Native C ABI の具体的な POD 宣言。
- 圧縮 format、depth／stencil コピー・resolve、sample count 拡張、wireframe／depth clamp、dual-source blend。
- DeviceCaps の全フィールドと、バックエンドごとの最低 capability。resource の具体的な fields は ADR-0009〜0011 に定める。

## 参考資料

- [グラフィックス共通契約](0004-graphics-library.md)
- [Slang コンパイルと GPU データ受け渡し](0005-shader-compilation-and-data-interop.md)
- [RenderEncoder](0006-render-encoder.md)
- [WebGPU descriptors](https://www.w3.org/TR/webgpu/)
