# ADR-GRAPHICS-0009: Argument Tableのシェーダー引数への接続

- 状態: 置換済み（実行時検証の方針のみ。その他の決定は引き続き採用）
- 日付: 2026-10-09
- 変更日: 2026-10-10
- 置換範囲: draw／Submitでの登録失効検出と、staging revision・command間metadata依存の再検証。利用者が寿命と同期を管理し、検証のための追跡費用を省く方針へ変更する。
- 後継: [ADR-GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)。以下の本文は判断時点の記録であり、上記の実行時検証には後継を適用する。

## 背景

[Argument TableとGPU参照](GRAPHICS-0005-argument-tables-and-gpu-references.md)はresourceの登録と不透明なIGpuRefを提供する。[shader artifact](GRAPHICS-0006-shader-compilation-and-modules.md)はtarget別codeとreflectionを保持し、[PSO](GRAPHICS-0008-pipeline-programs-and-render-state.md)はprogramと描画状態を分離する。これらをdraw／dispatchへ接続し、利用者による物理binding番号の指定を不要にする。

Argument Tableのslotは種類別の論理登録位置であり、shaderのbinding番号ではない。tableを設定するだけで全登録を一度にshaderへ渡す方式では、登録容量が物理binding上限に制約される。不透明参照をshader引数へ渡し、必要な登録だけをbackendが解決する契約が必要となる。

## 決定

### Table選択と引数指定を分離する

機能名は「シェーダー引数バインディング」とする。IRenderEncoderとIComputeEncoderにSetArgumentTableと`SetArguments<T>`を追加する。tableは参照の解決元、shader argumentsはそのdraw／dispatchが使うroot引数であり、別々に設定する。

最初の契約では一つのtableを選択する。texture・sampler・bufferの登録領域はそのtable内で独立している。backendが実際に設定するheapやbinding集合の数は共通APIへ露出しない。未使用slotをbindingへ含めず、textureとsamplerのペアを強制しない。

shader argumentsはIShaderArgumentsを実装するapplicationのstructとし、encoderへ値として渡す。CreateShaderArguments、文字列のfield pathを受けるSetValue／SetBuffer／SetTexture／SetSamplerは設けない。対象programのartifact reflectionと、引数型に対して生成したcodecを照合する。共通の所有wrapperやbackend contractは追加しない。

### 公開API

比較元はmain。以下はAPI差分で、公開契約はGraphics.Abstractionsに置く。source generatorは別projectに置き、Abstractionsからgeneratorやbackendへ依存しない。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
+    // applicationのpartial structが実装する、登録不要のcodec契約。
+    public interface IShaderArguments
+    {
+        // Slangの論理root parameter名。型の静的propertyで変更できる。
+        static virtual string RootParameter => "arguments";
+        // generatorが型名またはShaderTypeName属性から実装する。
+        static abstract string ShaderTypeName { get; }
+        // generatorがmemberを直接読み出す実装を型自身へ追加する。
+        // GPU転送、登録、所有権の取得は行わない。
+        // refで渡すgeneric writerは値型でもboxingを発生させない。
+        void Write<TWriter>(ref TWriter writer) where TWriter : IShaderValueWriter;
+        // Tの静的metadataとWriteを直接呼び、managed reflectionを使わない。
+        public static ShaderValueSnapshot Capture<T>(in T value)
+            where T : struct, IShaderArguments;
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
+        // codec契約の未実装・未対応型はcompile時に診断する。
+        // schemaの型・用途・field不一致はArgumentException。
+        // 独自IGpuRef実装や他deviceの参照もArgumentException。
+        // 失効済みの登録はInvalidOperationException。
+        void SetArguments<T>(in T arguments)
+            where T : struct, IShaderArguments;
     }
+
     public interface IComputeEncoder
     {
+        // renderと同じ選択・検証・寿命契約。
+        void SetArgumentTable(IArgumentTable table);
+
+        void SetArguments<T>(in T arguments)
+            where T : struct, IShaderArguments;
     }
 }
