# ADR 0012: Native Command Submission と同期

## 状態

採用（目標設計）。現行実装の完了を示さず、旧 API との互換経路は設けない。

## 依存 ADR

[ADR 0011: Native Command Recording API](0011-native-command-recording-api.md) の queue、command と Dispose、[ADR 0010: Native Pipeline State API](0010-native-pipeline-state-api.md) の native PSO 解決、[ADR 0002: Native Graphics API](0002-native-graphics-api.md) の error/device loss に依存する。

## 決定

記録を batch として一度だけ提出し、device に属する caller-owned timeline の明示 completion 値で完了を表す。MainQueue と optional CopyQueue の間は、提出時の timeline point 列で GPU の依存を指定する。native queue に受理される前の準備と、受理後の GPU completion を区別する。記録が空の場合も、依存待機と signal だけの提出を許す。

caller が timeline の値、提出順序、GPU wait と再利用時点を管理する。backend は指定された GPU wait だけを native へ渡し、application resource の pin、自動退役、暗黙 wait を追加しない。CPU の待機・照会は queue の可変状態から分離し、別 thread の提出と並行できる。

## API

提出は `NativeGpuQueue`、timeline の生成は `INativeGpuBackend`、CPU 操作と破棄は `NativeGpuSemaphore` の member とする。旧 queue 配下の生成・照会・待機 API は残さない。

| API | 契約 |
| --- | --- |
| `NativeGpuQueue.Submit(commands, signal, waits = default)` | `ReadOnlySpan<NativeGpuCommandBuffer>` を一回提出する。全 native 記録の生成・終了を成功させ、`ReadOnlySpan<NativeGpuTimelinePoint>` の全待機点を GPU が満たしてから batch の全 work を実行し、単一の `signal` point を通知する。CPU は GPU 完了を待たない。空の記録列も許す。 |
| `NativeGpuTimelinePoint(Semaphore, Value)` | device timeline の identity と ulong 値の非所有 record struct。異なる semaphore の同じ数値は同じ完了点ではない。 |
| `NativeGpuSubmissionException(completion, innerException)` | 受渡し後で未提出の保証がない同期失敗を通知する。受理不明の結果も含む。`Completion` は要求した caller signal、`InnerException` は元の障害。値の到達や GPU 利用終了を保証しない。 |
| `NativeGpuSemaphore` | 全 queue から利用できる caller-owned device timeline。public abstract 基底型と protected constructor から各 backend が実装する。 |
| `INativeGpuBackend.CreateSemaphore(initialValue = 0)` | 初期値を持つ device timeline を生成する。queue に所属させない。 |
| `NativeGpuSemaphore.IsComplete(value)` | 現在の値が指定値以上かを CPU から確認する。待機と queue 内部 memory の回収を行わない。 |
| `NativeGpuSemaphore.WaitCpu(value)` | 指定値以上への到達を呼出し元 CPU thread で待つ。queue の Submit を停止せず、内部 memory 回収も行わない。 |
| `NativeGpuSemaphore.WaitAsync(value, cancellationToken)` | CPU thread を占有せず値への到達を待つ。取消しはこの CPU 待機だけを終了し、GPU work や他の待機を取り消さない。 |
| `NativeGpuSemaphore.SignalCpu(value)` | CPU から値を signal する。GPU work の完了通知ではなく、caller が CPU producer や明示的な gate に用いる。 |
| `NativeGpuSemaphore.Dispose()` | この semaphore を参照する全 CPU 操作と GPU signal／wait を解消してから同期 object を破棄する。 |

caller は同じ timeline の全 signal 値を実行順に厳密な単調増加にする。queue ごと、または CPU producer ごとに timeline を分けると、複数 signaler の順序を明確に保ちやすい。GPU wait は現在値が待機値以上なら満たされ、値を消費しない。signal 提出前の wait も許すが、待機で停止した同じ queue の後方にしか signal がない構成や循環依存は caller が避ける。単調性、将来値、GPU の循環依存を検査する独自 scheduler は設けない。

