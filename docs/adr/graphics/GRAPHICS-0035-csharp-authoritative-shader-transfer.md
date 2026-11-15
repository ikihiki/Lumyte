# ADR-GRAPHICS-0035: シェーダー引数とC#の配置を正とする一括転送

- 状態: 採用
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`
- 変更関係: 一部変更
- 変更元: 下記「過去の判断との関係」に対象節と継続範囲を示す。
- 変更対象: C#転送配置、単一の値writer、ルート定数の即時記録、全登録のbinding、独立した任意検証層
- 継続する判断: 不透明な型付き参照、明示的なtable選択、利用者の資源所有、明示転送・送信・完了待機

## 背景

シェーダー引数を渡す利用者は、C#で定義した値の配置と、反射情報に従って再構築される転送配置を二重に管理していた。数値の再構築をなくし、型付きGPU参照と任意診断を分離して、明示的な転送時点と所有権を保つ。

## 決定

C#のunmanaged領域を転送ABIの唯一の基準とし、数値はパディング込みでコピーする。参照を含む値はstruct, IShaderArgumentsへ統一し、backendが二つの値writerで直接符号化する。寿命・同期の追加診断は借用deviceを包む独立したValidationDeviceだけが所有する。

### 利用者の変更前・変更後

変更前は、利用側で参照を含む引数構造体を宣言し、数値メンバーを個別に書き出していた。変更後は、数値だけの通常のunmanaged構造体を、インターフェース、partial宣言、ジェネレーター、登録なしでそのまま転送する。参照を含む引数は、非公開のunmanaged格納領域と通常の公開プロパティを持つmutable partial structにする。値の取得、明示的なコピー、送信、完了待機、解放の境界は変更しない。

数値だけのバッファ要素は、変更前にはpartial構造体へのIShaderArguments実装を必要とした。変更後は通常の構造体でよく、同じ準備と書き込みは次のように変わる。後続のバリア、コピー、送信、完了待機と解放は両例とも省略している。

```csharp
// 変更前（基準コミット）
public readonly partial record struct Vertex(Vector3 Position) : IShaderArguments;
using IGraphicsShaderDataBuffer<Vertex> upload =
    device.CreateShaderDataBuffer<Vertex>(artifact, 2, MemoryPreference.Upload);
await upload.MapAsync();
upload.CopyFrom(vertices);
upload.Unmap();
```

```csharp
// 変更後
public readonly record struct Vertex(Vector3 Position);
using IGraphicsBuffer<Vertex> upload =
    device.CreateShaderDataBuffer<Vertex>(artifact, 2, MemoryPreference.Upload);
await upload.MapAsync();
upload.CopyFrom(vertices);
upload.Unmap();
```

数値だけのルート引数も通常の構造体でよい。record struct CameraArguments(Matrix4x4 ViewProjection)を宣言し、選択済みのencoderへSetArguments(in cameraArguments)で渡す。必要ならrootParameterとshaderTypeNameを明示する。System.NumericsのVector3やMatrix4x4など外部のunmanaged型も、生のバッファAPIへ直接渡せる。

変更前の呼び出し例（基準コミットのIShaderArgumentsに従う）。初期化済みのpipeline、table、materialRefとcameraを使用し、資源の生成・解放は省略する。

```csharp
public readonly partial record struct DrawArguments(
    Matrix4x4 ViewProjection, IGpuRef<MaterialData> Material) : IShaderArguments;

encoder.SetPipeline(pipeline);
encoder.SetArgumentTable(table);
encoder.SetArguments(new DrawArguments(camera.ViewProjection, materialRef));
encoder.Draw(vertexCount);
```

変更後は参照を含む型を次のように宣言する。同じ描画に対するSetPipeline、SetArgumentTable、SetArguments、Drawの順序は維持する。

```csharp
public partial record struct DrawArguments : IShaderArguments
{
    private TransferData _data;
    public partial Matrix4x4 ViewProjection { readonly get; set; }
    public IGpuRef<MaterialData> Material { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Matrix4x4 ViewProjection;
        public ShaderReferenceSlot Material;
    }
}

