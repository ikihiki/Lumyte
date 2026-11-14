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
upload.SliceElements(2, 3).CopyFrom(new uint[] { 10, 20, 30 });
upload.Unmap();
```

`BufferSlice<T>` は非所有の値型です。CopyFrom／CopyTo は map 済み CPU memory のコピーだけを行います。mapping は MapAsync／Unmap で明示し、staging 確保、GPU copy、送信、完了待機、barrier は利用者が command 側で組み合わせます。

`BufferLayout<T>` の byte alignment と要素単位の倍数は backend が数値で提供します。要素数や論理 SizeInBytes を丸めず、コピー条件を満たさない場合は利用側の command 記録時に拒否します。

`IGraphicsBuffer<T>` に byte 範囲検証と CPU copy の契約も含め、slice は同じ allocation に処理を委譲します。buffer は device より先に解放します。resourceの有効期間と親子の解放順は利用者が保証します。

設計判断は [GRAPHICS-0002](../../../docs/adr/graphics/GRAPHICS-0002-typed-buffers.md) を参照してください。

範囲操作は`SliceElements(elementOffset, elementCount)`、
`CopyBytesFrom(source, byteOffset, byteLength)`、`CopyBytesTo(destination, byteOffset, byteLength)`、
`ValidateByteRange(byteOffset, byteLength)`へ統一します。mapping・範囲条件は各interfaceに定義します。
shader ABIを使うbufferは`device.CreateShaderDataBuffer<T>(artifact, count, memory)`で生成でき、
raw bufferとshader bufferの要素範囲はともに`SliceElements`で指定します。
shader data bufferの`MapAsync(cancellationToken)`もraw bufferと同様、native mappingの完了後に
キャンセルを確認し、成功したmappingをUnmapしてからキャンセルを返します。

## 明示的な描画初期化

passのdynamic stateは次の呼び出しでまとめて設定できます。

```csharp
encoder.SetRenderState(GraphicsRenderStateDesc.ForColorTargets(1));
encoder.SetDynamicState(new Viewport(0, 0, width, height, 0, 1), new ScissorRect(0, 0, width, height));
```

SetDynamicStateはviewport、scissor、blend constant、stencil referenceをその順で設定します。
blend constantとstencil referenceの既定は0で、引数による変更と後続の個別setterも使えます。
GPU同期やdrawを発行せず、passを開始しません。後のsetterが失敗しても以前の設定は戻しません。
ForColorTargets(0)はdepth-only pass用です。実attachmentとの互換性検証はbackendが行います。
設計は[GRAPHICS-0023](../../../docs/adr/graphics/GRAPHICS-0023-explicit-api-convenience.md)を参照してください。

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

ViewはSampledまたはRenderAttachment用途を必要とします。View、Texture、Deviceの順で解放します。子resource数による解放拒否、GPU完了待機、自動解放は行いません。textureのCPU mappingや自動upload／readbackはありません。

設計判断は [GRAPHICS-0003](../../../docs/adr/graphics/GRAPHICS-0003-textures-and-views.md) を参照してください。

## 利用者による同期

resource APIは並列実行の安全性を保証しません。backendが許す並列実行の範囲と、device・buffer・texture・viewの生成・アクセス・mapping・解放に必要な同期は利用者が管理します。子resourceの保持数やGPU使用中のresource一覧を検証目的で維持しません。GPUアクセスの同期もcommand／submissionの契約に従って利用者が保証します。

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

samplerをdeviceより先にDisposeしてください。同期は利用者が管理し、内部lock・アトミックカウンター・自動cacheを追加しません。設計は [GRAPHICS-0004](../../../docs/adr/graphics/GRAPHICS-0004-samplers.md) を参照してください。

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
IGpuRef<uint> values = table.WriteBuffer(0, storage.SliceElements(3, 5));
IGpuRef<uint> value = values.GetElement(2);
```

slotは利用者が管理する論理位置です。種類ごとに独立し、同時shader binding数ではありません。IGpuRefのCountは論理要素数で、bufferのGetElementは登録範囲から単一要素を選びます。GPU addressやbinding番号への変換は公開しません。

