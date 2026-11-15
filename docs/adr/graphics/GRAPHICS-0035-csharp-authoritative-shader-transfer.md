# ADR-GRAPHICS-0035: シェーダー引数とC#の配置を正とする一括転送

- 状態: 採用
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0005](GRAPHICS-0005-argument-tables-and-gpu-references.md)の「登録API」と、[GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md)の「生成codecとbackendの接続」「MaterialData内のIGpuRefと明示的な転送」と、[GRAPHICS-0023](GRAPHICS-0023-explicit-api-convenience.md)の型制約と、[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)の「取り除く処理」「維持する処理」
- 変更対象: 数値の転送表現、参照を含む型の宣言、転送サイズの基準、配置検証の実行条件、登録ごとのバッファアクセス選択、任意の寿命・同期診断
- 継続する判断: テーブルの明示選択、不透明な型付き参照、資源の所有権、明示転送・送信・完了待機
- リソース検証方針の一部変更: [GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)の「任意のvalidation modeへ移すAPIは追加しない」と検証追跡の除去を、明示的に有効化したdeviceに限って変更する。無効時の利用者責任、所有権、明示同期、ホスト範囲検査と必要なbinding metadataは継続する。
- 明示的な操作を補助するAPI: [GRAPHICS-0023](GRAPHICS-0023-explicit-api-convenience.md)。

## 背景

数値メンバーごとのシリアライズでは、C#の値の配置が暗黙にシェーダーのリフレクションへ従い、転送時に数値データを再構築していた。利用者が定義したC#のunmanaged構造体のメモリ表現を、転送形式の唯一の基準に変更する。

### rootとbuffer要素の転送契約を統合する

追加memberを持たない旧IShaderData markerは削除し、IShaderArgumentsだけを転送契約とする。互換aliasは残さない。同じIShaderArguments実装structを、SetArgumentsのrootとIGraphicsShaderDataBufferの要素の両方で使用できる。shader-data buffer／slice／factory／Graph／resource宣言の制約もstruct, IShaderArgumentsへ統一する。手書きcodecと生成型は同じ必須の値だけを渡すwriter契約を使う。通常のunmanaged型はこのinterfaceを実装せず、従来どおりraw buffer overloadを選ぶ。数値のfield別変換、snapshot書き込みの復活、GPU copy／所有権や同期契約の変更は行わない。

## 決定

C#のunmanaged構造体の配置を正とし、数値を一括コピーする。参照を含む引数は非公開のunmanaged格納領域と型付きIGpuRefを持つmutable partial structとし、生成コードが参照だけを解決する。数値の再構築をなくし、格納用構造体は通常の利用側APIに露出させない。

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

### struct限定と利用者による要素範囲の選択

本決定では、参照を含む引数をclassへ移す案よりも、structだけを転送する判断を優先する。native/root、shader-data buffer/slice、factory、Graph、resource宣言のIShaderArguments経路をすべて`where T : struct, IShaderArguments`とする。管理参照を含むためunmanaged制約にはしない。interfaceなしの通常unmanaged経路は変更しない。生成対象のclass、record class、ref structは手書きWriteがあってもLUMG001で拒否する。C#自体はclassのinterface実装を許すが、generatorなしの手書きclassもconsumer genericではCS0453となることを実際の負例コンパイルで確認する。

生成readonly Writeはprivate unmanaged wireだけをgetter前にコピーし、型付きIGpuRefを順に符号化する。管理参照を含む外側structをraw bytesとして転送しない。数値partial propertyは`{ readonly get; set; }`で可変値を提供できる。生成実装は宣言のreadonly修飾を正確に保持し、通常のget宣言へreadonly getを合成しない。これはC#のpartial propertyの互換性を保つためである。in/ref readonlyからのgeneric interface呼び出しやreadonlyでないgetterには外側structの防御的コピーがあり得る。外側コピーゼロや全体allocation-freeは保証しない。引数CaptureはDiagnosticTypeとキャッシュ済み静的generic DiagnosticFields callbackを保存し、入力struct、boxingやinstance closureを保持しない。CaptureとCopyはcallbackを呼ばず、静的TransferFieldsの遅延取得は次節の明示診断契約に従う。

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

### 転送形式と生成コードの契約

C#のunmanaged構造体の定義を転送ABIの正とする。値の取得時には、実際のパディングを含むメモリ表現全体を一括コピーする。数値メンバーの個別シリアライズ、並べ替え、行列の転置、型変換、リフレクションのオフセットに合わせた再配置は行わない。入れ子の構造体とMatrix4x4も元のバイト列を維持する。シェーダー側はC#のフィールド順、オフセット、パッキング、要素間隔を一致させる。どちらかの変更もABIの変更となる。