encoder.SetPipeline(pipeline);
encoder.SetArgumentTable(table);
encoder.SetArguments(new DrawArguments
{
    ViewProjection = camera.ViewProjection,
    Material = materialRef,
});
encoder.Draw(vertexCount);
```

両例ではSystem.NumericsとLumyte.Graphics.Abstractionsを使用し、変更後の例ではSystem.Runtime.InteropServicesも使用する。

書き込み可能な通常バッファの登録も、同じ初期化済みtableとstorageを使う場合は次のように変更する。storageはShaderWrite能力を持ち、呼び出しは内容の転送やGPU実行を行わない。

```csharp
// 変更前（基準コミット）: 使用箇所のshader宣言に従ってアクセスを分類する。
IGpuRef<uint> output = table.WriteBuffer(0, storage.SliceElements(0, 4));
```

```csharp
// 変更後: 登録時に選択する。公開される参照型は変わらない。
IGpuRef<uint> output = table.WriteBuffer(0, storage.SliceElements(0, 4), ShaderBufferAccess.ReadWrite);
```

### 過去の判断との関係

基準コミットに存在するGraphics ADR全体を、転送と診断の責務から読み分ける。本ADRは以下の範囲だけを一部変更し、取り込み済みの判断本文は書き換えない。

| 変更元と対象節 | 本ADRで変える契約 | 継続する判断 |
| --- | --- | --- |
| [GRAPHICS-0005](GRAPHICS-0005-argument-tables-and-gpu-references.md#登録api)の登録API・不透明参照とbackendの追跡 | 登録時のReadOnly／ReadWrite選択、全登録binding、検証層だけのbuffer全体依存 | 論理slot、非所有の型付き参照、GetElementの正確な範囲 |
| [GRAPHICS-0006](GRAPHICS-0006-shader-compilation-and-modules.md#gpu参照とreflection)のGPU参照とreflection | 反射配置への数値再構築をC#配置へ変更 | target別codeとreflectionの一体性、不透明artifact、row-major方針 |
| [GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md#生成codecとbackendの接続)の生成codec、型対応、Draw／dispatchでの解決、Snapshot・寿命、MaterialData、Copyと参照追跡、推移的な参照 | 値writer、IShaderArgumentsへの統合、Set即時定数、全table binding、非原子的CopyFrom、任意のbuffer単位追跡 | Set時点の値固定、program変更時の再設定、明示copy、不透明な参照ABI |
| [GRAPHICS-0011](GRAPHICS-0011-indexed-and-indirect-commands.md#決定)の間接実行時の参照集合 | root到達集合だけでなく選択tableの全登録をbind | GPU内の動的参照をCPUが正確に予測しないこと |
| [GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md#取り除く処理)の取り除く処理・維持する処理 | 任意validation APIを追加しない判断を独立decoratorへ変更。rawでは機能上不要な依存追跡も除去 | rawの利用者責任、host範囲・overflow、native所有と失敗処理 |
| [GRAPHICS-0013](GRAPHICS-0013-surface-swapchain-presentation.md#所有権と同期)の所有権と同期 | wrapper利用時だけframe／semaphore履歴の診断を追加 | Acquire／Submit／Presentの明示同期、借用画像、提示完了とGPU完了の区別 |
| [GRAPHICS-0015](GRAPHICS-0015-render-graph.md#公開api)のshader-data型制約・使用例 | IShaderArgumentsへ統合し、任意検証時だけ推移的依存を宣言と照合 | 通常のUse宣言によるbarrier・順序・culling、allocation単位の保守性 |
| [GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md#api利用例)・[GRAPHICS-0022](GRAPHICS-0022-2d-render-graph-layers.md#api利用例)・[RESOURCES-0008](../resources/RESOURCES-0008-graphics-generations.md)のshader-data型制約 | IShaderDataを削除しstruct, IShaderArgumentsへ統合 | 描画準備、所有者、GPU完了後の解放、世代管理 |
| [GRAPHICS-0023](GRAPHICS-0023-explicit-api-convenience.md#argument-table)の型制約・table所属検証 | rawの間接依存走査を除去し、検証層だけで追加診断 | 単位を明示したAPI、明示table優先、別tableを混在させない事前条件 |
| [GRAPHICS-0032](GRAPHICS-0032-drawing2d-shared-argument-tables.md#引数選択と専用-encoder)の引数選択・API差分 | snapshot書き込みを削除、codec一回の直接Set、任意のallocation／image単位read／write照合、保存参照の取得元制約を除去 | pass全体の容量単位、painter order、表の共有、exact view／range登録、wireの自動rebaseなし |

GRAPHICS-0001〜0004、0007〜0008、0010、0012、0016〜0017、0019〜0021、0024〜0025、0027〜0028、0030〜0031、0033の固有判断は変更しない。共通する寿命診断の差はGRAPHICS-0014を経由して上記の任意検証境界へ適用する。行列・色の数学的意味、描画順、保持描画、クリップ、計測方針をraw転送へ混入しない。

### 型とC#転送配置

C#のunmanaged構造体の定義を転送ABIの正とする。値の取得時には、実際のパディングを含むメモリ表現全体を一括コピーする。数値メンバーの個別シリアライズ、並べ替え、行列の転置、型変換、リフレクションのオフセットに合わせた再配置は行わない。入れ子の構造体とMatrix4x4も元のバイト列を維持する。シェーダー側はC#のフィールド順、オフセット、パッキング、要素間隔を一致させる。どちらかの変更もABIの変更となる。

通常の数値データにはIShaderArguments不要である。`CreateShaderDataBufferのunmanagedオーバーロードは既存のIGraphicsBuffer<T>`を返し、`BufferSlice<T>`、`BufferBarrierDesc<T>`、CopyBuffer、WriteBufferとRenderGraphの通常のバッファ経路を使う。RenderGraph.CreateShaderDataBufferにもunmanagedオーバーロードを用意し、`RenderGraphBuffer<T>`を返す。`Lumyte.Resources.Graphicsでは既存のGraphicsBufferDefinition<T>`と生バッファのインポートをそのまま利用する。参照依存メタデータがないため、要素ごとの取得・符号化は行わず`ReadOnlySpan<T>`をそのままコピーする。既存のインターフェースを明示実装する数値型も同じ必須の値だけを渡すwriter経路で使用できる。IGpuRefを含む型はmutable partial structにする。数値データは1つの非公開unmanaged構造体に保持し、利用側には通常の読み書き可能なプロパティを提供する。.NET 10のpartialプロパティはジェネレーターで実装できる。利用者が定義する格納用構造体には、参照プロパティと同名の16バイトのShaderReferenceSlotフィールドも配置する。ジェネレーターは非公開の格納領域をローカルへ一括コピーし、参照の識別情報をC#側のスロット位置で同期符号化する。管理オブジェクト自体のメモリは転送しない。

通常の構造体は`Unsafe.SizeOf<T>()`で、生成対象のstructは静的なTransferSizeで、バッファの要素間隔と割り当てサイズを決める。バッファ生成時に値のインスタンスを生成したり、default(T)から値を取得したりしない。生成型の静的なTransferFieldsは、任意の診断に使う実際の管理メモリ上のオフセットを表す。通常の構造体のfield診断は、保存済みmetadataの照会と明示的な反射要求を分ける。通常のunmanaged CaptureはDiagnosticTypeを保持する。明示診断の遅延取得とAOT上の制約は「明示的なCPU検査と配置診断」に従う。シェーダーのスキーマから求めた要素間隔でC#の要素間隔を上書きしない。

シェーダー書き込みはIShaderValueWriterのWriteValueとWriteReferenceへ統一する。数値だけの値は、パディングを含む全領域を一回のWriteValueで渡す。数値メンバー別のwriter、WriteWire、取得済みsnapshotを詰め直すPackは提供しない。参照のある場合の処理順は「連続する値writerと生成コード」に従う。参照の種類は参照先リソースとArgumentTableの登録から解決し、シェーダーの名前やパスから実行時のアクセス種別を決めない。

### 値型と利用者による要素範囲の選択

参照を含む引数はstructだけを転送する。native/root、shader-data buffer/slice、factory、Graph、resource宣言のIShaderArguments経路をすべて`where T : struct, IShaderArguments`とする。管理参照を含むためunmanaged制約にはしない。interfaceなしの通常unmanaged経路は変更しない。生成対象のclass、record class、ref structは手書きWriteがあってもLUMG001で拒否する。C#自体はclassのinterface実装を許すが、generatorなしの手書きclassもconsumer genericではCS0453となることを受け入れ条件とする。

生成readonly Writeはprivate unmanaged wireだけをgetter前にコピーし、型付きIGpuRefを順に符号化する。管理参照を含む外側structをraw bytesとして転送しない。数値partial propertyは`{ readonly get; set; }`で可変値を提供できる。生成実装は宣言のreadonly修飾を正確に保持し、通常のget宣言へreadonly getを合成しない。これはC#のpartial propertyの互換性を保つためである。in/ref readonlyからのgeneric interface呼び出しやreadonlyでないgetterには外側structの防御的コピーがあり得る。外側コピーゼロや全体allocation-freeは保証しない。

利用者は要素範囲を選択して転送する。次の例は初期化済みのDrawArguments配列values、mapped upload、gpu、互換のmaterialRefとcommandsを使用する。必要なbarrier、map/unmap、Submit、完了待機と解放は省略している。

```csharp
ref DrawArguments changed = ref values[1];
changed.ViewProjection = camera.ViewProjection;
changed.Material = materialRef;
upload.CopyFrom(values.AsSpan(1, 1), elementOffset: 1);
commands.CopyBuffer(upload.SliceElements(1, 1), gpu.SliceElements(1, 1));
```

一つのpropertyだけを変更しても、選択した要素のwire全体を転送する。field範囲転送、dirty tracking、自動coalescingや隠れた値cacheは追加しない。`var changed = values[1]`は値のコピーなので、配列を変更するには上例のrefまたは変更後の書き戻しが必要である。CopyFrom後の編集は次の明示転送まで反映しない。利用者はupload元をGPUが消費するまで変更・解放せず、同期を管理する。

structの`new()`はparameterless constructorを呼ぶが、default(T)と配列初期要素は呼ばない。DrawingInstructionの`new()`はresource indexの-1と参照のdefault!初期化を保つが、defaultや配列初期値のindexは0となり、参照は利用者が設定するまでnullである。default!はnullable解析を抑制するだけで有効なGPU参照を生成しない。default rootのnull IGpuRefは符号化時に拒否されるため、転送前に必要な参照を設定する。ShaderRootArgumentsは転送providerではなくmetadataのclassとして維持する。

### 連続する値writerと生成コード

IShaderValueWriterは`WriteValue(ReadOnlySpan<byte> value)`と`WriteReference<T>(IGpuRef<T>? value)`の二つだけを必須の抽象メソッドとして持つ。各呼び出しの引数は値一つだけで、offset、size、path、seek、skip、WriteAtは公開しない。IShaderWireSink、IShaderReferenceCursor、IShaderReferenceEmitter、ShaderWire、ShaderWireCursorとWriteWireは削除し、互換aliasを残さない。

`Write<TWriter>`の制約はIShaderValueWriter, allows ref structとし、`Span<byte>`を持つstack限定writerをboxingせず渡す。生成型と手書き型も同じ制約を使い、C# 13以降と固定.NET 10 SDKを要求する。

生成codecは非公開unmanaged wire全体をパディング込みでローカルへコピーし、raw prefixをWriteValue、参照をWriteReference、次のraw区間、最後のraw tailという物理順で渡す。数値だけの型は全領域を一回のWriteValueで渡す。数値のfield別シリアライズや全wireの転送後の参照差し替え、Completeでの参照再走査は行わない。source spanとin参照は同期呼び出し外へ保持せず、destinationとaliasさせない。

生成型のprivate static planは実際のC#オフセットによる参照順序とスロット妥当性のためだけに使う。任意診断のreflectionや数値field tableとは独立し、範囲外、負値、重複、交差をgetterの呼び出し前に拒否する。field、auto-propertyとcustom getterも同じ経路を使い、wireローカルコピーの後に各参照getterを物理順で一回だけ評価する。生成Writeはreadonlyであり、wireコピー後の数値変更は今回のraw bytesへ反映しない。readonlyでない利用者getterは外側structの防御的コピー上で実行され得るため、副作用が後続getterや元の値へ伝播するとは保証しない。emitter structは生成しない。

検証層の`ValidationArguments<T>`は静的RootParameter、ShaderTypeName、TransferSize、ReferenceCount、TransferFieldsを元のTへforwardする。ValidationWriterはnative writerを包み、利用者のcodecとgetterを一回だけ実行し、論理`IGpuRef<T>`を必要なnative element adapterへunwrapしてstreaming転送する。診断用adapter配列のコピーは許容するが、Capture／Snapshotの数値中間入力を作らない。Graph prepareはadapter内でnative発行より前に実行し、失敗時は旧rootを保持する。raw helperによるcodec呼び出しはこのadapterを通じるだけであり、二度目の利用者codec実行ではない。

VulkanShaderValueWriter、Direct3D12ShaderValueWriter、WgpuShaderValueWriter、BrowserShaderValueWriterを、それぞれのbackend内部でIShaderValueWriterとして直接実装する。各backendのShaderTransfer helperがSetと型付きCopyFromの両方でvalue.Write(ref writer)を呼ぶ。各writerが暗黙の現在位置とexpectedTransferSizeを所有し、raw区間をVulkan／D3D12の`Span<byte>`、Wgpu／Browserの非公開具象RootStorage.Writeまたは検査済みupload batch viewへ直接渡す。Sharedのwriterから参照encoder callbackを呼ぶ構造にはしない。WriteValueは渡された長さ、WriteReferenceは必ず16バイトだけ位置を進め、参照はその場でbackendの表現へ符号化する。残容量の検査はlength > expectedTransferSize - positionという減算形式でoverflowを避ける。Completeは一回だけ成功でき、position == expectedTransferSizeを必須とする。短い出力も長い出力も公開しない。

IShaderReferenceMetadataはReferencesという依存identityの列挙だけを公開する。検証層上のGraph・依存収集・検証層の非公開copy overlayはwire layoutを知らずにこの列挙を使う。検証層のprivate immutable metadata ownerはShaderValueの共有list、数値bytes、wire offsetや元の値objectを保持しない。登録slot／range／accessはbindingに必要なIShaderReferenceのidentityとして残り、C# wire内の位置とは区別する。

手書きcodecにも現在位置だけの同じwriterを使い、参照の物理位置は前後のraw区間の長さで表現する。生成planの検査とwriterの容量・完了検査は別の責務である。Vulkan／D3D12／WgpuのWriteReferenceは一回のwriter容量検査後に取得した16-byte destination Spanへlittle-endianの各fieldと予約ゼロを直接書き、stackalloc byte[16]、WriteValue経由の再検査・コピーを行わない。Browserだけは同期JS bridgeが一時bytesを読むため一個の固定16-byte stack領域へ全fieldを書き、WriteValueを経由せずrootまたはupload bridgeへ直接渡す。Browserでもwriter容量検査を重複させず、spanを保持しない。native参照ごとのShaderValue、new byte[16]やFuncによる符号化も作らない。検証層の任意診断にはValidateReference(Type valueType, object? reference, int offset)へ型・参照・現在位置を直接渡す。明示CaptureのShaderSnapshotWriterが論理記録を作る経路とは分離する。検証層の追跡・prepare時の参照配列とimmutable metadata owner、Wgpu／Browserのpayload allocation、delegateやnative buffer handle、実行ごとのbinding／descriptor heap確保は残り、zero allocationやzero GPU copyとは主張しない。Browserのinterop span-copyも残る。

### 宣言参照数と実バイト安全性の境界

IShaderArguments.ReferenceCountは重複resourceも数える物理16-byte slot数であり、必須の静的宣言とする。検証層はcodecの前に0以上かつTransferSize / 16以下であることを確認し、不正値をArgumentOutOfRangeExceptionで拒否する。余分なWriteReferenceはnative writerへ渡す前、不足はcodec終了後かつnative root発行前にInvalidOperationExceptionとする。宣言getterが投げた例外もそのまま伝える。

四raw backendのSetと型付きCopyFromはReferenceCount getterを読まず、宣言数・実呼び出し数の状態を持たない。宣言とcodecの一致は利用者の事前条件であり、宣言値が誤っていても実際のwireが合法ならraw転送は成功し得る。宣言数を機能上の配列indexとして使わない。

各参照が符号化する論理要素数Countとは別の契約である。Countのchecked uint変換、address／offsetのoverflow、実バイト容量、完全なサイズ一致、完了後の再書き込み拒否はDebug／Release・raw／wrapperの全構成で維持する。型・device・tableの機能上の受け入れ判定は書き込み前に行い、成功後だけ位置と暗黙table候補を更新する。

### 不透明参照の16バイトABI

全バックエンドで16バイトの参照スロットを共有するが、既存の符号化方法は維持する。

- Vulkan: バッファの物理デバイスアドレス。
- WgpuとBrowser: 論理登録スロットと、登録範囲を基準としたバイトオフセット。
- Direct3D12: ディスクリプターの識別情報と、バッファ内の絶対バイトオフセット。

参照は不透明で、所有権を持たず、型付きのままとする。参照や引数値を保持してもGPU登録の所有権は取得しない。参照を含むシェーダーデータバッファには、生の別名参照やシェーダーからの書き込み経路を追加しない。通常のunmanagedバッファでは既存の生バッファの用途・同期契約を維持する。

WgpuとBrowserの読み取り専用・書き込み可能poolとremapの各laneは登録のアクセス種別から選択する。同じバッファ範囲を両poolへ登録した場合も、各スロットの選択したlaneだけを設定する。nativeの同時使用・エイリアス制約は別途適用される。Vulkanの物理アドレス、Direct3D12の絶対オフセットの符号化は変更しない。Direct3D12のSRVまたはUAVも登録の選択に従う。シェーダーの参照種別・対象型との一致は検証層だけが診断し、Debug／Releaseともraw転送やbinding生成はシェーダー文字列やフィールドパスで分類しない。

16-byte ABIは従来の予約ゼロを含めて維持する。Vulkanはbufferの物理addressに絶対byte offsetを加えた64-bit値、またはtexture／samplerの32-bit slotを置き、byte 8にchecked uintの論理要素数Countを置く。このCountは宣言ReferenceCountとは異なり、uint変換のoverflowとaddress／offset演算は全modeで必ず検査する。内部の型付きIVulkanShaderBuffer.ShaderAddressから保存済みaddressを読み、object型ShaderHandleのboxingを避ける。D3D12はslot、checked uintの絶対byte offset、checked uintのcount、予約ゼロとする。Wgpu／Browserはslot、checked uintの登録相対byte offset、checked uintのcount、予約ゼロとし、相対offsetやcountの切り詰めを許さない。

ReadOnlyにはShaderRead、ReadWriteにはShaderWrite能力を登録時に要求する。参照付きShaderDataSliceは読み取り専用である。置換・Release後の参照を使用しない責任は全構成にあり、失効の追跡診断は検証層だけが行う。

### 明示転送とホストメモリの借用

buffer・slice作成、table登録、Graphのimport／Use／CompileはCPU値を固定しない。CopyFromが現在のC# wireを同期転送し、CopyBufferは元のCPU値でなく指定したstagingを読む。元の値の後続編集は次のCopyFromまで反映されない。

native CopyFromは要求batch全体のmapped spanを入口で一度だけ範囲・mapping方向・pointer／サイズoverflow検査し、各要素sliceへ直接書く。Browserは一つの検査済みUint8Array viewへ同期JS.setし、finallyでviewだけを解放する。Vulkan／D3D12はbuffer作成時に保存したGPU base addressへchecked offsetを加え、参照ごとのdriver照会やglobal cacheを作らない。

検証層だけがHostWriteLeaseで重なる書き込みとcallback中のMap／Unmap／Disposeを診断する。leaseのfinally cleanupは冪等かつ例外を投げない。rawでは借用中のmapping・寿命・並行操作を利用者が保証し、追加lock・pin・GPU待機を設けない。範囲検査があることを、unsafeな寿命違反も必ずmanaged例外にできる保証と混同しない。

CopyFrom失敗は部分byte更新を許す。検証層はbuffer全体の依存を無効化し、全体再転送だけで回復する。旧byte復元、数値履歴、隠れたreadbackは提供せず、cleanup例外で元の例外を隠さない。空sourceは有効offsetでno-opとする。map／unmap、barrier、copy、Submit、完了待機と再利用は利用者が明示し、GPUが消費するstagingを変更・解放しない。

### シェーダーの論理受け口と物理転送

`ConstantBuffer<T>`は論理的な引数の受け口であり、共通のGPU uniform所有型を意味しない。C#のフィールド順・offset・paddingとrow-majorの行列バイト列をwireの正とし、target emitterがopaqueなnative表現へ接続する。色変換と行列転置は行わず、乗算順序は利用者のshader式で決める。

ルート引数の論理wireは、バックエンド固有の構造体パッキングではなく、移植可能なuint4単位のバイト列として扱う。シェーダーは呼び出しごとに一度、そのバイト列を自然なスカラー配置で再解釈して論理的なルート値を初期化する。コンパイラーの通常の識別子スコープを維持し、ルート名のトークン置換は行わない。リフレクションには論理スキーマを保持する。uint4宣言は16バイト単位へ切り上げるが、Loadはceil(sizeof(T)/4)個の4-byte wordだけを読み、native constantsの容量は4-byte単位とする。backend-private uniform emulationの容量整列はbackendに任せ、TransferSize、数値のオフセット、取得済みのバイト列、シェーダーデータの要素間隔は変更しない。

### ルート定数の即時記録と容量

通常のunmanaged overloadとIShaderArguments overloadは、Set時点の値を同期的に固定する。Vulkan／D3D12はceil(wireSize/4) wordの有界stack spanをゼロクリアし、backendのref struct writerが直接書く。数値のCPU heap配列やArrayPool rental、pack・padding用の中間配列は作成・保持しない。末尾1／3 byteを含むpaddingはゼロとする。一時stackは最終payload ownerではなく、native command記録後の実際のコピーはdriver command bufferが所有する。明示CaptureとShaderValueSnapshotは独立した読み取り専用CPU検査のまま維持し、Set／upload入力へ戻さない。

Setではwriter.Complete、任意の配置診断、参照準備hook、不変metadata生成をすべて完了してから、VulkanPushConstants.EmitによるvkCmdPushConstants、またはD3D12のcommands_root_constantsによるSetGraphicsRoot32BitConstants／SetComputeRoot32BitConstantsを呼び、Setが戻る前に記録する。新しい選択はnative呼び出し後に公開する。host検査、codec、診断や準備失敗ではnative定数を発行せず、前回成功した選択を保つ。ただし既に発行したvoid driver commandはrollbackできず、device failureを含むnative失敗からの原子的復元は保証しない。

Vulkan／D3D12のrender／compute全四encoderはcodecとprepare中を_settingArgumentsで保護し、入れ子のSet、pipeline変更、End、Draw／Dispatchなどの再入を拒否する。Graph準備のtable自動選択に必要なSetArgumentTableだけは許可する。callback後は、捕捉したpipelineのValidateAlive、owner.ValidatePassとpipeline identity一致をすべてnative発行より前に再検査する。raw command bufferやpipelineのDisposeがguardを迂回しても、解放済みhandleを使うnative発行を阻止する。host失敗では旧root選択を保持するが、呼び出し側が明示したresource Dispose、table自動選択や発行済みdriver commandは取り消さない。

Draw／Dispatchはnumeric rootを再送しない。選択table全体からbindingを構築し、推移的依存とGraph宣言の照合は検証ONだけで行う。Wgpu／Browserの非公開uniform ownerと機能上必要なbinding／remapは維持する。

VulkanDevice.MaxPushConstantsSizeはphysical deviceから取得するpublic容量queryである。容量はdevice依存であり、256 byteのdeviceを全ハードウェアへ一般化しない。push rangeはbind pointごとにoffset 0、size MaxPushConstantsSizeへ固定し、computeの全pipeline variantはCompute、graphics／partialの全variantはVertex|Fragmentを宣言する。Setのstage maskは対応rangeと完全一致し、compute／graphicsのnative push状態を分離する。全三stageの重複rangeに対して一部stageだけを更新する設計にはしない。descriptor visibilityはこれと独立にVertex|Fragment|Computeを許容する。この固定契約はpush rangeに限り、descriptor数などが異なるpipeline layout全体のsingleton化を意味しない。実際のpush量は論理wireの4-byte切り上げだけで、uint4宣言のpaddingとは区別する。Setには物理容量に加えて64 KiBのbackend host stack guardを適用し、異常に大きい物理上限でも無制限stack確保やheap fallbackを行わない。公開する物理上限やlayoutのrangeは変更しない。

D3D12はdevice初期化時、Setより前にthread-safe cacheで一つの固定root signatureを生成し、全graphics／compute／partial pipelineで共有する。Setごとのroot signature生成は行わない。Commandsはgraphics／computeのroot pointer状態を別々に追跡し、同じpointerならbindを省く。通常のPSO／SetProgramも同じsignatureを使うためDraw時のSetPipelineRootSignature相当の再設定で定数をresetしない。このsignatureは64 DWORD予算を使う。resource tableとsampler tableは各1、parameter 3のdraw infoは2、parameter 4のvertex root SRVは2で、固定費は6。app rootはparameter 0、register b8 space0の58 DWORD（232 byte）とし、native static_assertで合計64を確認する。Direct3D12Device.RootConstantDwordCapacity／RootConstantByteCapacityで容量を公開する。shaderの論理constant buffer宣言は維持するが、native parameterはCBVではなく32BIT_CONSTANTSである。232-byte wireのuint4宣言は240 byteでも、Loadは58 DWORDだけを読み60 DWORDを要求しない。

artifactの容量事前検査は任意validationの追跡ではなく、常時必須のABI安全性検査とする。VulkanShaderは所有deviceのMaxPushConstantsSizeを用い、native shader module生成前に拒否する。D3D12はgraphics、computeとpartialの全pipeline経路でnative pipeline生成前に拒否する。どちらも全app ConstantBufferを実際のroot名でShaderDataLayout.Root(target, name).Sizeから取得した論理sizeで検査し、lumyteDrawInfoと__lumyte_schema_の項目を除外する。uint4宣言のpadding容量は判定に使わない。Set時の容量超過もpayload allocationと新選択の公開前に拒否し、前回成功した選択を維持する。shader metadataのlumyteRootStorageはtarget別にVkPushConstants／D3DRoot32BitConstants／UniformBufferを記録し、旧Vulkan uniform artifactは再compileを要求する。shader emitterは本質的にtarget-awareだが、共通の状態・object ownership・metadata field・Graph lifetimeはuniform非依存とする。共通コードのWgpu／Browser判定によるfeature dispatchやInternalsVisibleToは加えない。検証用decoratorは独立assemblyとする。

SetPipelineはtableや値を暗黙に書き換えず、rootを持つ別programにはSetArgumentsを再度必要とする。新passへ選択を継承しない。別のSetが成功する前に元pipelineへ戻った場合だけ最後の選択を再利用でき、pipeline別cacheは作らない。rootなしのshaderはSet不要で、数値だけのrootはtableを要求しない。rawの単一の暗黙table候補は自動選択の補助であり、明示SelectedTableが優先する。異なるtableの参照混在はnative Set前に拒否する。

### 共通の参照情報とbackend固有の所有

SharedのShaderTransferWriter、IShaderTransferDestination、ShaderRootDestination、ShaderUploadDestination、ShaderDataUpload、ShaderReferenceEncodingとVulkan／D3D12の旧参照encoderは削除する。ShaderRootArgumentsは数値payloadを所有しない不変metadataとし、公開境界は次の非generic APIとする。IShaderRootStorageは削除し、公開の代替storage型やobject bagを設けない。

```diff
 namespace Lumyte.Graphics.Shared
 {
+    public sealed class ShaderRootArguments
+    {
+        public string RootParameter { get; }
+        public string ShaderTypeName { get; }
+        public int WireSize { get; }
+        // nullは未収集。既知の空集合とは異なる。
+        public IShaderReferenceMetadata? References { get; }
+        // 名前・非負sizeを検査する。prepare指定時はmetadata必須。
+        public static ShaderRootArguments Create(string rootParameter,
+            string shaderTypeName, int wireSize,
+            IShaderReferenceMetadata? references,
+            Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null);
+        // detached callbackへ実行時の完全な参照集合を渡す。
+        public void Admit(IReadOnlyCollection<IShaderReference> references);
+    }
 }
