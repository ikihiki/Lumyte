# ADR-GRAPHICS-0029: シェーダーデータの転送表現を更新間で保持する

- 状態: 採用
- 日付: 2026-11-11
- 基準コミット: `3e50e2315743e23f6e5faf7ffdf393ac0a144a2d`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md)「MaterialData内のIGpuRefと明示的な転送」「型対応とcodec生成」
- 変更対象: CPU側の保持表現とCopyFrom時の変換
- 継続する判断: 論理構造体、artifactによる配置決定、明示的な転送、参照の推移的収集、command内overlay

## 背景

毎フレーム同じシェーダーデータを更新する利用者は、論理構造体から転送表現への変換を繰り返している。基準コミットの `src/Graphics/Lumyte.Graphics.Vulkan/VulkanShaderDataBuffer.cs` の `CopyFrom` と、Direct3D12・Wgpu・Browserの同名実装は、変更のない参照を含めてCaptureとPackを各要素で実行する。C#のIGpuRefを含む構造体自体はunmanagedではなく、そのメモリをGPUへ直接送れない。

## 決定

artifactに従うunmanagedな転送用領域と、登録identityを保持するCPU側の情報を分離したShaderDataStorageを追加する。通常の論理構造体を渡す既存経路を維持し、保持経路では数値変更と参照変更を個別に受け付ける。VulkanとDirect3D12では保持済みbytesをCopyFromで直接コピーし、各回のCaptureとPackを行わない。

### 利用者の変更前・変更後

初期値、staging、GPU用buffer、tableの生成とGPU完了待機は省略している。MaterialはVector4 Colorとtexture参照を持つ、生成対象のpartial IShaderData構造体である。

変更前:

```csharp
using System.Numerics;
using Lumyte.Graphics.Abstractions;

materials[0] = materials[0] with { Color = Vector4.One };
await staging.MapAsync();
staging.CopyFrom(materials);
staging.Unmap();
```

変更後:

```csharp
using System.Numerics;
using Lumyte.Graphics.Abstractions;

// 初期変換は一度だけ。以降の更新で元のmaterials配列は参照しない。
ShaderDataStorage<Material> retained = staging.CreateStorage(materials);
retained.SetValue(0, Material.StorageMembers.Color, Vector4.One);
retained.SetReference(0, Material.StorageMembers.Texture, textureRef);
await staging.MapAsync();
staging.CopyFrom(retained);
staging.Unmap();
```

どちらもCopyBuffer、barrier、Submitを利用者が明示する。全論理値を置き換えるSetは変換を行い、その費用をCopyFromの高速化に含めない。新経路は手書きcodecへ自動適用せず、生成metadataがない場合はNotSupportedExceptionとする。既存の `CopyFrom(ReadOnlySpan<T>)` は従来どおり互換経路として残る。

### 保持表現と型検証

生成器は対応する数値、vector、Matrix4x4、入れ子構造体、IGpuRefについて、値を読まない静的なmember metadata（bufferの論理参照先名を含む）と型付きのStorageMembersを生成する。参照をたどって型をinline展開しないため、buffer参照経由の循環を許可する。配列、custom getter、未対応型の診断は維持する。手書きcodecが生成metadataを偽って提供する場合は契約違反である。

生成metadataの全fieldのpath・型・参照先をartifactへ照合する。CreateStorage時に初期値を読む前に、両者の完全なfield集合（重複・欠落・余分fieldを含む）、配置、stride、alignment、範囲、scalar・vector・matrixの要素型と形状・順序、helperのkind・サイズ・fieldの型と配置を、要素数によらず検証する。C#のsizeof(T)をshader strideとみなさず、保持領域をartifactのoffsetへ直接配置する。paddingはゼロで初期化し、値を書き込む操作はそのfieldだけを変更する。Matrix4x4は既存のrow-major契約を維持し、別の配置は明確に拒否する。

backendは16 byteの不透明なunmanaged参照値を製造する。保持storageの製造元は、backendがdeviceごとに所有して公開しない専用identityで照合する。applicationが知るdevice object自体をこのidentityとして使わず、公開shared factoryに任意のencoderを渡して作ったstorageをnative backendへ流用できないようにする。wire bytesを登録identityへ読み戻したり、identityの辞書keyとして使ったりしない。CPU側はIGpuRefと論理型を別に保持する。利用者にaddressや整数の組み立てを要求せず、不透明参照値の任意構築APIを設けない。

### 更新、所有権と失敗

