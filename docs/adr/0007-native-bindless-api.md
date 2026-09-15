# ADR 0007: Native Bindless API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0002: Native Graphics API](0002-native-graphics-api.md) の capability と、[ADR 0004: Native Linear Data API](0004-native-linear-data-api.md) の range・実 GPU address に依存する。

## 決定

Bindless を Native の標準参照方式とする。resource と sampler の index 空間を分け、shader が caller の決めた index を直接解釈する。型別 profile、draw ごとの binding set、使用 resource 集合、安定 index の allocator を Native 層へ置かない。

index は descriptor storage の位置を表す通常の値であり、public な割当 object にしない。slot の確保・再利用と、そこへ descriptor を書く操作を分ける。

## API

この文書は heap と参照用途の分類値を定義する。index に新しい handle 型や割当 API は追加しない。

| API | 契約 |
| --- | --- |
| `NativeGpuDescriptorHeapKind` | `Resource` または `Sampler`。線形領域・texture の backing allocation とは別の専用 descriptor storage の分類。 |
| `NativeGpuTextureDescriptorType` | `Sampled` または `Storage`。 |
| `NativeGpuBufferAccess` | `ReadOnly` または `ReadWrite`。buffer descriptor の shader 参照方式。 |

resource 空間では texture と buffer descriptor を扱い、sampler は独立した空間で扱う。同じ数値の resource index と sampler index は互いに衝突しない。caller が index、初期化、更新時点と参照先の寿命を管理する。

`RawShaderPointers` に対応する target は線形 data を実 GPU pointer でも参照できる。`BufferDescriptors` は descriptor 経由の線形 range 参照であり、raw pointer 経路への強制条件にはしない。DirectX 12 で descriptor index を GPU address に偽装しない。

Bindless は使用する heap 全体の選択を不要にするものではない。個別 resource を command へ列挙せず、shader と storage は同じ index ABI に従う。

## コード配置

パスは repository root 相対とし、未実装機能の目標配置を含む。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済みで、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Bindless/` | descriptor heap kind、texture descriptor type と buffer access の公開分類値。index を所有する追加 handle や allocator は置かない。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Bindless/` | resource／sampler の index 空間と native heap indexing ABI の内部対応。storage の実体は同 project の `Descriptors/` に置く。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Bindless/` | resource／sampler の index 空間と native descriptor heap ABI の内部対応。固定 slot stride の storage 実装は `Descriptors/` が担当する。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Bindless/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Bindless/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Bindless/` | 分類値と native 参照方式の写像について、GPU を使わない契約試験。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Bindless/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Bindless/` | shader が resource／sampler index と対応する参照先を読む試験。raw pointer 対応は target の契約に従って別途確認する。 |

shader source とそのコンパイラは backend に内包せず、backend の試験は対象 ABI に合わせて準備した artifact を使う。

## 使用例

sampled texture と読み取り専用 buffer を resource 空間へ置く場合の用途値を選ぶ。slot の割当や書込みは行わない。

```csharp
var heapKind = NativeGpuDescriptorHeapKind.Resource;
var textureType = NativeGpuTextureDescriptorType.Sampled;
var bufferAccess = NativeGpuBufferAccess.ReadOnly;
```

用途値は native descriptor の表現に使う。shader index の所有や resource の寿命は付与しない。

## 検証方針

Native 独自の型別 profile 比較や shader reflection を追加しない。shader の参照方式と descriptor の合法性は compiler/native validation、index の配置と再利用は caller が担当する。

## 採用差分と未実装範囲

caller-owned index と Bindless を採用する。分類値、両 backend の resource／sampler storage と指定 index への書込み、command の heap 選択を実装した。buffer descriptor は NoGraphicsAPI の public API に対する Lumyte の補足であり、raw shader pointer の DirectX 12 対応は未提供。

compute から texture／buffer／sampler を非ゼロ index で参照する経路と、Vulkan の実 GPU pointer 経路を実機確認した。vertex／mesh raster にも同じ caller-owned heap の選択を接続した。mesh 専用の binding set や resource registry は追加しない。Native offline compiler、MSBuild、package／loader を機能 pass に接続した。Output／2D／標準画像処理の shader を製品 project の build で生成する。stage ごとの実機試験の範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
