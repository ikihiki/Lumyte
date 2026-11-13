# ADR-GRAPHICS-0035: シェーダー引数とC#の配置を正とする一括転送

- 状態: 採用
- 日付: 2026-11-13
- 基準コミット: `ef61258f27ec0f19240a5333d4388f1a3ac9de50`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0005](GRAPHICS-0005-argument-tables-and-gpu-references.md)の「登録API」と、[GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md)の「生成codecとbackendの接続」「MaterialData内のIGpuRefと明示的な転送」と、[GRAPHICS-0023](GRAPHICS-0023-explicit-api-convenience.md)の型制約
- 変更対象: 数値の転送表現、参照を含む型の宣言、転送サイズの基準、配置検証の実行構成、登録ごとのバッファアクセス選択
- 継続する判断: テーブルの明示選択、不透明な型付き参照、資源の所有権、明示転送・送信・完了待機
- リソース検証方針: [GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)。
- 明示的な操作を補助するAPI: [GRAPHICS-0023](GRAPHICS-0023-explicit-api-convenience.md)。

## 背景

数値メンバーごとのシリアライズでは、C#の値の配置が暗黙にシェーダーのリフレクションへ従い、転送時に数値データを再構築していた。利用者が定義したC#のunmanaged構造体のメモリ表現を、転送形式の唯一の基準に変更する。

## 決定

C#のunmanaged構造体の配置を正とし、数値を一括コピーする。参照を含む引数は非公開の格納用構造体を持つクラスへ移し、生成コードが参照だけを解決する。数値の再構築をなくし、格納用構造体は通常の利用側APIに露出させない。

### 利用者の変更前・変更後

変更前は、利用側で参照を含む引数構造体を宣言し、数値メンバーを個別に書き出していた。変更後は、数値だけの通常のunmanaged構造体を、インターフェース、partial宣言、ジェネレーター、登録なしでそのまま転送する。参照を含む引数は、非公開のunmanaged格納領域と通常の公開プロパティを持つpartialクラスにする。値の取得、明示的なコピー、送信、完了待機、解放の境界は変更しない。

数値だけのバッファ要素は、変更前にはpartial構造体へのIShaderData実装を必要とした。変更後は通常の構造体でよく、同じ準備と書き込みは次のように変わる。後続のバリア、コピー、送信、完了待機と解放は両例とも省略している。

```csharp
// 変更前（基準コミット）
public readonly partial record struct Vertex(Vector3 Position) : IShaderData;
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

通常の数値データにはIShaderArgumentsもIShaderDataも不要である。`CreateShaderDataBufferのunmanagedオーバーロードは既存のIGraphicsBuffer<T>`を返し、`BufferSlice<T>`、`BufferBarrierDesc<T>`、CopyBuffer、WriteBufferとRenderGraphの通常のバッファ経路を使う。RenderGraph.CreateShaderDataBufferにもunmanagedオーバーロードを用意し、`RenderGraphBuffer<T>`を返す。`Lumyte.Resources.Graphicsでは既存のGraphicsBufferDefinition<T>`と生バッファのインポートをそのまま利用する。参照依存メタデータがないため、要素ごとの取得・符号化は行わず`ReadOnlySpan<T>`をそのままコピーする。既存のインターフェースを明示実装する数値型も互換経路として使用できる。IGpuRefを含む型はpartialクラスにする。数値データは1つの非公開unmanaged構造体に保持し、利用側には通常の読み書き可能なプロパティを提供する。.NET 10のpartialプロパティはジェネレーターで実装できる。利用者が定義する格納用構造体には、参照プロパティと同名の16バイトのShaderReferenceSlotフィールドも配置する。ジェネレーターは非公開の格納領域を一括コピーし、参照の識別情報とC#側のスロット位置を記録する。管理オブジェクト自体のメモリは転送しない。

通常の構造体は`Unsafe.SizeOf<T>()`で、生成対象のクラスは静的なTransferSizeで、バッファの要素間隔と割り当てサイズを決める。バッファ生成時に値のインスタンスを生成したり、default(T)から値を取得したりしない。生成型の静的なTransferFieldsは、Debug診断に使う実際の管理メモリ上のオフセットを表す。通常の構造体ではDebugに限ってフィールドのリフレクションと実際の管理オフセットを求めるコードを型ごとに一度実行してキャッシュする。Releaseにはフィールド走査も実行時コード生成も含めない。動的コード生成ができないBrowserやDebug AOTでも、通常構造体を直接転送できる。その場合は名前とサイズを診断し、取得できないフィールドオフセットの診断だけを省略する。生成型の静的な配置情報はその環境でも使用する。シェーダーのスキーマから求めた要素間隔でC#の要素間隔を上書きしない。

旧フィールド単位のwriterと、リフレクションに従って数値を詰め直す処理は削除する。数値の書き込み操作はWriteBytesのみとし、Valuesには参照の差し替え情報だけを保持する。ShaderDataLayout.Packは取得済みのバイト列を複製し、C#側の各参照位置にある16バイトだけを差し替える。参照の種類は参照先リソースとArgumentTableの登録から解決する。シェーダーの名前やパスから実行時のアクセス種別を決めない。WriteReferenceはC#側のオフセットと不透明な参照だけを受け取る。

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
+        // 構造体全体を実際のパディング込みで一括コピーする。
+        void WriteBytes<T>(in T value) where T : unmanaged;
+        // C#側の16バイト参照スロットを指定する。
+        void WriteReference<T>(int offset, IGpuRef<T>? value);
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
+        byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, byte[]> reference);
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
+        // 引数の個数が異なるため制約だけで区別するオーバーロードではない。
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
+    // C#の配置をDebug診断へ渡す。GPUアドレスは含まない。
+    public sealed record ShaderTransferField(
+        string Path, int Offset, int Size, Type ValueType, bool IsReference = false);
-    public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : struct, IShaderData
+    public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : IShaderData
     {
     }
 }
```

