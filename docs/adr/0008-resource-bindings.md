# ADR-0008: 論理 Argument Tableによる bindless API

- 状態: 採用
- 日付: 2026-10-07
- 更新日: 2026-10-08

## 背景

NoGraphicsAPI のように、Argument Table へ resource descriptor を登録し、その参照を GPU データへ格納する利用 API を共通化する。利用側がマテリアルごとに物理 binding 集合を構築すると、WebGPU の配置・容量と shader variant を意識する必要がある。共通 API は論理的な登録と参照を扱い、実際の descriptor heap、descriptor set、bind group の構築は backend に閉じる。

[ADR-0005](0005-buffer-resource-contract.md) の buffer、[ADR-0006](0006-texture-resource-contract.md) の texture／view、[ADR-0007](0007-sampler-resource-contract.md) の sampler を登録対象とする。Slang と serializer は [ADR-0010](0010-shader-compilation-and-data-interop.md) に従う。Core にマテリアルや buffer 内容の構造体を置かず、論理型と Slang wire 型は利用側が所有する。

本 ADR は登録・参照・所有権の公開契約を扱う。WebGPU の有限 bindings へ調整する参照追跡、CPU 対応表、変換表と Slang helper は [ADR-0012](0012-bindless-binding-lowering.md) に分離する。wgpu の対応範囲と公開 API は [ADR-0011](0011-wgpu-first-backend.md) に記載する。main へ未導入の Graphics API は本 PR 内でこの設計に更新し、別の旧 API 契約や移行履歴を残さない。

## 決定

### 論理 Argument Table と不透明な参照

利用者は Device に属する論理 Argument Table を生成し、論理 slot を選んで descriptor を書く。slot は利用者が管理する登録位置であり、GPU descriptor index、shader binding 番号、GPU address ではない。書き込みは型付きの不透明な参照を返す。参照には内部で Device、table identity、slot、世代と型を関連付けるが、その数値表現・constructor・整数変換・物理表現の serialization は公開しない。複数の Argument Table で同じ slot 番号を使っても別の登録になる。

Argument Table は texture、sampler、buffer の論理領域を別々に持つ。texture と sampler を一対一の組として登録しない。同じ sampler を複数 texture が共有できる。論理 Argument Table の容量は登録可能数であり、同時に一つの draw／dispatch が参照できる数を保証しない。Argument Table 数や物理配置も backend の binding 数と一対一には対応しない。

共通 API にマテリアル用の texture profile や物理 binding 方式の選択肢を設けない。参照の物理表現と解決方法は backend が決定する。利用者は登録対象の型・用途・範囲を指定し、backend は対応可否と実際の制約を検証する。

公開登録先は backend が直接実装する IArgumentTable とする。一つの table に texture view、buffer range、sampler を種類別の slot へ登録できる。Metal の native Argument Table などの物理オブジェクトと一対一であることは要求せず、backend が到達集合に応じて物理配置を構築する。

Texture の初期参照型は sample count 1 の filterable float D2 color view、Sampler は通常 filtering／non-filtering、buffer は読み取り用に限定する。comparison、integer、storage texture、GPU から書き換える参照は型・操作・同期を別途具体化する。native backend でも既知の未対応契約を有効として報告しない。

### 利用側 API と Desc