```

Storage、nullable storage、Allocate、Seal、Dispose、IDisposableと共通のsealed／disposed状態は持たない。Createは書き込み、generic codecやencode callbackを受け付けず、参照metadataとdetached admission callbackだけを保持する。SharedのShaderBindingDataもRootSizeと共通Sealを持たない。末尾1／3 byteを含むVulkan／D3D12の4-byte切り上げ、Wgpu／Browserの最小16 byte・16-byte整列とnative uniform binding容量検査はbackendが所有する。

Wgpu／Browserの既存の非公開具象RootStorageだけがIDisposableとしてuniformと一度だけ設定するArgumentsを所有します。writerは具象RootStorageへ直接書き込み、bindingも具象ownerを直接受け取ってSealします。commandはKeepBindingでGPU利用終了まで具象ownerを保持します。Vulkan／D3D12のmetadataは解放不要でSet時のKeepBinding(root)もありません。raw bindingは登録とtable identityを保持し、検証層だけがShaderBindingDependenciesの不変revisionも保持します。未使用metadataは再選択後に保持不要です。

backend helperは書き込み、診断、準備またはCreate失敗時に未公開の具象ownerを解放し、cleanup例外で元の例外を隠さない。commandへの所有権移転失敗はSelectRootが処理する。数値root用の追加storage wrapper、global cacheや公開storage型は設けない。検証用decoratorのcanonical mapはdomain内だけで保持する。Sharedはbackend featureで分岐しない。

### 独立した任意検証層

DebugとReleaseは同じ契約とし、任意診断を独立したLumyte.Graphics.Validation assemblyへ分離する。ValidationDevice(IGraphicDevice backend)はbackendとqueueを借用するIGraphicDevice decoratorである。native backendはValidationを参照せず、診断engine、nullable tracker、EnableValidation APIを持たない。rawのValidationEnabledはfalse、wrapperはtrueとする。D3D12のEnableDebugLayerは独立したnative設定である。

従来はmanaged診断を付けず利用者が寿命と同期を保証するしかなかった。変更後もrawの利用方法は維持し、診断が必要な場合だけ次のようにwrapperを選ぶ。usingの逆順に子資源、wrapper、rawを解放する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;

using var raw = VulkanDevice.Create();
using var validated = new ValidationDevice(raw);
IGraphicDevice device = validated;
using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint>
{
    Count = 16,
    Usage = BufferUsage.CopySource,
    Memory = MemoryPreference.Upload,
});
```