参照差し替え情報のShaderValueはValueType、Reference、C#のOffsetだけを保持し、Pathを持たない。IShaderDataLayout.ReferenceKind(string path)は削除し、Packの参照callbackは`Func<ShaderValue, byte[]>`にする。backendは参照先と登録のメタデータから符号化する。

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

### DebugとReleaseの違い

Debugでは、C#の名前、サイズ、フィールド型、オフセット、入れ子のパス、参照スロットをシェーダーのスキーマと照合する。4つのパイプラインでのステージ間のValidateProgram呼び出しと、ModelVertexの配置検証（ModelMeshからの呼び出しを含む）はDebug限定とする。

Releaseでは数値スキーマの不一致を拒否せず、バイト列も補正しない。シェーダー宣言が誤っていれば描画結果が壊れることを許容する。ホスト側の範囲検査、参照ABIの長さと範囲、整数オーバーフロー、選択したテーブルへの所属、登録時の用途能力と、現在操作するインスタンス自身のdisposed・mapping状態などの局所的な検査は維持する。リソースと登録の寿命はGRAPHICS-0014に従って利用者が保証し、登録の世代・失効検出やdraw・Submit時の生存確認の再走査は再導入しない。ホストメモリの安全性をDebug診断に依存させない。

### 参照ABI

全バックエンドで16バイトの参照スロットを共有するが、既存の符号化方法は維持する。

- Vulkan: バッファの物理デバイスアドレス。
- WgpuとBrowser: 論理登録スロットと、登録範囲を基準としたバイトオフセット。
- Direct3D12: ディスクリプターの識別情報と、バッファ内の絶対バイトオフセット。

参照は不透明で、所有権を持たず、型付きのままとする。参照や引数値を保持してもGPU登録の所有権は取得しない。参照を含むシェーダーデータバッファには、生の別名参照やシェーダーからの書き込み経路を追加しない。通常のunmanagedバッファでは既存の生バッファの用途・同期契約を維持する。

WgpuとBrowserの読み取り専用・書き込み可能poolとremapの各laneは登録のアクセス種別から選択する。同じバッファ範囲を両poolへ登録した場合も、各スロットの選択したlaneだけを設定する。nativeの同時使用・エイリアス制約は別途適用される。Vulkanの物理アドレス、Direct3D12の絶対オフセットの符号化は変更しない。Direct3D12のSRVまたはUAVも登録の選択に従う。シェーダーの参照種別・対象型との一致はDebugだけで診断し、Releaseの転送やbinding生成はシェーダー文字列やフィールドパスで分類しない。

