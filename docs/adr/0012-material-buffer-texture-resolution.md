# ADR-0012: マテリアルバッファからのテクスチャ参照解決

- 状態: 提案
- 日付: 2026-10-07

## 背景

glTF のようなマテリアルは色・係数と複数の texture／sampler の組を持つ。マテリアル配列を GPU buffer に置き、シェーダーが material index で要素を選んで画像を sample できるようにする。利用側に GPU address、descriptor index、binding slot を公開せず、[ADR-0005](0005-shader-compilation-and-data-interop.md) の生成 serializer と Slang の library module で解決する。

Texture／view の契約は [ADR-0010](0010-texture-resource-contract.md)、sampler は [ADR-0011](0011-sampler-resource-contract.md)、CPU pack と明示的な GPU 転送は [ADR-0009](0009-buffer-resource-contract.md) に従う。WebGPU の storage buffer に texture／sampler object は格納できず、buffer 内の整数を任意の resource binding として使うこともできない。GPU buffer にマテリアルを格納するだけでは参照先の binding と寿命を確定できないため、有限の resource 集合を伴う契約を定める。

本 ADR はマテリアルデータと sampled D2 texture の間接参照を設計する。glTF parser、画像 decoder、mesh importer、完全な PBR renderer は対象外。以下の API と shader helper は未実装であり、初期 wgpu 実装の対応範囲を変更したとは扱わない。

## 決定

### 論理マテリアル、wire data、binding 集合

1. importer は生成された論理マテリアル型に係数と `SampledTexture2DReference` を設定する。参照は Device が `IGraphicsTextureView` と `Sampler` の組から生成する不透明な値型。公開する整数値・descriptor index・serialization はない。
2. 利用者が compiled artifact の `MaterialResourceLayout<T>` とマテリアル配列から immutable な `IGraphicsMaterialBindings<T>` を生成する。backend は値と参照を snapshot し、参照の有限な集合、wire layout、schema、BindingPlan、Device 所属、依存 lease を確定する。GPU buffer の確保・pack・コピー・送信はしない。
3. 利用者が用意した `IGraphicsBuffer<byte>` の Upload 範囲に、生成 serializer の `CopyFrom` 拡張で pack する。係数と texture selector／存在 flag／UV 情報を反射 layout に従って格納し、範囲に binding 集合との関連を登録する。
4. 利用者がその全範囲を `RecordCopyBuffer` で shader-readable な GPU buffer へ転送し、`Finish`／`Submit` と必要な依存を明示する。完全なコピーは schema／layout／binding 集合の関連を引き継ぐ。
5. `CreateReference<T>` で得たマテリアル配列の `GpuReference<T>` を生成 root 引数へ渡す。引数 pack はその範囲に関連する binding 集合を取り込み、同じ set と互換な pipeline の resource bindings を構築する。利用者が set を別の root field に重複して設定する必要はない。
6. シェーダーが GPU buffer からマテリアルを load し、参照 field を library の sampling helper に渡す。helper は同じ BindingPlan と集合を使って texture／sampler を選択する。

この material 配列の pack では MaterialDataTransfer.CopyFrom(set) を使う。ADR-0005 の汎用 CopyFrom(values, dataLayout) が binding 集合を自動生成することは要求しない。必要な集合の関連がないまま texture 参照を含む material 配列を pack する要求は拒否する。

material index は論理配列の要素番号であり、利用側が指定できる。texture selector は backend が wire data に pack する内部表現であり、material index と同じ契約にはしない。参照を含む論理型は `IShaderData` で、`IGraphicsBuffer<T> where T : unmanaged` の T に直接使わない。反射に基づく wire data は byte storage に置き、C# struct の memcpy や `Unsafe.SizeOf<LogicalMaterial>()` で stride を求めない。

### 公開 API

API 差分の比較元は origin/main（Graphics API は未導入）。宣言は本 ADR の対象メンバーの抜粋。