比較元の基準コミットにはこのdecoratorがない。各backendへの有効化API追加ではなく、次の独立した公開境界を追加する。互換aliasは設けない。

```diff
+namespace Lumyte.Graphics.Validation
+{
+    public sealed class ValidationDevice : IGraphicDevice, IDisposable
+    {
+        // backendとqueueを借用し、子資源を同じ検証domainから生成する。
+        public ValidationDevice(IGraphicDevice backend);
+        public bool ValidationEnabled => true;
+        // 新規・同一backendのsurfaceだけを借用。過去の取得状態は取り込まない。
+        public IGraphicsSurface WrapSurface(IGraphicsSurface raw);
+        // 子資源の解放が先。backend自体はDisposeしない。
+        public void Dispose();
+    }
+}
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // raw backendはfalse。任意検証層はtrue。
+        bool ValidationEnabled => false;
     }
 }
```

ValidationはAbstractionsとSharedの純粋なhelperだけを参照し、状態を持つ検証engineとIValidatedShaderReferenceを新namespaceへ移す。Sharedの読み取り専用依存queryと収集helperは残すが、共通のmutation APIは追加しない。rawにはdriver API、host span bounds、overflow、mappingのmemory safety、ABI offset／符号化する論理要素数Count、実際のwire容量・完了状態、root／program状態、artifact容量、native失敗時の所有権に必要な検査を必須として残す。

