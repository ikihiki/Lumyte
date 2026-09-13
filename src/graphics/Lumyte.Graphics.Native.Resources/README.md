# Native resource utilities

`Lumyte.Graphics.Native.Resources` は Native backend の上で resource の保持・回収を組み立てるライブラリです。現在は [ADR 0028](../../../docs/adr/0028-resource-utilities.md) の memory arena 基盤を実装しています。参照先は `Lumyte.Graphics.Native` だけで、backend、Portable、shader library、資産 loader、RenderGraph へ依存しません。

## Memory arena API

| API | 契約 |
| --- | --- |
| `GpuMemoryArena(backend, blockSize)` | backend を借用し、純粋な backing heap の block と貸出範囲を所有します。`blockSize` は通常 block の最小容量です。 |
| `Allocate(size, alignment, kind, compatibilities)` | 指定した予約 byte 数を整列した offset に貸し出します。返す `GpuMemorySlice` は貸出ごとに新しい identity を持ちます。 |
| `GpuMemorySlice.Heap / Offset / Size` | backing heap、heap 基準の offset、予約 byte 数です。slice は heap や配置 resource を所有しません。 |
| `Release(slice)` | 全 CPU／GPU 利用と未提出参照を終了し、配置 resource を破棄した後に範囲を返します。別 arena の slice、返却済みの slice は拒否します。 |
| `Trim()` | 貸出のない block を切り離して native heap を解放します。GPU 待機は行いません。 |
| `Dispose()` | 全 slice が返却済みの場合に arena の終了と空 block の解放を行います。未返却があれば一切変更せず失敗し、caller は返却後に再試行できます。backend は破棄しません。 |

caller は arena の操作を直列化し、backend の thread 条件を守ります。自動的な resource 生成、mapping、descriptor 確保、barrier、shader 参照の探索は行いません。

## 要件と再利用

pool は memory kind と、取得済みの opaque compatibility token の**参照 identity の集合**で分けます。列の順序と同じ token の重複は意味を持ちません。token の `Equals`、hash override、backend 固有の bit 表現は利用しません。異なる token を同等と推測せず、部分集合と上位集合も別 pool にします。caller は description と kind に対応する取得済み requirements を保存して再利用してください。同じ条件で問い合わせ直しても、新しい token identity なら別 pool になります。

新しい heap には集合内の全 token を渡します。共通 memory の選択と対応可否は backend が判断し、失敗時に複数 allocation へ分割したり memory kind を変更したりしません。

`NativeGpuMemoryRequirements.Size` は logical な Buffer byte 数ではなく、配置境界の余白を含む予約量です。backend は返却する alignment と size に Vulkan の buffer/image granularity 等を含めています。arena はこの予約量と指定 alignment を使い、追加の native 制約を照会・分類しません。mixed allocation では同じ全 token 集合と、全 resource を満たす最大 alignment を指定します。

slice の開始 offset は要求 alignment へ整列します。既存 heap は base alignment が要求 alignment の倍数である場合だけ再利用します。前後の未使用範囲と alignment 用の隙間を保持し、隣接する返却範囲を結合します。新しい block の容量は `max(blockSize, size)` を alignment に切り上げます。大きな要求は一つの大きな heap に収め、返却後は同じ条件の貸出に再利用できます。

size／alignment／blockSize のゼロと、CPU 範囲計算の overflow は拒否します。GPU の format、usage、heap への配置適合性や native alignment の合法性を独自に再検証しません。

## 使用例と寿命

次は GPU work を提出しない配置例です。`native` は作成済みの `INativeGpuBackend` とします。

```csharp
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Resources;

const ulong logicalSize = 256;
const NativeGpuMemoryKind kind = NativeGpuMemoryKind.CpuVisible;
var requirements = native.GetLinearMemoryRequirements(logicalSize, kind);
using var arena = new GpuMemoryArena(native, checked(requirements.Size * 16));

var slice = arena.Allocate(requirements.Size, requirements.Alignment, kind,
    [requirements.Compatibility]);
try
{
    var region = native.CreateLinearRegion(logicalSize, slice.Heap, slice.Offset);
    try
    {
        // CPU mapping は region.CpuAddress を使用する。
        // GPU に提出した場合は、その利用を終了してから次の finally へ進む。
    }
    finally { native.DestroyLinearRegion(region); }
}
finally { arena.Release(slice); }

arena.Trim();
```

heap 全体の破棄権限は arena にあります。caller は `slice.Heap` を手動で破棄しません。配置時は `slice.Offset + localOffset` を一度だけ加え、配置後の `NativeGpuRange.Offset` と混同しないでください。

返却後も slice の数値は読めますが、使用権限は失効しています。同じ heap／offset が再貸出しされても、古い slice の `Release` は新しい貸出を返しません。再利用した memory の内容、resource state、alias の初期化状態を生成直後と同じとはみなしません。必要な GPU 同期と再初期化は caller が行います。

`Trim`／正常に開始した `Dispose` は対象 block を先にすべて切り離し、各 `DestroyGpuHeap` を一度ずつ試みます。一つが失敗しても残りを試み、一件なら元の例外、複数なら `AggregateException` を返します。native 解放の副作用は例外だけでは判定できないため、失敗した heap の解放を再試行したり pool へ戻したりしません。arena の終了後や backend 破棄後の操作を続けないでください。

## 実装範囲

割当、返却、再利用、空 block の解放と所有境界を実装しています。隣接 `.Tests` の `Unit/Utilities/` に、GPU 不要の fake backend を用いた範囲計算、opaque identity、失敗時の保持・解放の試験を置きます。実 GPU の配置・転送は DirectX12／Vulkan の適合試験で確認します。

completion token、`Retire`／`Collect`、転送 utility、resource manager、descriptor 自動管理、package upload と上位 RenderGraph の接続は後続です。現在の `Release` を GPU 完了前の自動回収として使用することはできません。