```diff
+namespace Lumyte.Graphics
+{
+    // texture と sampler の組への非所有参照。内部表現と constructor は非公開。
+    // default は無効。nullable field の null は「そのマップなし」として別に扱う。
+    public readonly struct SampledTexture2DReference
+    {
+    }
+
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // 同じ Device、D2 color view、Sampled 用途、sample count 1、float sample type を検証。
+        // Comparison sampler は不可。Filtering／NonFiltering と format は ADR-0011 に従う。
+        // resource を複製せず、GPU コピー・送信をしない。default／失効参照は pack 時にも拒否。
+        public Result<SampledTexture2DReference> CreateSampledTexture2DReference(IGraphicsTextureView texture, Sampler sampler);
+
+        // 論理値と参照の snapshot、serializer／profile／容量／Device／format の検証。
+        // 参照先と fallback を lease し、成功時だけ具象 backend の所有 instance を返す。
+        // GPU buffer の確保、CPU pack、GPU コピー、送信、完了待機を行わない。
+        public Result<IGraphicsMaterialBindings<T>> CreateMaterialBindings<T>(MaterialBindingsDesc<T> desc, ReadOnlySpan<T> materials) where T : IShaderData;
+    }
+
+    public enum MaterialTextureProfile
+    {
+        // wgpu／Browser WebGPU 向け。固定数の個別 bindings と選択 helper。
+        PortableFiniteBindings,
+        // DirectX／Vulkan 向け。descriptor indexing／non-uniform indexing を要求する。
+        NativeDescriptorIndexing
+    }
+
+    // artifact に対応する immutable metadata。直接 constructor は非公開。
+    public sealed class MaterialResourceLayout<T> where T : IShaderData
+    {
+        public MaterialTextureProfile Profile { get; }
+        // fallback 用の組を含む正数の容量。profile ごとの binding／descriptor 上限に照合する。
+        public uint PairCapacity { get; }
+        // 同じ compiled program の生成 serializer と target wire layout。
+        public ShaderDataLayout<T> DataLayout { get; }
+        // checked(count * reflected stride)。正数の要素数を要求、overflow は OverflowException。
+        // GPU copy alignment などは利用者が用意する byte buffer／命令の契約でも検証する。
+        public ulong GetSizeInBytes(ulong count);
+    }
+
+    public sealed record MaterialBindingsDesc<T> where T : IShaderData
+    {
+        public required MaterialResourceLayout<T> Layout { get; init; }
+        // 未使用の固定 binding を埋める、有効な同 Device の texture／sampler の組。
+        // fallback resource の確保・Upload は利用者が明示する。暗黙の GPU resource 生成はしない。
+        public required SampledTexture2DReference UnusedSlotFallback { get; init; }
+    }
+
+    // backend が直接実装。immutable な論理データ snapshot と有限の参照集合を所有する。
+    public interface IGraphicsMaterialBindings<T> : IDisposable where T : IShaderData
+    {
+        public MaterialResourceLayout<T> Layout { get; }
+        public ulong MaterialCount { get; }
+        // 生成 wire data の全 byte 数。内部 selector や descriptor は公開しない。
+        public ulong SizeInBytes { get; }
+        // buffer 範囲、ShaderArguments、記録／Submission に lease がある場合は拒否。
+        // idle 時に参照先の lease と native bindings を解放する。idempotent。
+        public void Dispose();
+    }
+
+    public static class MaterialDataTransfer
+    {
+        // idle な Upload の caller 所有範囲へ生成 serializer で全 snapshot を pack。
+        // destination.SizeInBytes == materials.SizeInBytes、所属 Device と serializer を検証。
+        // 成功時に schema／layout／set の関連を登録する。GPU コピー・確保・送信はしない。
+        public static void CopyFrom<T>(this BufferSlice<byte> destination, IGraphicsMaterialBindings<T> materials) where T : IShaderData;
+    }
+
+    public sealed class DeviceCaps
+    {
+        // program 全体に適用。material 以外の環境 texture や sampler も含める。
+        public uint MaxSampledTexturesPerStage { get; }
+        public uint MaxSamplersPerStage { get; }
+    }
+}
+
+namespace Lumyte.Graphics.Shaders
+{
+    public sealed class ShaderArtifact
+    {
+        // 生成 schema、serializer、profile、library ABI、linked program を照合。
+        // layout を宣言しない artifact は拒否。オンラインコンパイルを起動しない。
+        public MaterialResourceLayout<T> GetMaterialResourceLayout<T>() where T : IShaderData;
+    }
+}
```