すべての子資源をwrapped deviceから作る。raw／typed buffer、shader-data buffer、texture／view、sampler、argument table／GpuRef、shader／pipeline、command／render・compute encoder、queue／submission／semaphore、surface／swapchain／frameを包み、desc内のwrapperもbackendの具象cast前にunwrapする。domain内のraw objectは一つのcanonical wrapperに対応付ける。GetElementも論理参照を返し、native element adapterの具象型を漏らさない。raw子資源の後付け採用、別domain、rawとの混在は拒否する。raw参照やreflectionで外へ出る操作は診断を迂回するため利用者責任であり、security sandboxではない。

IGraphicDeviceにはsurface factoryがない。同じbackendの固有factoryで新規rawSurfaceを作り、usingで保持し、using var surface = validated.WrapSurface(rawSurface)で借用する。取得済みの履歴は取り込まず、surface wrapperをDisposeしてもrawSurfaceをnative Disposeしない。frame imageは借用で直接Disposeしない。viewを先に解放し、明示的にframe.WaitForReleaseAsyncを待ってframeをDisposeする。この待機はsubmission完了ではなく、GPU submissionは別途待機する。

ValidationDevice.Disposeは所有する子資源が先に解放されていることを要求し、backendをDisposeしない。DeviceLostのsenderはwrapperへ置き換え、Dispose時に購読解除する。利用者は外部で操作を直列化する。暗黙GPU待機、pin、lock、静的global dictionaryや解放済み資源の永久保持は設けない。

