# ADR 0004: Native Linear Data API

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0003: Native Memory Allocation API](0003-native-memory-allocation-api.md) の共通 heap、memory kind と配置 requirement に依存する。

## 決定

線形 data を置く `NativeGpuLinearRegion` を caller-owned heap の指定位置に生成し、使用範囲を `NativeGpuRange` で渡す。region は native の線形 resource と CPU/GPU address を持ち、heap の所有権を取得しない。allocation と線形 resource の生成・破棄は独立させる。

region は byte size を持つ一つの線形 resource である。vertex、index、constant、storage ごとの owning class や追加の `NativeGpuBufferHandle` は作らない。共通 heap を確保しただけでは region は存在せず、全 heap を覆う暗黙の線形 resource も生成しない。

## API

以下の要件取得・生成・破棄操作は `INativeGpuBackend` の member とする。

| API | 契約 |
| --- | --- |
| `GetLinearMemoryRequirements(size, kind)` | 指定 byte size と memory kind の線形 resource に必要な `NativeGpuMemoryRequirements` を返す。生成時と同じ native 条件で取得する。 |
| `CreateLinearRegion(size, heap, offset)`／`DestroyLinearRegion(region)` | caller-owned `NativeGpuHeap` の指定 byte offset に線形 resource を生成し、resource だけを破棄する。memory kind は `heap.Kind` を使う。 |
| `NativeGpuLinearRegion` | region の identity、非所有の `Heap`、heap 相対の `HeapOffset`、logical byte 数の `Size`、実 `GpuAddress`、mapping がある場合の `CpuAddress` を持つ。 |
| `NativeGpuRange(Region, Offset, Size)` | region 内の非所有 byte 範囲。`Offset` は region の先頭からの値であり、heap 相対ではない。 |
| `NativeGpuRange.GpuAddress` | `Region.GpuAddress + Offset`。CPU で dereference する pointer ではない。 |
| `NativeGpuRange.Slice(offset, size)` | 元の range 内の部分範囲を返す。CPU 側の整数 overflow と範囲外の値生成を防ぐ。 |

region の logical `Size` と配置 requirement の予約 `Size` は異なり得る。caller は後者を heap 内で占有させ、padding を data の範囲へ含めない。region の破棄は heap を解放せず、range のコピーや Slice は region／heap の寿命を延長しない。

NoGraphicsAPI の `GpuRange` は address/size だけを持つ。本 API は copy と indirect command の native resource を取り出すため、range に region identity と offset を保持する。command が受け取る region 内 offset と、heap 上への配置 offset を区別し、global な address→resource の逆引き表を作らない。

GPU address は生成した線形 resource から取得する。heap の仮想 base address を作って `HeapOffset` を加算したり、その address を同じ heap の texture まで使える pointer としたりしない。

CPU から扱うときは region の `CpuAddress` と範囲を使う。`CpuVisible` と `Readback` は各 kind に必要な mapping を region の生成・破棄と対応させ、`GpuOnly` は CPU mapping を公開しない。heap 内部で mapping を共有する必要があっても、公開 address の基点は region の先頭に固定する。mapping の具体的な native 処理は backend の実装契約に置く。

CPU/GPU 同期、shader が導出する pointer の整列・範囲と寿命は caller が保証する。shader の dereference は `RawShaderPointers` に従い、数値の GPU address があるだけで対応済みとは扱わない。

## コード配置

パスは repository root 相対の目標配置とする。`Lumyte.Graphics.Native` と隣の `.Tests` は新設予定、DirectX 12／Vulkan と各 `.Tests` は既存 project の改編であり、テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/LinearData/` | `NativeGpuLinearRegion`、`NativeGpuRange` と Slice。要件取得・生成・破棄 member は `Device/INativeGpuBackend.cs` に宣言する。 |
| `src/graphics/Lumyte.Graphics.DirectX12/LinearData/` | placed buffer、region の実 GPU address、Map と resource の解放。 |
| `src/graphics/Lumyte.Graphics.Vulkan/LinearData/` | buffer の生成・bind、device address と heap 内部 mapping からの region pointer の導出。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/LinearData/` | range の Slice、overflow と region 相対 offset を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/LinearData/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/LinearData/` | 配置 offset と region 内 offset の変換を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/LinearData/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/LinearData/` | 実 resource の配置・mapping・address と copy による結果確認。 |

用途別の owning buffer class や address の逆引き registry は置かない。

## 使用例

`native` は初期化済み device とする。範囲を切り出すだけで GPU work は実行しない。

```csharp
const ulong size = 4096;
var kind = NativeGpuMemoryKind.CpuVisible;
var requirements = native.GetLinearMemoryRequirements(size, kind);
var heap = native.CreateGpuHeap(
    requirements.Size, requirements.Alignment, kind, [requirements.Compatibility]);
try
{
    var region = native.CreateLinearRegion(size, heap, 0);
    try
    {
        var data = new NativeGpuRange(region, 0, 256);
        var element = data.Slice(64, 32);
        var gpuAddress = element.GpuAddress;
    }
    finally { native.DestroyLinearRegion(region); }
}
finally { native.DestroyGpuHeap(heap); }
```

GPU に使用する場合は、その全利用と未提出参照を解消してから region、heap の順に破棄する。

## 検証方針

Slice の CPU 整数 overflow と元の range 外の値生成を防ぐ。native resource の取得・配置・address の変換と、owned region／mapping の局所的な終了を扱う。CPU 側の範囲検査は shader が実際にアクセスする GPU 範囲の検証ではない。native の配置・usage・同期の validator と使用履歴 tracker は作らない。

## 採用差分と未実装範囲

線形 data を byte range で渡す方針を採用する。純粋 allocation と線形 resource を分離するための `NativeGpuLinearRegion`、range の region identity は Lumyte の補足である。参照実装の「線形 heap 自体が backing buffer と address を持つ」構成は採用せず、address-only API の完全採用とも説明しない。

raw shader pointer の DirectX 12 対応は未提供であり、region の明示配置、mapping、copy／indirect／descriptor の region 対応は未実装の移行作業である。
