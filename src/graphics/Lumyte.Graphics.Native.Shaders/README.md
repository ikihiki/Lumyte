# Native shader runtime

`Lumyte.Graphics.Native.Shaders` は、展開済み package から DirectX 12／Vulkan に一致する shader artifact を選ぶ。参照先は `Lumyte.Graphics.Native` だけとし、backend、compiler、`Lumyte.Resources`、Portable shader library に依存しない。[ADR 0015](../../../docs/adr/0015-native-shader-package-api.md) の runtime 部分を実装する。

## API

| 型・操作 | 契約 |
| --- | --- |
| `NativeShaderTarget` | `DirectX12` と `Vulkan`。backend の公開 `ShaderCodeFormat` で対象を判断する。 |
| `NativeShaderCapabilities` | artifact が要求する raw pointer、buffer descriptor、mesh、amplification の flags。device へ機能を追加要求する値ではない。 |
| `NativeShaderDescriptorHeapAbi`／`NativeShaderDescriptorHeapAbiKind` | descriptor index の解釈方式と version。`None`、`DirectX12`、`VulkanUnified`、`VulkanFixed(layout)` を持つ。 |
| `NativeShaderStageArtifact(stage, entryPoint, code)` | stage と entry、所有済み raw bytes。構築時に bytes をコピーし、`Code` は割当てを行わない `ReadOnlySpan<byte>` を返す。shader を解析しない。 |
| `NativeShaderInputField`／`NativeShaderInputFieldKind` | 名前、scalar／vector／matrix／GPU address／descriptor index の種別、byte offset と size。資源参照の列挙や所有を表さない。 |
| `NativeShaderInputLayout(abiId, size, alignment, fields)` | 不変の byte 配置。field 列をコピーし、重複名、layout 外の field、表現できない alignment を拒否する。 |
| `NativeShaderArtifact` | target、format、stage 列、要求機能、descriptor ABI、root／parameter layouts、生成 host 型に対応する `AbiHash`。列は構築時にコピーする。 |
| `NativeShaderPackage(version, artifacts)` | 一つの論理 program の展開済み CPU 入力。`CurrentVersion` は1。artifact 列をコピーし、file や stream を持たない。 |
| `NativeShaderLoader(native).Load(package, expectedAbiHash = null)` | 借用 backend の format、有効機能と descriptor ABI に合う artifact を選ぶ。任意の expected hash は選択後に ordinal 比較する。GPU object を作らない。 |
| `NativeShaderProgram` | 選択済み `Code`、`Target`、`RootLayout`、`ParameterLayouts`、`AbiHash`。`Code` は既存の `NativeGpuShaderProgram`。 |
| `NativeShaderProgram.Dispose()` | program の CPU 側保持を解放する。以後の property 取得を拒否し、backend／pipeline の破棄や GPU wait は行わない。 |

program の stage 構成は Compute、Vertex＋任意 Pixel、Mesh＋任意 Amplification＋任意 Pixel とする。Mesh／Amplification の要求機能は stage 構成から必ず追加し、空の要求 flags によって非対応 device を選ばない。

複数の artifact が一致した場合は `InvalidOperationException`、一致なし・未知の package version／descriptor ABI は `NotSupportedException` とする。先頭候補への暗黙優先や pointer から descriptor への変換は行わない。`expectedAbiHash` の不一致は `InvalidOperationException` とする。hash は compiler と生成 host 型が共有する不透明な文字列として扱い、runtime で shader code や任意の C# 型から再計算しない。

## Descriptor ABI

`DirectX12` は opaque な heap indexing の契約で、byte-based な descriptor limits を要求しない。`VulkanUnified` version1 は Slang の unified descriptor heap が生成する resource stride `max(imageDescriptorSize, bufferDescriptorSize)` と sampler stride `samplerDescriptorSize` に、device の公開 slot stride が一致する場合だけ選ぶ。host の alignment 計算による padding を勝手に shader の式へ追加しない。`VulkanFixed(layout)` は code に埋め込んだ size／alignment／stride と device の値をすべて一致させる。descriptor を参照しない artifact は `None` を使える。

これは artifact の addressing と backend の ABI を一致させる処理である。shader の合法性、entry の存在、root の device 上限、dispatch 数、native pipeline の linkage は compiler／native の検証に委ねる。package の形式、圧縮、破損検出は container を展開する CPU resource 層が担当する。

## 所有と使用例

`package` は `Lumyte.Resources` 等が準備した不変 CPU data、`native` は作成済み `INativeGpuBackend` とする。loader は native を所有しない。program はロードごとに独立した code bytes を持ち、低レベル `Code` の memory を caller が変更しても package や別の program に伝播しない。

```csharp
using Lumyte.Graphics.Native.Shaders;

var loader = new NativeShaderLoader(native);
using var program = loader.Load(package, expectedAbiHash: "my-native-program-v1");
var pipeline = native.CreateComputePipeline(program.Code);
program.Dispose();

// pipeline は作成時に必要な code を消費・保持する。
// caller はこの pipeline を用いる GPU work を完了させてから破棄する。
native.DestroyComputePipeline(pipeline);
```

root／Parameter Data は layout 情報から loader が構築しない。caller が対応する生成型を使い、明示した pointer/index を root に設定する。upload、resource 寿命、pipeline の保持と世代更新は caller または Native Resources が担当する。program の `Code` を pipeline 作成へ渡している間は program を生存させ、同時に Dispose しない。

## 実装範囲

メモリ上の入力型、artifact 選択と host 側保持を実装した。隣接する `.Tests` は GPU を使わず、不変入力、ABI／機能選択、stage の受渡しと program の独立した所有を確認する。実 GPU の package 経由試験は DirectX12／Vulkan の test project に配置する。

offline compiler、container 出力、生成 C# 構造体、Resources の decoder、pass factory／RenderGraph の接続は後続とする。ファイル読込み、デシリアライズ、runtime compilation、Parameter Data の自動生成や root の buffer fallback はこの runtime の API に含めない。