encoder.SetArgumentTable(table)で使用するtableを明示できます。未選択ならrootの単一table hintを自動選択に使い、明示SelectedTableを優先します。
backendは直接参照のtable整合性を符号化時に検査し、検証ONだけでbuffer全体の間接依存も検査します。必要なtableが解決できなければInvalidOperationException、別tableの参照はArgumentExceptionです。
同じresourceやslotを登録していても、別tableから発行された参照は使えません。
tableを切り替えた後のdraw／dispatchにも現在のtableで検証を行います。値だけの引数にはtableは不要です。

slotの置換・Release・table Dispose後の古い参照と派生要素は使用できません。既定ではその有効期間を利用者が管理します。backendの実deviceで子資源の作成前にEnableValidation()を明示すると、記録時とSubmit直前の登録identityとresourceの生存も診断します。登録・参照を使い終わってからbuffer／view／samplerを解放してください。tableを解放しても登録resource自体はDisposeしません。

登録は選択table全体のbindingに使われます。

設計判断は [GRAPHICS-0005](../../../docs/adr/graphics/GRAPHICS-0005-argument-tables-and-gpu-references.md) を参照してください。

raw範囲のWriteBufferは登録ごとにShaderBufferAccessを選択します。既定はReadOnlyで、ShaderReadの能力が必要です。ReadWriteを明示する登録にはShaderWriteが必要です。BufferUsageは作成済みリソースの能力であり、その登録のアクセス種別を推定するためには使いません。

```csharp
IGpuRef<uint> read = table.WriteBuffer(0, storage.SliceElements(0, 4));
IGpuRef<uint> write = table.WriteBuffer(1, storage.SliceElements(0, 4), ShaderBufferAccess.ReadWrite);
IGpuRef<uint> element = write.GetElement(2);
```

この例のstorageはShaderReadとShaderWriteの両能力を持ちます。参照型はどちらも`IGpuRef<uint>`のままで、GetElementは元の登録を保持します。別スロットへの別アクセス登録は独立し、nativeの同時使用制約と寿命・同期責務は維持します。参照を含むShaderDataSliceは引き続き読み取り専用です。シェーダーとの種別・対象型の一致は明示的な検証でのみ診断し、通常の参照転送に文字列パスやシェーダーからのアクセス推定を使いません。

## シェーダー

`ShaderCompilationDesc` は Slang source、entry、stage と生成 target を指定します。`IShaderCompiler.CompileAsync` は GPU に依存せず `ShaderArtifact` を返します。artifactは全targetのcode、reflection、コンパイルmetadataを格納したopaque binaryを保持し、`LoadEmbedded(assembly, resourceName)`でDLLから読み込めます。online compilerのtarget選択は任意で、省略／null時は全対応target、指定時はそのtargetだけを生成します。

opaque binaryのartifactをそのまま `IGraphicDevice.CreateShader` に渡すと、backendが自分用のtargetを取り出し、バックエンド所有の `IGraphicsShader` が返ります。必要な同期は利用側が管理します。[設計](../../../docs/adr/graphics/GRAPHICS-0006-shader-compilation-and-modules.md)／[Slang コンパイラーとオフライン設定](../Lumyte.Graphics.Shaders/README.md)を参照してください。

形状・静的モデルの描画には [Lumyte.Graphics.Drawing](../Lumyte.Graphics.Drawing/README.md)、共有画像処理には [ImageEffects](../Lumyte.Graphics.ImageEffects/README.md) を使用できます。

## struct限定の転送と利用者が選ぶ範囲

IShaderArgumentsを使うnative/root、buffer/slice、factory、Graph、resource宣言はすべて`where T : struct, IShaderArguments`です。管理参照を許すためunmanaged制約にはしません。通常のinterfaceなしunmanaged経路は不変です。生成対象はmutable partial struct/record structで、private unmanaged wireと型付きIGpuRefを持ちます。class/record class/ref structは手書きWriteがあってもLUMG001、generatorなしの手書きclassもconsumerでCS0453です。