```

現在のprogramを先にSetPipelineで選択する。SetArgumentsはsetter呼び出し時に値と登録identityのsnapshotを作り、元structやそのローカル変数を後から変更しても設定済み引数へ影響させない。drawごとにmanaged reflectionでstructを探索しない。

SetPipelineはtable選択や引数を暗黙に書き換えない。programを切り替えた場合は、そのprogramに対してSetArgumentsを再度呼ぶ。以前のprogramに対応付けたsnapshotを使うdraw／dispatchはInvalidOperationExceptionとし、reflectionの偶然の一致で受け入れない。

root引数のないshaderはSetArgumentsを必要としない。値だけのstructではArgumentTableの選択を要求しない。IGpuRefを含む場合は、draw／dispatchまでにその参照を登録したtableを選択する。新しいpassでは設定状態を初期化し、別passから継承しない。

### 生成codecとbackendの接続

生成codecはapplicationのpartial struct自身にIShaderArgumentsの実装として追加する。SetArgumentsは`IShaderArguments.Capture<T>`を通してTの静的metadataとWriteを直接呼ぶ。codec登録、型別registry、module initializerは設けない。別assemblyのbackendから呼び出せるAOT対応の接続をGraphics.Abstractionsへ置き、managed reflectionやInternalsVisibleToを使わない。以下は生成コードとbackendが使用する契約であり、通常の利用側はSetArgumentsとCopyFromを使う。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
+    // 生成codecが数値と不透明参照を列挙する接続。
+    public interface IShaderValueWriter
+    {
+        void WriteValue<T>(string path, in T value) where T : unmanaged;
+        void WriteReference<T>(string path, IGpuRef<T>? value);
+    }
+
+    // 数値のCPU snapshotと参照identityを保持し、native addressは公開しない。
+    public sealed record ShaderValue(string Path, Type ValueType,
+        ReadOnlyMemory<byte> Data, object? Reference, bool IsReference);
+    public sealed record ShaderValueSnapshot(string RootParameter,
+        string ShaderTypeName, IReadOnlyList<ShaderValue> Values);
+
+    // offline／online compilerで共通のhelper ABIと型metadataを準備する。
+    public static class ShaderSourcePreparation
+    {
+        public static string Prepare(string source);
+        public static string CompleteReflection(string reflection, string source);
+    }
 }
```

### 利用例とshader側の受け取り

引数struct、camera data、material dataはapplicationに定義する。数値型とIGpuRefを同じstructへ入れられる。

