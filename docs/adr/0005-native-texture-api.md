# ADR 0005: Native Texture API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0001: Graphics の層構造と共通契約](0001-graphics-api.md) の format/layout と、[ADR 0003: Native Memory Allocation API](0003-native-memory-allocation-api.md) の共通 heap・requirement に依存する。混在配置の使用例は [ADR 0004: Native Linear Data API](0004-native-linear-data-api.md) の線形 region を使う。

## 決定

texture identity と backing allocation を分け、caller が指定した `NativeGpuHeap` と offset に texture を配置する。線形 region と同じ確保 API を使い、native 条件が許す場合は同じ heap に同居させる。texture の破棄は allocation の破棄を意味しない。

format の追加の再解釈は `MutableFormat` で指定する。許可 format の列挙、詳細 format capability と初期 state の照会 API は設けない。

## API

以下の要件取得・生成・破棄操作は `INativeGpuBackend` の member とする。

| API | 契約 |
| --- | --- |
| `NativeGpuTextureDimension` | `OneD`、`TwoD`、`ThreeD`。array/cube の解釈は view が指定する。 |
| `NativeGpuTextureAspect` | `Color`、`Depth`、`Stencil`、`DepthStencil`。texture 範囲の aspect。copy の `Aspect` は先頭の三つのいずれか一つだけを指定する。 |
| `NativeGpuTextureUsage` | `Sampled`、`Storage`、`ColorAttachment`、`DepthStencilAttachment`、`CopySource`、`CopyDestination` の flags。 |
| `NativeGpuTextureDescription` | dimension、width/height/depth、mip/layer count、sample count、format、usage と `MutableFormat`（bool、default false）。 |
| `GetTextureMemoryRequirements(description, kind)` | 指定 texture と memory kind の配置に必要な共通の `NativeGpuMemoryRequirements` を返す。 |
| `NativeGpuTextureHandle` | device に属する opaque texture identity。public abstract 基底型と protected constructor から各 backend が非公開派生型を実装する。heap の所有権は取得しない。 |
| `CreateTexture(description, heap, offset)`／`DestroyTexture(texture)` | caller-owned `NativeGpuHeap` へ配置し、texture だけを破棄する。memory kind は `heap.Kind` に従う。 |
| `NativeGpuTextureCopyFootprint` | `Mip`、`Aspect`、`BaseLayer`／`LayerCount`、`Origin`、`Extent`、byte 単位の `RowPitch`／`ImagePitch` を指定する非所有の転送配置。 |

`NativeGpuTextureDescription` は作成時の dimension、extent、mip/layer 数、sample count、format、usage と `MutableFormat` を指定する。`GetTextureMemoryRequirements` が返す予約 `Size` と `Alignment` に従って配置し、`Compatibility` を変更せず共通 heap の確保へ渡す。他の resource と同居させる場合はその requirement も確保時の列に含める。texture 自体から通常の CPU/GPU pointer は公開しない。

NoGraphicsAPI と同様に `MutableFormat` は bool、default false とする。false でも作成 format の基本用途、depth/stencil の aspect 解釈と sampled depth を維持する。true は color format の再解釈など native で可能な追加の互換 format を許可し、未対応の組を保証しない。native 作成処理と診断が互換条件を扱う。

新しい texture の内容は不定である。DirectX 12 は `Undefined` で生成し、caller が最初の用途の layout へ明示的に遷移させる。Vulkan は backend が使用前の `GENERAL` 初期化を順序付け、通常利用はその layout のまま caller が global dependency を明示する。この初回処理は、他の alias が書き込んだ後の再初期化を代行しない。再利用時は caller が影響する subresource を command で明示的に discard／再初期化する。初期条件は生成契約に固定し、別の照会や property では公開しない。内容の初期化と使用間の同期は caller の責務である。

`NativeGpuTextureCopyFootprint.Aspect` は `Color`、`Depth`、`Stencil` の一つを必須入力とする。depth/stencil を一度に interleaved bytes として転送せず、必要なら footprint と range を分けて二回 copy する。選択していない aspect を copy は変更しない。

転送 range の先頭を byte 配列の原点とする。`RowPitch` は次の texel block 行まで、`ImagePitch` は次の 2D slice までの byte 間隔である。2D array では一つの slice が一つの layer、3D では一つの slice が一つの depth block 面に対応する。`Origin`／`Extent` は texture 側の texel 座標で、`Mip` と `BaseLayer`／`LayerCount` が対象 subresource を選ぶ。非圧縮では一 texel が一 block、圧縮 color では format の block の大きさで pitch を計算する。depth/stencil の転送 element は以下とする。