readonly Writeはgetter前にwireだけをコピーします。数値partial propertyには`{ readonly get; set; }`を使用できます。生成実装は宣言のreadonly修飾を正確に保持し、宣言が通常のgetならreadonly getを合成しません。in/ref readonly経由のinterface呼び出しやreadonlyでないgetterでは外側structの防御的コピーがあり得ます。引数CaptureはDiagnosticTypeとキャッシュ済みの静的generic DiagnosticFields callbackを保持しますが、入力struct、boxingやinstance closureは保持しません。CaptureとCopyはcallbackを呼ばず、最初の明示GetDiagnosticFieldsで`ArgumentDiagnosticCache<T>`を初期化し、T.TransferFieldsを直接静的dispatchします。`Fields<T>(in T)`も同じ経路で、DynamicMethodやMakeGenericMethodなしにBrowser／NativeAOTで生成metadataを利用できます。ClassDiagnosticSource/GetTransferFieldsはありません。

利用者が要素範囲を選び、CopyFromとCopyBufferを明示します。dirty tracking、field範囲転送、自動coalescingや値cacheはありません。配列要素はrefで編集するかコピーを書き戻してください。default(T)と配列初期要素はconstructorを実行せず、参照は利用者が設定するまでnullです。DrawingInstructionのnew()はresource indexの-1と参照のdefault!初期化を保ちます。default!はnullable解析の抑制であり有効なGPU参照ではなく、default rootのnull IGpuRefは符号化時に拒否されます。upload元の寿命・不変性はGPUが消費するまで利用者が保証します。

## 値だけを渡す必須writer

IShaderValueWriterは次の二つの抽象メソッドだけを持ちます。各呼び出しは値一つだけを受け、offset、size、path、seek、skip、WriteAtはありません。

```csharp
void WriteValue(ReadOnlySpan<byte> value);
void WriteReference<T>(IGpuRef<T>? value);
```

数値だけの型はパディングを含む全wireを一回のWriteValueへ渡します。参照を含む型はprivate unmanaged wire全体をローカルへコピーし、raw区間と16-byte参照を物理順で交互に渡します。生成したprivate static planは実際のC#オフセットによる順序と妥当性だけを扱い、不正なplanはgetter前に拒否します。custom getterもローカルコピー後に物理順で一回ずつ評価し、emitter structは生成しません。

VulkanShaderValueWriter、Direct3D12ShaderValueWriter、WgpuShaderValueWriter、BrowserShaderValueWriterは各backend内部のIShaderValueWriter実装です。各backendのShaderTransfer helperがSetと型付きCopyFromの両方でvalue.Write(ref writer)を直接呼び、Sharedのwriterや参照符号化callbackを経由しません。各writerが暗黙位置とexpectedTransferSizeを所有し、length > expectedTransferSize - positionという残容量検査と、Complete時のposition == expectedTransferSizeを必須とします。WriteReferenceはゼロクリアしたstackalloc byte[16]へABIをその場で符号化し、Vulkan／D3D12のroot span、Wgpu／BrowserのrootのWriteまたは検査済みupload batch viewへ渡します。任意診断は型、参照、現在位置を受けるValidateReference overloadを使い、native転送の参照ごとにShaderValueや16-byte配列を生成しません。spanは同期借用だけで保持せず、destinationとaliasさせません。IShaderWireSink、IShaderReferenceCursor、IShaderReferenceEmitter、ShaderWire、ShaderWireCursorとWriteWireは削除し、aliasや旧cursor互換経路は残しません。IShaderArgumentsが唯一の転送interfaceで、旧IShaderData markerもありません。

検証ONまたは明示prepare時の参照配列とimmutable metadata owner、Wgpu／Browserのroot payload allocationやdelegate、実行ごとのbinding／descriptor heap確保は残ります。Browserのspan-to-JSコピーも残り、zero allocation、zero copyや性能向上の実測は主張しません。

## SetArgumentsの値の所有権