```csharp
public readonly partial record struct DrawArguments(
    Matrix4x4 ViewProjection,
    IGpuRef<MaterialData> Material) : IShaderArguments;

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
public readonly partial record struct CameraArguments(Matrix4x4 ViewProjection) : IShaderArguments;

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

source generatorはIShaderArgumentsを実装するpartial structの公開instance fieldと読み出せる自動propertyを検査し、型自身にShaderTypeNameとWriteの明示的interface実装を生成する。IShaderDataもIShaderArgumentsを継承し、同じ生成・呼び出し経路を使う。partialでない生成対象型はcompile時に診断する。手書きの実装はgeneratorなしでも同じ契約で呼び出せる。readonly record structのprimary constructor由来のpropertyも対象とする。member名はSlangの論理member名と大文字・小文字を含めて一致させ、宣言順やC#のbyte offsetをshader layoutと仮定しない。root名はIShaderArguments.RootParameterの既定値argumentsを使い、異なる名前は型の静的propertyで指定する。setterへ文字列やcodecを渡さない。

対応するroot値はint／uint／float、Vector2／Vector3／Vector4、Matrix4x4、それらとIGpuRefを含む入れ子structとする。IGpuRefの論理型・用途をschemaへ照合する。配列、string、任意class、delegate、boolや未定義の数値変換、inline展開で循環する型、custom getterは診断して拒否する。IGpuRefの型引数は参照先schemaを示すedgeとして扱い、inline展開しないため、IGpuRef経由の循環は許可する。buffer要素型Tの有効性はbufferの既存layout契約に従う。

codecは値memberの読み出しとIGpuRefの列挙を行い、backendはprogramに含まれるtarget別reflectionのoffset・stride・alignmentへpackする。型Tとprogram schemaの検証結果は再利用できるが、registrationの有効性は各draw／dispatchでも確認する。source generatorはartifactのlayoutを別のruntime設定で上書きしない。online生成されたartifactにも同じcodecとschema照合を適用する。

Matrix4x4はSlangのfloat4x4へ対応する。System.NumericsのM11〜M44を論理的な行・列として扱い、artifactのrow-major設定に従ってpackする。matrix/vectorの掛ける順序は利用者のshader式で定め、codecが暗黙にtransposeして数学上の意味を変えない。paddingは初期化し、sizeof(T)のmemcpyでshader layoutが一致したと仮定しない。

snapshot用writerは値型とし、genericなref引数でWriteへ渡してwriter本体のヒープ確保とboxingを避ける。snapshotと数値payloadは引数設定後や転送metadataとして保持するため、その保持用メモリの確保は残る。

### 反射情報とtarget別ABI

rootParameterとstruct memberの論理pathはtarget固有のbinding番号と分離する。同じlogical pathが複数stageで使われる場合、型と用途の互換性をprogram作成時に検証する。target別の論理layout、binding生成規則、root値の配置、GPU参照helper ABI、schema versionはcompile時に確定してopaque artifact内へ保存する。sourceから物理binding数を調整するtargetでは、変更対象の宣言と参照操作を識別する情報もartifactへ含める。backendはdeviceの有効limitsと使用するresource型から物理配置を確定し、実際のsource・binding plan・pipelineを一体として生成する。runtimeで利用者がlayout補助情報を渡す方式にしない。

offlineは全targetを含む一つのartifactをDLLへ埋め込み、onlineは指定targetまたは全targetを含む同じformatを生成する。buffer生成の受理条件は使用backendのtarget code・対象型schema・helper ABIが揃うこととし、他targetのcodeやschemaは要求しない。この条件を満たす単一targetのonline artifactから、shader data bufferを生成できる。全targetの生成はoffline compilerの成果物要件であり、CreateBufferの受理条件には適用しない。使用backendのtargetが欠落する場合と、必要なABI/schemaがない場合はNotSupportedExceptionとし、必要な項目を診断する。生成WGSLやreflection JSONをソース管理へ追加しない。

buffer参照は登録rangeと要素位置を保持する。GetElementで得た参照のbyte位置をbackendが解決し、生addressや整数への変換は提供しない。有限bindingでは、元rangeのbindingと要素位置を別々に表現し、単一要素offsetをnative bindingのalignmentへ暗黙に丸めない。元range自体がalignment・用途・サイズ制約を満たさない場合は拒否する。参照に対応するshader helperのABIがない通常のresource parameterに、要素参照を無理に当てはめない。

### Draw／dispatchでの解決

1. programと引数snapshotの対応、必要なtable、各IGpuRefのdevice・登録identity・生存・用途・型を確認する。
2. root引数から参照した登録を収集し、selected table以外の登録を拒否する。
3. resource種類別に同じ登録と同じrangeを重複排除し、shaderのreflectionと有効なdevice limitsへ照合する。
4. SetArgumentsで取り出した数値snapshotと参照metadataをtarget別ABIへ変換し、必要なresourceとroot値をcommandへ設定する。
5. native命令の記録に成功した時点のsnapshotと登録identityをcommandへ関連付ける。

texture・sampler・bufferの上限は種類別・shader stage別に検証する。root値と参照変換表の補助bindingも予約し、生成した物理layout全体がdevice limitsに収まることを検証する。一回のdraw／dispatchが上限を超えたらNotSupportedExceptionとし、自動的に描画を分割しない。tableのcapacityはこの上限と独立したままにする。

direct address／native descriptorで解決できるbackendへ有限bindingの変換表を強制しない。物理binding生成、cache key、native objectの所有と破棄の詳細は各backend READMEへ記載する。

### Snapshot・寿命・失敗時

draw／dispatch後のarguments変更は記録済みcommandを変更しない。tableのslotを後から置換・解放した場合、古いIGpuRefを新しい登録へ読み替えない。submit前に記録済みidentityの有効性を再検証し、失効したcommandのsubmitを拒否する。submit後の登録変更やresource解放に必要なGPU同期は利用者が管理する。

引数structは非所有のCPU値でDisposeを要求しない。IGpuRefも非所有で、登録を保持する権利を追加しない。encoderはsnapshotの数値と参照metadataを保持し、記録済みcommandはそのdraw／dispatchのsnapshotを保持する。program・table・resourceの寿命は既存のcommand／registration契約に従い、引数struct自体がprogramのDisposeを禁止する所有objectにはならない。

不正なsetterは以前の値を維持する。draw／dispatchの事前検証失敗時はnative draw／dispatchを記録しない。GPU使用中のobjectを自動で待機・変更・破棄しない。CPU／GPU同期は利用者が管理し、内部lock、atomic ownership、InternalsVisibleToを追加しない。

### MaterialData内のIGpuRefと明示的な転送

MaterialDataはapplicationが定義するIShaderData structとする。数値とIGpuRefを生成codecで列挙し、artifactのtarget別schemaへ照合する。ライブラリにmaterial専用の構造体を置かない。

shader data用bufferには二つの用途を設ける。MemoryPreference.UploadはmapしてCPUから値を書き込むstaging、Automaticはshaderから読み取るGPU用bufferである。CreateBufferはartifact・要素数・memory指定を受け、ShaderDataBufferDescは設けない。Readbackとshader writeは提供しない。通常bufferのunmanaged制約と、IShaderDataのmarker制約は維持する。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
+    // applicationの論理structに生成codecを付けるmarker。
+    public interface IShaderData : IShaderArguments { }
+
+    // C#型名と異なるSlangの論理型名を指定する場合に使用する。
+    [AttributeUsage(AttributeTargets.Struct)]
+    public sealed class ShaderTypeNameAttribute(string name) : Attribute
+    {
+        public string Name { get; }
+    }
+
     public interface IGraphicDevice
     {
+        // AutomaticはGPU用、UploadはCPU書き込みstaging。
+        // artifactには使用targetのcode・型schema・helper ABIが必要。
+        // ReadbackはArgumentException。他targetの存在は要求しない。
+        IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact,
+            ulong count, MemoryPreference memory = MemoryPreference.Automatic)
+            where T : struct, IShaderData;
     }
+
+    public interface IGraphicsShaderDataBuffer<T> : IDisposable
+        where T : struct, IShaderData
+    {
+        ulong Count { get; }
+        // Count * artifactのstride。alignmentのための暗黙の補正はしない。
+        ulong SizeInBytes { get; }
+        ulong ShaderElementStrideInBytes { get; }
+        MemoryPreference Memory { get; }
+        bool IsMapped { get; }
+        // Uploadだけを明示的にmapする。GPU完了の自動待機はしない。
+        ValueTask MapAsync();
+        void Unmap();
+        // map済みUploadへtarget別bytesと依存metadataを書き込むCPU操作。
+        // Automatic・未mapはInvalidOperationException。
+        // GPU転送・copy命令・submit・barrierを発行しない。
+        void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0);
+        ShaderDataSlice<T> SliceElements(ulong offset, ulong count);
+    }
+
+    public readonly struct ShaderDataSlice<T> where T : struct, IShaderData
+    {
+        // 非所有・非空range。defaultは無効。
+        public ShaderDataSlice(IGraphicsShaderDataBuffer<T> buffer, ulong offset, ulong count);
+        public IGraphicsShaderDataBuffer<T> Buffer { get; }
+        public ulong Offset { get; }
+        public ulong Count { get; }
+    }
+
     public interface IArgumentTable
     {
+        // Automaticだけを登録する。staging登録はArgumentException。
+        IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
+            where T : struct, IShaderData;
     }
+
     public interface IGraphicsCommandBuffer
     {
+        // pass外でUploadからAutomaticへ明示的にcopyを記録する。
+        // 同device・同schema・同要素数・unmap済み・copy alignmentを検証する。
+        // GPU間copy、raw bufferからのcopy、readbackはこのoverloadで受けない。
+        void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
+            where T : struct, IShaderData;
+        // コピー前後の依存は利用者が明示する。
+        void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
+            where T : struct, IShaderData;
     }
+
+    public sealed record ShaderDataBufferBarrierDesc<T> where T : struct, IShaderData
+    {
+        public required ShaderDataSlice<T> Buffer { get; init; }
+        public required BarrierScope Before { get; init; }
+        public required BarrierScope After { get; init; }
+    }
 }
```