| 選択 aspect と format の成分 | 一 texel の転送 bytes |
| --- | --- |
| `Depth`、16-bit UNORM | 2 byte の unsigned normalized 値。 |
| `Depth`、24-bit UNORM | 4 byte の下位 24 bit に値を格納する。upload の上位 8 bit はゼロ、readback では上位 8 bit を内容として利用しない。 |
| `Depth`、32-bit float | 4 byte の IEEE 754 float。stencil を含む format でも depth だけを格納する。 |
| `Stencil`、8-bit unsigned | 1 byte の unsigned 値。depth の byte 領域は含めない。 |

この配置は線形転送 bytes の契約であり、texture 内部の memory layout ではない。byte order は対応 native API の転送表現に従う。format の意味、component の bit 幅、pitch の native 整列条件を caller が満たす。wrapper は staging allocation、interleave／deinterleave、format 変換、pitch の補正を行わない。native の plane/aspect への写像と制約は各 backend の実装 ADR に置く。

## コード配置

パスは repository root 相対とする。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済み、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。aspect・copy footprint と転送の配置は後続実装の目標を含む。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Textures/` | texture description、dimension／aspect／usage、handle と copy footprint。要件取得・生成・破棄 member は `Device/INativeGpuBackend.cs` に宣言する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Textures/` | native description と MutableFormat の変換、`Undefined` の placed texture の生成・破棄。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Textures/` | image description、heap への bind、MutableFormat と使用前の `GENERAL` 初期化を接続する局所状態。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Device/ExternalNativeGpuBackendTests.cs` | 公開契約だけで外部 backend が opaque texture と混在配置を提供できることを確認する consumer test。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Textures/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Textures/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Textures/` | description の native 変換、後続の footprint の aspect・pitch の変換と初期化の失敗境界を確認する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Textures/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Textures/` | 混在配置、初回使用と depth／stencil の独立した upload／readback を実 GPU で確認する試験。 |

image のデコードやファイル取得はここに置かず、GPU へ渡された description と data の処理に限定する。

## 使用例

`native` は生成済みの `INativeGpuBackend` とする。`MutableFormat` の既定値は false とし、一つの heap に線形 data と texture を配置する。共用できない device／組合せでは確保が失敗する。この例では GPU に使用させない。

```csharp
static ulong AlignUp(ulong value, ulong alignment)
    => checked((value + alignment - 1) / alignment * alignment);

var kind = NativeGpuMemoryKind.GpuOnly;
var description = new NativeGpuTextureDescription(
    NativeGpuTextureDimension.TwoD,
    Width: 256, Height: 256, Depth: 1,
    MipCount: 1, LayerCount: 1, SampleCount: 1,
    Format: GpuFormat.Rgba8Unorm,
    Usage: NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopyDestination);
const ulong dataSize = 4096;
var dataRequirements = native.GetLinearMemoryRequirements(dataSize, kind);
var textureRequirements = native.GetTextureMemoryRequirements(description, kind);
ulong alignment = Math.Max(dataRequirements.Alignment, textureRequirements.Alignment);
ulong textureOffset = AlignUp(dataRequirements.Size, textureRequirements.Alignment);
ulong size = AlignUp(checked(textureOffset + textureRequirements.Size), alignment);
var heap = native.CreateGpuHeap(size, alignment, kind,
    [dataRequirements.Compatibility, textureRequirements.Compatibility]);
try
{
    var data = native.CreateLinearRegion(dataSize, heap, 0);
    try
    {
        var texture = native.CreateTexture(description, heap, textureOffset);
        native.DestroyTexture(texture);
    }
    finally { native.DestroyLinearRegion(data); }
}
finally { native.DestroyGpuHeap(heap); }
```

offset 0 と `textureOffset` はそれぞれの requirement に整列し、予約範囲を重ねていない。配置位置と寿命は caller が管理し、同じ範囲の再利用には別途必要な同期を行う。

## 検証方針

native requirement の取得と配置情報の変換を確認する。usage、format、dimension、sample count、memory の適合性は native 作成・validation に委ねる。現在 layout と lifetime の汎用 tracker は作らない。

## 採用差分と未実装範囲

caller-owned heap への texture 配置と MutableFormat を採用する。線形 resource と共通の allocation、DirectX 12 の明示 layout と opaque compatibility、単一 aspect の copy footprint は補足である。

description、opaque handle、requirement 取得と heap への明示配置・独立破棄を両 backend に実装した。要件取得と生成は同じ native description 変換を使う。GPU を使った描画や転送前の配置処理として、dimension、array／mip、MSAA、format、MutableFormat、混在配置と破棄後の heap 再利用を検証した。特定 GPU の成功は他の device や組合せの保証にはしない。

aspect・copy footprint、view、初回利用の layout 遷移、alias 再初期化と depth／stencil の独立転送は未実装である。Vulkan の `GENERAL` 初期化義務は非公開状態に保持するが、command／submit への接続と実行は後続作業とする。`CreateTexture` は暗黙の queue 生成・提出・待機を行わない。実機試験結果は [進捗記録](../designs/graphics-implementation-progress.md) を参照する。