通常の数値データにはIShaderArguments不要である。`CreateShaderDataBufferのunmanagedオーバーロードは既存のIGraphicsBuffer<T>`を返し、`BufferSlice<T>`、`BufferBarrierDesc<T>`、CopyBuffer、WriteBufferとRenderGraphの通常のバッファ経路を使う。RenderGraph.CreateShaderDataBufferにもunmanagedオーバーロードを用意し、`RenderGraphBuffer<T>`を返す。`Lumyte.Resources.Graphicsでは既存のGraphicsBufferDefinition<T>`と生バッファのインポートをそのまま利用する。参照依存メタデータがないため、要素ごとの取得・符号化は行わず`ReadOnlySpan<T>`をそのままコピーする。既存のインターフェースを明示実装する数値型も同じ必須の値だけを渡すwriter経路で使用できる。IGpuRefを含む型はmutable partial structにする。数値データは1つの非公開unmanaged構造体に保持し、利用側には通常の読み書き可能なプロパティを提供する。.NET 10のpartialプロパティはジェネレーターで実装できる。利用者が定義する格納用構造体には、参照プロパティと同名の16バイトのShaderReferenceSlotフィールドも配置する。ジェネレーターは非公開の格納領域をローカルへ一括コピーし、参照の識別情報をC#側のスロット位置で同期符号化する。管理オブジェクト自体のメモリは転送しない。

通常の構造体は`Unsafe.SizeOf<T>()`で、生成対象のstructは静的なTransferSizeで、バッファの要素間隔と割り当てサイズを決める。バッファ生成時に値のインスタンスを生成したり、default(T)から値を取得したりしない。生成型の静的なTransferFieldsは、任意の診断に使う実際の管理メモリ上のオフセットを表す。通常の構造体では検証を明示的に有効にした場合に限ってフィールドのリフレクションと実際の管理オフセットを求めるコードを型ごとに一度実行してキャッシュする。通常のunmanaged CaptureはDiagnosticTypeを保持する。引数CaptureはDiagnosticTypeに加えてキャッシュ済み静的generic DiagnosticFields callbackを保持するが、入力struct、boxingやinstance closureは保持しない。CaptureとCopyではcallbackを呼ばず、最初の明示GetDiagnosticFieldsで`ArgumentDiagnosticCache<T>`を初期化し、T.TransferFieldsを直接静的dispatchする。`Fields<T>(in T)`も同じ経路を使うため、Browser／NativeAOTでも生成metadataの取得にDynamicMethodやMakeGenericMethodを必要としない。ClassDiagnosticSourceとGetTransferFieldsは削除する。明示診断を要求しない転送ではフィールド走査、診断table初期化や実行時コード生成を実行しない。Typeだけで任意の未知Tを調べるreflectionとraw unmanagedの反射field fallbackは動的コードが必要であり、AOTでは利用できず空の診断を返す。動的コードを使えるJITでは、default TransferFieldsが空の手書きunmanaged IShaderArgumentsについて反射field fallbackを維持する。静的metadataのない管理参照入り外側structは空の診断を返し、その管理配置を数値転送しない。動的コードがない環境でも通常構造体の直接転送と名前・サイズの診断は可能で、利用できないraw field offsetの診断だけを省略する。シェーダーのスキーマから求めた要素間隔でC#の要素間隔を上書きしない。

シェーダー書き込みはIShaderValueWriterのWriteValueとWriteReferenceへ統一する。数値だけの値は、パディングを含む全領域を一回のWriteValueで渡す。数値メンバー別のwriter、WriteWire、取得済みsnapshotを詰め直すPackは提供しない。参照のある場合の処理順は「値だけを渡すwriterとbackend固有metadata」の契約に従う。参照の種類は参照先リソースとArgumentTableの登録から解決し、シェーダーの名前やパスから実行時のアクセス種別を決めない。

ルート引数の論理wireは、バックエンド固有の構造体パッキングではなく、移植可能なuint4単位のバイト列として扱う。シェーダーは呼び出しごとに一度、そのバイト列を自然なスカラー配置で再解釈して論理的なルート値を初期化する。コンパイラーの通常の識別子スコープを維持し、ルート名のトークン置換は行わない。リフレクションには論理スキーマを保持する。uint4宣言は16バイト単位へ切り上げるが、Loadはceil(sizeof(T)/4)個の4-byte wordだけを読み、native constantsの容量は4-byte単位とする。backend-private uniform emulationの容量整列はbackendに任せ、TransferSize、数値のオフセット、取得済みのバイト列、シェーダーデータの要素間隔は変更しない。

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
     public interface IShaderArguments{
+        static abstract int TransferSize { get; }
+        // 物理16-byte参照slot数。重複resourceも数え、数値だけなら0。
+        static abstract int ReferenceCount { get; }
+        static virtual IReadOnlyList<ShaderTransferField> TransferFields => [];
         public static ShaderValueSnapshot Capture<T>(in T value)
             where T : struct, IShaderArguments;
     }
