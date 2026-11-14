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

追加memberを持たない旧IShaderData markerは削除し、IShaderArgumentsだけを転送契約とする。互換aliasは残さない。同じIShaderArguments実装classを、SetArgumentsのrootとIGraphicsShaderDataBufferの要素の両方で使用できる。shader-data buffer／slice／factory／Graph／resource宣言の制約もIShaderArgumentsへ統一する。手書きcodecと生成型は同じ必須の値だけを渡すwriter契約を使う。通常のunmanaged型はこのinterfaceを実装せず、従来どおりraw buffer overloadを選ぶ。数値のfield別変換、snapshot書き込みの復活、GPU copy／所有権や同期契約の変更は行わない。

## 決定

C#のunmanaged構造体の配置を正とし、数値を一括コピーする。参照を含む引数は非公開の格納用構造体を持つクラスへ移し、生成コードが参照だけを解決する。数値の再構築をなくし、格納用構造体は通常の利用側APIに露出させない。

### 利用者の変更前・変更後

変更前は、利用側で参照を含む引数構造体を宣言し、数値メンバーを個別に書き出していた。変更後は、数値だけの通常のunmanaged構造体を、インターフェース、partial宣言、ジェネレーター、登録なしでそのまま転送する。参照を含む引数は、非公開のunmanaged格納領域と通常の公開プロパティを持つpartialクラスにする。値の取得、明示的なコピー、送信、完了待機、解放の境界は変更しない。

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
public sealed partial class DrawArguments : IShaderArguments
{
    private TransferData _data;
    public partial Matrix4x4 ViewProjection { get; set; }
    public IGpuRef<MaterialData> Material { get; set; } = null!;

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

### 転送形式と生成コードの契約

C#のunmanaged構造体の定義を転送ABIの正とする。値の取得時には、実際のパディングを含むメモリ表現全体を一括コピーする。数値メンバーの個別シリアライズ、並べ替え、行列の転置、型変換、リフレクションのオフセットに合わせた再配置は行わない。入れ子の構造体とMatrix4x4も元のバイト列を維持する。シェーダー側はC#のフィールド順、オフセット、パッキング、要素間隔を一致させる。どちらかの変更もABIの変更となる。

通常の数値データにはIShaderArguments不要である。`CreateShaderDataBufferのunmanagedオーバーロードは既存のIGraphicsBuffer<T>`を返し、`BufferSlice<T>`、`BufferBarrierDesc<T>`、CopyBuffer、WriteBufferとRenderGraphの通常のバッファ経路を使う。RenderGraph.CreateShaderDataBufferにもunmanagedオーバーロードを用意し、`RenderGraphBuffer<T>`を返す。`Lumyte.Resources.Graphicsでは既存のGraphicsBufferDefinition<T>`と生バッファのインポートをそのまま利用する。参照依存メタデータがないため、要素ごとの取得・符号化は行わず`ReadOnlySpan<T>`をそのままコピーする。既存のインターフェースを明示実装する数値型も同じ必須の値だけを渡すwriter経路で使用できる。IGpuRefを含む型はpartialクラスにする。数値データは1つの非公開unmanaged構造体に保持し、利用側には通常の読み書き可能なプロパティを提供する。.NET 10のpartialプロパティはジェネレーターで実装できる。利用者が定義する格納用構造体には、参照プロパティと同名の16バイトのShaderReferenceSlotフィールドも配置する。ジェネレーターは非公開の格納領域をローカルへ一括コピーし、参照の識別情報をC#側のスロット位置で同期符号化する。管理オブジェクト自体のメモリは転送しない。

通常の構造体は`Unsafe.SizeOf<T>()`で、生成対象のクラスは静的なTransferSizeで、バッファの要素間隔と割り当てサイズを決める。バッファ生成時に値のインスタンスを生成したり、default(T)から値を取得したりしない。生成型の静的なTransferFieldsは、任意の診断に使う実際の管理メモリ上のオフセットを表す。通常の構造体では検証を明示的に有効にした場合に限ってフィールドのリフレクションと実際の管理オフセットを求めるコードを型ごとに一度実行してキャッシュする。取得済みsnapshotは型またはinstanceの配置providerだけを保持し、通常の取得で診断delegateやfield tableを生成しない。生成型の診断情報も初めて明示的に要求したときだけ初期化する。無効時にはフィールド走査も実行時コード生成も実行しない。動的コード生成ができないBrowserやAOTでも、通常構造体を直接転送できる。その場合は名前とサイズを診断し、取得できないフィールドオフセットの診断だけを省略する。生成型の静的な配置情報はその環境でも使用する。シェーダーのスキーマから求めた要素間隔でC#の要素間隔を上書きしない。

シェーダー書き込みはIShaderValueWriterのWriteValueとWriteReferenceへ統一する。数値だけの値は、パディングを含む全領域を一回のWriteValueで渡す。数値メンバー別のwriter、WriteWire、取得済みsnapshotを詰め直すPackは提供しない。参照のある場合の処理順は「値だけを渡すwriterとbackend固有metadata」の契約に従う。参照の種類は参照先リソースとArgumentTableの登録から解決し、シェーダーの名前やパスから実行時のアクセス種別を決めない。

ルート引数のuniform領域は、バックエンド固有の構造体パッキングではなく、移植可能なuint4単位のバイト列として扱う。シェーダーは呼び出しごとに一度、そのバイト列を自然なスカラー配置で再解釈して論理的なルート値を初期化する。コンパイラーの通常の識別子スコープを維持し、ルート名のトークン置換は行わない。リフレクションには論理スキーマを保持する。物理的なuniform領域の割り当て・バインド容量は16バイト単位で切り上げるが、TransferSize、数値のオフセット、取得済みのバイト列、シェーダーデータの要素間隔は変更しない。

### 公開APIの差分

比較元は基準コミットのsrc/Graphics/Lumyte.Graphics.AbstractionsにあるIShaderArguments、IShaderValueWriter、IGraphicsShaderDataBufferとする。以下は変更する主要宣言の抜粋である。数値の格納型は引数クラスの非公開実装であり、利用側の公開シグネチャに追加しない。

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
     public interface IShaderArguments
     {
+        static abstract int TransferSize { get; }
+        static virtual IReadOnlyList<ShaderTransferField> TransferFields => [];
         public static ShaderValueSnapshot Capture<T>(in T value)
-            where T : struct, IShaderArguments;
+            where T : IShaderArguments;
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
-            where T : struct, IShaderArguments;
+            where T : IShaderArguments;
+        // 実装必須。Captureへ委譲するdefault実装は提供しない。
+        void SetArguments<T>(in T arguments, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
     public interface IComputeEncoder
     {
         void SetArguments<T>(in T arguments)
-            where T : struct, IShaderArguments;
+            where T : IShaderArguments;
+        void SetArguments<T>(in T arguments, string rootParameter = "arguments",
+            string? shaderTypeName = null) where T : unmanaged;
     }
+    // 非公開の格納用構造体で使う16バイトの参照領域。
+    [StructLayout(LayoutKind.Sequential, Size = 16, Pack = 4)]
+    public struct ShaderReferenceSlot { }
+    // C#の配置を任意の診断へ渡す。GPUアドレスは含まない。
+    public sealed record ShaderTransferField(
+        string Path, int Offset, int Size, Type ValueType, bool IsReference = false);
-    public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : struct, IShaderArguments
+    public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : IShaderArguments
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

DebugとReleaseは同じ実行契約とし、検証は既定で無効にする。backendの実deviceを生成した直後、子資源を作る前にEnableValidation()を呼び出す場合だけ、有効期間・同期・配置の診断を追加する。D3D12のEnableDebugLayerは別のnative設定として維持する。

変更は過去のGRAPHICS-0014の任意モードを追加しない判断と追跡の除去だけに限定する。生存期間と同期を利用者が明示管理する契約は継続する。子資源の作成後に初めてEnableValidation()を呼ぶとInvalidOperationExceptionとなる。無効時はresource作成済みのboolだけをdeviceに保持し、診断trackerを生成せず、診断callbackの記録、状態表の走査、配置診断のためのリフレクションを行わない。ホストspanの範囲・overflow・参照ABI長・native失敗の処理と、実際のbinding生成に必要なmetadataは保持する。HasRootなどの必要なbinding探索とschema診断のcacheを分離し、無効時にroot／dataの診断dictionaryも生成しない。

変更前はdevice生成後に構成による暗黙の検査を受けた。変更後は次のように指定する。

```csharp
using var device = VulkanDevice.Create(); // 既定では無効
device.EnableValidation(); // 診断が必要な場合だけ、子資源を作る前に指定
```

他のbackendの実deviceも同名のAPIを持ち、別deviceへforwardしない。

比較元は基準コミットの各backend deviceであり、4型とも次の追加を行う。

```diff
 namespace Lumyte.Graphics.Vulkan;
 public sealed class VulkanDevice
 {
+    // 既定null。診断状態の有無を示す。
+    public GraphicsValidation? Validation { get; private set; }
+    // 子資源の生成前だけに呼ぶ。Debug／Release共通。
+    public void EnableValidation();
 }
```

WgpuDevice、BrowserDevice、Direct3D12Deviceも同じメンバーを追加する。上位のModelMeshとRenderGraphも同じdeviceの明示指定に従い、配置診断を暗黙に再有効化しない。

```diff
 namespace Lumyte.Graphics.Abstractions;
 public interface IGraphicDevice
 {
+    // 任意検証の状態。第三者backendでは既定false。
+    bool ValidationEnabled => false;
 }
```

検出対象は登録identity、記録中／提出済みresourceの寿命、device・surface・swapchain・frame・texture・viewとshader・pipelineの関係、CPU mappingとGPU使用の競合である。draw／dispatchの参照を記録時に保持し、Submit直前にも登録identityを照合する。登録の置換・Releaseが記録後に起きた場合もnative Submit前に拒否する。

bufferはbyte範囲、textureはmip／layer単位でcopy、shader、index、indirect、attachmentのアクセスとbarrierのBefore／After scopeを照合する。textureの初期状態は生成・外部取得の契約から確立し、提出が成功した状態を後続commandに引き継ぐ。複数commandをまとめて提出する場合も指定順で照合する。barrierが覆わない範囲やstageのアクセスは残し、shader内部の動的indexは登録範囲として保守的に扱う。

検証の記録は操作ごとにtransactionとし、native記録まで到達しなかった操作の依存を残さない。Submitの事前照合はqueue状態の複製で行い、同期的なnative失敗では公開しない。D3D12のようにwait・execute・signalを別々に発行する経路は、既に成功したnative操作を取り消せない。部分的に発行済みの依存は保持し、実際の完了またはnative device removalを確認するまで解放を許可しない。

binary semaphoreはAcquire、queue Submit、Presentで同じsignal／wait履歴を使う。成功したnative操作だけを公開し、native fenceまたはbackendの明示的な完了境界まで使用中状態を保持する。取得画像の使用はacquire完了または適切なstageのacquire semaphore待機を必要とし、in-flight画像のPresentには生成元submissionのsemaphoreまたは観測済みのGPU完了を必要とする。キャンセルされた待機や早期Disposeは完了の代わりにならない。copy metadataもcommand内の順序とSubmit時のreference metadata identityを照合し、成功した明示copyだけを後続へ公開する。数値の過去snapshotをこの診断のために保持しない。

診断はCPUから観測できる明示操作を対象とし、GPU内の動的index、shader内部の実アクセス、直接変更したstaging byte、native外部操作を完全には観測できない。WebGPUの非同期nativeエラーとdevice lossは実行結果として伝達し、GPU上で既に実行した操作の状態を巻き戻したとは扱わない。native実行失敗後はqueue状態を不明として再提出を拒否し、deviceの再生成を必要とする。検証を有効にしたdeviceの操作は外部で直列化する。

### 参照ABI

全バックエンドで16バイトの参照スロットを共有するが、既存の符号化方法は維持する。

- Vulkan: バッファの物理デバイスアドレス。
- WgpuとBrowser: 論理登録スロットと、登録範囲を基準としたバイトオフセット。
- Direct3D12: ディスクリプターの識別情報と、バッファ内の絶対バイトオフセット。

参照は不透明で、所有権を持たず、型付きのままとする。参照や引数値を保持してもGPU登録の所有権は取得しない。参照を含むシェーダーデータバッファには、生の別名参照やシェーダーからの書き込み経路を追加しない。通常のunmanagedバッファでは既存の生バッファの用途・同期契約を維持する。

WgpuとBrowserの読み取り専用・書き込み可能poolとremapの各laneは登録のアクセス種別から選択する。同じバッファ範囲を両poolへ登録した場合も、各スロットの選択したlaneだけを設定する。nativeの同時使用・エイリアス制約は別途適用される。Vulkanの物理アドレス、Direct3D12の絶対オフセットの符号化は変更しない。Direct3D12のSRVまたはUAVも登録の選択に従う。シェーダーの参照種別・対象型との一致は検証を有効にした場合だけ診断し、Releaseの転送やbinding生成はシェーダー文字列やフィールドパスで分類しない。

### SetArgumentsの最終uniform格納領域

通常のunmanaged overloadとIShaderArguments overloadは、Set時点で新しいcommand所有の最終uniform backingを作り、C# wire bytesを同期的に直接書く。ShaderValueSnapshot、numeric byte配列、pack・padding用の中間配列を作らない。GPUから読む最終領域に数値が存在することまで無くす判断ではない。SetArguments(ShaderValueSnapshot)は削除し、全backendでsnapshotを実行入力として受け付けない。独立CPU値を求める明示的なCaptureだけを検査・診断用に残す。

一つのSetは一つの世代を所有する。次のSetは別のbackingへ書き、既に記録したDraw／Dispatchが読む領域を変更しない。元のmutable classやstackのin参照は保持しない。新しいSetのcodec、host検査、配置診断、graph admissionやnative割り当てに失敗した場合は未公開の世代だけを解放し、前回成功した選択と元の例外を保つ。CopyFromで許可した部分失敗を、現在選択中のSet値を破損してよいという意味へ広げない。

参照はSet時点でidentityを取得し、安定したwire表現も最終uniformへ符号化する。Vulkanの物理address、D3D12のslot／offset、WGSLの論理slotは登録済み参照から決まる。最初の実行時には書き込みをsealする。後続の同じSetからの実行はnumeric領域やroot slotを上書きしない。転送済み要素のtransitive metadata、table照合、Graphのresource・範囲・access宣言、WGSLのbinding pool／remapは実行ごとに収集し直す。pipeline変更時のprogram一致検査と、任意のvalidationを無効にした経路で診断状態を持たない方針も維持する。

Vulkanはhost-visible coherent uniform、D3D12は256-byte容量整列したCBV、WgpuとBrowserはmapped-at-creation uniformへ書く。root constants／push constantsへのABI変更はしない。最終bufferはGPUが後で読むため、bindingとは別にcommandが所有し、GPU完了後のcommand解放まで保持する。BrowserのMemoryView→JSのslice／mapped setというinteropコピーは残るが、これはC#のnumeric snapshot保持ではない。

RenderGraphは既存のIRenderEncoder／IComputeEncoderへ一時的な参照準備callbackを設定し、通常のgeneric SetArgumentsを直接呼ぶ。公開型やfriend assemblyを追加せず、numeric snapshotや互換adapterを介さない。ShaderBindingSnapshot.CaptureはShaderRootArgumentsを受け、ArgumentsとRootParameterおよび依存metadataだけを収集する。ShaderValueSnapshotを受けるoverloadとnumeric CPU Rootは削除し、隠れたreadbackも行わない。

ShaderResourcesのunmanaged Draw／DrawIndexed／Dispatchもtable選択後にgeneric SetArgumentsへ渡し、helperでCaptureしない。IRenderEncoderとIComputeEncoderのunmanaged SetArgumentsは実装必須とし、Captureするdefault実装は設けない。ModelVertexの明示的な配置検証はUnsafe.SizeOfとShaderTransfer.Fieldsを用い、Captureによるprobeを作らない。

### 値だけを渡すwriterとbackend固有metadata

IShaderValueWriterは`WriteValue(ReadOnlySpan<byte> value)`と`WriteReference<T>(IGpuRef<T>? value)`の二つだけを必須の抽象メソッドとして持つ。各呼び出しの引数は値一つだけで、offset、size、path、seek、skip、WriteAtは公開しない。IShaderWireSink、IShaderReferenceCursor、IShaderReferenceEmitter、ShaderWire、ShaderWireCursorとWriteWireは削除し、互換aliasを残さない。以前のcursorとemitterを必須とする判断を、この平坦なwriter契約へ置き換える。

生成codecは非公開unmanaged wire全体をパディング込みでローカルへコピーし、raw prefixをWriteValue、参照をWriteReference、次のraw区間、最後のraw tailという物理順で渡す。数値だけの型は全領域を一回のWriteValueで渡す。数値のfield別シリアライズや全wireの転送後の参照差し替え、Completeでの参照再走査は行わない。source spanとin参照は同期呼び出し外へ保持せず、destinationとaliasさせない。

生成型のprivate static planは実際のC#オフセットによる参照順序とスロット妥当性のためだけに使う。任意診断のreflectionや数値field tableとは独立し、範囲外、負値、重複、交差をgetterの呼び出し前に拒否する。field、auto-property、virtual propertyと副作用のあるcustom getterも同じ経路を使い、wireローカルコピーの後に各参照getterを物理順で一回だけ評価する。getterによる数値格納領域の変更は今回のraw bytesへ反映せず、参照プロパティの変更は後続getterが観測する。emitter structは生成しない。

VulkanShaderValueWriter、Direct3D12ShaderValueWriter、WgpuShaderValueWriter、BrowserShaderValueWriterを、それぞれのbackend内部でIShaderValueWriterとして直接実装する。各backendのShaderTransfer helperがSetと型付きCopyFromの両方でvalue.Write(ref writer)を呼ぶ。各writerが暗黙の現在位置とexpectedTransferSizeを所有し、raw区間をIShaderRootStorage.WriteまたはuploadのCopyBytesFromへ直接渡す。Sharedのwriterから参照encoder callbackを呼ぶ構造にはしない。WriteValueは渡された長さ、WriteReferenceは必ず16バイトだけ位置を進め、参照はその場でbackendの表現へ符号化する。残容量の検査はlength > expectedTransferSize - positionという減算形式でoverflowを避ける。Completeは一回だけ成功でき、position == expectedTransferSizeを必須とする。短い出力も長い出力も公開しない。

IShaderReferenceMetadataはReferencesという依存identityの列挙だけを公開する。Graph、transitive traversal、copy overlayと任意検証はwire layoutを知らずにこの列挙を使う。各backendのprivate immutable ownerはShaderValueの共有list、数値bytes、wire offsetや元の値objectを保持しない。登録slot／range／accessはbindingに必要なIShaderReferenceのidentityとして残り、C# wire内の位置とは区別する。

CopyFromは失敗時の部分byte更新を許可し、要求範囲のmetadata全体を無効にして元の例外を返す。手書きcodecにも現在位置だけの同じwriterを使い、参照の物理位置は前後のraw区間の長さで表現する。生成planの検査とwriterの容量・完了検査は別の責務である。参照の一時領域はゼロクリアしたstackalloc byte[16]とし、native参照ごとのShaderValue、new byte[16]やFuncによる符号化をなくす。任意診断にはValidateReference(Type valueType, object? reference, int offset)へ型・参照・現在位置を直接渡す。明示CaptureのShaderSnapshotWriterが論理記録を作る経路とは分離する。identity list、配列、read-only wrapper、immutable metadata owner、rootのnative allocation、delegateやnative buffer handleは残り、zero allocationやzero GPU copyとは主張しない。Browserのinterop span-copyも残る。

16-byte ABIは従来の予約ゼロを含めて維持する。Vulkanはbufferの物理addressに絶対byte offsetを加えた64-bit値、またはtexture／samplerの32-bit slotを置き、byte 8にchecked uintのcountを置く。内部の型付きIVulkanShaderBuffer.ShaderBufferを使い、object型ShaderHandleのboxingを避ける。D3D12はslot、checked uintの絶対byte offset、checked uintのcount、予約ゼロとする。Wgpu／Browserはslot、checked uintの登録相対byte offset、checked uintのcount、予約ゼロとし、相対offsetやcountの切り詰めを許さない。

SharedのShaderTransferWriter、IShaderTransferDestination、ShaderRootDestination、ShaderUploadDestination、ShaderDataUpload、ShaderReferenceEncodingとVulkan／D3D12の旧参照encoderは削除する。ShaderRootArgumentsは書き込み済みstorageの所有だけを引き受け、公開境界は次の非generic APIとする。

```diff
 namespace Lumyte.Graphics.Shared
 {
+    public sealed class ShaderRootArguments : IDisposable
+    {
+        // 引数・容量を検証して割り当てるだけ。戻り値の所有権は呼び出し側。
+        public static IShaderRootStorage Allocate(string root, string name, int size,
+            Func<int, IShaderRootStorage> factory);
+        // 書き込み済みstorageを受ける。検証とprepareが成功したときだけ所有権を取得。
+        public static ShaderRootArguments Create(string rootParameter,
+            string shaderTypeName, int wireSize, IShaderRootStorage storage,
+            IShaderReferenceMetadata references,
+            Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null);
+    }
 }
```

Allocateはsizeを16-byte単位へ切り上げ、最小16 byteの容量をfactoryへ要求する。返された容量が違えば解放し、元の検証例外を返す。Createも容量を照合するが、書き込みやgeneric codec／encode callbackは受け付けない。Create失敗ではstorageの所有権は呼び出し側に残る。backend helperは書き込み、診断、準備またはCreate失敗時に未公開storageを解放し、cleanup例外で元の例外を隠さない。Create成功後のcommandへの所有権移転失敗は既存のSelectRootが処理する。

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

通常nativeのSetArgumentsは従来どおり直接呼べる。callbackがnullならhook用の追加allocationを行わない。GraphのRenderGraphRenderEncoder／RenderGraphComputeEncoderは各encoderに一つ保存したPrepareArguments delegateをSet中だけ設定し、try/finallyでnullへ戻し、End前にも解除する。EnsureActiveはSet中の再入によるEnd、入れ子のSetやpipeline変更を拒否する。Graph encoderには公開SetArgumentTableを設けず、native encoderの手動SetArgumentTableは維持する。

検査境界は二つとする。準備時はRenderGraphContext.SelectArgumentsからGraphShaderResourceOwner.SelectArgumentsへ渡したroot参照を、そのpassの既存の許可一覧とnative resource identity・byte offset／size・accessで照合し、BindResourcesでnative tableを自動選択してから新rootを公開する。実行時はnativeの各Draw／Dispatchが最新のcommand-local CopyFrom／CopyBuffer metadataを含む全root／推移的参照を一度更新し、同じ許可条件を検査する。

prepared passごとに一つ保存したActionが実行時検査を行い、準備はそのActionを返す。Actionがcaptureするのは不変の許可配列だけで、owner、context、pass、元入力は保持しない。ShaderRootArgumentsがこのActionを保持するため、数値snapshot、codec再実行やGraph側のcopy overlay代理は不要である。

callback Bで取得して保存した参照も、使用するpass A自身がnative resource・範囲・accessを宣言し、native tableが互換ならAで使用できる。callbackの取得元、登録groupやslotによる所属判定は行わない。JoinTableによるtable共有だけでは使用宣言にならず、native table照合と宣言照合は別の条件である。Graphはnative command内の最新metadataを直接観測できないため、このhookで二つの検査境界を接続し、任意のdevice検証が無効でも宣言照合を行う。

準備が失敗すると未公開のnative storageだけを解放し、元の例外と前回成功したroot選択を保持する。後続の実行時admissionが失敗した場合はnative描画・dispatchの記録を阻止するが、既に変更したCPU状態のrollbackは保証しない。IShaderRootEncoder、IShaderArgumentScope、GraphRootArgumentScopeとReferenceAdmissionのnested classは削除し、新しい公開型、friend accessやInternalsVisibleToは追加しない。

### 手書き実装と第三者backendの移行

手書きIShaderArguments.Writeもunmanaged wireのローカルコピーからraw区間をWriteValueへ渡し、必要な位置で型付きWriteReferenceを呼ぶ。参照なしなら全wireへ一回だけWriteValueを呼ぶ。第三者writerはこの二つのメソッドを実装し、暗黙位置、16-byte ABI、容量とCompleteの一致検査、同期借用と非alias性を守る。能力判定、既定のthrow実装、cursor／emitter adapterは提供しない。

明示的なCPU検査のShaderSnapshotWriterもサイズをconstructorで受け、同じ二つのメソッドを実装する。raw bytesを独立領域へコピーし、参照slotはゼロで保存して論理参照identityを記録する。Captureは読み取り・検査専用で、snapshotをnativeへ戻す書き込み口ではない。SetArguments(ShaderValueSnapshot)、CreateSnapshot、SharedPackRaw、ShaderDataLayout.PackとIShaderDataLayout.Packは削除したままとする。Fields、任意reflectionと静的metadata取得補助も診断専用である。

第三者encoderは両generic SetArgumentsと上記SetArgumentValidationを既存interfaceで実装する。ShaderBindingSnapshot.CaptureはShaderRootArgumentsの依存metadataだけを収集し、numeric CPU Rootやsnapshot入力を持たない。通常のunmanaged bufferの直接memcpy、primitive buffer API、GPU copy commandと無関係なコンパイル経路は変更しない。この契約自体は新しい性能測定を意味しない。

### 値の取得・転送・寿命

明示的なCaptureは、従来どおり呼び出し時点の独立したCPU値snapshotを作る。SetArgumentsは同じ値固定の意味を維持するが、前述の最終uniform格納領域へ直接書き、numericの中間snapshotを作らない。一方、参照付きCopyFromは現在のC# wire bytesを同期的にupload領域へ書き、bindingに必要な参照identityだけを保持する。転送のために数値のShaderValueSnapshotを作成・保存しない。宣言、登録、RenderGraph Compileより後、CopyFromより前の変更はすべて反映する。CopyFrom後の元のクラス変更には、次の明示CopyFromが必要である。GPU copyが実際に読む瞬間までCPU objectやstack参照を保持するものではない。

CopyFromの失敗atomicityは要求しない。codec、参照符号化、native mapped書き込みで失敗すると、指定したupload範囲のbyteは部分更新され得る。旧byteへrollbackせず、要求した範囲のreference metadataを全て無効にし、元の例外を返す。範囲外のmetadataは変更しない。成功した全範囲の明示再転送で回復する。無効なmetadataを読むcopy／bindingは、native使用前に拒否する。in-flight GPU使用中のstaging変更を許す判断ではなく、従来の所有権・同期責任は維持する。

参照付きCopyFromもbackend writerからmapped領域へ直接書く。Sharedのbatch領域やsnapshot経由の代替経路は設けない。これは失敗atomicityを追加する判断ではなく、部分更新とmetadata無効化の契約を維持する。転送時間や割り当て量の改善は、この設計だけでは実測結果として主張しない。

公開backend契約も明示的に変更する。IShaderDataSource.Readが返していた完全なCPU値と、ShaderBindingSnapshot.Elementsのnumeric snapshotは廃止し、ReadReferencesとSetTransferredReferencesを使う。IShaderReferenceMetadataはimmutableな依存identityの列挙だけを公開する。privateなbackend ownerはData、数値の配列、wire offset、配置providerや元の値objectを保持しない。GPU-only destinationから隠れたreadbackや待機を行わない。数値の履歴を必要とする利用側は、独立した明示Captureまたは明示readbackを使う。

通常のunmanagedバッファは依存メタデータを作らず、範囲全体を直接コピーする。参照を含むbufferでは、数値byteはGPU copy、reference-only metadataはcommand-local overlayと成功したSubmitで、それぞれ必要な順序に従って伝播する。

MemoryPreference.UploadのバッファはCPUステージング領域、AutomaticのバッファはGPUのシェーダーデータとする。マップ、書き込み、アンマップ、バリア、コピー、送信、完了待機、再利用の同期は引き続き明示的に行う。コピーの記録時には、コマンド内だけで有効なメタデータの上書き情報を作る。送信が成功した場合に限ってGPUバッファへメタデータを反映し、破棄したコマンドや未送信のコマンドからは反映しない。部分コピーは転送先の対象要素だけを置き換える。転送元の変更とリソース寿命には、既存のコマンドと登録の契約を適用する。

ルート引数の取得時には、転送済みシェーダーデータの参照依存関係を再帰的に収集する。確認済みのバッファと要素の組を記録し、自己参照や相互参照で探索が循環しないようにする。同一テーブルへの所属とリソースの検査は、ルートからの直接参照だけでなく、間接的な参照にも適用する。バッファ参照のアクセス種別はArgumentTableの登録が保持する。参照は登録の識別情報を持ち、GetElementも同じ登録を参照する。BufferUsageはリソースの能力であり、選択したアクセス種別ではない。WriteBuffer(slot, range, ShaderBufferAccess.ReadOnly)を既定とし、書き込みにはReadWriteを明示する。ReadOnlyにはShaderRead、ReadWriteにはShaderWriteの能力を登録時に要求する。参照を含むShaderDataSliceの登録は読み取り専用に限定する。同じバッファに別スロットで異なるアクセス種別を登録できる。置換は新しい登録を作り、古い参照のCPUメタデータを変更しないが、古い参照の実行はGRAPHICS-0014に従って禁止する。検証を有効にした場合は登録identityの照合で失効を検出する。

### 検証方針

値だけを渡すwriterの受け入れ条件は、二つの必須メソッドと各一引数、数値だけの一回のWriteValue、custom getterの物理順一回評価、getter前のwireコピーと不正plan拒否、減算形式の容量検査とCompleteのサイズ一致、失敗した準備による旧世代保持とする。Graph callbackのfinally解除、End前の解除、Set中の再入拒否、passごとに一つのActionと許可配列だけのcapture、command-local copy後の最新推移的参照による毎回一度のadmissionも検査する。別callbackで取得した参照は互換tableと使用passのresource・範囲・access宣言が揃えば許可し、共有tableだけの未宣言使用は検証無効時にも拒否する。snapshotを受けるbackend入力、Pack、遅延fixupが公開契約に残らないことも確認する。四backendのSetと型付きCopyFromが内部writerを直接使うこと、予約ゼロを含む16-byte ABI、Wgpu／Browserの相対offsetとcountのchecked変換、D3D12の絶対offset、SharedのAllocate／Createの所有権境界とcleanup時の元例外保持を検査する。native参照ごとのShaderValue、byte[16]配列、encode callbackやsnapshot代替入力を残さず、生成コードにEmitScopeがないことも確認する。

テストでは、インターフェースもpartial宣言もない構造体と外部の数値型、1回の一括書き込み、入れ子の構造体、行列、実際のパディング、C#の要素間隔、クラスの非公開格納領域とプロパティ書き込み、取得済みの値の独立性、参照差し替えの範囲、インスタンス生成なしの静的サイズ取得、有効時の不一致検出、無効時のバイト列の保持を確認する。バックエンドの動作確認では、明示的な転送、登録範囲を基準とするオフセット、循環参照、送信成功後だけのメタデータ反映、登録ごとのアクセス種別と能力の検査、同じバッファの別登録、GetElementによる登録の保持、置換前後の独立したCPUメタデータとremapのlane分離も確認する。GPU、Browser、Direct3D12の検証範囲は、CPUテストの成功件数と区別して報告する。

## 検討した代替案

- 数値型にもインターフェースと生成コードを必須にする: 外部のunmanaged型や通常の構造体をそのまま使えないため採用しない。
- 型制約だけを外して任意の管理型を受け付ける: 参照を含む値と生のunmanagedバイト列の境界が不明確になるため採用しない。生バッファはunmanaged制約、参照を含む値は既存のインターフェース制約で分離する。
- Sharedの汎用writerとbackend参照符号化callbackを維持する: 共通化はできるが、native転送で論理参照記録や一時配列を介さずbackendが直接ABIを書き込む境界にするため採用しない。Graphの参照だけの準備hookは別の責務として維持する。
- 数値ごとのwriterを維持する: バイト列を再構築し、ABIの定義が重複するため採用しない。
- Releaseでリフレクションのオフセットに詰め直す: 不整合な宣言によってC#のメモリ表現を暗黙に変更しない方針のため採用しない。
- 管理クラスのメモリを直接転送する: 転送可能な領域は非公開のunmanaged格納領域だけとし、採用しない。

## 結果と影響

シェーダー引数とソース生成の契約に対する破壊的変更となる。利用側は参照を含む構造体をクラスへ移行し、シェーダー宣言の配置を一致させる責務を持つ。検証を有効にした場合に配置診断を提供し、いずれの構成でも元の数値バイト列を保持する。所有権の管理、リソース検証、明示的なGPU同期は引き続き必要となる。