profile、PairCapacity、logical schema と material block の構成は artifact の生成時に固定する。target profile、capacity、helper の版を ADR-0005 の cache key と library ABI に含める。binding 集合を作る時に capacity を増やしたり、暗黙にシェーダーを再コンパイルしたりしない。オフラインの場合は利用する capacity の variant を事前コンパイルし、artifact と反射 metadata を DLL に埋め込む。利用者が明示的に compiler provider を呼ぶオンライン方式も同じ metadata を出力する。

### wgpu／Browser WebGPU: PortableFiniteBindings

backend は texture view と sampler の組を同一 instance の組で deduplicate し、内部の有限 selector を割り当てる。format、view dimension、sampler category の適合は各組で検証する。同じ画像でも view または sampler が異なれば別の組とする。初期 material profile は filterable な float D2 color view と通常 sampler に限定する。unfilterable float、integer、depth／comparison、MSAA、異なる dimension はこの参照型では拒否し、専用の型と shader variant を別途設計する。各固定 slot と fallback の sample type／sampler binding category が同じ compiled layout に適合することを検証する。glTF の texture index をそのまま GPU selector にしない。

PairCapacity = N の shader variant は N 個の texture binding と N 個の sampler binding を宣言し、それぞれの組に一対一で対応させる。参照が同じ組なら複数の material field が同じ selector を使用できる。fallback の組は集合に必ず含め、未使用の宣言済み binding にはその有効な組を割り当てる。これは selector 数値の公開や暗黙の texture 確保を必要としない。配列内の logical null は存在 flag を false にして sampling 自体を行わず、glTF の係数だけを使う。

wgpu では通常の texture／sampler 個別 binding で実現する。optional な binding array／descriptor indexing はこの profile の成立条件にしない。library の Slang module は selector を有限の switch へ lowering し、各 branch で具体的な texture／sampler binding を参照する。Slang から生成された WGSL を artifact と反射 metadata で検証する。buffer の値を WGSL の opaque resource object に変換する方式は採らない。

fragment ごとに material index や selector が異なることを許可する。switch 内で暗黙の derivative を使う textureSample を発行すると、WebGPU の uniformity 条件を満たさない可能性がある。そのためこの profile の初期 helper は `sampleGrad` と `sampleLevel` を提供する。sampleGrad の UV と gradient は caller が一様な制御フローで計算して渡し、各 branch は明示 gradient を使う。compute／vertex や明示 LOD では sampleLevel を使う。uniformity 診断を無効化して成立した扱いにしない。

N は program 全体の sampled textures／samplers、bind groups、bindings per group、storage buffers と各 stage の limits に照合する。他の shader 引数が使う環境 map、shadow map なども計数する。必要 bytes と material 数は MaxStorageBufferBindingSize などにも照合する。capacity より少ない実リソースしか使わなくても N 個の宣言に対する上限検証を省略しない。失敗時は必要量と利用可能量の診断を返す。

集合の組が N を超える場合、Runtime／importer が利用者の選択した方針で material と draw をバッチに分け、各バッチに binding 集合と対応 buffer 範囲を用意する。Core の factory は split、別 draw の追加、順序変更を自動で行わない。同じ GPU draw 内で参照できる material は、その draw の引数に取り込まれた一つの集合の範囲に限定する。GPU-driven な draw でも集合を越えた任意の参照は保証しない。

