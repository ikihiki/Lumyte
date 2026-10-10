# Lumyte.Graphics.Abstractions

バックエンドが実装するグラフィックデバイスの共通契約です。外部の graphics binding には依存しません。

```csharp
using Lumyte.Graphics.Abstractions;

static DeviceCaps Inspect(IGraphicDevice device) => device.Caps;
```

`IGraphicDevice` は `Caps`、型付き buffer の `CreateBuffer<T>` と `GetBufferLayout<T>`、texture の `CreateTexture`、sampler の `CreateSampler`、論理登録先の `CreateArgumentTable`、shader module、command buffer、copy layoutとdevice所有の `Queue` を提供します。デバイス自体を生成する factory、バックエンドの選択・解放は持ちません。生成と解放はアプリケーションの起動・終了部分で、選んだバックエンドの具象型を使って行います。

`DeviceCaps` は生成済みデバイスの利用可能な機能と上限の非所有 snapshot です。同じデバイスは同じ instance を返し、読み取りでは native query、allocation、GPU work を行いません。`with` で作ったコピーは元の snapshot を変更しません。

- サイズと alignment は byte 単位です。texture の dimension は texel 単位、その他の上限は個数です。
- 上限の `0` はその用途への対応がないことを表します。alignment は正数で、`1` は追加の alignment 制約がないことを表します。
- alignment は利用側が検証する値で、要素数やサイズを暗黙に補正するものではありません。image の format ごとの texel block 制約は別途必要です。
- `GraphicsFeatures` は共通の機能 flags です。対応する resource・command API の提供範囲とは区別します。
- 最大サイズは allocation の成功や空きメモリを保証しません。

設計判断は [GRAPHICS-0001](../../../docs/adr/graphics/GRAPHICS-0001-graphics-device.md)、生成手順は [Wgpu](../Lumyte.Graphics.Wgpu/README.md)、[Browser](../Lumyte.Graphics.Browser/README.md)、[Vulkan](../Lumyte.Graphics.Vulkan/README.md) を参照してください。

## 型付きbuffer

`BufferDesc<T>` の Count は要素数です。`IGraphicsBuffer<T>` は backend の具象 allocation 自身が実装し、`SizeInBytes` は raw element stride × Count を checked 計算します。T は unmanaged で、数値型・enum・struct を扱えます。shader ABI への pack は行いません。

```csharp
using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint>
{
    Count = 16,
    Usage = BufferUsage.CopySource,
    Memory = MemoryPreference.Upload,
});
await upload.MapAsync();
upload.Slice(2, 3).CopyFrom(new uint[] { 10, 20, 30 });
upload.Unmap();
```

`BufferSlice<T>` は非所有の値型です。CopyFrom／CopyTo は map 済み CPU memory のコピーだけを行います。mapping は MapAsync／Unmap で明示し、staging 確保、GPU copy、送信、完了待機、barrier は利用者が command 側で組み合わせます。

`BufferLayout<T>` の byte alignment と要素単位の倍数は backend が数値で提供します。要素数や論理 SizeInBytes を丸めず、コピー条件を満たさない場合は利用側の command 記録時に拒否します。

`IGraphicsBuffer<T>` に byte 範囲検証と CPU copy の契約も含め、slice は同じ allocation に処理を委譲します。buffer は device より先に解放します。各 backend の device は所有 buffer・texture・sampler が残っている場合に Dispose を拒否します。

設計判断は [GRAPHICS-0002](../../../docs/adr/graphics/GRAPHICS-0002-typed-buffers.md) を参照してください。

## TextureとView

```csharp
using IGraphicsTexture texture = device.CreateTexture(new TextureDesc
{
    Width = 64,
    Height = 64,
    ArrayLayers = 6,
    MipLevels = 7,
    Format = TextureFormat.Rgba8Unorm,
    Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
});
using IGraphicsTextureView cube = texture.CreateView(new TextureViewDesc
{
    Dimension = TextureViewDimension.Cube,
    BaseMipLevel = 1,
    MipLevelCount = 2,
});
```

`IGraphicsTexture` と `IGraphicsTextureView` は具象backend自身が実装します。初期範囲は単一sampleの2D color textureで、mipとarray layerを指定できます。ViewのInfoはnull countを解決済みで、sourceと同じformatを保持します。GetMipSizeはmipの幅・高さを返します。Cube系はsquareなtextureの連続6 layer単位です。

ViewはSampledまたはRenderAttachment用途を必要とします。View、Texture、Deviceの順で解放します。生きた子resourceがある親のDisposeは拒否し、GPU完了待機や自動解放は挿入しません。textureのCPU mappingや自動upload／readbackはありません。