caller は同じ queue の操作を直列化する。異なる queue と recording は並行して利用できるが、一つの recording の記録・提出・破棄を競合させない。同じ resource の host 操作と初回使用の提出も直列化する。CPU の `WaitCpu`／`WaitAsync`／`IsComplete` は Submit／SignalCpu と並行可能であり、queue の回収リストへ触れない。SignalCpu 同士と GPU signal の順序は native の条件に従って caller が保証する。semaphore や backend の Dispose は他の操作と競合させない。command 列と wait 列の storage は Submit が戻った後に再利用できる。

`WaitAsync` の既定実装は非同期 timer を挟んで `IsComplete` を照会し、待機専用 thread を作らない。DirectX 12／Vulkan は進行中の非同期待機を数え、その間の semaphore 破棄を拒否する。待機の取消しや例外は GPU 利用終了を保証しない。CPU signal で到達させた値についても、GPU work の完了と取り違えない。

producer の signal を CPU が観測しただけでは、その semaphore を待っている consumer queue の参照は終了していない。consumer の completion も含む全利用終了まで semaphore を保持する。CPU root 入力の snapshot と、GPU が参照する resource／descriptor の寿命も別であり、最終 consumer が終わるまで後者を保持する。

backend の終了前に、caller は提出済み work を完了させ、未提出分を含む全 recording と semaphore を Dispose する。未提出の native command memory は recording が所有し、queue は受理した work の内部 memory だけを追跡する。backend 終了を application object の自動回収入口にしない。

DirectX 12 の `Submit` は次の順序で処理する。

1. batch の draw/indexed draw/mesh dispatch とそれぞれの indirect 版に実際に使う pipeline/depth-stencil の組を解決する。
2. 不足する native PSO を生成する。成功済みの組は pipeline が Destroy まで所有して再利用する。
3. CPU 記録を順番どおりに native command へ変換し、batch 全体の記録を終了する。
4. batch 全体の準備成功後、指定された GPU wait、work、内部 completion、caller completion の順に native queue へ渡す。

Vulkan は作成済み pipeline と記録済み native command を使い、depth/stencil の組の追加生成は行わない。batch 全体の native 記録終了が成功してから queue が受理する。

mesh も raster work としてこの提出・completion 契約を共有する。amplification の有無は program 定義の一部で、提出時に別の program へ差し替えない。DirectX 12 では mesh 用 PSO の生成失敗も batch 全体の受理前失敗になり、既存 draw と mesh が混在していても成功分だけを提出しない。mesh 専用 queue、準備呼出し、completion object は追加しない。

PSO 生成、native command への変換・終了に失敗した batch は受理せず、その batch の GPU wait も work も追加しない。caller は失敗した記録を `Dispose` して新しく記録する。失敗までに生成できた PSO は pipeline に残してよい。受理済み work は未提出へ戻さず、再提出を許可しない。

native queue への受渡し開始後で、native API が未提出を保証しない同期障害は `NativeGpuSubmissionException` に要求した completion と元の例外を保持する。Vulkan のメモリ不足による確実な拒否は元の例外を維持し、受理の有無を確定できない結果を包む。これは失敗した提出を識別する契約であり、公開 command 状態や完了証明ではない。受理済み・受理不明の記録を再提出せず、application resource と semaphore の所有を維持する。例外を包む処理自体が成立しない致命的な host 障害もあるため、この例外以外なら常に未提出だったとは推定しない。

受理後は command を Dispose しても caller の semaphore で completion を照会できる。ただし、提出失敗の `Completion` が到達する保証はない。device loss として通知された場合も GPU 利用終了とはみなさない。backend 所有の command memory は必要な completion または別途確定した利用終了まで保持し、Dispose を理由に早期再利用しない。backend の Dispose は caller が先に全利用を終了する契約であり、戻り値を GPU 停止の証明に使わない。