API 差分の比較元は origin/main（Graphics API は未導入）。以下は設計宣言。API 未導入の main からの追加として表示する。共通 Desc 規約は ADR-0004 に従う。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record ArgumentTableDesc
+    {
+        // 診断専用。物理ヒープの指定ではない。
+        public string? Label { get; init; } = null;
+        // 種類ごとの論理登録数。0 はその種類を登録しない。合計は正数。
+        // native allocation／backend が対応可能な容量を生成時に検証する。
+        public uint TextureCapacity { get; init; } = 0;
+        public uint SamplerCapacity { get; init; } = 0;
+        public uint BufferCapacity { get; init; } = 0;
+    }
+
+    public sealed class GraphicsDevice
+    {
+        // Desc を snapshot。成功時だけ backend の具象所有 instance を返す。
+        // resource の生成・Upload・コピー・送信は行わない。
+        public Result<IArgumentTable> CreateArgumentTable(ArgumentTableDesc desc);
+    }
+
+    // backend が直接実装。native handles と登録表は具象 instance 内で管理。
+    public interface IArgumentTable : IDisposable
+    {
+        public uint TextureCapacity { get; }
+        public uint SamplerCapacity { get; }
+        public uint BufferCapacity { get; }
+        // 空き slot または未参照の既存 slot に descriptor を設定する。
+        // 同じ Device、Sampled 用途、view 型を検証し、view とその元 texture を lease。
+        public TextureDescriptorReference WriteTexture(uint slot, IGraphicsTextureView view);
+        // 同じ Device と sampler category を検証し、sampler を lease。
+        public SamplerDescriptorReference WriteSampler(uint slot, Sampler sampler);
+        // 数値型や unmanaged struct の読み取り用領域。用途・範囲・alignment を検証。
+        // buffer を lease。GPU data の再配置やコピーはしない。
+        public BufferDescriptorReference<T> WriteBuffer<T>(uint slot, BufferSlice<T> range) where T : unmanaged;
+        // nested schema 対応時の拡張。serializer schema を持つ GPU data 範囲。単一要素または明示した配列範囲。
+        // buffer 登録領域を共有し、schema／世代／範囲を snapshot する。
+        public GpuReference<T> WriteShaderData<T>(uint slot, GpuReference<T> range) where T : IShaderData;
+        // metadata／引数／記録／GPU 使用から参照中なら InvalidOperationException。
+        // 成功時に登録を無効化し、世代を進め、resource lease を返す。
+        public void ReleaseTexture(uint slot);
+        public void ReleaseSampler(uint slot);
+        public void ReleaseBuffer(uint slot);
+        // 登録が参照中なら拒否。idle 時に全登録と resource lease を解放。idempotent。
+        public void Dispose();
+    }
+
+    // 内部表現・constructor は非公開。default は無効。null はマップ省略として別に扱う。
+    public readonly struct TextureDescriptorReference;
+    public readonly struct SamplerDescriptorReference;
+    public readonly struct BufferDescriptorReference<T> where T : unmanaged;
+
+    public interface IShaderDataWriter
+    {
+        // Slang reflection／schema で宣言された参照 field に対応する wire data を pack。
+        // 同時に要素ごとの CPU dependency metadata を記録。数値 descriptor を返さない。
+        public void WriteTextureReference(string fieldName, TextureDescriptorReference? reference);
+        public void WriteSamplerReference(string fieldName, SamplerDescriptorReference? reference);
+        public void WriteBufferReference<T>(string fieldName, BufferDescriptorReference<T> reference) where T : unmanaged;
+        // nested schema 対応時の拡張。wgpu の初期 top-level writer には含めない。
        public void WriteDataReference<T>(string fieldName, GpuReference<T> reference) where T : IShaderData;
+    }
+}
```

Write は descriptor の登録であり、texel や buffer 内容の転送ではない。初回登録では shader read に必要な resource 用途と範囲を検証し、内容の準備・同期は利用者が明示する。CPU pack、Upload buffer 確保、RecordCopyBuffer／RecordCopyBufferToTexture、Barrier、Finish、Submit と完了待機は従来どおり別操作とする。

### 構造体と root 引数

利用者の論理構造体は texture／sampler／buffer の不透明参照と係数を持つ。serializer は Slang の反射 layout へ pack し、通常の unmanaged struct の memcpy と混同しない。GPU data の内容を定義する型を Core に追加しない。

root 引数も同じ参照契約を使い、通常の draw では「GPU buffer の特定要素」を表す GpuReference を渡す。schema と reflected stride に従った単一要素の byte slice から CreateReference を作れば、material index を別の整数として重複指定する必要はない。配列参照が必要な draw は、その配列範囲を参照として渡す。整数 index だけでは CPU 側が到達範囲を絞れないため、shader が動的に選ぶ可能性のある全要素を対象にする。追跡と binding 構築の契約は ADR-0012 に従う。

利用者は物理 binding 集合の作成や slot の割り当てを操作しない。draw／dispatch は ShaderArguments を受け取り、backend がその到達可能な参照を処理する。GraphicsRuntime／FrameContext の引数領域の寿命・遅延解放は Submission の完了に結び付ける。具体的な Runtime factory の設計は ADR-0009／0010 の契約を使用する。

### 利用例

以下は提案 API の抜粋。RequireSuccess は利用側の Result 処理。textureView／sampler は利用者が生成し、texture の転送・完了を観測済み。MaterialData／DrawRoot と serializer は利用側が定義する。materialStrideInBytes は Slang の反射 layout から解決した値とする。

```csharp
using var table = RequireSuccess(device.CreateArgumentTable(new ArgumentTableDesc {
    TextureCapacity = 20, SamplerCapacity = 1,
}));
var textureRef = table.WriteTexture(0, textureView);
var samplerRef = table.WriteSampler(0, sampler);
var materials = new MaterialData[] {
    new MaterialData { BaseColorTexture = textureRef, Sampler = samplerRef },
};
// caller 所有の Upload 範囲に pack。shader schema と要素依存を記録する。
uploadRange.CopyFrom(materials.AsSpan(), materialLayout, materialSerializer);
using (var transfer = RequireSuccess(device.CreateCommandEncoder())) {
    transfer.RecordCopyBuffer(uploadRange, gpuRange);
    transfer.Barrier(uploadToShaderRead);
    using var transferCommands = transfer.Finish();
    var uploaded = RequireSuccess(device.Submit(transferCommands));
    await uploaded.WaitAsync();
}
// 単一要素の参照を root に渡す。別途 binding 集合を作らない。
var element = gpuRange.Buffer.Slice(gpuRange.Offset, materialStrideInBytes);
var material = device.CreateShaderDataReference<MaterialData>(element);
var args = frame.CreateArguments(rootLayout, new DrawRoot { Material = material });
using var encoder = RequireSuccess(device.CreateCommandEncoder());
using (var render = encoder.BeginRenderPass(passDesc)) {
    render.SetPipeline(pipeline);
    render.Draw(args, drawDesc);
}
using var commands = encoder.Finish();
var completion = RequireSuccess(device.Submit(commands));
await completion.WaitAsync();
// frame／metadata の lease も終了してから table と参照先を解放する。
```

Argument Table の容量20は一つのdrawのtexture binding数ではない。この例のrootは単一要素を指すため、その要素の一枚と一つのsamplerだけが到達集合に入る。

### backend の実装責務

DirectX／Vulkan は native descriptor indexing の必要な能力を確認し、論理登録を native descriptor へ対応付ける。実 GPU address、descriptor placement、non-uniform indexing と Slang の wire ABI は backend 内部で合わせる。buffer は対応する buffer device address を使って shader から直接参照してよく、buffer ごとの descriptor binding を必須にしない。texture／sampler は対応する descriptor 機構を使用し、通常の buffer address から直接アクセスできると仮定しない。必須能力不足は明示的に拒否し、DirectX／Vulkan 向けに有限個別 binding の fallback は提供しない。

wgpu／Browser WebGPU は論理登録表と、ADR-0012 の到達可能参照からの有限 binding 構築を実装する。標準 WebGPU が任意の GPU pointer や無制限の descriptor array を持つとは仮定しない。wgpu は既存 .NET binding を直接使い、独自の .Native は作らない。

### 所有権、世代と変更

登録は resource を lease し、参照値そのものは非所有。serializer の metadata、root arguments、記録済み commands と Submission は必要な登録世代を lease する。参照された slot の上書き・Release は、対応 metadata が失効・解放され、引数・記録・GPU 使用の lease も終了するまで拒否する。別の空き slot に登録して新データを明示 Upload することはできる。

新しい世代は古い参照を復活させない。Device 所属、slot の世代、型と buffer の内容世代を pack／引数構築／記録で再検証する。Argument Table を解放しても他の Argument Table で同じ slot を使う参照へ誤って解決しない。Device は Argument Table と全 GPU 使用が終了してから解放する。初期契約は登録更新と記録を利用側で直列化する。

## 検討した代替案

### 利用側が有限 binding 集合をマテリアル配列ごとに生成する

利用側へ集合容量と分割を露出するため採用しない。共通 API はArgument Table への登録と型付き参照に統一し、有限集合の構築を backend へ置く。

### native descriptor index をそのまま公開する

GPU データは単純になるが、Device・型・世代・backend の違いを利用側へ公開する。論理 slot と不透明な参照を分け、物理表現を backend と Slang の境界に保持する。

### 全登録を全 draw に束縛する

登録数が多いシーンでは WebGPU の上限へ到達し、一つのマテリアルだけを使う draw も実行できなくなる。登録可能数と draw の到達集合を分離する。

## 結果と影響

- Argument Table へ登録して構造体に参照を格納する、bindless 型の共通 API になる。
- texture、sampler、buffer の登録・重複排除・上限を個別に扱える。
- WebGPU では draw の到達集合に上限があり、論理 Argument Tableの全登録へ無制限にアクセスできる保証はない。
- backend は登録表、世代、lease と serializer／Slang の ABI を実装する必要がある。
- backend ごとの対応範囲は ADR-0011 に記録し、未対応の shader schema と機能を明示的に拒否する。

## 検証方針

共通 API のみで、複数の Argument Tableの同一 slot の区別、texture と sampler の独立登録、同一 sampler の共有、型・用途・別 Device・default・失効参照・容量違反、metadata／記録／GPU 使用中の変更と Dispose の拒否を確認する。descriptor 登録と CPU pack が user resource の Upload・GPU コピー・送信を行わないことを検証する。bindless lowering の GPU 動作は ADR-0012 の検証方針に従う。これらは未実行の検証方針である。

## 参考資料

- [グラフィックデバイス](0004-graphics-device.md)
- [シェーダーと GPU data の受け渡し](0010-shader-compilation-and-data-interop.md)
- [bindless 参照の追跡と有限 binding への変換](0012-bindless-binding-lowering.md)
- [wgpu の対応範囲](0011-wgpu-first-backend.md)
- [NoGraphicsAPI descriptor heap](https://github.com/sebbbi/NoGraphicsAPI/blob/04004140f5b3b8ec7c566fd43bfed77d586155f8/include/NoGraphicsAPI/NoGraphicsAPI.hpp#L748)