設計判断は [GRAPHICS-0003](../../../docs/adr/graphics/GRAPHICS-0003-textures-and-views.md) を参照してください。

## 利用者による同期

resource APIは並列実行の安全性を保証しません。backendが許す並列実行の範囲と、device・buffer・texture・viewの生成・アクセス・mapping・解放に必要な同期は利用者が管理します。live childやmapping状態の検証は必要な同期の代わりにはなりません。GPUアクセスの同期もcommand／submissionの契約に従って利用者が保証します。

## Sampler

```csharp
using IGraphicsSampler sampler = device.CreateSampler(new SamplerDesc
{
    MinFilter = FilterMode.Linear,
    MagFilter = FilterMode.Linear,
    MipmapFilter = FilterMode.Linear,
    AddressU = AddressMode.Repeat,
    AddressV = AddressMode.ClampToEdge,
    LodMinClamp = 0,
    LodMaxClamp = 0,
});
```

SamplerはTexture／Viewとは独立した所有resourceで、複数textureで同じinstanceを共有できます。Descは生成時の指定値を保持します。LODは有限・非負かつmin <= maxで、max=0を暗黙に変更しません。MaxAnisotropyはcaps以下で、1より大きい場合はすべてのfilterにLinearが必要です。Compareがnull以外なら比較samplerとして確保します。shader／textureとの互換性はbinding側で検証します。

samplerを先にDisposeし、samplerが残ったdeviceの解放は拒否します。同期は利用者が管理し、内部lock・アトミックカウンター・自動cacheを追加しません。設計は [GRAPHICS-0004](../../../docs/adr/graphics/GRAPHICS-0004-samplers.md) を参照してください。

## Argument TableとGPU参照

```csharp
using IArgumentTable table = device.CreateArgumentTable(new ArgumentTableDesc
{
    TextureCapacity = 20,
    SamplerCapacity = 1,
    BufferCapacity = 1,
});
IGpuRef<IGraphicsTextureView> image = table.WriteTexture(0, sampledView);
IGpuRef<IGraphicsSampler> sampling = table.WriteSampler(0, sampler);
IGpuRef<uint> values = table.WriteBuffer(0, storage.Slice(3, 5));
IGpuRef<uint> value = values.GetElement(2);
```

slotは利用者が管理する論理位置です。種類ごとに独立し、同時shader binding数ではありません。IGpuRefのCountは論理要素数で、bufferのGetElementは登録範囲から単一要素を選びます。GPU addressやbinding番号への変換は公開しません。

slotの置換・Release・table Disposeは古い参照と派生要素を失効させます。tableは登録したresourceを保持し、登録中のbuffer／view／samplerのDisposeは拒否します。tableまたは登録を先に解放してください。tableを解放しても登録resource自体はDisposeしません。

今回のAPIは登録・要素参照・失効と寿命の基盤です。IGpuRefをGPU dataへpackするserializerと、root参照から物理bindingを構築するshader／commandの接続はそのAPIで扱います。IGpuRefを含む論理structはunmanagedではないため、raw bufferのCopyFromへそのまま渡しません。登録はGPU copy・upload・bind group生成を行いません。

設計判断は [GRAPHICS-0005](../../../docs/adr/graphics/GRAPHICS-0005-argument-tables-and-gpu-references.md) を参照してください。

## シェーダー

`ShaderCompilationDesc` は Slang source、entry、stage と生成 target を指定します。`IShaderCompiler.CompileAsync` は GPU に依存せず `ShaderArtifact` を返します。artifactは全targetのcode、reflection、コンパイルmetadataを格納したopaque binaryを保持し、`LoadEmbedded(assembly, resourceName)`でDLLから読み込めます。online compilerのtarget選択は任意で、省略／null時は全対応target、指定時はそのtargetだけを生成します。

opaque binaryのartifactをそのまま `IGraphicDevice.CreateShader` に渡すと、backendが自分用のtargetを取り出し、バックエンド所有の `IGraphicsShader` が返ります。必要な同期は利用側が管理します。[設計](../../../docs/adr/graphics/GRAPHICS-0006-shader-compilation-and-modules.md)／[Slang コンパイラーとオフライン設定](../Lumyte.Graphics.Shaders/README.md)を参照してください。

## CommandBufferとGPU実行