検出対象は登録identity、記録中／提出済みresourceの寿命、親子関係、mapping競合、byte範囲別bufferとmip／layer別textureのアクセスである。shader、index、indirect、copy、attachmentとbarrierのBefore／After scopeを照合し、登録の置換・Releaseを記録時とSubmit前に検査する。GPU内の動的indexは登録範囲として保守的に扱い、shaderの実アクセス、直接変更したstaging byte、外部native操作は観測できない。

記録とqueue／presentationはnative前に事前照合し、成功後だけ診断状態をcommitする。Submit事前照合はqueue状態の複製へ指定順に適用するが、metadata identityとsource epochは複製しない。不透明なnative Submit例外はwait／execute／signalの部分発行を否定できないため、元例外と使用した可能性のあるoperandを保持し、queueを未知状態として隔離する。健全queueの安全な回復APIは公開せず、明示的なbackend device-loss retirementだけが未知使用を退役させる。これはdevice所有権の取得や自動Disposeではなく、汎用の新通知APIも設けない。Windows D3D12のnative失敗時のfence owner保持は任意診断とは独立した正しさとして残す。

binary semaphoreはAcquire、Submit、Presentで同じsignal／wait履歴を使い、native完了境界まで使用状態を保持する。取得画像にはacquire完了または適切なstageのsemaphore待機が必要で、in-flight画像のPresentには生成元submissionのsemaphoreまたは観測済みGPU完了を要求する。キャンセルされた待機と早期Disposeは完了ではない。native Acquire成功後にframe wrapper構築だけが失敗した場合、raw frameのDispose成功でも発行済みsemaphore signal状態は残り、再利用前に明示的なwaitで消費する。rollback cleanupにも失敗した場合はraw leaseを保持・隔離する。不透明なacquire例外はswapchainを隔離し得るが、根拠のないsemaphore消費を作らない。

GPU copy metadataもcommand内の順序とSubmit時のsource identityを照合し、native copy記録とSubmitが成功した場合だけ公開する。数値履歴、隠れたreadbackや暗黙待機は持たない。WebGPUの非同期nativeエラーとdevice lossは結果として伝達し、既に実行したGPU操作をrollbackしたとは扱わない。

### 選択した登録表全体のbindingと宣言照合

bindingは選択したArgumentTableの書き込み済み登録すべてから構築します。capacityの未登録holeはentryではありません。Vulkanは全texture／sampler登録をdescriptorへ投影し、buffer参照は保存済みGPU addressへoffsetを加えてinline符号化します。Direct3D12は全texture／sampler／ReadOnly SRV／ReadWrite UAV登録を投影し、各上限128／16／64／64を適用します。Wgpu／Browserは全登録から有限のbinding poolとremapを構築し、WGSLの個数はcapacityではなく実際の書き込み済み登録を基にします。native binding数・storage範囲・remap容量へ収まらなければ明確に拒否し、巨大な無効binding、追加feature・package・pool・cacheで回避しません。rootなしのshaderも明示選択tableの全登録をbindし、Wgpuは未使用のroot uniform binding 8を省略します。

全登録をbinding可能にすることは、全登録をGPU使用済みとして記録することではありません。backend-privateな登録・remapは検証OFFでも必要な機能状態ですが、同期の所有権や使用hazardを追加しません。resource寿命とCPU／GPU同期は利用者が管理します。

Graphのbarrier・依存順・cullingは、必要なresourceの正確で完全なpass宣言だけを使い、従来どおりallocation全体を保守的に同期します。検証OFFでは宣言の真実性・完全性が利用者の事前条件です。root走査、依存query、準備hook、許可集合、admission callbackの確保を行いません。table共有だけでは使用宣言になりません。

検証ONの場合だけ、Graphはrootとbuffer全体の推移的依存を、buffer allocationまたはtextureの元imageを正規化したresource object identityと宣言read／write権限の合併で照合します。native handle値の衝突判定、要素・byte範囲・view・callback取得元による許可判定は行いません。同じbufferの別宣言範囲から保存した参照も許可されますが、未宣言の別resourceとread-only宣言に対するReadWrite参照は拒否します。

GetGpuRefの具体化はこの粗い許可判定とは別です。要求offset／countと一致する登録を選び、単一要素なら既存登録からGetElementで導出できます。未登録の複数要素範囲に、より広いCountの参照を返しません。新しいslice APIや自動dirty trackingは追加しません。

### バッファ全体の読み取り専用依存照会

共通境界はIShaderDataSource.TryGetDependenciesだけです。ReadReferences(index)、SetTransferredReferences、SharedのShaderDataTransferStateおよびShaderBindingDependencies.Elementsは削除し、共通の書き込みAPIは設けません。

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

raw backendのTryGetDependenciesは、数値だけのReferenceCount = 0を含めて常にfalse／nullを返します。これは追跡利用不可であり、既知の空集合ではありません。登録・remapなどの非公開機能状態は残ります。ValidationDeviceのbuffer wrapperはbuffer全体の不変な依存unionを返し、ReferenceCount = 0は既知の空集合としてtrueです。参照付きbufferの未初期化・失敗後の状態は無効で、queryはInvalidOperationExceptionとなり、buffer全体の明示再転送で回復します。部分転送だけでは回復しません。

既知状態への部分更新は旧依存とのunionを保持でき、buffer寿命内で保守的な偽陽性が残ります。全体書き直しは現在のunionを置き換えられますが、記録済みcommandが保持する旧revisionは不変です。要素ごとの配列や毎drawの行走査は不要で、検証ONのShaderBindingDependencies.Buffersが各bufferを一回だけ訪問します。

CPU CopyFromの依存更新は検証層が所有します。GPU copyも検証層のbuffer単位の非公開command overlayを使い、native copy記録成功後に更新し、queue Submit成功後にだけ公開します。失敗・未送信・破棄したcommandから公開せず、隠れたreadback・待機・数値履歴も持ちません。

### グラフの参照準備と実行時照合

既存のIRenderEncoderとIComputeEncoderに次のメソッドを追加する。同じ宣言を両interfaceへ適用する。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IRenderEncoder
     {
+        // nullは通常native経路。準備は新しいrootの公開前に行う。
+        void SetArgumentValidation(
+            Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare);
     }
     public interface IComputeEncoder
     {
+        void SetArgumentValidation(
+            Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare);
     }
 }