IShaderDataを含むmanaged structのメモリをそのままGPUへコピーしない。map済みstagingのCopyFromが生成codecとartifactのlayoutを使ってpackする。参照は登録identityに対応するtarget別の安定したopaque値として格納する。drawごとの物理binding indexをstagingへ書き込まず、draw側のbinding解決と組み合わせる。直接参照できるtargetではGPU用bufferの確保済みaddressを使用できる。stagingやCPU structのaddressを参照先にしない。

### 利用例

```csharp
using var materials = device.CreateBuffer<MaterialData>(artifact, materialCount);
using var staging = device.CreateBuffer<MaterialData>(artifact, materialCount, MemoryPreference.Upload);
var materialRef = table.WriteBuffer(0, materials.SliceElements(0, materialCount));

await staging.MapAsync();
staging.CopyFrom(materialArray);
staging.Unmap();

commands.Barrier(new ShaderDataBufferBarrierDesc<MaterialData>
{
    Buffer = staging.SliceElements(0, materialCount),
    Before = new(PipelineStage.Host, ResourceAccess.HostWrite),
    After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
});
commands.CopyBuffer(staging.SliceElements(0, materialCount), materials.SliceElements(0, materialCount));
commands.Barrier(new ShaderDataBufferBarrierDesc<MaterialData>
{
    Buffer = materials.SliceElements(0, materialCount),
    Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
    After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead),
});

// この後にrender passを開始する。更新しなければ後続drawでもGPU bufferを再利用する。
encoder.SetPipeline(pipeline);
encoder.SetArgumentTable(table);
encoder.SetArguments(new DrawArguments(camera.ViewProjection, materialRef.GetElement(materialIndex)));
encoder.Draw(vertexCount);
// Finish、Submit、完了待ち、staging再利用の同期も利用者が行う。
```