`CreateCommandBuffer(new())` はRecording状態のone-shot記録を返します。GPUのbuffer／textureコピー、明示的なbarrier、render／compute passを記録し、`Finish()` の後で `device.Queue.Submit([commands])` を呼びます。`IGraphicsSubmission.WaitAsync()` でその提出分の完了を待ちます。各操作はstagingの確保、CPUコピー、map／unmapやGPU待機を自動実行しません。

textureは利用前に `TextureBarrierDesc` でstateを宣言します。pass内ではcopy・barrier・Finishを拒否します。render passはcolor attachmentのclear／load・store／discardを扱い、両encoderは `End()` で終了します。draw／dispatchとpipeline設定は次の契約を使用します。

resourceはGPU完了まで利用者が生存させます。Submit時に生存とmappingを再検証し、Pending中のsubmissionとSubmitted状態のcommand bufferのDisposeを拒否します。待機のキャンセルでGPU実行は取り消しません。内部lockや並列呼び出しの保証は設けません。設計は [GRAPHICS-0007](../../../docs/adr/graphics/GRAPHICS-0007-command-buffers-and-submission.md) を参照してください。

## Pipelineと描画状態

`CreateGraphicsPipeline(GraphicsPipelineDesc)` はshader moduleを保持するprogramを返します。`IRenderEncoder.SetPipeline(IGraphicsPipeline)` と `SetRenderState` は独立し、viewport・scissor・blend constant・stencil referenceも明示してから `Draw` を呼びます。利用側にattachment layoutやprepare objectを要求せず、passの実viewからformatを取得してnative variantを解決します。ColorTargetsはsnapshotにし、write mask／sample maskの0を補正しません。

`CreateComputePipeline(ComputePipelineDesc)` はbinaryのentry・stage・workgroup sizeを使用します。compute encoderでSetPipeline後にDispatchを記録します。workgroup各軸・invocationの積とdispatch group数はcapsで検証し、数値を補正しません。

初期の実行範囲は既存のsingle-sample color attachmentとresource bindingを必要としないshaderです。depth/stencil、MSAA、root data／Argument Tableの物理binding接続はそれぞれのresource・binding契約で追加します。対応するattachmentがないdepth test等と、未接続のresource ABIは明確に拒否します。program → shader → deviceの順で解放し、GPU完了までの寿命と同期は利用者が管理します。

設計は [GRAPHICS-0008](../../../docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md) を参照してください。

## 構造体のshader argumentsとshader data

applicationのpartial root structにIShaderArgumentsを実装し、IRenderEncoder／IComputeEncoder.SetArgumentsへ渡します。IShaderDataを実装したstructは`CreateBuffer<T>`(artifact, count)でGPU buffer、memoryにUploadを指定するとstaging bufferを生成します。map済みstagingへCopyFromし、unmap後にcommandのCopyBufferとShaderDataBufferBarrierDescで明示転送します。`ShaderDataSlice<T>`をIArgumentTable.WriteBufferへ登録すると`IGpuRef<T>`を取得でき、GetElementで各要素をroot引数へ渡せます。

生成codecは[Lumyte.Graphics.Generators](../Lumyte.Graphics.Generators/README.md)から導入します。各draw／dispatchは到達可能な要素と参照をsnapshotします。shader dataのCPU値設定はUpload stagingだけに許可します。Automaticへは明示copyし、draw時の自動転送はしません。raw byte aliasとshader writeは公開しません。通常のunmanaged bufferは従来の明示copyとbarrierを使います。同期と登録resourceの寿命は利用者が管理します。

バックエンドと共有実装ライブラリの境界には `IShaderReference`、`IShaderDataSource`、`IShaderRawBuffer`、`IShaderDataLayout` を使用します。参照の登録 identity、CPU 側の要素 snapshot、native storage handle を backend が提供し、[Lumyte.Graphics.Shared](../Lumyte.Graphics.Shared/README.md) が型配置と依存収集を処理します。利用側のアプリケーションは `IGpuRef<T>` と graphics API を使用します。

`IShaderReference` は参照先のbyte範囲に加え、登録時の `RegistrationOffsetInBytes` と `RegistrationSizeInBytes` を保持します。GetElementでも登録範囲を維持し、bindingのoffset alignmentと要素位置を分けて検証します。元のbuffer全体のサイズで小さな登録範囲を拒否せず、登録範囲をbinding上限へ照合します。

`IShaderArguments.Capture<T>`はTの静的metadataと生成済みWriteを直接呼び、codec登録やmodule initializerを必要としません。RootParameterは既定でarguments、異なるroot名は型の静的propertyで指定します。IShaderDataはIShaderArgumentsを継承し、partial structへ同じcodecを生成します。