### glTF のマテリアルへの適用

生成する論理型の例は baseColorFactor、metallicFactor、roughnessFactor、emissiveFactor、normalScale、occlusionStrength と最大五つの optional sampled reference を持つ。各マップには texCoord の選択と UV transform を持たせる。alphaMode、alphaCutoff、doubleSided などの draw／pipeline 分類は importer と renderer が行い、resource 集合の生成だけで pipeline 状態を変更しない。

- baseColor と emissive の RGB は sRGB 解釈の view、metallicRoughness／normal／occlusion は線形の view とする。backend が material 名から format を推測しない。
- 同じ画像を sRGB と線形の両方で使う場合は、ADR-0010 の compatible view format 条件を満たすように texture を生成する。実現できなければ importer が別 texture を明示的に用意する。
- metallicRoughness は G=roughness、B=metallic、occlusion は R を使用する。チャンネルの解釈と normal map の復元は material shader の責務とする。
- UV transform による gradient の変換も caller が行う。normal map の tangent／normal、TEXCOORD_0／1 の供給は mesh と shader の契約であり、本 API は生成しない。
- glTF の sampler 省略時の設定とマップ省略時の係数は importer が仕様に従って解決する。optional map の null と無効な default reference を混同しない。

以下は library module が提供する Slang の論理操作の例。実 resource binding 宣言、selector の field、switch 本体は生成側に置く。この helper は ADR-0005 の linked program と同じ BindingPlan を使用する。

```slang
// MaterialData は Slang schema と生成 C# 型に対応する論理型。
MaterialData material = load(args.materials, materialIndex);
float4 baseColor = material.baseColorFactor;
// 勾配はこの branch より前の一様な制御フローで計算済み。
if (material.baseColorTexture.hasValue)
    baseColor *= sampleGrad(material.baseColorTexture.value, uv, uvDdx, uvDdy);
```

### 利用側の明示的な Upload と draw

以下は提案 API の利用例。RequireSuccess は利用者の Result 処理、GltfMaterialData／MaterialDrawArguments は生成型、Texture 用 upload と完了観測は先に利用者が実施済みとする。materialIndex は保存した logical material 配列内の番号。

```csharp
var layout = artifact.GetMaterialResourceLayout<GltfMaterialData>();
using var bindings = RequireSuccess(device.CreateMaterialBindings(
    new MaterialBindingsDesc<GltfMaterialData> {
        Layout = layout,
        UnusedSlotFallback = fallbackReference,
    }, logicalMaterials.AsSpan()));
ulong byteCount = bindings.SizeInBytes;
using var upload = RequireSuccess(device.CreateBuffer(new BufferDesc<byte> {
    Count = byteCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
}));
using var gpu = RequireSuccess(device.CreateBuffer(new BufferDesc<byte> {
    Count = byteCount, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead,
}));
// CPU pack のみ。set と schema の関連もこの領域に記録する。
upload.Slice(0, byteCount).CopyFrom(bindings);
using var encoder = RequireSuccess(device.CreateCommandEncoder());
encoder.RecordCopyBuffer(upload.Slice(0, byteCount), gpu.Slice(0, byteCount));
// 利用者が transfer write → shader read の依存を構築している。
encoder.Barrier(materialUploadToShaderRead);
var materialRef = device.CreateReference<GltfMaterialData>(gpu.Slice(0, byteCount));
var args = frame.CreateArguments(argumentsLayout, new MaterialDrawArguments {
    Materials = materialRef, MaterialIndex = materialIndex,
});
using (var render = encoder.BeginRenderPass(passDesc)) {
    render.SetPipeline(pipeline);
    render.Draw(args, drawDesc);
}
using var commands = encoder.Finish();
var completion = RequireSuccess(device.Submit(commands));
await completion.WaitAsync();
// frame の引数 lease も終了してから buffer／bindings／view／sampler を解放する。
```