generic SetArgumentsはSet時点の値を固定します。Vulkan／D3D12は4-byte切り上げの有界stack spanをクリアして書き、Setが戻る前にnative定数commandを記録します。Complete、任意診断、参照準備、metadata生成はnative呼び出しより前です。driver command bufferが数値コピーを所有し、一時stackは最終payload ownerではありません。Draw／Dispatchは数値を再送せず、選択rootは不変metadataを保持し、参照metadataとadmissionは収集時だけ保持します。Wgpu／Browserは従来のbackend-private uniform storage世代へ直接書きます。元のstruct／in参照をDrawまで借用しません。無効な参照の符号化失敗はSet中に発生し、前回成功した選択を保ちます。別Setは別世代を所有し、Drawは数値を再転送せず、Wgpu／Browserのsealed世代も書き換えません。WGSLのpool／remapは選択table全体の登録から構築し、間接依存の更新は検証ONだけです。

## 明示検査としてのCapture

IShaderArguments.Capture、ShaderTransfer.CaptureとShaderValueSnapshotは明示的なCPU検査・診断専用です。サイズをconstructorで受けるShaderSnapshotWriterが同じ二つのwriterメソッドを使い、参照スロットをゼロにした独立bytesと論理参照identityを取得しますが、backendはsnapshotをSet／upload／binding入力として受け付けません。SetArguments(ShaderValueSnapshot)、CreateSnapshot、Pack、SharedPackRawとsnapshotの実行admissionは削除しました。Fields、任意のreflection、手書き型の静的metadata取得補助は配置診断用に残り、書き込みの代替ではありません。Typeのみの未知Tのreflectionとraw unmanagedの反射field fallbackはAOTでは利用できず空を返します。JITではdefault TransferFieldsが空の手書きunmanaged IShaderArgumentsの反射fallbackを維持し、静的metadataがない管理参照入り外側structは空の診断を返します。Browser build成功はAOT publicationやAOT runtime実行の証拠ではなく、両者は未検証です。

ModelVertex.ValidateShaderLayoutはUnsafe.SizeOfとShaderTransfer.Fieldsを使い、Captureによるprobeを作りません。ShaderBindingDependencies.Collect(rootArguments, …)は依存metadataだけの収集で、numeric CPU snapshotではありません。RootプロパティとShaderValueSnapshotを受けるCollect overloadはありません。GPU上の数値が必要なら明示readbackを使います。

## transportに依存しないroot payload

ShaderRootArgumentsはRootParameter、ShaderTypeName、WireSize、Referencesの四つの読み取り専用propertyとdetached admission callbackだけを持つ不変metadataです。Create(rootParameter, shaderTypeName, wireSize, references, prepare)は名前、非負のwireSizeとnullableなreferencesを受け取り、任意の参照準備を行います。Admitは実行時の完全な参照集合をcallbackへ渡します。IShaderRootStorageは削除し、公開の代替storage型やobject bagは設けません。Storage、Allocate、Seal、Dispose、IDisposableや共通のsealed／disposed状態はありません。Wgpu／Browserの既存の非公開具象RootStorageだけがIDisposableとしてuniformと一度だけ設定するArgumentsを所有します。writerは具象RootStorageへ直接書き込み、bindingも具象ownerを直接受け取ってSealします。commandはKeepBindingでGPU利用終了まで具象ownerを保持します。Vulkan／D3D12のmetadataは解放不要でSet時のKeepBinding(root)もありません。記録済みbindingは機能上必要な登録とtable identityを保持し、検証ONの場合だけ追加の依存revisionを保持します。未使用metadataは再選択後に保持不要です。

VulkanDevice.MaxPushConstantsSizeは物理deviceの上限です。Radeon8060Sでの256 byteは普遍的な上限ではありません。Direct3D12Device.RootConstantDwordCapacityは58、RootConstantByteCapacityは232です。artifact容量の事前検査はEnableValidationに依存しない必須のABI安全性検査です。VulkanShaderは所有deviceの上限をshader module生成前に検査し、D3D12はgraphics／compute／partialの全pipeline経路でnative pipeline生成前に拒否します。全app ConstantBufferを実際のroot名でShaderDataLayout.Rootから取得した論理Sizeで検査し、lumyteDrawInfoと__lumyte_schema_は除外します。論理232 byte／宣言padding240 byteは58 DWORDだけを読むため許可します。Setでも超過はpayload確保・公開前に拒否し、前回成功したroot選択を維持します。Vulkan／D3D12はSet中にnative constantsを記録し、CPU heap数値配列やArrayPool rental、per-Set GPU uniform／CBVもfallbackもありません。Wgpu／Browserは従来のuniform emulationをbackend内部に保ち、最小16 byte・16-byte整列とnative uniform binding上限もbackend自身が検査します。