CopyFromは全source要素を検証してから、staging bytesとmetadataを置換する。空sourceは有効offsetでno-opとする。byte換算後のcopy offset・size alignmentはbackendが別途検証し、公開Count・SizeInBytes・strideを丸めない。stagingの再map・書き換え・破棄は、コピーがGPUで完了してから利用者が行う。

### Copyと参照追跡の整合性

GPU用bufferは初期状態では未転送であり、必要な要素に明示copyがないdraw／dispatchはInvalidOperationExceptionとする。CopyFromだけで転送済みにならず、encoderがcopyを補うこともない。

コピーした範囲のCPU依存metadataは、copy命令と同じ範囲へ伝播する。command内ではコピー後のmetadataをoverlayとして保持し、その後のdrawから参照できる。partial copyは指定要素だけを置換する。copy命令がない要素は以前のmetadataを維持する。未submit・破棄したcommandのoverlayをbufferの転送済みmetadataへ反映しない。

submit成功後にmetadataを公開し、後続commandの記録で利用できる。別commandのuploadを利用するdrawはuploadのsubmit後に記録し、queue順序と必要な依存を利用者が管理する。同じcommandならcopy、barrier、drawを順に記録する。upload前の古いmetadataで記録した別commandを同じsubmissionのupload後に実行する場合は拒否する。GPU完了までstagingとdestinationを保持する。