### 範囲の関連、寿命、変更

MaterialBindings は snapshot の全参照先 view／texture／sampler と fallback を lease する。CPU pack 成功時に対象 Upload 範囲が set を lease し、完全な GPU コピーはコピー先範囲にも同じ set の lease を登録する。binding 集合そのものを一度生成しても、依存の登録された buffer 範囲がある間は Dispose を拒否する。範囲の上書きによる登録失効、buffer の Dispose、引数の終了でそれぞれの lease を返し、最後の lease と GPU 使用が終了してから set と参照先を解放する。

コピー先の metadata は記録時に「当該コピーにより確定する予定の値」として関連付け、コピーより後の command だけで参照可能にする。同じ Encoder の後続 draw は前述の明示 Barrier で利用できる。別 Submission ではコピー完了を観測するか、利用者が対応する queue 依存を明示する。記録したコピーの破棄・送信失敗・DeviceLost では予定の登録を失効させ、未実行の wire data を有効な参照として扱わない。

metadata を維持するコピーは登録済みの source 全範囲と一致し、destination の layout と関連する set が適合する場合に限る。部分コピー、raw byte 上書き、別 set の関連付けは重複した登録を失効させる。失効した範囲の CreateReference／引数 pack は拒否する。マテリアルの再編成や参照追加は新しい set を生成して再 pack／転送する。別 set の selector bytes を流用しない。同じ compiled layout を使う別 set でも identity と世代を照合する。

参照 selector を含む material wire data は本 ADR の両 profile で shader-read-only。GPU による任意 selector の生成・書き換えは対応しない。GPU material index は load helper が配列範囲を検証し、範囲外では schema が artifact に登録した、texture 参照を持たない診断用の既定 material を返す。glTF 用の schema では baseColorFactor を magenta、metallicFactor=0、roughnessFactor=1、emissiveFactor=0、全 map を null とする。sampling helper の範囲外 selector は既定値 float4(0) を返し、未束縛 resource や無効 descriptor を参照しない。logical default reference、別 Device、型・layout 不一致は CPU 側で先に拒否する。

### backend と Slang が実装するもの

| profile／backend | resource と wire data の受け渡し |
| --- | --- |
| PortableFiniteBindings／managed wgpu | finite selector と存在 flag を buffer に pack。固定 texture／sampler 個別 bindings と Slang の switch helper、明示 gradient／LOD で解決。独自 .Native は不要 |
| NativeDescriptorIndexing／DirectX | 対応 Shader Model／binding tier／non-uniform indexing を要求。Native が descriptor を配置して内部 index を pack、Slang が同じ layout で参照する |
| NativeDescriptorIndexing／Vulkan | descriptor indexing の必要な feature、limits、non-uniform decoration と descriptor layout を確認。必須 feature がなければ拒否する |
| Browser WebGPU | PortableFiniteBindings を同じ WGSL と metadata で使用。JS／Wasm 側が binding と snapshot を所有し、ホスト Native を要求しない |

DirectX／Vulkan は NativeDescriptorIndexing のみを提供し、PortableFiniteBindings は実装しない。wgpu／Browser WebGPU は PortableFiniteBindings を使用する。artifact の profile と backend が不適合な場合、または DirectX／Vulkan で descriptor indexing の必須能力がない場合は UnsupportedTarget／UnsupportedFeature として拒否し、別 profile へ自動切り替えしない。NativeDescriptorIndexing の PairCapacity は descriptor heap／descriptor set の容量と indexing の limits に照合する。PortableFiniteBindings の N 個の texture／sampler 個別 bindings と有限 switch の制約を NativeDescriptorIndexing に適用しない。descriptor の live な値を GPU buffer 内に保持する場合も Device、set、schema、世代との関連を維持し、利用者に整数を公開しない。任意の GPU address に texture object があるとは仮定しない。

