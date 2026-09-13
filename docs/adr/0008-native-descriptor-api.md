# ADR 0008: Native Descriptor API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0007: Native Bindless API](0007-native-bindless-api.md) の index 空間と用途値、[ADR 0006: Native View API](0006-native-view-api.md) の view、[ADR 0004: Native Linear Data API](0004-native-linear-data-api.md) の range に依存する。

## 決定

descriptor storage は caller-owned の専用 heap とする。backend は容量と指定 slot への書込みを実装し、caller が空き slot の選択、再利用、更新時点と heap の寿命を管理する。slot allocator、snapshot、空 slot の補完、application resource の自動延命を storage に重ねない。

NoGraphicsAPI の CPU address への descriptor 書込みは設計の基礎とするが、Native 共通 API は専用 heap と index を受け取る。DirectX 12 の opaque descriptor handle を mapped CPU pointer や通常の GPU address に偽装しない。

## API

以下の生成・書込み・破棄操作は `INativeGpuBackend` の member とする。

| API | 契約 |
| --- | --- |
| `NativeGpuDescriptorHeap` | caller-owned descriptor storage。public abstract 基底型の `Kind`／`Capacity` は不変値で、protected constructor に渡す。外部 backend の非公開派生型が native slot の配置情報を保持する。resource と sampler の index 空間は別。 |
| `CreateDescriptorHeap(kind, capacity)`／`DestroyDescriptorHeap(heap)` | 指定容量の descriptor heap を確保・解放する。slot の suballocation と再利用は caller が決める。 |
| `WriteTextureDescriptor(heap, index, view, type = Sampled)` | resource heap の caller 指定 slot に texture descriptor を書く。 |
| `WriteBufferDescriptor(heap, index, range, access)` | `BufferDescriptors` 対応時の補足。resource heap の slot に線形 range を書く。raw pointer 経路での使用は要求しない。 |
| `NativeGpuSamplerFilter`／`NativeGpuSamplerAddressMode` | filter は `Nearest/Linear`、address mode は `Repeat/MirrorRepeat/ClampToEdge`。 |
| `NativeGpuSamplerDescription` | `MinFilter`、`MagFilter`、`MipFilter`、`AddressU/V/W`、float の `MinLod`／`MaxLod`／`MaxAnisotropy`、`CompareEnabled`／`CompareOp` の値。引数なしの `new` は Linear、Repeat、LOD 0～float.MaxValue、anisotropy 1、比較なし／Always。anisotropy 1 は無効化を表す。native 表現に変換できない値を切捨て・補正しない。 |
| `WriteSamplerDescriptor(heap, index, description)` | sampler heap の caller 指定 slot に descriptor を書く。public sampler object は作らない。 |

heap は `kind`、`capacity` と既存の native slot 配置情報を持つ。resource と sampler の空間を分け、resource heap の同じ index 計算を texture/buffer の書込みと shader 参照で共有する。public sampler object は作らない。

Vulkan の resource heap は、対応する image/buffer descriptor の size と alignment を満たす固定 slot stride を使う。sampler は別の stride を持つ。slot index が i なら、書込み先と shader の参照先をともに `i * slotStride` の byte offset にする。descriptor 自身の byte size と slot 間隔を同一視せず、heap が保持する配置情報と device の既存 size/alignment 情報を一致させる。

Vulkan の型ごとの自然 stride や Slang の既定 heap indexing が、この共通 stride と一致するとは仮定しない。raw shader を用意する caller が同じ byte offset を読む Native ABI に合わせる。CPU で shader reflection を再実行したり、型別 profile や追加の root lookup table を導入したりしない。

DirectX 12 の index は heap type ごとの native descriptor handle increment で位置を計算する。これは opaque handle の計算であり、descriptor bytes の size や mapped memory の byte stride として公開しない。

Vulkan の alignment と reserved range は backing storage の作成に必要な条件として扱う。NoGraphicsAPI の参照実装も descriptor 用の専用 allocation と full heap range の設定を使うため、任意の通常 buffer をそのまま descriptor storage と扱えるとは説明しない。

sampler の LOD 範囲と数値 anisotropy、texture view と buffer range の要求値は native 表現へ渡す。slot を書き換えても参照先を延命せず、caller が未提出参照と GPU 使用を解消してから上書き・再利用・heap 破棄を行う。

descriptor の書込みは texture の初期化や layout 遷移を行わない。Vulkan で最初の利用が shader からの参照である場合、caller は先に明示 discard により内容を破棄して `GENERAL` へ初期化するか、copy など texture identity を渡す操作で初期化を済ませる。heap 選択から descriptor の参照先を列挙・逆引きする仕組みは作らない。

## コード配置

パスは repository root 相対とし、未実装機能の目標配置を含む。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済みで、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Descriptors/` | caller-owned descriptor heap、sampler の値と description。生成・書込み・破棄 member は `Device/INativeGpuBackend.cs` に宣言する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Descriptors/` | shader-visible heap、opaque handle increment と指定 slot への texture／buffer／sampler descriptor 書込み。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Descriptors/` | 専用 storage と reserved range、固定 slot stride、native descriptor bytes の書込みと解放。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Descriptors/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Descriptors/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Descriptors/` | slot 位置の計算、native 引数への要求値の受渡しと owned storage の解放を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Descriptors/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Descriptors/` | texture／buffer 混在 slot と sampler を shader が正しく読むことを確認する試験。 |

slot の自動割当・再利用と参照先の延命はこの下位 storage 実装に追加しない。

## 使用例

`view` と `data` は非所有の texture view と線形 range、`BufferDescriptors` は対応済みとする。caller が index 7 と 8 を選び、この例では GPU に使用させない。

```csharp
var heap = native.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 16);
native.WriteTextureDescriptor(heap, 7, view);
native.WriteBufferDescriptor(heap, 8, data, NativeGpuBufferAccess.ReadOnly);
native.DestroyDescriptorHeap(heap);

var samplers = native.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 4);
native.WriteSamplerDescriptor(samplers, 2, new NativeGpuSamplerDescription(
    AddressU: NativeGpuSamplerAddressMode.ClampToEdge, MaxLod: 5));
native.DestroyDescriptorHeap(samplers);
```

書込みは slot を割り当てる操作ではない。descriptor heap の破棄は参照する texture、線形 region と共通 backing allocation を破棄しない。

## 検証方針

host memory の安全、slot から native の位置への変換、owned storage の生成・解放を確認する。native の descriptor 条件を再検証する validator、slot/resource の生存 registry、allocator は作らない。

## 採用差分と未実装範囲

caller-owned storage と明示 index を採用する。専用 heap/index は DirectX 12 の opaque heap に合わせた部分採用であり、NoGraphicsAPI の CPU destination と GPU range をそのまま公開する契約ではない。Vulkan の raw mapped storage・CPU address 書込み・GPU range 設定を公開する追加機能は未採用・未実装とする。

両 backend の storage 生成・破棄、texture／buffer／sampler の指定 slot への書込み、command の heap 選択を実装した。Vulkan は descriptor size と alignment から求めた共通 resource slot stride と独立した sampler slot stride を `Limits.Descriptors` に公開する。DirectX 12 の同 property は null とし、opaque handle increment を byte stride として公開しない。

両 backend の compute shader から混在 heap と sampler を参照する実機検証を追加した。Vulkan は Slang の unified descriptor stride と storage の一致を確認した。vertex raster にも同じ storage を接続し、heap ごとの所有と caller による選択を維持する。mesh からの参照と製品用 shader toolchain の移行は未実装である。
