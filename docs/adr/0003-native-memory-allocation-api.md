# ADR 0003: Native Memory Allocation API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0002: Native Graphics API](0002-native-graphics-api.md) の device、memory 上限、所有権と native 診断に依存する。

## 決定

線形 data と texture のメモリ確保を、一種類の `NativeGpuHeap` と `CreateGpuHeap`／`DestroyGpuHeap` に統一する。heap は純粋な backing allocation であり、resource を内包せず、CPU/GPU address を公開しない。その上に置く線形領域と texture は、それぞれ独立して生成・破棄する。

caller は同じ heap の別々の offset へ異なる resource を配置できる。確保時には、配置予定の resource から取得した opaque な compatibility requirement を列で渡す。backend は列全体と memory kind を満たす native allocation の引数を構成する。異なる resource の共用は native 条件が許す場合に限り、統一 API がすべての memory kind・device・resource の組合せを保証するものではない。

heap 内の suballocation、offset の決定、重なり、再利用、退役待ちは caller が管理する。確保へ渡す requirement の列は配置予約でも resource の登録でもなく、backend はその列から resource を生成したり寿命を追跡したりしない。

重複配置した resource 間の内容継承は保証しない。別の alias が書き込んだ後に texture を再利用する場合は、caller が先行利用と memory dependency を順序付け、影響する subresource を明示的に discard／再初期化してから内容を書き直す。heap は alias の切替を検出せず、再初期化 command を自動挿入しない。

## API

以下の生成・破棄操作は `INativeGpuBackend` の member とする。

| API | 契約 |
| --- | --- |
| `NativeGpuMemoryKind` | `CpuVisible`、`GpuOnly`、`Readback`。memory の用途を指定する。選択できる native memory の種類は resource の requirement にも従う。 |
| `NativeGpuMemoryCompatibility` | 同じ device と memory kind の確保へ渡す opaque な配置 requirement。public abstract 基底型と protected の引数なし constructor を提供し、backend が非公開型で継承する。公開の分類値、bit 演算、比較・統合・適合判定の API は持たない。 |
| `NativeGpuMemoryRequirements(size, alignment, compatibility)` | 公開 constructor を持つ不変値。`Size`、`Alignment`、`Compatibility` は線形領域と texture に共通の返却型。`Size` は予約する byte 数、`Alignment` は heap の指定 alignment と placement offset が満たす整列条件。 |
| `NativeGpuHeap` | public abstract 基底型。protected constructor `(size, alignment, kind)` で caller-owned allocation の `Size`、`Alignment`、`Kind` を初期化する。identity は派生 object 自体であり、配置 resource の所有権、CPU/GPU address は持たない。 |
| `CreateGpuHeap(size, alignment, kind, compatibilities)` | caller 指定の byte 容量を一つの native allocation として確保する。`compatibilities` は取得済みの `ReadOnlySpan<NativeGpuMemoryCompatibility>` で、少なくとも一つ渡す。 |
| `DestroyGpuHeap(heap)` | caller が全使用終了と配置 resource の破棄を保証した heap を解放する。内部 suballocator、暗黙の待機、自動 resource 破棄を置かない。 |

`Size` と `Alignment` は配置に使う予約量であり、必要なら native の最小 requirement を保守的に切り上げる。返却 `Size` は返却 `Alignment` の倍数にする。異なる resource の配置境界に必要な余白を含み、caller は返された `Size` 分を占有させ、各開始 offset をその `Alignment` に整列させる。これにより通常の非重複配置のための resource 分類や追加の境界照会 API を不要にする。小さい resource でも余白が増える場合があり、線形 data は一つの region 内を range に分けて使える。具体的な native 条件と変換は各 backend の実装 ADR に置く。

heap の `alignment` は配置予定の全 requirement の alignment を満たす値、`size` は予約範囲を収める整列済み容量とする。一つの resource だけならその requirement を使い、混在する場合は caller が最大 alignment と offset・総容量を計算する。logical data の byte 数と、alignment・padding を含む予約量を混同しない。