storageは作成元と同じdeviceと同じschemaのstagingだけへコピーできる。生成元bufferを所有せず、自身のCPU領域を所有する。storageはDispose不要であり、明示的な共有cacheを作らない。並行更新・copy・map・解放を同期せず、利用者が競合を避ける。

SetValueは数値だけを直接更新する。SetReferenceはbackend製の同deviceの参照を型・用途・登録rangeへ照合してから、そのfieldのwire値とCPU情報を一緒に変更する。任意のIGpuRef実装は拒否する。初期変換と全値を置き換えるSetにも同じ参照検査を適用し、全memberを検証してから反映する。公開IShaderReferenceの実装だけをprovenanceの根拠にせず、backend内部の製造元を確認する。失敗時は以前のbytesとmetadataを維持する。

CopyFromはmap済みUploadに限定する。sourceOffsetとcountはsourceの要素単位、elementOffsetはdestinationの要素単位とする。countの既定値-1はsource末尾までを意味する。空範囲は有効な末尾offsetでもno-opとし、それ以外の負値、overflow、範囲外は拒否する。device・schema不一致と未対応形式は空範囲でも検証する。

通常の引数不正、schema不一致、凍結用の確保失敗、変換失敗ではdestinationのbytesとmetadataをともに維持する。範囲全体のsnapshotを準備してからbytesを書き、書込み後にmetadata用の確保を残さない。native失敗のrollbackは保証しない。コピー前に指定範囲のbytesとCPU参照を凍結する。各要素のsnapshotは最大で一要素のbytesとその参照情報だけを所有し、一要素を残すことで元の巨大なcopy全体を保持しない。sourceの後続更新、別範囲の更新、stagingの再書き込みは以前のRead結果を書き換えない。Readは従来と同じ完全な論理snapshotであり、数値のデコード費用は残る。

[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)の寿命・同期契約を継続する。登録をRelease・置換した後の古い参照を使用せず、copy完了前にstagingを再利用しない。失効検出、世代の再検証、Submit時の生存走査は追加しない。command内overlay、copy順序、Submit成功後だけのmetadata公開、失敗・破棄したcommandの未公開、実行結果不明時のretirementは既存のままとする。

### backendの違い

Vulkanはbuffer address、Direct3D12はdescriptor slotを含む各backendの既存helper表現を保持する。両backendのCopyFromは保持領域からのbytesのコピーと不変metadataの確保だけを行う。数値のみの更新でも参照変更でもCapture・Packを再実行しない。

WebGPUのnative wgpuとbrowserも同じ公開APIを提供する。保持表現はbackendごとに作成し、WGSLの有限bindingへの変換はCPU側のidentityから既存のbinding解決で行う。nativeと同じaddress表現や変換費用ゼロは保証しない。異なるbackendやdeviceへ保持表現を流用しない。

### 公開APIと契約の差分

比較元は基準コミットのGraphics.Abstractionsである。生成するStorageMembersは各applicationの型へ属し、以下のMaterialは利用例の型を示す。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IShaderData : IShaderArguments
     {
+        // 生成器が値によらない型情報を提供する。既定は未対応。
+        static virtual IReadOnlyList<ShaderDataMember> StorageSchema { get; }
     }
+
     public interface IShaderArguments
     {
+        // 生成器がbuffer参照先の論理名をreflectionなしで取得する。
+        public static string TypeName<T>() where T : struct, IShaderArguments;
     }
+
+    // backendと生成器の接続用metadata。native handleは持たない。
+    public sealed record ShaderDataMember(string Path, Type ValueType, bool IsReference,
+        string? ReferenceTarget = null);
+    public readonly record struct ShaderDataValueMember<T, TValue>(string Path)
+        where T : struct, IShaderData where TValue : unmanaged;
+    public readonly record struct ShaderDataReferenceMember<T, TReference>(string Path)
+        where T : struct, IShaderData;
+
+    public abstract class ShaderDataStorage<T> where T : struct, IShaderData
+    {
+        public abstract int Count { get; }
+        // index範囲外はArgumentOutOfRangeException。
+        // 論理値全体の変換を行う。失敗時は元の要素を維持する。
+        public abstract void Set(int index, in T value);
+        // 型とpath不一致はArgumentException。数値だけを直接変更する。
+        public abstract void SetValue<TValue>(int index,
+            ShaderDataValueMember<T, TValue> member, in TValue value)
+            where TValue : unmanaged;
+        // null・独自実装・別device・用途不一致はArgumentException。
+        // wire値と独立したCPU参照を一緒に更新する。
+        public abstract void SetReference<TReference>(int index,
+            ShaderDataReferenceMember<T, TReference> member, IGpuRef<TReference> value);
+    }

     public interface IGraphicsShaderDataBuffer<T> where T : struct, IShaderData
     {
+        // 値の初期変換とschemaの初回検証。空initialでもschemaを検証する。
+        // metadataのない手書きcodecはNotSupportedException。
+        // 未対応の第三者backendは既定実装でNotSupportedException。
+        ShaderDataStorage<T> CreateStorage(ReadOnlySpan<T> initial);
+        // map済みUpload限定。詳細は更新・所有権契約による。
+        void CopyFrom(ShaderDataStorage<T> source, int sourceOffset = 0,
+            int count = -1, ulong elementOffset = 0);
         void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0);
     }
 }

