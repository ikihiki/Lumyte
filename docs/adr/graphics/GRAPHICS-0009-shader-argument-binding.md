# ADR-GRAPHICS-0009: Argument Tableのシェーダー引数への接続

- 状態: 提案
- 日付: 2026-10-09

## 背景

[Argument TableとGPU参照](GRAPHICS-0005-argument-tables-and-gpu-references.md)はresourceの登録と不透明なIGpuRefを提供する。[shader artifact](GRAPHICS-0006-shader-compilation-and-modules.md)はtarget別codeとreflectionを保持し、[PSO](GRAPHICS-0008-pipeline-programs-and-render-state.md)はprogramと描画状態を分離する。これらをdraw／dispatchへ接続し、利用者による物理binding番号の指定を不要にする。

Argument Tableのslotは種類別の論理登録位置であり、shaderのbinding番号ではない。tableを設定するだけで全登録を一度にshaderへ渡す方式では、登録容量が物理binding上限に制約される。不透明参照をshader引数へ渡し、必要な登録だけをbackendが解決する契約が必要となる。

## 決定

### Table選択と引数指定を分離する

機能名は「シェーダー引数バインディング」とする。IRenderEncoderとIComputeEncoderにSetArgumentTableと`SetArguments<T>`を追加する。tableは参照の解決元、shader argumentsはそのdraw／dispatchが使うroot引数であり、別々に設定する。

最初の契約では一つのtableを選択する。texture・sampler・bufferの登録領域はそのtable内で独立している。backendが実際に設定するheapやbinding集合の数は共通APIへ露出しない。未使用slotをbindingへ含めず、textureとsamplerのペアを強制しない。

shader argumentsはapplicationが定義するstructとし、encoderへ値として渡す。IShaderArguments、CreateShaderArguments、文字列のfield pathを受けるSetValue／SetBuffer／SetTexture／SetSamplerは設けない。対象programのartifact reflectionと、引数型に対して生成したcodecを照合する。共通の所有wrapperやbackend contractは追加しない。

### 公開API

比較元はmain。以下は提案するAPI差分で、公開契約はGraphics.Abstractionsに置く。source generatorは別projectに置き、Abstractionsからgeneratorやbackendへ依存しない。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
+    // applicationの引数structにcodecを生成するcompile-time marker。
+    // rootParameterはSlangの論理root parameter名。物理binding番号ではない。
+    [AttributeUsage(AttributeTargets.Struct)]
+    public sealed class ShaderArgumentsAttribute(string rootParameter = "arguments") : Attribute
+    {
+        public string RootParameter { get; } = rootParameter;
+    }
+
     public interface IRenderEncoder
     {
+        // 同じdeviceの生存中のtableを選択する。
+        // 次のdrawへ使うtableであり、全slotのbinding命令は発行しない。
+        void SetArgumentTable(IArgumentTable table);
+
+        // 現在のprogramのroot schemaへ引数structを照合してsnapshotする。
+        // unmanaged制約は付けない。IGpuRefを含むstructも受け入れる。
+        // codec未生成・未対応型はNotSupportedException。
+        // schemaの型・用途・field不一致はArgumentException。
+        // 独自IGpuRef実装や他deviceの参照もArgumentException。
+        // 失効済みの登録はInvalidOperationException。
+        void SetArguments<T>(in T arguments)
+            where T : struct;
     }
+
     public interface IComputeEncoder
     {
+        // renderと同じ選択・検証・寿命契約。
+        void SetArgumentTable(IArgumentTable table);
+
+        void SetArguments<T>(in T arguments)
+            where T : struct;
     }
 }