+    public static class ShaderTransfer
+    {
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

### デバイスごとの任意の検証

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

ValidationはAbstractionsとSharedの純粋なhelperだけを参照し、状態を持つ検証engineとIValidatedShaderReferenceを新namespaceへ移す。Sharedの読み取り専用依存queryと収集helperは残すが、共通のmutation APIは追加しない。rawにはdriver API、host span bounds、overflow、mappingのmemory safety、ABI offset／physical Count、ReferenceCount、root／program状態、artifact容量、native失敗時の所有権に必要な検査を必須として残す。

すべての子資源をwrapped deviceから作る。raw／typed buffer、shader-data buffer、texture／view、sampler、argument table／GpuRef、shader／pipeline、command／render・compute encoder、queue／submission／semaphore、surface／swapchain／frameを包み、desc内のwrapperもbackendの具象cast前にunwrapする。domain内のraw objectは一つのcanonical wrapperに対応付ける。GetElementも論理参照を返し、native element adapterの具象型を漏らさない。raw子資源の後付け採用、別domain、rawとの混在は拒否する。raw参照やreflectionで外へ出る操作は診断を迂回するため利用者責任であり、security sandboxではない。

IGraphicDeviceにはsurface factoryがない。同じbackendの固有factoryで新規rawSurfaceを作り、usingで保持し、using var surface = validated.WrapSurface(rawSurface)で借用する。取得済みの履歴は取り込まず、surface wrapperをDisposeしてもrawSurfaceをnative Disposeしない。frame imageは借用で直接Disposeしない。viewを先に解放し、明示的にframe.WaitForReleaseAsyncを待ってframeをDisposeする。この待機はsubmission完了ではなく、GPU submissionは別途待機する。

ValidationDevice.Disposeは所有する子資源が先に解放されていることを要求し、backendをDisposeしない。DeviceLostのsenderはwrapperへ置き換え、Dispose時に購読解除する。利用者は外部で操作を直列化する。暗黙GPU待機、pin、lock、静的global dictionaryや解放済み資源の永久保持は設けない。

検出対象は登録identity、記録中／提出済みresourceの寿命、親子関係、mapping競合、byte範囲別bufferとmip／layer別textureのアクセスである。shader、index、indirect、copy、attachmentとbarrierのBefore／After scopeを照合し、登録の置換・Releaseを記録時とSubmit前に検査する。GPU内の動的indexは登録範囲として保守的に扱い、shaderの実アクセス、直接変更したstaging byte、外部native操作は観測できない。

記録とqueue／presentationはnative前に事前照合し、成功後だけ診断状態をcommitする。Submit事前照合はqueue状態の複製へ指定順に適用するが、metadata identityとsource epochは複製しない。不透明なnative Submit例外はwait／execute／signalの部分発行を否定できないため、元例外と使用した可能性のあるoperandを保持し、queueを未知状態として隔離する。健全queueの安全な回復APIは公開せず、明示的なbackend device-loss retirementだけが未知使用を退役させる。これはdevice所有権の取得や自動Disposeではなく、汎用の新通知APIも設けない。Windows D3D12のnative失敗時のfence owner保持は任意診断とは独立した正しさとして残す。

binary semaphoreはAcquire、Submit、Presentで同じsignal／wait履歴を使い、native完了境界まで使用状態を保持する。取得画像にはacquire完了または適切なstageのsemaphore待機が必要で、in-flight画像のPresentには生成元submissionのsemaphoreまたは観測済みGPU完了を要求する。キャンセルされた待機と早期Disposeは完了ではない。native Acquire成功後にframe wrapper構築だけが失敗した場合、raw frameのDispose成功でも発行済みsemaphore signal状態は残り、再利用前に明示的なwaitで消費する。rollback cleanupにも失敗した場合はraw leaseを保持・隔離する。不透明なacquire例外はswapchainを隔離し得るが、根拠のないsemaphore消費を作らない。

GPU copy metadataもcommand内の順序とSubmit時のsource identityを照合し、native copy記録とSubmitが成功した場合だけ公開する。数値履歴、隠れたreadbackや暗黙待機は持たない。WebGPUの非同期nativeエラーとdevice lossは結果として伝達し、既に実行したGPU操作をrollbackしたとは扱わない。

### 参照ABI

全バックエンドで16バイトの参照スロットを共有するが、既存の符号化方法は維持する。

- Vulkan: バッファの物理デバイスアドレス。
- WgpuとBrowser: 論理登録スロットと、登録範囲を基準としたバイトオフセット。
- Direct3D12: ディスクリプターの識別情報と、バッファ内の絶対バイトオフセット。

参照は不透明で、所有権を持たず、型付きのままとする。参照や引数値を保持してもGPU登録の所有権は取得しない。参照を含むシェーダーデータバッファには、生の別名参照やシェーダーからの書き込み経路を追加しない。通常のunmanagedバッファでは既存の生バッファの用途・同期契約を維持する。

WgpuとBrowserの読み取り専用・書き込み可能poolとremapの各laneは登録のアクセス種別から選択する。同じバッファ範囲を両poolへ登録した場合も、各スロットの選択したlaneだけを設定する。nativeの同時使用・エイリアス制約は別途適用される。Vulkanの物理アドレス、Direct3D12の絶対オフセットの符号化は変更しない。Direct3D12のSRVまたはUAVも登録の選択に従う。シェーダーの参照種別・対象型との一致は検証層だけが診断し、Debug／Releaseともraw転送やbinding生成はシェーダー文字列やフィールドパスで分類しない。

### SetArgumentsのbackend所有payload

通常のunmanaged overloadとIShaderArguments overloadは、Set時点の値を同期的に固定する。Vulkan／D3D12はceil(wireSize/4) wordの有界stack spanをゼロクリアし、backendのref struct writerが直接書く。数値のCPU heap配列やArrayPool rental、pack・padding用の中間配列は作成・保持しない。末尾1／3 byteを含むpaddingはゼロとする。一時stackは最終payload ownerではなく、native command記録後の実際のコピーはdriver command bufferが所有する。明示CaptureとShaderValueSnapshotは独立した読み取り専用CPU検査のまま維持し、Set／upload入力へ戻さない。

Setではwriter.Complete、任意の配置診断、参照準備hook、不変metadata生成をすべて完了してから、VulkanPushConstants.EmitによるvkCmdPushConstants、またはD3D12のcommands_root_constantsによるSetGraphicsRoot32BitConstants／SetComputeRoot32BitConstantsを呼び、Setが戻る前に記録する。新しい選択はnative呼び出し後に公開する。host検査、codec、診断や準備失敗ではnative定数を発行せず、前回成功した選択を保つ。ただし既に発行したvoid driver commandはrollbackできず、device failureを含むnative失敗からの原子的復元は保証しない。

Vulkan／D3D12のrender／compute全四encoderはcodecとprepare中を_settingArgumentsで保護し、入れ子のSet、pipeline変更、End、Draw／Dispatchなどの再入を拒否する。Graph準備のtable自動選択に必要なSetArgumentTableだけは許可する。callback後は、捕捉したpipelineのValidateAlive、owner.ValidatePassとpipeline identity一致をすべてnative発行より前に再検査する。raw command bufferやpipelineのDisposeがguardを迂回しても、解放済みhandleを使うnative発行を阻止する。host失敗では旧root選択を保持するが、呼び出し側が明示したresource Dispose、table自動選択や発行済みdriver commandは取り消さない。

Draw／Dispatchはnumeric rootを再送しない。選択table全体からbindingを構築し、推移的依存とGraph宣言の照合は検証ONだけで行う。Wgpu／Browserの非公開uniform ownerと機能上必要なbinding／remapは維持する。

VulkanDevice.MaxPushConstantsSizeはphysical deviceから取得するpublic容量queryである。Radeon8060Sで確認した256 byteを他deviceに一般化しない。push rangeはbind pointごとにoffset 0、size MaxPushConstantsSizeへ固定し、computeの全pipeline variantはCompute、graphics／partialの全variantはVertex|Fragmentを宣言する。Setのstage maskは対応rangeと完全一致し、compute／graphicsのnative push状態を分離する。全三stageの重複rangeに対して一部stageだけを更新する設計にはしない。descriptor visibilityはこれと独立にVertex|Fragment|Computeを許容する。この固定契約はpush rangeに限り、descriptor数などが異なるpipeline layout全体のsingleton化を意味しない。実際のpush量は論理wireの4-byte切り上げだけで、uint4宣言のpaddingとは区別する。Setには物理容量に加えて64 KiBのbackend host stack guardを適用し、異常に大きい物理上限でも無制限stack確保やheap fallbackを行わない。公開する物理上限やlayoutのrangeは変更しない。

D3D12はdevice初期化時、Setより前にthread-safe cacheで一つの固定root signatureを生成し、全graphics／compute／partial pipelineで共有する。Setごとのroot signature生成は行わない。Commandsはgraphics／computeのroot pointer状態を別々に追跡し、同じpointerならbindを省く。通常のPSO／SetProgramも同じsignatureを使うためDraw時のSetPipelineRootSignature相当の再設定で定数をresetしない。このsignatureは64 DWORD予算を使う。resource tableとsampler tableは各1、parameter 3のdraw infoは2、parameter 4のvertex root SRVは2で、固定費は6。app rootはparameter 0、register b8 space0の58 DWORD（232 byte）とし、native static_assertで合計64を確認する。Direct3D12Device.RootConstantDwordCapacity／RootConstantByteCapacityで容量を公開する。shaderの論理constant buffer宣言は維持するが、native parameterはCBVではなく32BIT_CONSTANTSである。232-byte wireのuint4宣言は240 byteでも、Loadは58 DWORDだけを読み60 DWORDを要求しない。

artifactの容量事前検査は任意validationの追跡ではなく、常時必須のABI安全性検査とする。VulkanShaderは所有deviceのMaxPushConstantsSizeを用い、native shader module生成前に拒否する。D3D12はgraphics、computeとpartialの全pipeline経路でnative pipeline生成前に拒否する。どちらも全app ConstantBufferを実際のroot名でShaderDataLayout.Root(target, name).Sizeから取得した論理sizeで検査し、lumyteDrawInfoと__lumyte_schema_の項目を除外する。uint4宣言のpadding容量は判定に使わない。Set時の容量超過もpayload allocationと新選択の公開前に拒否し、前回成功した選択を維持する。shader metadataのlumyteRootStorageはtarget別にVkPushConstants／D3DRoot32BitConstants／UniformBufferを記録し、旧Vulkan uniform artifactは再compileを要求する。shader emitterは本質的にtarget-awareだが、共通の状態・object ownership・metadata field・Graph lifetimeはuniform非依存とする。共通コードのWgpu／Browser判定によるfeature dispatchやInternalsVisibleToは加えない。検証用decoratorは独立assemblyとする。Windows native D3D12はこのLinux環境で実行しておらず、native compilerも未導入である。source検査をnative実行成功として扱わない。

ValidationDevice上のRenderGraphだけが検証層のIRenderEncoder／IComputeEncoderへ一時的な参照準備callbackを設定し、通常のgeneric SetArgumentsを呼ぶ。既存のencoder interfaceを使い、numeric snapshotを介さない。prepare hookは検証層のencoderだけが保持し、raw encoderへ非nullを渡す操作は未対応とする。ShaderBindingDependencies.CollectはShaderRootArgumentsを受け、ArgumentsとRootParameterおよび依存metadataだけを収集する。内部の参照走査はprivateなVisitReferencesで行い、数値snapshotを取得しない。ShaderValueSnapshotを受けるoverloadとnumeric CPU Rootは削除し、隠れたreadbackも行わない。ShaderResourcesのunmanaged Draw／DrawIndexed／Dispatchもtable選択後にgeneric SetArgumentsへ渡し、helperでCaptureしない。IRenderEncoderとIComputeEncoderのunmanaged SetArgumentsは実装必須とし、Captureするdefault実装は設けない。ModelVertexの明示的な配置検証はUnsafe.SizeOfとShaderTransfer.Fieldsを用い、Captureによるprobeを作らない。

### 値だけを渡すwriterとbackend固有metadata

公開転送interfaceのwriter制約は次のように変更する。比較元は基準コミットのsrc/Graphics/Lumyte.Graphics.Abstractions/IShaderArguments.csにあるWriteである。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IShaderArguments
     {
         void Write<TWriter>(ref TWriter writer)
-            where TWriter : IShaderValueWriter;
+            where TWriter : IShaderValueWriter, allows ref struct;
     }
 }