内部 command memory の回収は caller の semaphore の寿命に依存させない。各 queue は独立した内部 completion と回収リストを持ち、queue 操作に伴って完了済みの native memory を回収する。全 GPU 利用を解消した caller semaphore を破棄しても、内部回収は継続できる。CPU の待機・照会は回収リストを操作しない。この追跡は allocator／native command memory だけを対象とし、application resource の退役を引き受けない。回収のための公開 polling API、常駐 thread、暗黙の CPU wait は追加しない。

CopyQueue は linear data と color texture の転送を対象にする。depth/stencil、shader と rendering は MainQueue を使う。queue が複数あっても、競合する資源使用は caller の timeline wait と barrier／layout に従う。Vulkan の初回 image 初期化を担当する producer は consumer より先に Submit を受理させ、その後の consumer に GPU wait を付ける。初期化前の image を含む consumer を未来値待機だけで先行提出しない。初期化後の依存や linear data の依存には通常の wait-before-signal を使える。

CPU の先行数と staging／描画 data の slot は上位が管理する。複数 frame を先に提出し、次に上書きする slot の最終 consumer が未完了の場合だけ WaitCpu する。slot allocator、frame 数制限、application resource の GC と暗黙 multi-buffering は低層に追加しない。

native 記録中・終了済み・受理済みの区別は内部管理であり、公開 command 状態は設けない。提出自体は GPU 完了の CPU wait を行わないが、提出中の native PSO 生成と command 変換は完了させる。

## コード配置

パスは repository root 相対とし、未実装機能の目標配置を含む。`Lumyte.Graphics.Native` と隣の `.Tests` は作成済み、DirectX 12／Vulkan と各 `.Tests` は既存 project 内へ実装を追加する。テストは xUnit を使う。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native/Submission/`、`src/graphics/Lumyte.Graphics.Native/Synchronization/` | 前者は `NativeGpuQueue`、`NativeGpuSubmissionException` と記録開始・wait point を受け取る提出、後者は `NativeGpuTimelinePoint` と device に属する caller-owned `NativeGpuSemaphore` の CPU 操作。生成 member は `Device/INativeGpuBackend.cs`。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Submission/` | batch 内の PSO 解決、command list の変換・終了から queue 受理までの処理、内部 command memory の回収との接続。 |
| `src/graphics/Lumyte.Graphics.Vulkan/Submission/` | 記録済み command buffer の batch 終了、queue 受理と内部 command memory の回収との接続。 |
| `src/graphics/Lumyte.Graphics.DirectX12/Synchronization/`、`src/graphics/Lumyte.Graphics.Vulkan/Synchronization/` | device に所属する fence／timeline semaphore、CPU signal・照会・wait、同期 object の解放と device 全体の loss の接続。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Device/` | 一回提出、device timeline と CPU 同期操作を外部 backend の公開契約から実装・利用する consumer test。 |
| `src/graphics/Lumyte.Graphics.Native.Tests/Synchronization/` | 非同期待機、取消し、独立した観測と障害通知を制御可能な semaphore で確認する。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Submission/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Submission/`、`src/graphics/Lumyte.Graphics.DirectX12.Tests/Synchronization/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Synchronization/` | native 呼出しを差し替え、受理前失敗、受理後の再提出拒否、completion と command memory の寿命を検証する unit test。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/Submission/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Commands/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/Pipelines/` | 実 queue の GPU wait と CPU の先行、転送先の compute／描画、逆方向 copy、失敗境界と完了後の内部 memory 回収を確認する試験。 |

提出の順序付けと同期 primitive の実装を別フォルダに置き、application resource の自動退役をいずれにも追加しない。

## 使用例

`upload` と `readback` は同じ byte 数の確保済み range とし、upload の CPU 書込みは済んでいるとする。caller が使用する resource と backing heap を completion まで保持する。以下は提出と待機が正常に完了する経路の例である。

```csharp
var queue = native.MainQueue;
using var completion = native.CreateSemaphore();
using var commands = queue.StartCommandRecording();
commands.CopyMemory(upload, readback);
commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite,
    GpuStage.Host, GpuAccess.HostRead);