```

raw nativeのSetArgumentsは直接呼べるが、非nullのSetArgumentValidationは未対応として拒否する。検証ONのGraphだけがencoderごとに準備delegateを保存し、Set中だけ設定してfinallyとEnd前に解除する。準備はtable選択後に新rootを公開し、実行時はcommand-local overlayを含むbuffer全体の依存を照合する。prepared passごとのdetached Actionは不変の許可集合だけをcaptureし、owner・context・pass・入力を保持しない。検証OFFではこれらを作成・設定・実行しない。再入、pipeline変更、End、callback後の生存・pipeline一致の再検査は維持する。準備失敗は元例外と旧root選択を保持するが、既発行native命令や利用者によるDisposeのrollbackは保証しない。

### 明示的なCPU検査と配置診断

IShaderArguments.CaptureとShaderTransfer.Captureは呼び出し時点の独立したCPU検査値を作る。ShaderSnapshotWriterはサイズを受け、raw bytesを独立領域へ複製し、参照slotをゼロにして論理identityを記録する。ShaderValueはValueType、Reference、C# Offsetを持ち、Pathやnative addressを持たない。snapshotをSet／uploadへ戻す入口は設けない。

CaptureはDiagnosticTypeとキャッシュ済み静的generic DiagnosticFields callbackだけを保持し、入力struct、boxing、instance closureを保持しない。Capture／Copyではcallbackを呼ばず、最初の明示GetDiagnosticFieldsと`Fields<T>(in T)`で`ArgumentDiagnosticCache<T>`からT.TransferFieldsを静的dispatchする。生成metadataの取得にはDynamicMethod／MakeGenericMethodが不要で、Browser／NativeAOTでもこの経路を使える。

Fields(Type)と`Fields<T>()`は初期化済みの診断metadataを照会し、未知なら空を返す。Typeだけからgeneric methodを構築したり、反射を暗黙に始めたりしない。IShaderArgumentsの静的TransferFieldsが空なら、その型の診断も空であり、外側structの配置を推測しない。

通常のblittable値型に限る明示的なReflectFields(Type)を追加する。sequential／explicit配置で、再帰的に調べるfieldが保持されていることを要求し、managed wrapper、auto layout、非blittable primitiveをArgumentExceptionで拒否する。Marshal.OffsetOfを使える型だけを受け付け、marshalling配置と管理wire配置が異なる型へ推測値を返さない。この入口はRequiresUnreferencedCodeを明記し、呼び出し側がtrim後のfield保持を保証する。通常の転送・Capture・Type照会はこの反射入口を呼ばない。実行時コード生成を用いず、静的metadataのgeneric取得をAOTの通常経路とする。名前・サイズと直接転送はfield診断の有無に依存しない。

### 公開APIの差分

比較元は基準コミットのsrc/Graphics/Lumyte.Graphics.AbstractionsにあるIShaderArguments、IShaderValueWriter、IGraphicsShaderDataBufferとする。以下は変更する主要宣言の抜粋である。数値の格納型は引数structの非公開実装であり、利用側の公開シグネチャに追加しない。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IShaderValueWriter
     {
-        void WriteValue<T>(string path, in T value) where T : unmanaged;
-        void WriteReference<T>(string path, IGpuRef<T>? value);
+        // 現在位置へ同期コピーする。spanを保持しない。
+        void WriteValue(ReadOnlySpan<byte> value);
+        // 現在位置で常に16-byte参照を同期符号化する。
+        void WriteReference<T>(IGpuRef<T>? value);
     }
+    public interface IShaderReferenceMetadata
+    {
+        // 数値bytesとwire offsetを公開しない。
+        IReadOnlyList<IShaderReference> References { get; }
+    }
     public interface IShaderDataSource
     {
-        ShaderValueSnapshot Read(ulong index);
-        void SetTransferredValue(ulong index, ShaderValueSnapshot value);
+        // buffer全体の読み取り専用query。false/nullは追跡利用不可。
+        // 検証ONの不明状態はInvalidOperationException、既知空ならtrue。
+        bool TryGetDependencies(
+            [NotNullWhen(true)] out IShaderReferenceMetadata? dependencies);
     }
     public interface IArgumentTable
     {
-        IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range) where T : unmanaged;
+        IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range,
+            ShaderBufferAccess access = ShaderBufferAccess.ReadOnly) where T : unmanaged;
     }
+    public enum ShaderBufferAccess { ReadOnly, ReadWrite }
     public interface IShaderReference
     {
+        // 元の登録を参照し、GetElementでアクセス種別を変更しない。
+        ShaderBufferAccess BufferAccess { get; }
     }
     public interface IShaderDataLayout
     {
-        string ReferenceKind(string path);
-        byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, string, byte[]> reference);
+        // 配置の記述と比較だけを提供し、snapshotの書き込みは提供しない。
     }
-    public sealed record ShaderValue(string Path, Type ValueType,
-        ReadOnlyMemory<byte> Data, object? Reference, bool IsReference);
+    public sealed record ShaderValue(Type ValueType, object? Reference, int Offset);
-    public interface IShaderData : IShaderArguments { }
     public interface IShaderArguments
     {
+        static abstract int TransferSize { get; }
+        // 物理16-byte参照slot数。重複resourceも数え、数値だけなら0。
+        static abstract int ReferenceCount { get; }
+        static virtual IReadOnlyList<ShaderTransferField> TransferFields => [];
         void Write<TWriter>(ref TWriter writer)
-            where TWriter : IShaderValueWriter;
+            where TWriter : IShaderValueWriter, allows ref struct;
+        // CPU検査専用。Set／uploadへ戻す入口は提供しない。
         public static ShaderValueSnapshot Capture<T>(in T value)
             where T : struct, IShaderArguments;
     }
+    public static class ShaderTransfer
+    {
+        // 初期化済みmetadataを照会し、未知なら空。暗黙の反射はしない。
+        public static IReadOnlyList<ShaderTransferField> Fields(Type type);
+        // trim後も再帰的なfield保持を呼び出し側が保証する。
+        [RequiresUnreferencedCode("Preserve inspected fields or use static metadata.")]
+        public static IReadOnlyList<ShaderTransferField> ReflectFields(Type type);
+        // パディング込みのバイト列を即座に複製する。参照を含む型は受け付けない。
+        public static ShaderValueSnapshot Capture<T>(in T value,
+            string rootParameter = "arguments", string? shaderTypeName = null) where T : unmanaged;
+    }
     public interface IShaderRawBuffer
     {
+        // バッファ参照の型と用途の受け入れ判定に使用する。フィールド走査はしない。
+        Type ElementType { get; }
+        string ShaderTypeName { get; }
     }
     public interface IGraphicDevice
     {
+        // C#サイズと要素間隔を持つ生バッファ。UploadではCopySourceを使う。
+        IGraphicsBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count,
+            MemoryPreference memory = MemoryPreference.Automatic,
+            BufferUsage usage = BufferUsage.ShaderRead | BufferUsage.CopyDestination) where T : unmanaged;
     }
     public class ShaderResources
     {
+        public void Draw<T>(IRenderEncoder encoder, in T arguments, uint vertexCount,
+            uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0,
+            string rootParameter = "arguments", string? shaderTypeName = null) where T : unmanaged;
+        public void DrawIndexed<T>(IRenderEncoder encoder, in T arguments, uint indexCount,
+            uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0,
+            string rootParameter = "arguments", string? shaderTypeName = null) where T : unmanaged;
+        public void Dispatch<T>(IComputeEncoder encoder, in T arguments, uint x,
+            uint y = 1, uint z = 1, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
     public interface IRenderEncoder
     {
         void SetArguments<T>(in T arguments)
             where T : struct, IShaderArguments;
+        // 実装必須。Captureへ委譲するdefault実装は提供しない。
+        void SetArguments<T>(in T arguments, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
     public interface IComputeEncoder
     {
         void SetArguments<T>(in T arguments)
             where T : struct, IShaderArguments;
+        void SetArguments<T>(in T arguments, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
+    // 非公開の格納用構造体で使う16バイトの参照領域。
+    [StructLayout(LayoutKind.Sequential, Size = 16, Pack = 4)]
+    public struct ShaderReferenceSlot { }
+    // C#の配置を任意の診断へ渡す。GPUアドレスは含まない。
+    public sealed record ShaderTransferField(
+        string Path, int Offset, int Size, Type ValueType, bool IsReference = false);
     public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : struct, IShaderArguments
     {
     }
 }
```