```

現在のprogramを先にSetPipelineで選択する。SetArgumentsはsetter呼び出し時に値と登録identityのsnapshotを作り、元structやそのローカル変数を後から変更しても設定済み引数へ影響させない。drawごとにmanaged reflectionでstructを探索しない。

SetPipelineはtable選択や引数を暗黙に書き換えない。programを切り替えた場合は、そのprogramに対してSetArgumentsを再度呼ぶ。以前のprogramに対応付けたsnapshotを使うdraw／dispatchはInvalidOperationExceptionとし、reflectionの偶然の一致で受け入れない。

root引数のないshaderはSetArgumentsを必要としない。値だけのstructではArgumentTableの選択を要求しない。IGpuRefを含む場合は、draw／dispatchまでにその参照を登録したtableを選択する。新しいpassでは設定状態を初期化し、別passから継承しない。

### 利用例とshader側の受け取り

引数struct、camera data、material dataはapplicationに定義する。数値型とIGpuRefを同じstructへ入れられる。

```csharp
[ShaderArguments]
public readonly record struct DrawArguments(
    Matrix4x4 ViewProjection,
    IGpuRef<MaterialData> Material);

encoder.SetPipeline(pipeline);
encoder.SetArgumentTable(table);
encoder.SetArguments(new DrawArguments(
    camera.ViewProjection,
    materialRef.GetElement(materialIndex)));
encoder.Draw(vertexCount);
```

Slangのroot structも同じ論理member名と型で宣言する。数値memberは通常の値、IGpuRef memberはartifactに記録されたtarget別helper ABIを持つ型として受け取る。IGpuRefをC# interfaceのメモリ表現のままGPUへ送らない。

カメラ行列だけを渡す最小例は次の対応となる。この場合はtableを設定しなくてよい。

```csharp
[ShaderArguments]
public readonly record struct CameraArguments(Matrix4x4 ViewProjection);

encoder.SetPipeline(pipeline);
encoder.SetArguments(new CameraArguments(camera.ViewProjection));
```

```slang
struct CameraArguments
{
    float4x4 ViewProjection;
};
ConstantBuffer<CameraArguments> arguments;