ShaderSourcePreparationのtarget別出力はlumyteRootStorage metadataをVkPushConstants／D3DRoot32BitConstants／UniformBufferとして記録します。shader emitterのtarget認識は必要ですが、共通の状態・metadata・寿命管理へWgpu／Browser判定によるfeature dispatchを持ち込みません。公開型階層やInternalsVisibleTo、immediate API、package更新は追加しません。

Vulkan／D3D12 writerはref structです。IShaderArguments.`Write<TWriter>`は`where TWriter : IShaderValueWriter, allows ref struct`となり、C# 13以降と固定.NET 10 SDKが必要です。公開writerの二メソッド・各一引数・offsetなしは不変です。host検査や準備hookの失敗ではnative定数を発行せず旧選択を保ちますが、発行済みvoid driver commandやdevice failureのrollbackは保証しません。

Vulkan／D3D12のrender／compute全四encoderはcodecとprepare中を_settingArgumentsで保護し、入れ子のSet、pipeline変更、End、Draw／Dispatchなどの再入を拒否します。Graph準備のtable自動選択に必要なSetArgumentTableだけは許可します。callback後は、捕捉したpipelineのValidateAlive、owner.ValidatePassとpipeline identity一致をすべてnative発行より前に再検査します。raw command bufferやpipelineのDisposeがguardを迂回しても、解放済みhandleを使うnative発行を阻止します。host失敗では旧root選択を保持しますが、呼び出し側が明示したresource Dispose、table自動選択や発行済みdriver commandは取り消しません。

## 選択table全体のbindingと任意の依存検証

bindingは選択したArgumentTableの書き込み済み登録すべてから構築します。capacityの未登録holeはentryではありません。Vulkanは全texture／sampler登録をdescriptorへ投影し、buffer参照は保存済みGPU addressへoffsetを加えてinline符号化します。Direct3D12は全texture／sampler／ReadOnly SRV／ReadWrite UAV登録を投影し、各上限128／16／64／64を適用します。Wgpu／Browserは全登録から有限のbinding poolとremapを構築し、WGSLの個数はcapacityではなく実際の書き込み済み登録を基にします。native binding数・storage範囲・remap容量へ収まらなければ明確に拒否し、巨大な無効binding、追加feature・package・pool・cacheで回避しません。rootなしのshaderも明示選択tableの全登録をbindし、Wgpuは未使用のroot uniform binding 8を省略します。

全登録をbinding可能にすることは、全登録をGPU使用済みとして記録することではありません。backend-privateな登録・remapは検証OFFでも必要な機能状態ですが、同期の所有権や使用hazardを追加しません。resource寿命とCPU／GPU同期は利用者が管理します。

Graphのbarrier・依存順・cullingは、必要なresourceの正確で完全なpass宣言だけを使い、従来どおりallocation全体を保守的に同期します。検証OFFでは宣言の真実性・完全性が利用者の事前条件です。root走査、依存query、準備hook、許可集合、admission callbackの確保を行いません。table共有だけでは使用宣言になりません。

検証ONの場合だけ、Graphはrootとbuffer全体の推移的依存を、buffer allocationまたはtextureの元imageを正規化したresource object identityと宣言read／write権限の合併で照合します。native handle値の衝突判定、要素・byte範囲・view・callback取得元による許可判定は行いません。同じbufferの別宣言範囲から保存した参照も許可されますが、未宣言の別resourceとread-only宣言に対するReadWrite参照は拒否します。

GetGpuRefの具体化はこの粗い許可判定とは別です。要求offset／countと一致する登録を選び、単一要素なら既存登録からGetElementで導出できます。未登録の複数要素範囲に、より広いCountの参照を返しません。新しいslice APIや自動dirty trackingは追加しません。

