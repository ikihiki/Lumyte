# Native GPU 基盤

DirectX 12／Vulkan 向けの caller-owned な低レベル契約。現在は device、共通 heap、線形 region と region 内の range、texture の明示配置を対象とする。[設計の正本](../../../docs/adr/0002-native-graphics-api.md) は Native ADR 群。

`CreateGpuHeap` は backing allocation だけを確保する。`CreateLinearRegion` が指定した heap offset に独立した native buffer を配置し、その resource の GPU address と必要な CPU mapping を提供する。`NativeGpuRange.Offset` は region 内の値であり、heap offset を含めない。

`CreateTexture` も同じ heap と offset を受け取り、独立した native texture を配置する。`GetTextureMemoryRequirements` と線形 region の requirements をまとめて heap 確保に渡すことで、native 条件が許す組合せを同居させられる。texture は opaque identity であり、CPU/GPU pointer や metadata の照会は公開しない。[混在配置の使用例](../../../docs/adr/0005-native-texture-api.md#使用例) を参照する。

heap、region、texture と GPU 利用の寿命は caller が管理する。managed reference をコピーしても native resource を延命しない。解放順は GPU 利用終了 → region／texture → heap → backend。現段階には GPU work の記録・提出 API は含まれない。

同一 heap／resource の操作は、配置と破棄も含めて caller が直列化する。backend の Dispose と利用も競合させない。Vulkan の同一 allocation 内で共有する mapping は、この host 同期の下で取得・解放する。

## 使用例

以下は `Lumyte.Graphics.DirectX12` と `Lumyte.Graphics.Native` を参照する Native 利用側の例。CPU から配置した region に書き込むだけであり、GPU command は実行しない。

```csharp
using System.Runtime.InteropServices;
using Lumyte.Graphics.DirectX12;
using Lumyte.Graphics.Native;

using INativeGpuBackend backend = DirectX12Backend.Create();
byte[] bytes = [3, 5, 8, 13, 21];
var kind = NativeGpuMemoryKind.CpuVisible;
var requirements = backend.GetLinearMemoryRequirements(256, kind);
var heap = backend.CreateGpuHeap(
    checked(requirements.Size * 2), requirements.Alignment, kind,
    [requirements.Compatibility]);
try
{
    var region = backend.CreateLinearRegion(256, heap, requirements.Size);
    try
    {
        var range = new NativeGpuRange(region, 32, (ulong)bytes.Length);
        nint destination = checked(region.CpuAddress + (nint)range.Offset);
        Marshal.Copy(bytes, 0, destination, bytes.Length);
        Console.WriteLine(range.GpuAddress);
    }
    finally { backend.DestroyLinearRegion(region); }
}
finally { backend.DestroyGpuHeap(heap); }
```

requirement の Size は alignment を含む予約容量で、region の Size は論理データ容量。Compatibility は同じ backend・memory kind で取得した値をそのまま列として渡す。

## バックエンドの追加

別 assembly で `INativeGpuBackend` を実装し、次の基底型から実装内の非公開型を派生させる。`Lumyte.Graphics.Native` に `InternalsVisibleTo` はなく、追加 backend の assembly 名を登録する必要もない。

| 基底型 | protected constructor | 派生型が保持するもの |
| --- | --- | --- |
| `NativeGpuHeap` | `(size, alignment, kind)` | native allocation、所属 device、局所的な解放状態 |
| `NativeGpuMemoryCompatibility` | `()` | native requirement、取得元 device と memory kind |
| `NativeGpuLinearRegion` | `(heap, heapOffset, size, gpuAddress, cpuAddress)` | native resource、所属 device、mapping と局所的な解放状態 |
| `NativeGpuTextureHandle` | `()` | native texture、作成値、所属 device と局所的な解放・初期化状態 |

`NativeGpuMemoryRequirements(size, alignment, compatibility)` は public constructor で返せる。基底型が公開する metadata は不変とし、texture handle と compatibility は opaque に保つ。基底型自体は native resource を生成・破棄しない。backend は受け取った object の派生型と device identity を検査し、別実装・別 device の object を native API に渡さない。`object BackendData` や共通の resource registry は使わない。

公開契約だけで実装・利用できることは、内部公開指定のない別 assembly の [consumer test](../Lumyte.Graphics.Native.Tests/Device/ExternalNativeGpuBackendTests.cs) で検証する。本体向け `InternalsVisibleTo` は repository 全体で使用せず、テスト向けだけに限定する。

## 現段階の範囲

旧 `IGpuBackend` の adapter は作らない。既存の描画系は未移行の source として残り、新しい backend は native API を直接呼ぶ。共通 `Lumyte.Graphics` から現在利用するものは code format と device loss 例外などの基礎型である。

texture の配置までを実装し、view、copy footprint、初回 layout 遷移と GPU 転送は後続段階とする。DirectX 12 の texture は `Undefined`、Vulkan の image は `UNDEFINED` で生成する。Vulkan は使用前に `GENERAL` へ遷移させる義務を非公開の texture 状態に保持し、今後の command／submit 実装へ接続する。生成時に queue を作成・提出・待機しない。

descriptor、shader、limits、command／queue、Resources と機能 RenderGraph の移行も後続段階とする。Vulkan は ADR が要求する拡張・feature を初期化時に要求し、古い descriptor set の実装へ切り替えない。実装と実機検証の範囲は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) を参照する。