+// applicationのpartial Material内に生成する。
+partial record struct Material
+{
+    public static class StorageMembers
+    {
+        public static ShaderDataValueMember<Material, Vector4> Color { get; }
+        public static ShaderDataReferenceMember<Material, IGraphicsTextureView> Texture { get; }
+    }
+}
```

新しいbufferメソッドにはNotSupportedExceptionを返す既定実装を置き、第三者backendの既存実装を壊さない。Vulkan・Direct3D12・Wgpu・Browserは各backendで実装する。

member tokenの既定値や任意のpathもschemaへ照合するため、未定義fieldを書き換えられない。RenderGraphのwrapperは同じ所有権と転送の意味を透過的に委譲する。入れ子の数値はMaterial.StorageMembers.Nested.Colorのようにアクセスする。backend間には次の公開shared factoryを追加し、その内部がschema認証、field書込み、デコードを担う。保持bytesをapplicationへ返さず、reflection、InternalsVisibleTo、実行時codec登録を追加しない。

```diff
 namespace Lumyte.Graphics.Shared
 {
     public sealed partial class ShaderDataLayout
     {
+        // backend向け。ownerはbackendだけが保持する製造identity。
+        // encoderは同deviceのbackendが提供する。
+        // schema認証後に初期値を変換し、encoderが製造元・用途も検査する。
+        public ShaderDataStorage<T> CreateStorage<T>(object owner,
+            ReadOnlySpan<T> initial, Func<ShaderValue, string, byte[]> encoder)
+            where T : struct, IShaderData;
+        // 同owner・schema・範囲を確認し、全metadata確保後にbytesを転送する。
+        // 呼出backendがmap状態・destination範囲を先に確認する。
+        public ShaderValueSnapshot[] Upload<T>(ShaderDataStorage<T> source,
+            object owner, IGraphicsBuffer<byte> destination, int sourceOffset,
+            int count, ulong byteOffset) where T : struct, IShaderData;
     }
 }
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IShaderDataSource
     {
-        // 不変のCPU要素snapshotを返す。
+        // 全数値と元のtyped reference identityを返す。
+        // 保持経路のsnapshotは一要素分だけを所有し、copy全体・storage・
+        // mutable stagingを保持しない。参照resourceのGPU寿命は延長しない。
         ShaderValueSnapshot Read(ulong index);
     }
 }
```

## 検討した代替案

- 既存の論理Tを毎回変換する方式: 互換性が高く少量のデータには簡単である。参照を含む同じ構造の変換が更新ごとに残るため、互換経路として残す。
- C#のTを無条件にmemcpyする方式: unmanaged制約を満たさない参照とshaderの配置差を扱えないため採用しない。
- addressやdescriptor indexをapplicationに組み立てさせる方式: CPUの登録identityを失い、型・device・有限bindingの解決を壊すため採用しない。
- 全memberを共通中間形式へ毎回まとめる方式: backend間の見かけの共通性は得られるが、native側の安定した表現を再構築するため採用しない。

## 結果と影響

利用者は保持表現と型付きmember更新を選ぶことで、nativeの繰り返し転送からCaptureとPackを外せる。初期変換、登録変更、metadataの凍結、Read、WebGPUのbinding変換にはそれぞれ費用があり、CPU memoryも保持する。既存の論理構造体とサンプルを強制移行しない。

生成器、AOT、schema不一致、空・ゼロ値・slot 0、数値・行列・入れ子・参照変更、循環、partial copy、古いReadの不変性、失敗時の未変更を検証する。各native backendで繰り返し更新時のCaptureとPackがゼロであることを確認し、画素は独立した期待値と照合する。Windowsの実行とbrowserの実行はそれぞれの環境で確認する。性能は最終公開APIの初回・更新・copy・Readを分けて測定し、microbenchmarkの改善をUI全体へ一般化しない。