取得した `Compatibility` は変更せず列へ入れ、取得時と同じ device・memory kind の確保に使う。共通の native memory を選べない場合は確保失敗とし、複数 heap への自動分割や別 memory kind への変更は行わない。caller が用途を分けて再度確保できる。後から resource を置くときも適合する heap と offset を caller が指定し、backend は以前の requirement の列を使った使用許可 registry を作らない。

descriptor storage の確保は専用の契約である。resource backing allocation の統一に、opaque な descriptor storage の CPU/GPU address への変換を含めない。

backend は heap と compatibility の非公開派生型に native allocation／requirement と所属 device を保持する。caller には共通基底型を返し、使用時に実装型と所属 device を確認する。これにより外部 backend は共通 assembly への内部アクセスや追加の resource registry を必要としない。基底 constructor は metadata を設定し、native allocation の作成・破棄は行わない。

## コード配置

パスは repository root 相対の目標配置とする。`Lumyte.Graphics.Native` と隣の `.Tests` を新設し、DirectX 12／Vulkan と各 `.Tests` は既存 project 内に Native 実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Memory/` | memory kind、opaque compatibility、requirements と `NativeGpuHeap` の公開型。生成・破棄 member は `Device/INativeGpuBackend.cs` に宣言する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Memory/` | requirement から heap の flags・alignment を構成し、`ID3D12Heap` を確保・解放する処理。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Memory/` | memory type の選択、granularity を含む予約量、`VkDeviceMemory` の確保・解放と共有 mapping の内部所有。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Memory/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Memory/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Memory/` | 容量計算・整数変換・native 確保引数の構成と局所的な解放を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Memory/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Memory/` | 実 GPU の allocation と混在配置条件を確認する試験。 |

suballocator、配置の予約 registry と application resource の退役処理は、この配置範囲に含めない。

## 使用例

`first` と `second` は同じ device・`GpuOnly` 用に取得済みの `NativeGpuMemoryRequirements` とする。`size` は caller が配置を計画した総容量で、最大 alignment に整列済みである。この例では resource を配置せず、GPU に使用させない。

```csharp
ulong alignment = Math.Max(first.Alignment, second.Alignment);
NativeGpuHeap heap = native.CreateGpuHeap(
    size, alignment, NativeGpuMemoryKind.GpuOnly,
    [first.Compatibility, second.Compatibility]);
try
{
    Console.WriteLine(heap.Size);
}
finally
{
    native.DestroyGpuHeap(heap);
}
```

GPU に使用させる場合は、caller が全未提出参照と GPU 利用を解消し、配置 resource を破棄してから heap を解放する。

## 検証方針

自身の allocation identity、CPU size 変換と解放の局所状態を扱う。compatibility の列から native 確保引数を作る処理は実装に必要な選択であり、配置・usage・format・同期の validator を重複実装する根拠にしない。公開の requirement 統合・分類・互換性照会、application resource の参照グラフ、全資源の lifetime tracker は作らない。

## 採用差分と未実装範囲

allocation を caller が所有し、resource と分離する方針を採用する。[NoGraphicsAPI の参照 header](https://github.com/sebbbi/NoGraphicsAPI/blob/main/include/NoGraphicsAPI/NoGraphicsAPI.hpp) の線形 heap／texture heap の分割は採用せず、純粋 allocation の heap に統一する。opaque requirement の列と、配置境界を含む予約量は Lumyte の補足である。

GPU address は配置した線形 resource のものとし、heap 全体を一つの address 空間として公開する設計は採用しない。suballocator と退役 helper は範囲外である。

純粋 allocation の生成・破棄、opaque requirement 列からの native 引数の構成と線形 resource・texture の分離を両 backend に実装した。線形配置・mapping・heap 再利用に続き、texture の requirement と混在配置を実機確認済み。GPU copy と同期・alias 再利用を command 側の検証へ接続する。shader からの利用を含む未実装機能の完了とは扱わず、特定 PC の試験を全 device・memory kind の対応保証にはしない。詳細と実機試験結果は [進捗記録](../designs/graphics-implementation-progress.md) を参照する。