copy記録後にsourceの対象要素を書き換えた場合はsubmitを拒否し、記録時のmetadataと実際のcopy bytesの食い違いを防ぐ。同じsubmission内で異なる値を転送する場合は別staging領域を用意する。sourceの対象外要素の更新では記録済みcopyを失効させない。利用者の同期を内部lockや自動waitで置き換えない。

### 推移的な参照と循環

rootからGPU用shader dataの選択要素をたどり、転送済みmetadataから別要素の参照を推移的に収集する。texture・sampler・raw bufferはleafとし、shader dataのrangeは含まれる全要素を候補として追う。GetElementを使えば対象要素を限定できる。

訪問済みのbufferと要素を記録し、同じ要素の再訪で探索を止める。自己参照・相互参照・循環を許可する。GPU用bufferを先に確保・登録できるため、stagingへ循環参照を書き込んでから明示copyできる。GPU上のデータをCPUへ読み戻して解析せず、CPUの依存metadataを使う。

drawのsnapshotはroot値と依存metadataを保持する。shader dataの数値payloadはGPU用bufferから読み、draw専用backingへコピーしない。後から記録した明示copyによる更新はcommand順序に従う。drawとcopyの間のbarrierは利用者が設定する。stagingのCPU変更だけではGPU用bufferは変化しない。

登録identityの失効をdrawとsubmitで検証する。slot再利用を古いGPU参照の読み替えに使わない。shader dataにraw byte aliasやshader writeの経路を設けず、数値と参照metadataを破壊する書き換えを防ぐ。backendのpack、binding生成、native allocationの詳細は各READMEへ記載する。

## 検討した代替案

- 文字列のfield pathへ値を逐次設定する所有wrapper: 引数を一つのapplication structとして渡す方式を選び、型検証とcodec生成の対象を明確にする。
- tableの論理slotをshader binding番号へ流用する: 種類別slotとtarget別の物理配置を混同するため採用しない。
- table全体を常にbindingする: 登録容量が物理上限へ制約され、未使用resourceも保持するため採用しない。
- shader sourceとは別のruntime binding layoutを利用者へ要求する: compile済み情報をartifactへ保持する方針に反するため採用しない。
- IGpuRefをaddressや整数へ変換してroot値へ入れる: 不透明参照と登録失効の契約を破るため採用しない。

## 結果と影響

利用者は論理tableと型付き参照を使い、物理binding配置をbackendへ任せられる。root引数をprogram reflectionに照合することでtarget間の違いを隠せる。一方、shader artifactへhelper ABIとbinding planを含めるcompiler側の接続が必要で、tableを設定するだけで任意のGPU bufferの内容を自動解析できるわけではない。

## 検証方針

shared projectの検証は共通APIのみを使う。texture・sampler・bufferの実アクセス、buffer単一要素、sampler共有、不要slot、同じ論理slot番号の種類別登録、引数のsnapshot、欠落・型不一致・別device・別table・失効参照を検証する。MaterialData内のtextureと共有sampler、別要素参照と循環、単一要素とrangeの収集、map済みstagingだけのCPU書き込み、明示copyとpartial copy、未転送drawの拒否、未submit copyのmetadata非公開、copy後のGPU再利用、ShaderWrite非提供も検証する。カメラ行列、入れ子struct、record struct、値だけの引数、setter後の元struct変更、program切替後の再設定を検証する。非対称の行列で既知のvertex座標を変換し、transposeや乗算順の誤りを検出する。generatorの非対応型診断、IShaderData markerの有無、任意の型名annotation、引数数で分けたCreateBuffer overloadの解決とBrowser/Wasmでのcodec動作も確認する。

computeは通常のraw storage buffer更新後に明示的なbarrier、copy、submit、wait、map、CopyToで結果を読む。graphicsは異なるtextureを使ったdrawの全画素を確認する。bindingのないPSOの検証も維持し、wgpu・Vulkan・Browser/Wasmを既存CIで実行する。使用backendだけのonline artifactによるshader data生成と、該当target欠落時の失敗も確認する。backend固有のsource調整、生成・cacheの詳細と測定は各READMEに記録する。