// System.Numericsのrow-vector規約に対応する式。
// positionはapplicationのvertex shaderから与える。
float4 clipPosition(float3 position)
{
    return mul(float4(position, 1), arguments.ViewProjection);
}
```

ConstantBuffer宣言はshaderの論理的な受け口を示す。利用者に転送bufferの作成やbinding番号の指定を要求しない。backendがcompiled ABIに対応するroot data storageを用意し、commandに所有期間を関連付ける。一般bufferへのアップロード・copy APIは従来どおり利用者が明示し、この引数設定とは区別する。

### 型対応とcodec生成

source generatorはShaderArguments属性を付けたstructの公開instance fieldと読み出せる自動propertyを検査し、直接アクセスするcodecを生成する。readonly record structのprimary constructor由来のpropertyも対象とする。member名はSlangの論理member名と大文字・小文字を含めて一致させ、宣言順やC#のbyte offsetをshader layoutと仮定しない。rootParameterはattributeのcompile-time情報としてcodecへ保持し、setterへ文字列を渡さない。

対応するroot値はint／uint／float、Vector2／Vector3／Vector4、Matrix4x4、それらとIGpuRefを含む入れ子structとする。IGpuRefの論理型・用途をschemaへ照合する。配列、string、任意class、delegate、boolや未定義の数値変換、循環する型、custom getterは診断して拒否する。buffer要素型Tの有効性はbufferの既存layout契約に従う。

codecは値memberの読み出しとIGpuRefの列挙を行い、backendはprogramに含まれるtarget別reflectionのoffset・stride・alignmentへpackする。型Tとprogram schemaの検証結果は再利用できるが、registrationの有効性は各draw／dispatchでも確認する。source generatorはartifactのlayoutを別のruntime設定で上書きしない。online生成されたartifactにも同じcodecとschema照合を適用する。

Matrix4x4はSlangのfloat4x4へ対応する。System.NumericsのM11〜M44を論理的な行・列として扱い、artifactのrow-major設定に従ってpackする。matrix/vectorの掛ける順序は利用者のshader式で定め、codecが暗黙にtransposeして数学上の意味を変えない。paddingは初期化し、sizeof(T)のmemcpyでshader layoutが一致したと仮定しない。

### 反射情報とtarget別ABI

rootParameterとstruct memberの論理pathはtarget固有のbinding番号と分離する。同じlogical pathが複数stageで使われる場合、型と用途の互換性をprogram作成時に検証する。target別layout、binding plan、root値の配置、GPU参照のhelper ABI、schema versionはcompile時に確定してopaque artifact内へ保存する。runtimeで利用者がlayout補助情報を渡す方式にしない。

offlineは全targetを含む一つのartifactをDLLへ埋め込み、onlineは指定targetまたは全targetを含む同じformatを生成する。追加ABIに対応しないartifactは明示的にNotSupportedExceptionとする。生成WGSLやreflection JSONをソース管理へ追加しない。

buffer参照は登録rangeと要素位置を保持する。GetElementで得た参照のbyte位置をbackendが解決し、生addressや整数への変換は提供しない。有限bindingでは、元rangeのbindingと要素位置を別々に表現し、単一要素offsetをnative bindingのalignmentへ暗黙に丸めない。元range自体がalignment・用途・サイズ制約を満たさない場合は拒否する。参照に対応するshader helperのABIがない通常のresource parameterに、要素参照を無理に当てはめない。

### Draw／dispatchでの解決

1. programと引数snapshotの対応、必要なtable、各IGpuRefのdevice・登録identity・生存・用途・型を確認する。
2. root引数から参照した登録を収集し、selected table以外の登録を拒否する。
3. resource種類別に同じ登録と同じrangeを重複排除し、shaderのreflectionと有効なdevice limitsへ照合する。
4. SetArgumentsで取り出した数値snapshotと参照metadataをtarget別ABIへ変換し、必要なresourceとroot値をcommandへ設定する。
5. native命令の記録に成功した時点のsnapshotと登録identityをcommandへ関連付ける。

texture・sampler・bufferの上限は種類別に検証する。一回のdraw／dispatchが上限を超えたらNotSupportedExceptionとし、自動的に描画を分割しない。tableのcapacityはこの上限と独立したままにする。

direct address／native descriptorで解決できるbackendへ有限bindingの変換表を強制しない。物理binding生成、cache key、native objectの所有と破棄の詳細は各backend READMEへ記載する。

### Snapshot・寿命・失敗時

draw／dispatch後のarguments変更は記録済みcommandを変更しない。tableのslotを後から置換・解放した場合、古いIGpuRefを新しい登録へ読み替えない。submit前に記録済みidentityの有効性を再検証し、失効したcommandのsubmitを拒否する。submit後の登録変更やresource解放に必要なGPU同期は利用者が管理する。

引数structは非所有のCPU値でDisposeを要求しない。IGpuRefも非所有で、登録を保持する権利を追加しない。encoderはsnapshotの数値と参照metadataを保持し、記録済みcommandはそのdraw／dispatchのsnapshotを保持する。program・table・resourceの寿命は既存のcommand／registration契約に従い、引数struct自体がprogramのDisposeを禁止する所有objectにはならない。

不正なsetterは以前の値を維持する。draw／dispatchの事前検証失敗時はnative draw／dispatchを記録しない。GPU使用中のobjectを自動で待機・変更・破棄しない。CPU／GPU同期は利用者が管理し、内部lock、atomic ownership、InternalsVisibleToを追加しない。

### MaterialData内のIGpuRef

MaterialDataはapplicationの論理structであり、ライブラリへ固定のmaterial schemaを入れない。root引数と同様の生成codecを使い、数値をtarget別layoutへpackする。同時に各IGpuRefの登録identity、resource種別、range、要素位置、field pathをCPU metadataへ記録する。

IGpuRefを含むstructはunmanagedではない。IGraphicsBufferのunmanaged制約を外したり、managed参照をraw copyしたりしない。shader data用bufferのbackend具象型がnative allocationを直接所有し、同じobjectで`IGraphicsBuffer<byte>`も実装する。raw byte accessのための第二の所有buffer instanceを作らない。

### Shader dataの公開API

```diff
 namespace Lumyte.Graphics.Abstractions
 {
+    // shader data codecの生成対象を示すmarker。instance methodは要求しない。
+    public interface IShaderData { }
+
+    // C#とSlangの論理型名が異なる場合だけ指定する任意annotation。
+    // markerやlayoutの上書きとしては使わない。
+    [AttributeUsage(AttributeTargets.Struct)]
+    public sealed class ShaderTypeNameAttribute(string name) : Attribute
+    {
+        public string Name { get; } = name;
+    }
+
+    public sealed record ShaderDataBufferDesc<T> where T : struct, IShaderData
+    {
+        // このartifactの現在のbackend用schemaへpackする。
+        public required ShaderArtifact Artifact { get; init; }
+        // 論理T要素数。byte数やalignmentのために補正しない。
+        public required ulong Count { get; init; }
+        public required BufferUsage Usage { get; init; }
+        public MemoryPreference Memory { get; init; }
+    }
+
+    public interface IGraphicDevice
+    {
         // 既存のraw buffer生成API。unmanaged制約を維持する。
         IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
             where T : unmanaged;
+
+        // codec・型schemaを検証し、backend自身がbufferを生成する。
+        // Artifactに型の全target schemaと参照helper ABIが必要。
+        IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderDataBufferDesc<T> desc)
+            where T : struct, IShaderData;
+    }
+
+    public interface IGraphicsShaderDataBuffer<T> : IGraphicsBuffer<byte>
+        where T : struct, IShaderData
+    {
+        // 継承したCountはbyte数。論理要素数はElementCountで区別する。
+        ulong ElementCount { get; }
+        // artifactのtarget layoutから求めたstride。sizeof(T)ではない。
+        ulong ShaderElementStrideInBytes { get; }
+        // 明示的にmapしたUpload memoryへ数値とopaque wire dataをpackし、
+        // 同じ要素範囲の依存metadataを置換する。GPU命令を発行しない。
+        void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0);
+        // 非所有の論理要素range。継承したSliceはbyte rangeのまま。
+        ShaderDataSlice<T> SliceElements(ulong offset, ulong count);
+    }
+
+    public readonly struct ShaderDataSlice<T> where T : struct, IShaderData
+    {
+        // 非空・範囲内を検証する。defaultは無効なrange。
+        public ShaderDataSlice(IGraphicsShaderDataBuffer<T> buffer, ulong offset, ulong count);
+        public IGraphicsShaderDataBuffer<T> Buffer { get; }
+        public ulong Offset { get; }
+        public ulong Count { get; }
+        // byte copy／barrierの既存APIへ渡す非所有range。
+        public BufferSlice<byte> Bytes { get; }
+    }
+
+    public interface IArgumentTable
+    {
+        // raw unmanaged bufferの既存overloadとは区別する。
+        // schemaとrangeに対応するopaque参照を返す。
+        // Write時に未uploadでもよいが、drawでは要素metadataが有効でなければ拒否。
+        IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
+            where T : struct, IShaderData;
+    }
 }