### 値の取得・転送・寿命

SetArgumentsと、マップ済みアップロード領域へのCopyFromは、呼び出し時点のバイト列のコピーと参照の識別情報を即座に取得する。その後に元のクラスを変更しても、記録済みの値は変化しない。参照を含むクラスでは、ステージング領域のバイト列やメタデータを置き換える前に、全要素の取得と符号化を完了する。通常のunmanagedバッファは依存メタデータを作らず、範囲全体を直接コピーする。数値のバイト列と参照メタデータは、明示的なコピーでも一緒に転送する。

MemoryPreference.UploadのバッファはCPUステージング領域、AutomaticのバッファはGPUのシェーダーデータとする。マップ、書き込み、アンマップ、バリア、コピー、送信、完了待機、再利用の同期は引き続き明示的に行う。コピーの記録時には、コマンド内だけで有効なメタデータの上書き情報を作る。送信が成功した場合に限ってGPUバッファへメタデータを反映し、破棄したコマンドや未送信のコマンドからは反映しない。部分コピーは転送先の対象要素だけを置き換える。転送元の変更とリソース寿命には、既存のコマンドと登録の契約を適用する。

ルート引数の取得時には、転送済みシェーダーデータの参照依存関係を再帰的に収集する。確認済みのバッファと要素の組を記録し、自己参照や相互参照で探索が循環しないようにする。同一テーブルへの所属とリソースの検査は、ルートからの直接参照だけでなく、間接的な参照にも適用する。バッファ参照のアクセス種別はArgumentTableの登録が保持する。参照は登録の識別情報を持ち、GetElementも同じ登録を参照する。BufferUsageはリソースの能力であり、選択したアクセス種別ではない。WriteBuffer(slot, range, ShaderBufferAccess.ReadOnly)を既定とし、書き込みにはReadWriteを明示する。ReadOnlyにはShaderRead、ReadWriteにはShaderWriteの能力を登録時に要求する。参照を含むShaderDataSliceの登録は読み取り専用に限定する。同じバッファに別スロットで異なるアクセス種別を登録できる。置換は新しい登録を作り、古い参照のCPUメタデータを変更しないが、古い参照の実行はGRAPHICS-0014に従って禁止する。世代照合や失効検出は追加しない。

### 検証方針

テストでは、インターフェースもpartial宣言もない構造体と外部の数値型、1回の一括書き込み、入れ子の構造体、行列、実際のパディング、C#の要素間隔、クラスの非公開格納領域とプロパティ書き込み、取得済みの値の独立性、参照差し替えの範囲、インスタンス生成なしの静的サイズ取得、Debugでの不一致検出、Releaseでのバイト列の保持を確認する。バックエンドの動作確認では、明示的な転送、登録範囲を基準とするオフセット、循環参照、送信成功後だけのメタデータ反映、登録ごとのアクセス種別と能力の検査、同じバッファの別登録、GetElementによる登録の保持、置換前後の独立したCPUメタデータとremapのlane分離も確認する。GPU、Browser、Direct3D12の検証範囲は、CPUテストの成功件数と区別して報告する。

## 検討した代替案

- 数値型にもインターフェースと生成コードを必須にする: 外部のunmanaged型や通常の構造体をそのまま使えないため採用しない。
- 型制約だけを外して任意の管理型を受け付ける: 参照を含む値と生のunmanagedバイト列の境界が不明確になるため採用しない。生バッファはunmanaged制約、参照を含む値は既存のインターフェース制約で分離する。
- 数値ごとのwriterを維持する: バイト列を再構築し、ABIの定義が重複するため採用しない。
- Releaseでリフレクションのオフセットに詰め直す: 不整合な宣言によってC#のメモリ表現を暗黙に変更しない方針のため採用しない。
- 管理クラスのメモリを直接転送する: 転送可能な領域は非公開のunmanaged格納領域だけとし、採用しない。

## 結果と影響

シェーダー引数とソース生成の契約に対する破壊的変更となる。利用側は参照を含む構造体をクラスへ移行し、シェーダー宣言の配置を一致させる責務を持つ。Debugでは配置診断を提供し、Releaseでは元の数値バイト列を保持する。所有権の管理、リソース検証、明示的なGPU同期は引き続き必要となる。