共通 Core は論理参照・layout・set・範囲の契約を保持し、backend はその interface を実装する具象 instance 内で descriptor／bind group と native リソースを管理する。Slang composition／specialization と反射から helper と serializer を生成し、offline／online と native／WGSL で同じ library ABI を照合する。Slang に `SampledTexture2DReference` をそのまま WGSL resource handle として出力することは要求せず、論理参照を selector と bindings に明示的に lowering する。

### エラーと検証方針

範囲、無効 default、別 Device、失効した set、型・layout 不一致は引数例外または ObjectDisposedException、使用中変更・Dispose は InvalidOperationException。有効な要求の未対応 profile／format／能力不足は Result の GraphicsError と構造化診断を返す。失敗した factory／pack は新しい関連や lease を残さず、GPU 転送も行わない。pack は生成 byte 数と参照解決を先に検証し、書き込み開始後の native memory failure では対象範囲の関連を失効させる。都合のよい別 image や別 sampler に自動置換しない。

実装時には共通 API だけで次を検証する。

- 異なる materialIndex の要素が異なる画像を選ぶこと、同じ画像と異なる sampler の区別、同じ組の共有、optional map の係数、UV transform と glTF の channel 解釈。
- fragment ごとに selector が異なる場合の sampleGrad／sampleLevel と WGSL uniformity 検証。上限ちょうどの集合、fallback 分を含む容量超過、他の shader resource との合計上限。
- CPU pack と GPU copy／Submit の分離、完全コピーの関連伝播、部分上書きの失効、コピー前の利用拒否、未送信破棄と DeviceLost の登録回収。
- view／sampler／set の lease、引数と pipeline の layout 不一致、別 set の bytes の拒否、異なる Device、GPU 完了前の Dispose、frame 終了後の解放順。
- Slang の反射 stride と serializer の一致、オフラインの DLL 埋め込みとオンラインの同一 ABI、backend と profile の不適合および native profile の必須 feature 拒否、material index／selector 範囲外の安全な既定値。

これらは本 ADR の検証方針であり、今回実行済みのテスト結果ではない。初期実装は [ADR-0008](0008-wgpu-first-backend.md) のままで、Sampler、一般 serializer、material bindings、finite switch helper、独立 Barrier は未実装。

## 検討した代替案

### storage buffer に texture／sampler object や native handle をそのまま格納する

WebGPU で表現できず、型・Device・寿命を保証できない。buffer には backend と Slang が共有する wire 表現を pack し、実 resource は明示的な有限 binding 集合で保持する。

### 全ターゲットで無制限の bindless resource array を必須にする

標準 WebGPU と device limits に適合しない。wgpu／Browser WebGPU には有限 binding、DirectX／Vulkan には必須能力を確認した descriptor indexing を提供する。共通にするのは論理参照、pack と寿命の API とし、物理的な解決方式を全ターゲットへ強制しない。

### draw ごとに CPU が単一 material の texture だけを選ぶ

単純な renderer では有用だが、同じ draw の中で GPU が複数 material を選ぶ目的を満たさない。有限集合内の選択を共通経路にし、上限を越える場合のバッチ分割を利用側が選べるようにする。

### backend が全 material を自動 atlas 化する

format、色空間、sampler、wrap、mip、UV transform の意味や resource の所有権を変え得る。atlas／texture array への変換は importer／Runtime の明示的な別設計とする。

## 結果と影響

- material の係数と参照を一つの GPU 配列として扱い、共通 API のまま GPU で material と texture を選択できる。
- serializer、Slang helper、resource bindings、範囲 metadata の ABI と寿命を揃える必要がある。
- wgpu／Browser WebGPU の portable profile は同時参照集合と shader binding 数に上限があり、シーン全体を一つの draw に集約できるとは保証しない。
- glTF の logical material と GPU wire data を分離し、参照先や schema を変更した場合は再 pack／転送する。
- 本 ADR は設計の提案。実装と GPU 検証は別の作業で行う。