queue.Submit([commands], new(completion, 1));
commands.Dispose();
completion.WaitCpu(1);
```

この例は caller が明示的に WaitCpu してから readback を読む。受理後すぐ command を Dispose しても work は続き、内部 command memory は完了まで保持される。command の Dispose や Submit が暗黙に GPU 完了を待つわけではない。

非同期 copy では、`copyCommands` に準備済み linear data の転送、`drawCommands` に転送先を使用する描画と必要な barrier を記録しておく。入力と全資源は最終利用まで保持する。texture の初期化・引渡し layout は backend の契約に従って先に準備する。

```csharp
var copy = native.CopyQueue
    ?? throw new NotSupportedException("This path requires a separate copy queue.");
using var uploaded = native.CreateSemaphore();
using var rendered = native.CreateSemaphore();
copy.Submit([copyCommands], new(uploaded, 1));
native.MainQueue.Submit([drawCommands], new(rendered, 1), [new(uploaded, 1)]);

// CPU は別 slot の次 frame を準備・提出できる。
// この slot を再利用する時点で、必要なら待つ。
await rendered.WaitAsync(1, cancellationToken);
```

GPU wait は CPU を停止しない。上記の最終 consumer 完了後なら、uploaded と rendered の双方を破棄できる。CPU producer を明示 gate にする場合は別 timeline を待機列へ入れ、CPU 処理後にその timeline の SignalCpu を呼ぶ。SignalCpu で GPU completion 用の値を先取りしない。

## 検証方針

batch の一回受理、PSO/command 変換の失敗時に GPU wait も追加しないこと、queue 間 dependency、CPU signal による gate、CPU wait と後続提出の並行、空提出、timeline identity と内部 memory の回収を確認する。GPU が実際にコピーした data を描画・compute と readback で検証し、処理時間や sleep を同期の代用にしない。実際の barrier と GPU 使用条件は native validation、application resource の生存と再利用は caller に委ねる。

## 採用差分と未実装範囲

一回提出、明示 completion と caller lifetime を採用し、転送 command の batch と caller-owned semaphore に実装した。内部 command memory は queue 所有の completion で回収する。受理前の command 変換・終了失敗を GPU 提出へ進めず、受理済み recording の再利用を拒否する。外部 assembly からの実装と GPU 試験の結果は [進捗記録](../designs/graphics-implementation-progress.md) に記録する。

compute pipeline、直接 root と直接／間接 dispatch、vertex／mesh raster の直接／一件の間接実行を提出へ接続した。DirectX 12 の提出時 PSO 解決は Lumyte の補足であり、mesh の PSO 生成を含む batch の native 変換に失敗した場合は一部の recording だけを実行しない。

受渡し後・受理不明の同期失敗を識別する例外、内部 command memory の保持、非同期の Native CPU wait を実装した。上位 Resources は独自の token と使用保持をこの完了観測へ接続する。完了点の通知だけで device loss 後の回収まで保証しない。停止を強制して利用終了を証明する公開操作は設けず、観測不能な work は保持して終了を失敗させる。

複数 queue の選択と GPU wait、queue に所属しない CPU timeline 操作は NoGraphicsAPI の現行 prototype を拡張する方針として採用し、DirectX 12／Vulkan に実装した。CopyQueue は独立した native queue を取得できる場合だけ提供する。任意数の queue 作成、専用 compute queue、stage を指定する部分 wait、複数 signal、presentation と application resource の自動退役はこの段階に含めない。実装と実機検証の範囲は進捗記録に従う。