```

このanti-constraintにより、`Span<byte>`を保持するVulkan／D3D12のref struct writerをboxingせず渡す。手書き実装と生成型にも同じanti-constraintが必要で、C# 13以降と固定.NET 10 SDKを使用する。公開writerのメソッド数・値引数数・offsetなしの契約は変えない。

IShaderValueWriterは`WriteValue(ReadOnlySpan<byte> value)`と`WriteReference<T>(IGpuRef<T>? value)`の二つだけを必須の抽象メソッドとして持つ。各呼び出しの引数は値一つだけで、offset、size、path、seek、skip、WriteAtは公開しない。IShaderWireSink、IShaderReferenceCursor、IShaderReferenceEmitter、ShaderWire、ShaderWireCursorとWriteWireは削除し、互換aliasを残さない。以前のcursorとemitterを必須とする判断を、この平坦なwriter契約へ置き換える。

生成codecは非公開unmanaged wire全体をパディング込みでローカルへコピーし、raw prefixをWriteValue、参照をWriteReference、次のraw区間、最後のraw tailという物理順で渡す。数値だけの型は全領域を一回のWriteValueで渡す。数値のfield別シリアライズや全wireの転送後の参照差し替え、Completeでの参照再走査は行わない。source spanとin参照は同期呼び出し外へ保持せず、destinationとaliasさせない。

生成型のprivate static planは実際のC#オフセットによる参照順序とスロット妥当性のためだけに使う。任意診断のreflectionや数値field tableとは独立し、範囲外、負値、重複、交差をgetterの呼び出し前に拒否する。field、auto-propertyとcustom getterも同じ経路を使い、wireローカルコピーの後に各参照getterを物理順で一回だけ評価する。生成Writeはreadonlyであり、wireコピー後の数値変更は今回のraw bytesへ反映しない。readonlyでない利用者getterは外側structの防御的コピー上で実行され得るため、副作用が後続getterや元の値へ伝播するとは保証しない。emitter structは生成しない。

検証層の`ValidationArguments<T>`は静的RootParameter、ShaderTypeName、TransferSize、ReferenceCount、TransferFieldsを元のTへforwardする。ValidationWriterはnative writerを包み、利用者のcodecとgetterを一回だけ実行し、論理`IGpuRef<T>`を必要なnative element adapterへunwrapしてstreaming転送する。診断用adapter配列のコピーは許容するが、Capture／Snapshotの数値中間入力を作らない。Graph prepareはadapter内でnative発行より前に実行し、失敗時は旧rootを保持する。raw helperによるcodec呼び出しはこのadapterを通じるだけであり、二度目の利用者codec実行ではない。

VulkanShaderValueWriter、Direct3D12ShaderValueWriter、WgpuShaderValueWriter、BrowserShaderValueWriterを、それぞれのbackend内部でIShaderValueWriterとして直接実装する。各backendのShaderTransfer helperがSetと型付きCopyFromの両方でvalue.Write(ref writer)を呼ぶ。各writerが暗黙の現在位置とexpectedTransferSizeを所有し、raw区間をVulkan／D3D12の`Span<byte>`、Wgpu／Browserの非公開具象RootStorage.Writeまたは検査済みupload batch viewへ直接渡す。Sharedのwriterから参照encoder callbackを呼ぶ構造にはしない。WriteValueは渡された長さ、WriteReferenceは必ず16バイトだけ位置を進め、参照はその場でbackendの表現へ符号化する。残容量の検査はlength > expectedTransferSize - positionという減算形式でoverflowを避ける。Completeは一回だけ成功でき、position == expectedTransferSizeを必須とする。短い出力も長い出力も公開しない。

IShaderReferenceMetadataはReferencesという依存identityの列挙だけを公開する。検証層上のGraph・依存収集・検証層の非公開copy overlayはwire layoutを知らずにこの列挙を使う。検証層のprivate immutable metadata ownerはShaderValueの共有list、数値bytes、wire offsetや元の値objectを保持しない。登録slot／range／accessはbindingに必要なIShaderReferenceのidentityとして残り、C# wire内の位置とは区別する。

CopyFromは失敗時の部分byte更新を許可し、検証ONのbuffer全体のmetadataを無効にして元の例外を返す。手書きcodecにも現在位置だけの同じwriterを使い、参照の物理位置は前後のraw区間の長さで表現する。生成planの検査とwriterの容量・完了検査は別の責務である。参照の一時領域はゼロクリアしたstackalloc byte[16]とし、native参照ごとのShaderValue、new byte[16]やFuncによる符号化をなくす。検証層の任意診断にはValidateReference(Type valueType, object? reference, int offset)へ型・参照・現在位置を直接渡す。明示CaptureのShaderSnapshotWriterが論理記録を作る経路とは分離する。検証層の追跡・prepare時の参照配列とimmutable metadata owner、Wgpu／Browserのpayload allocation、delegateやnative buffer handle、実行ごとのbinding／descriptor heap確保は残り、zero allocationやzero GPU copyとは主張しない。Browserのinterop span-copyも残る。

16-byte ABIは従来の予約ゼロを含めて維持する。Vulkanはbufferの物理addressに絶対byte offsetを加えた64-bit値、またはtexture／samplerの32-bit slotを置き、byte 8にchecked uintのcountを置く。内部の型付きIVulkanShaderBuffer.ShaderAddressから保存済みaddressを読み、object型ShaderHandleのboxingを避ける。D3D12はslot、checked uintの絶対byte offset、checked uintのcount、予約ゼロとする。Wgpu／Browserはslot、checked uintの登録相対byte offset、checked uintのcount、予約ゼロとし、相対offsetやcountの切り詰めを許さない。

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

### 厳密な参照数とmapped batchの契約

IShaderArgumentsのstatic abstract ReferenceCountは重複も数えた物理16-byte slot数です。backend writerは全modeで0 <= N <= wireSize / 16、WriteReferenceの超過とCompleteの不足、位置・容量・型・範囲を検査します。rootの直接参照配列とprepare hookは検証層だけが保持します。raw backendはscalar countと単一の暗黙table hintだけを保持し、非nullのSetArgumentValidationは未対応として拒否します。数値だけの追跡rootは共有empty metadataであり、未収集のnullとは区別します。

ShaderRootArgumentsはstorageを持たない不変metadata classです。Referencesはnullableでnullは未収集を表し、Createにprepareを渡す場合は利用可能なmetadataが必須です。rootの単一table hintは自動選択を補助しますが、明示SelectedTableが優先します。異なるtableが混在すればnative Set発行前に拒否します。

native CopyFromはbatch全体のmapped spanを一度だけ範囲・mapping方向・overflow検査し、row sliceへ直接書きます。Browserは一つの検査済みUint8Array viewへJS.setでコピーし、finallyでviewだけを解放します。検証層だけがHostWriteLeaseで再入書き込み・Map／Unmap／Disposeを診断します。失敗時は部分byte更新を許し、追跡buffer全体を無効にして元例外を保持します。OFFでは利用者がmappingと寿命・並行操作を保証し、追加lock・待機・pinは設けず、無効pointerを実行する試験もしません。

VulkanとD3D12はbuffer作成時の保存済みaddressへchecked offsetを加え、参照ごとのdriver queryやglobal cacheを作りません。native root即時定数、固定signature／stage、pipeline・End・callback後の再検査は継続します。残るbinding確保やBrowserコピーを含めzero allocation／zero copyは保証しません。この設計だけから性能向上の実測結果は主張しません。

### Graphの参照準備と実行時admission

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

### 選択table全体のbindingと任意の依存検証

bindingは選択したArgumentTableの書き込み済み登録すべてから構築します。capacityの未登録holeはentryではありません。Vulkanは全texture／sampler登録をdescriptorへ投影し、buffer参照は保存済みGPU addressへoffsetを加えてinline符号化します。Direct3D12は全texture／sampler／ReadOnly SRV／ReadWrite UAV登録を投影し、各上限128／16／64／64を適用します。Wgpu／Browserは全登録から有限のbinding poolとremapを構築し、WGSLの個数はcapacityではなく実際の書き込み済み登録を基にします。native binding数・storage範囲・remap容量へ収まらなければ明確に拒否し、巨大な無効binding、追加feature・package・pool・cacheで回避しません。rootなしのshaderも明示選択tableの全登録をbindし、Wgpuは未使用のroot uniform binding 8を省略します。

全登録をbinding可能にすることは、全登録をGPU使用済みとして記録することではありません。backend-privateな登録・remapは検証OFFでも必要な機能状態ですが、同期の所有権や使用hazardを追加しません。resource寿命とCPU／GPU同期は利用者が管理します。

Graphのbarrier・依存順・cullingは、必要なresourceの正確で完全なpass宣言だけを使い、従来どおりallocation全体を保守的に同期します。検証OFFでは宣言の真実性・完全性が利用者の事前条件です。root走査、依存query、準備hook、許可集合、admission callbackの確保を行いません。table共有だけでは使用宣言になりません。

検証ONの場合だけ、Graphはrootとbuffer全体の推移的依存を、buffer allocationまたはtextureの元imageを正規化したresource object identityと宣言read／write権限の合併で照合します。native handle値の衝突判定、要素・byte範囲・view・callback取得元による許可判定は行いません。同じbufferの別宣言範囲から保存した参照も許可されますが、未宣言の別resourceとread-only宣言に対するReadWrite参照は拒否します。

GetGpuRefの具体化はこの粗い許可判定とは別です。要求offset／countと一致する登録を選び、単一要素なら既存登録からGetElementで導出できます。未登録の複数要素範囲に、より広いCountの参照を返しません。新しいslice APIや自動dirty trackingは追加しません。

### buffer全体の読み取り専用依存query

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

raw backendのTryGetDependenciesは、数値だけのReferenceCount = 0を含めて常にfalse／nullを返します。これは追跡利用不可であり、既知の空集合ではありません。登録・remapなどの非公開機能状態は残ります。ValidationDeviceのbuffer wrapperはbuffer全体の不変な依存unionを返し、ReferenceCount = 0は既知の空集合としてtrueです。参照付きbufferの未初期化・失敗後の状態は無効で、queryはInvalidOperationExceptionとなり、buffer全体の明示再転送で回復します。部分転送だけでは回復しません。

既知状態への部分更新は旧依存とのunionを保持でき、buffer寿命内で保守的な偽陽性が残ります。全体書き直しは現在のunionを置き換えられますが、記録済みcommandが保持する旧revisionは不変です。要素ごとの配列や毎drawの行走査は不要で、検証ONのShaderBindingDependencies.Buffersが各bufferを一回だけ訪問します。

CPU CopyFromの依存更新は検証層が所有します。GPU copyも検証層のbuffer単位の非公開command overlayを使い、native copy記録成功後に更新し、queue Submit成功後にだけ公開します。失敗・未送信・破棄したcommandから公開せず、隠れたreadback・待機・数値履歴も持ちません。

### 手書き実装と第三者backendの移行

手書きIShaderArguments.Writeもunmanaged wireのローカルコピーからraw区間をWriteValueへ渡し、必要な位置で型付きWriteReferenceを呼ぶ。参照なしなら全wireへ一回だけWriteValueを呼ぶ。第三者writerはこの二つのメソッドを実装し、暗黙位置、16-byte ABI、容量とCompleteの一致検査、同期借用と非alias性を守る。能力判定、既定のthrow実装、cursor／emitter adapterは提供しない。

明示的なCPU検査のShaderSnapshotWriterもサイズをconstructorで受け、同じ二つのメソッドを実装する。raw bytesを独立領域へコピーし、参照slotはゼロで保存して論理参照identityを記録する。Captureは読み取り・検査専用で、snapshotをnativeへ戻す書き込み口ではない。SetArguments(ShaderValueSnapshot)、CreateSnapshot、SharedPackRaw、ShaderDataLayout.PackとIShaderDataLayout.Packは削除したままとする。Fields、任意のmanaged field reflectionと静的metadata取得補助も配置診断専用である。shader生成・pipeline生成で必要なartifact reflectionや容量事前検査を取り除く意味ではない。

手書きIShaderDataSourceは要素ごとのRead／SetTransferredValueからbuffer全体のTryGetDependenciesへ移行し、追跡不可ならoutをnullにしてfalseを返す。共通のmetadata書き込みAPIを追加せず、rawは常にfalse／nullを返し、CPU依存更新とGPU copy metadata公開は検証層が所有する。第三者encoderは両generic SetArgumentsと上記SetArgumentValidationを既存interfaceで実装する。ShaderBindingDependencies.CollectはShaderRootArgumentsの依存metadataだけを収集し、numeric CPU Rootやsnapshot入力を持たない。通常のunmanaged bufferの直接memcpy、primitive buffer API、GPU copy commandと無関係なコンパイル経路は変更しない。この契約自体は新しい性能測定を意味しない。

### 値の取得・転送・寿命

Captureは呼び出し時点の独立したCPU値snapshotを作る明示検査であり、native Set／upload入力にはしない。SetArgumentsはSet時点の値をbackendへ直接固定する。CopyFromは現在のC# wire bytesを同期転送し、元structの後続編集には次の明示転送が必要となる。upload元はGPU消費まで利用者が変更・解放せず、map／unmap、barrier、copy、Submit、完了待機、再利用を明示管理する。

CopyFrom失敗時はbyteの部分更新を許し、検証ONのbuffer全体の依存を無効にする。全体再転送でのみ回復し、数値履歴やrollback、隠れたreadbackは提供しない。buffer全体の依存queryと成功Submit後の公開は前節に従う。

参照は登録のidentityと選択accessを保持し、GetElementでも同じ登録を使う。ReadOnlyにはShaderRead、ReadWriteにはShaderWriteの能力を登録時に要求する。参照付きShaderDataSliceは読み取り専用である。置換・Release後の古い参照は使用できず、検証ONだけで登録identityの失効を診断する。

### 検証方針

writerの二メソッド・各一引数、getter前のwireコピーと物理順一回評価、厳密なReferenceCount、ABIの予約ゼロ、容量・完了検査を受け入れ条件とする。mapped batchの一回検査と保存済みaddressの使用、native root即時記録、固定stage／signature、再入・callback後の再検査を維持する。

全backendでrootなしでも選択tableの全登録をbinding可能にし、未登録holeをentryに数えず、有限native binding／storage／remap上限を明確に拒否する。全登録を使用hazardとして記録しない。

依存queryはOFFで数値型を含めfalse／null、ONの参照ゼロ型でtrue／既知空とする。未初期化・失敗後・部分更新・全体回復、旧revisionの不変性、buffer一回の循環探索、native copy記録とSubmit成功後だけの公開を確認する。GraphはOFFでroot走査・query・hook・許可集合・callbackを作らず、ONで正規化allocation／image identityとread／write合併だけを照合する。同allocationの別範囲の保存参照を許可し、別resourceとread-onlyからのReadWriteを拒否する。GetGpuRefは要求offset／countと完全一致するか単一GetElementを使い、広いCountへ置換しない。

struct制約、partial property、defaultのnull参照拒否、明示Captureの独立性も確認する。OFFの無効pointerを実行する試験は行わない。GPU・Browser・D3D12、native validation layer、AOT publication／runtimeを区別し、source確認やbuildだけを実行成功とは扱わない。

## 検討した代替案

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