```

TはIShaderDataを実装するstructに限り、source generatorもinterface実装からcodec対象を識別する。markerだけではmemberやlayoutの互換性を保証せず、ShaderArgumentsと同じ数値型・入れ子struct・IGpuRefの規則で検証する。IGpuRefのbuffer要素型はraw unmanaged型だけでなく、IShaderDataを実装する論理型も認める。これはraw bufferの型制約を変更するものではない。

shader data bufferのSizeInBytesと継承したCountは `ElementCount * ShaderElementStrideInBytes` でchecked計算する。artifactで定まるstruct内paddingとstrideはshader ABIの一部であり、backendのGPU copy alignmentのための追加丸めとは区別する。計算済みbyte rangeへcopy制約を別途検証し、不適合なら拒否する。

CreateBufferはDesc型の違いでoverloadする。generic制約だけでoverloadを分けることはできないため、BufferDescとShaderDataBufferDescを別型として維持する。ShaderData側のDesc・buffer interface・slice・table登録overloadの全てに `where T : struct, IShaderData` を適用し、通常bufferのunmanaged制約は維持する。

Slangの論理型名は既定でC#の型名と一致させ、名前が異なる場合だけShaderTypeName属性を使う。artifact内のschemaへ一意に対応しない場合は拒否する。この属性だけを付けた型はcodec対象にならず、IShaderDataの実装を必要とする。instanceをinterfaceへboxingしてpackせず、生成されたgeneric codecでmemberを読み出す。

### 明示的なpack・転送と利用例

```csharp
public readonly record struct MaterialData(
    Vector4 BaseColor,
    IGpuRef<IGraphicsTextureView> BaseColorTexture,
    IGpuRef<IGraphicsSampler> BaseColorSampler) : IShaderData;

