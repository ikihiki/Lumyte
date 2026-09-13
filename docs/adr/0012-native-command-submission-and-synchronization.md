# ADR 0012: Native Command Submission と同期

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0011: Native Command Recording API](0011-native-command-recording-api.md) の queue、command と Dispose、[ADR 0010: Native Pipeline State API](0010-native-pipeline-state-api.md) の native PSO 解決、[ADR 0002: Native Graphics API](0002-native-graphics-api.md) の error/device loss に依存する。

## 決定

一つ以上の記録を batch として一度だけ提出し、caller-owned semaphore の明示 completion 値で完了を表す。native queue に受理される前の準備と、受理後の GPU completion を区別する。

caller が timeline の値、提出順序と再利用時点を管理する。backend が application resource を pin したり、自動退役や GPU wait を追加したりしない。

## API

提出・semaphore の生成・照会・待機は `NativeGpuQueue` の member とする。

| API | 契約 |
| --- | --- |
| `NativeGpuQueue.Submit(commands, semaphore, value)` | 同じ queue の一つ以上の記録について、必要な native PSO の解決と native command の生成・終了を batch 全体で成功させてから一度だけ提出する。caller 指定の値を batch 完了時に signal し、GPU 完了の CPU wait はしない。 |
| `NativeGpuSemaphore` | caller-owned completion timeline。public abstract 基底型と protected constructor から各 backend が実装する。 |
| `NativeGpuQueue.CreateSemaphore(initialValue)` | 初期値を持つ同期 object を生成する。 |
| `NativeGpuQueue.IsComplete(semaphore, value)`／`Wait(semaphore, value)` | 指定値の completion を CPU から照会・待機する。 |
| `NativeGpuSemaphore.Dispose()` | 関連する提出・待機を解消してから同期 object を破棄する。 |

`NativeGpuSemaphore` は caller-owned な completion timeline である。caller は同じ timeline の signal 値を単調増加させ、同期 object を関連する提出・待機の解消後に破棄する。別 queue への GPU wait は最小契約に含めない。

caller は同じ queue の操作と semaphore の利用・破棄を直列化する。異なる recording は別々に組み立てられるが、一つの recording の記録・提出・破棄を競合させない。batch は `ReadOnlySpan<NativeGpuCommandBuffer>` で渡し、span の storage は `Submit` が戻った後に保持する必要がない。

backend の終了前に、caller は提出済み work を完了させ、未提出分を含む全 recording と semaphore を Dispose する。未提出の native command memory は recording が所有し、queue は受理した work の内部 memory だけを追跡する。backend 終了を application object の自動回収入口にしない。

DirectX 12 の `Submit` は次の順序で処理する。

1. batch の draw/indexed draw/mesh dispatch とそれぞれの indirect 版に実際に使う pipeline/depth-stencil の組を解決する。
2. 不足する native PSO を生成する。成功済みの組は pipeline が Destroy まで所有して再利用する。
3. CPU 記録を順番どおりに native command へ変換し、batch 全体の記録を終了する。
4. batch 全体が成功してから native queue が受理し、batch の GPU 完了時に指定値を signal する。

Vulkan は作成済み pipeline と記録済み native command を使い、depth/stencil の組の追加生成は行わない。batch 全体の native 記録終了が成功してから queue が受理する。

mesh も raster work としてこの提出・completion 契約を共有する。amplification の有無は program 定義の一部で、提出時に別の program へ差し替えない。DirectX 12 では mesh 用 PSO の生成失敗も batch 全体の受理前失敗になり、既存 draw と mesh が混在していても成功分だけを提出しない。mesh 専用 queue、準備呼出し、completion object は追加しない。

PSO 生成、native command への変換・終了に失敗した batch は受理せず、その batch の GPU work を開始しない。caller は失敗した記録を `Dispose` して新しく記録する。失敗までに生成できた PSO は pipeline に残してよい。受理済み work は未提出へ戻さず、再提出を許可しない。