## buffer全体の読み取り専用依存query

共通境界はIShaderDataSource.TryGetDependenciesだけです。ReadReferences(index)、SetTransferredReferences、SharedのShaderDataTransferStateおよびShaderBindingDependencies.Elementsは削除し、共通の書き込みAPIは設けません。

```csharp
using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;

// IShaderDataSourceの宣言抜粋。手書きbackendも実装する。
public interface IShaderDataSource
{
    bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies);
}
```

既存の有効なsourceに対する呼び出し例です。

```csharp
// falseを既知の空集合として扱わない。
if (source.TryGetDependencies(out IShaderReferenceMetadata? dependencies))
{
    foreach (IShaderReference reference in dependencies.References)
    {
        // 追跡済みのbuffer全体の依存を読み取る。
    }
}
else
{
    // 追跡利用不可。呼び出し側が宣言と同期を保証する。
}
```

検証OFFでは全backendが数値だけのReferenceCount = 0を含めてfalse／nullを返します。これは追跡利用不可であり、既知の空集合ではありません。登録・remapなどの非公開機能状態は残ります。検証ONではbuffer全体の不変な依存unionを返し、ReferenceCount = 0は既知の空集合としてtrueです。参照付きbufferの未初期化・失敗後の状態は無効で、queryはInvalidOperationExceptionとなり、buffer全体の明示再転送で回復します。部分転送だけでは回復しません。

既知状態への部分更新は旧依存とのunionを保持でき、buffer寿命内で保守的な偽陽性が残ります。全体書き直しは現在のunionを置き換えられますが、記録済みcommandが保持する旧revisionは不変です。要素ごとの配列や毎drawの行走査は不要で、検証ONのShaderBindingDependencies.Buffersが各bufferを一回だけ訪問します。

CPU CopyFromの更新はbackendが所有します。GPU copyは検証ONだけでbackend-privateなbuffer単位のcommand overlayを作り、native copy記録成功後に更新し、queue Submit成功後にだけ公開します。失敗・未送信・破棄したcommandから公開せず、隠れたreadback・待機・数値履歴も持ちません。

### 厳密な参照数とmapped batchの契約

IShaderArgumentsのstatic abstract ReferenceCountは重複も数えた物理16-byte slot数です。backend writerは全modeで0 <= N <= wireSize / 16、WriteReferenceの超過とCompleteの不足、位置・容量・型・範囲を検査します。rootの直接参照配列を収集するのは検証ONまたは利用者が明示したprepare hookがある場合だけです。それ以外はscalar countと単一の暗黙table hintだけを保持します。数値だけの追跡rootは共有empty metadataであり、未収集のnullとは区別します。

ShaderRootArgumentsはstorageを持たない不変metadata classです。Referencesはnullableでnullは未収集を表し、Createにprepareを渡す場合は利用可能なmetadataが必須です。rootの単一table hintは自動選択を補助しますが、明示SelectedTableが優先します。異なるtableが混在すればnative Set発行前に拒否します。

native CopyFromはbatch全体のmapped spanを一度だけ範囲・mapping方向・overflow検査し、row sliceへ直接書きます。Browserは一つの検査済みUint8Array viewへJS.setでコピーし、finallyでviewだけを解放します。検証ONだけHostWriteLeaseで再入書き込み・Map／Unmap／Disposeを診断します。失敗時は部分byte更新を許し、追跡buffer全体を無効にして元例外を保持します。OFFでは利用者がmappingと寿命・並行操作を保証し、追加lock・待機・pinは設けず、無効pointerを実行する試験もしません。

VulkanとD3D12はbuffer作成時の保存済みaddressへchecked offsetを加え、参照ごとのdriver queryやglobal cacheを作りません。native root即時定数、固定signature／stage、pipeline・End・callback後の再検査は継続します。残るbinding確保やBrowserコピーを含めzero allocation／zero copyは保証しません。c1bdf69以前の固定checkpoint測定はこの全table・任意追跡方式の性能結果ではありません。