var material = new MaterialData(color, textureRef, samplerRef);
using var upload = device.CreateBuffer<MaterialData>(new()
{
    Artifact = artifact,
    Count = 1,
    Usage = BufferUsage.CopySource,
    Memory = MemoryPreference.Upload,
});
using var materials = device.CreateBuffer<MaterialData>(new()
{
    Artifact = artifact,
    Count = 1,
    Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead,
});

await upload.MapAsync();
upload.CopyFrom([material]);
upload.Unmap();
// 利用者が必要なbarrierを記録する。
commands.CopyBuffer(upload.SliceElements(0, 1).Bytes,
                    materials.SliceElements(0, 1).Bytes);
// 利用者がshader読み出しに必要なbarrierを記録する。
var materialRef = table.WriteBuffer(0, materials.SliceElements(0, 1));
encoder.SetArgumentTable(table);
encoder.SetArguments(new DrawArguments(matrix, materialRef.GetElement(0)));
// drawとsubmit・waitも利用者が明示する。
```

例は同じcommand内でcopy後にdrawを記録する連携を示し、render passやbarrierの詳細を省略している。packはCPU memoryだけを更新し、stagingの自動確保、queue write、submit、waitを挿入しない。upload bufferをGPU使用完了前に解放しない。

### GPU上の参照表現

shader dataへ格納するIGpuRefは登録identityに対応するtarget別opaque wire値であり、そのdrawの有限binding indexをMaterialDataへ書き込まない。登録の置換・slot再利用は新しいidentityとし、以前のwire値やCPU metadataを新登録へ読み替えない。

shader側のMaterialDataは同じ数値memberと、compilerが提供するtexture・sampler・buffer参照helper型で宣言する。helper型のABI version、wire layout、resourceの種類・型、論理要素schema、参照先へのアクセス方法をartifactへ保存する。利用側が整数からhelper参照を作ったりbinding番号を計算したりしない。

有限binding backendはrootから収集した安定identityをdraw専用の物理indexへ対応付ける。helperはそのdrawの変換情報からresourceを選び、元MaterialData bufferをdrawごとに書き換えない。bufferはbinding可能なrangeと要素位置を分けて解決する。直接参照可能なbackendはnative表現を使えるが、CPU metadataとidentityの検証契約は共通とする。具体的なhelperのlowering、変換表のnative表現、生成とcacheはbackend READMEへ記載する。

### 要素依存metadataと再帰収集

各論理要素にschema identity、内容version、有効性、直接texture／sampler参照、他bufferの要素／rangeへの参照を記録する。参照のない数値要素も有効な空依存として記録する。初期確保直後の要素は未初期化であり、空依存と区別する。

rootが単一要素参照ならその要素だけ、配列参照ならそのrange全体を候補集合として収集する。MaterialDataから別のshader data要素へ参照する場合は、参照先のmetadataもたどる。flatな要素では直接依存一覧を使い、別要素参照がある場合だけ探索stackとvisited集合を使う。共有参照と循環はbuffer identity・schema・論理要素位置で重複を排除し、無限探索を防ぐ。未初期化・無効・失効した要素や登録が探索経路にあれば拒否する。推移的に収集した参照もselected tableに属することを検証する。

shader実行中に選択されるindexをCPUが推測しない。range参照は全候補を追跡するため、不要なbindingを減らしたい利用者はGetElementでdrawが使う要素を指定する。GPU data内の数値から依存を逆算しない。

依存metadataは非所有である。texture・sampler・bufferの実寿命はtableのlive registrationとcommandの既存契約で維持する。数値と登録identityはsnapshotするが、参照先のbuffer内容をroot設定時に凍結しない。draw記録時に、そのcommandの記録順序に従う要素versionから依存を収集する。

### Copy・上書き・GPU書き込み

型付きCopyFromは全source要素のschemaと参照を先に検証し、成功後に対象要素のbytesとmetadataを一緒に更新する。失敗時は以前のbytesとmetadataを維持する。raw byte CopyFromは触れた論理要素全体を無効にし、既知のcodecから書かれたと推測しない。raw CopyToはbytesだけを読み、IGpuRefへ復元しない。

既存commandのCopyBufferはbyte copyを記録する。同じdevice・互換schemaのshader data間で、sourceとdestinationが両方とも完全な要素境界に揃うときだけ、そのrangeのmetadataを対応して伝播する。destinationの登録identityは維持し、内容versionを更新する。metadataを持たないsource、schema不一致、部分要素のcopyは、destinationの触れた要素を無効にする。byte copy自体が合法なら記録を拒否するのではなく、後の参照解決で無効要素を拒否する。sliceやCountを暗黙に拡張しない。

commandは未submitのcopyによって共有bufferのmetadataを即変更しない。記録中のmetadata変更をcommand内のoverlayへ保持し、同じcommandのcopy後drawはそのoverlayを使う。submitはqueue順にsource versionとoverlayを検証し、後続commandが使う既知のmetadata状態を更新する。記録後に外部のCPU書き込みや先行submitで前提versionが変わった場合、内容を推測して再packせずsubmitを拒否し、利用者がcommandを記録し直す。submit失敗時は共有metadataへの変更を確定しない。

初期契約ではIGpuRefを含むshader data bufferのShaderWrite用途を拒否する。GPUから参照や数値を変更すると、CPU依存metadataとの整合を保証できないためである。通常のraw storage bufferのGPU書き込みは従来どおり可能。参照memberを維持した数値memberだけのGPU更新などは、field別write範囲を保証できる別の契約で扱う。

利用者はcopy／draw／dispatch間のbarrier、queue間の同期、CPU packとGPU実行の同期を管理する。metadataのversion検証はCPU／GPU同期の代わりにはならない。submit後にGPUが使う範囲を書き換えることを許可する仕組みではなく、内部lockや自動待機を追加しない。

## 検討した代替案

- 文字列のfield pathへ値を逐次設定するIShaderArguments: 引数を一つのapplication structとして渡す方式を選び、型検証とcodec生成の対象を明確にする。
- tableの論理slotをshader binding番号へ流用する: 種類別slotとtarget別の物理配置を混同するため採用しない。
- table全体を常にbindingする: 登録容量が物理上限へ制約され、未使用resourceも保持するため採用しない。
- shader sourceとは別のruntime binding layoutを利用者へ要求する: compile済み情報をartifactへ保持する方針に反するため採用しない。
- IGpuRefをaddressや整数へ変換してroot値へ入れる: 不透明参照と登録失効の契約を破るため採用しない。

## 結果と影響

利用者は論理tableと型付き参照を使い、物理binding配置をbackendへ任せられる。root引数をprogram reflectionに照合することでtarget間の違いを隠せる。一方、shader artifactへhelper ABIとbinding planを含めるcompiler側の接続が必要で、tableを設定するだけで任意のGPU bufferの内容を自動解析できるわけではない。

## 検証方針

shared projectの検証は共通APIのみを使う。texture・sampler・bufferの実アクセス、buffer単一要素、sampler共有、不要slot、同じ論理slot番号の種類別登録、引数のsnapshot、欠落・型不一致・別device・別table・失効参照を検証する。MaterialData内のtextureと共有sampler、別要素参照と循環、単一要素とrangeの収集、schema一致copy、部分copyとraw上書き、記録中overlay、submit前のversion不一致、ShaderWrite拒否も検証する。カメラ行列、入れ子struct、record struct、値だけの引数、setter後の元struct変更、program切替後の再設定を検証する。非対称の行列で既知のvertex座標を変換し、transposeや乗算順の誤りを検出する。generatorの非対応型診断、IShaderData markerの有無、任意の型名annotation、Desc別CreateBuffer overloadの解決とBrowser/Wasmでのcodec動作も確認する。

computeはstorage buffer更新後に明示的なbarrier、copy、submit、wait、map、CopyToで結果を読む。graphicsは異なるtextureを使ったdrawの全画素を確認する。bindingのないPSOの検証も維持し、wgpu・Vulkan・Browser/Wasmを既存CIで実行する。backend固有の生成・cacheの詳細と測定は各READMEに記録する。