受理後は command を Dispose しても caller の semaphore で completion を照会できる。signal を保証できなくなった場合は device loss として停止し、完了値が届かない通常待機を残さない。backend 所有の command memory は必要な completion または確定した device 終了まで保持し、Dispose を理由に早期再利用しない。

内部 command memory の回収は caller の semaphore の寿命に依存させない。backend は queue 所有の内部 completion を使い、caller が `Wait` の直後に semaphore を破棄しても回収を継続できるようにする。この追跡は allocator／native command memory だけを対象とし、application resource の退役を引き受けない。回収のための公開 polling API や暗黙の CPU wait は追加しない。

native 記録中・終了済み・受理済みの区別は内部管理であり、公開 command 状態は設けない。提出自体は GPU 完了の CPU wait を行わないが、提出中の native PSO 生成と command 変換は完了させる。

## コード配置

パスは repository root 相対とし、未実装機能の目標配置を含む。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済み、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Submission/`、`src/graphics/Lumyte.Graphics.Native/Synchronization/` | 前者は `NativeGpuQueue` と記録開始・提出・completion 操作の宣言、後者は caller-owned `NativeGpuSemaphore` の公開型。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Submission/` | batch 内の PSO 解決、command list の変換・終了から queue 受理までの処理、内部 command memory の回収との接続。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Submission/` | 記録済み command buffer の batch 終了、queue 受理と内部 command memory の回収との接続。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Synchronization/`、`src/graphics/Lumyte.Graphics.Vulkan/Synchronization/` | native fence／timeline semaphore、signal・照会・CPU wait、同期 object の解放と device loss の接続。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Submission/`、`src/graphics/Lumyte.Graphics.Native.Tests/Synchronization/` | 一回提出と caller-owned completion の公開契約を確認する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Submission/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Submission/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Synchronization/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Synchronization/` | native 呼出しを差し替え、受理前失敗、受理後の再提出拒否、completion と command memory の寿命を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Submission/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Submission/` | 実 queue の提出と明示 completion、vertex／mesh 混在 batch、完了後の内部 memory 回収を確認する試験。 |

提出の順序付けと同期 primitive の実装を別フォルダに置き、application resource の自動退役をいずれにも追加しない。

## 使用例

`upload` と `readback` は同じ byte 数の確保済み range とし、upload の CPU 書込みは済んでいるとする。caller が使用する resource と backing heap を completion まで保持する。以下は提出と待機が正常に完了する経路の例である。

```csharp
var queue = native.MainQueue;
using var completion = queue.CreateSemaphore(0);
using var commands = queue.StartCommandRecording();
commands.CopyMemory(upload, readback);
commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.Host, GpuAccess.HostRead);
queue.Submit([commands], completion, 1);
commands.Dispose();
queue.Wait(completion, 1);
```

この例は caller が明示的に Wait してから readback を読む。受理後すぐ command を Dispose しても work は続き、内部 command memory は完了まで保持される。command の Dispose や Submit が暗黙に GPU 完了を待つわけではない。

## 検証方針

batch の一回受理、PSO/command 変換の失敗境界、completion の接続と backend 所有 memory の回収を確認する。実際の barrier と GPU 使用条件は native validation、application resource の生存と再利用は caller に委ねる。

## 採用差分と未実装範囲

一回提出、明示 completion と caller lifetime を採用し、転送 command の batch と caller-owned semaphore に実装した。内部 command memory は queue 所有の completion で回収する。受理前の command 変換・終了失敗を GPU 提出へ進めず、受理済み recording の再利用を拒否する。外部 assembly からの実装と GPU 試験の結果は [進捗記録](../designs/graphics-implementation-progress.md) に記録する。

compute pipeline、直接 root と直接／間接 dispatch、vertex／mesh raster の直接／一件の間接実行を提出へ接続した。DirectX 12 の提出時 PSO 解決は Lumyte の補足として実装し、mesh の PSO 生成を含む batch の native 変換に失敗した場合は一部の recording だけを実行しない。queue をまたぐ GPU wait、presentation と application resource の自動退役はこの Native 契約に含めない。