ShaderValueは明示的なCPU検査の論理記録としてValueType、Reference、C#のOffsetを持ち、Pathを持たない。nativeの同期符号化では生成しない。IShaderDataLayout.ReferenceKind(string path)とPackは削除する。backendが保持する実行用metadataはIShaderReferenceMetadataであり、ShaderValueの共有リストではない。

RenderGraphの同名オーバーロードも通常のバッファ型を返す。数値だけの引数は、ShaderResourcesのDraw、DrawIndexed、Dispatchでも末尾にrootParameterとshaderTypeNameを持つunmanagedオーバーロードを使える。インターフェースを明示実装した型の従来の呼び出しは、短い引数リストの既存オーバーロードを選ぶ。通常の型をラップしたり、型を登録したりする必要はない。

```diff
 namespace Lumyte.Graphics.RenderGraph
 {
     public sealed class RenderGraph
     {
+        public RenderGraphBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact,
+            ulong count, string? name = null,
+            BufferUsage usage = BufferUsage.ShaderRead | BufferUsage.CopyDestination) where T : unmanaged;
     }
     public sealed class RenderGraphRenderEncoder
     {
+        public void SetArguments<T>(in T arguments, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
     public sealed class RenderGraphComputeEncoder
     {
+        public void SetArguments<T>(in T arguments, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
 }
```

### 手書き実装と第三者backendの移行

手書きIShaderArguments.Writeもunmanaged wireのローカルコピーからraw区間をWriteValueへ渡し、必要な位置で型付きWriteReferenceを呼ぶ。参照なしなら全wireへ一回だけWriteValueを呼ぶ。第三者writerはこの二つのメソッドを実装し、暗黙位置、16-byte ABI、容量とCompleteの一致検査、同期借用と非alias性を守る。能力判定、既定のthrow実装、cursor／emitter adapterは提供しない。

手書きIShaderDataSourceは要素ごとのRead／SetTransferredValueからbuffer全体のTryGetDependenciesへ移行し、追跡不可ならoutをnullにしてfalseを返す。共通のmetadata書き込みAPIを追加せず、rawは常にfalse／nullを返し、CPU依存更新とGPU copy metadata公開は検証層が所有する。第三者encoderは両generic SetArgumentsと上記SetArgumentValidationを既存interfaceで実装する。ShaderBindingDependencies.CollectはShaderRootArgumentsの依存metadataだけを収集し、numeric CPU Rootやsnapshot入力を持たない。通常のunmanaged bufferの直接memcpy、primitive buffer API、GPU copy commandと無関係なコンパイル経路は変更しない。この契約自体は新しい性能測定を意味しない。

### 検証方針

writerの二メソッド・各一引数、getter前のwireコピーと物理順一回評価、検証層だけの厳密なReferenceCount診断、全modeのABI予約ゼロ・実byte容量・完了検査を受け入れ条件とする。mapped batchの一回検査と保存済みaddressの使用、native root即時記録、固定stage／signature、再入・callback後の再検査を維持する。

全backendでrootなしでも選択tableの全登録をbinding可能にし、未登録holeをentryに数えず、有限native binding／storage／remap上限を明確に拒否する。全登録を使用hazardとして記録しない。

依存queryはOFFで数値型を含めfalse／null、ONの参照ゼロ型でtrue／既知空とする。未初期化・失敗後・部分更新・全体回復、旧revisionの不変性、buffer一回の循環探索、native copy記録とSubmit成功後だけの公開を確認する。GraphはOFFでroot走査・query・hook・許可集合・callbackを作らず、ONで正規化allocation／image identityとread／write合併だけを照合する。同allocationの別範囲の保存参照を許可し、別resourceとread-onlyからのReadWriteを拒否する。GetGpuRefは要求offset／countと完全一致するか単一GetElementを使い、広いCountへ置換しない。

struct制約、partial property、defaultのnull参照拒否、明示Captureの独立性も確認する。OFFの無効pointerを実行する試験は行わない。GPU・Browser・D3D12、native validation layer、AOT publication／runtimeを区別し、source確認やbuildだけを実行成功とは扱わない。

## 検討した代替案

- cursor・emitter・sinkへ書き込み境界を分割する: 同じ同期streamを複数interfaceへ広げるため採用せず、値一引数の二メソッドへ統一する。
- 数値snapshotをSet／uploadの中間入力として残す: 取得・保持・再構築を重ねるため採用せず、明示的なCPU検査だけに限定する。
- backend内にnullable診断trackerと切替APIを残す: 未選択時にも診断境界が全backendへ広がるため採用せず、独立したdecoratorへ分離する。
- 検証OFFでも要素別依存とGraph照合を維持する: bindingは全table登録から構築できるため、機能状態と任意診断を分離し採用しない。
- 数値型にもインターフェースと生成コードを必須にする: 外部のunmanaged型や通常の構造体をそのまま使えないため採用しない。
- 型制約だけを外して任意の管理型を受け付ける: 参照を含む値と生のunmanagedバイト列の境界が不明確になるため採用しない。生バッファはunmanaged制約、参照を含む値はstruct, IShaderArguments制約で分離する。
- Sharedの汎用writerとbackend参照符号化callbackを維持する: 共通化はできるが、native転送で論理参照記録や一時配列を介さずbackendが直接ABIを書き込む境界にするため採用しない。Graphの参照だけの準備hookは別の責務として維持する。
- 数値ごとのwriterを維持する: バイト列を再構築し、ABIの定義が重複するため採用しない。
- Releaseでリフレクションのオフセットに詰め直す: 不整合な宣言によってC#のメモリ表現を暗黙に変更しない方針のため採用しない。
- 参照を含む引数をclassにする: 値の所有と配列要素の明示編集をstructへ統一するため採用しない。
- 自動dirty trackingで変更箇所を検出する: 利用者が要素範囲と転送時点を選び、隠れた比較・cache・coalescingを持たない契約を優先する。
- 管理クラスのメモリを直接転送する: 転送可能な領域は非公開のunmanaged格納領域だけとし、採用しない。

## 結果と影響

シェーダー引数、ソース生成、任意検証の選択方法に対する破壊的変更となる。backendの有効化APIや互換aliasは提供せず、診断を選ぶ利用者は独立したValidationDeviceを参照する。利用側は参照を含むクラスをmutable partial structへ移行し、シェーダー宣言の配置を一致させる責務を持つ。検証を有効にした場合に配置診断を提供し、いずれの構成でも元の数値バイト列を保持する。所有権の管理、リソース検証、明示的なGPU同期は引き続き必要となる。
