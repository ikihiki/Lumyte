# ADR 0026: Portable Command の提出と Completion API

## 状態

採用（目標設計）。提出操作と完了確認を独立した操作として定義する。

## 依存 ADR

- [0024 Pipeline State](0024-pipeline-state-api.md): 提出時に実体化する論理 pipeline。
- [0025 Command Recording](0025-command-recording-api.md): queue と一回提出する記録。

## 決定

提出は work の受理であり、GPU の完了ではない。一つの queue に一回だけ提出し、caller が指定した単調な timeline 値で完了を確認する。queue は frame 数制限、application resource の GC、自動 lifetime 管理を行わない。

completion は Portable の queue timeline とする。WebGPU に GPU 間の semaphore を存在するように見せず、CPU から queue の完了を観測する。GPU による利用終了と、その提出の処理成功を区別する。ブラウザーの実行を妨げる blocking wait を標準 API にせず、非同期の待機を用いる。

## API

### 提出

| API | 説明 |
| --- | --- |
| `IGpuQueue.Submit(commandBuffers, signalSemaphore, signalValue)` | 必要な実 pipeline と encode を準備し、一つ以上の記録を順に提出する。その batch の利用終了と診断結果を指定した timeline 値へ対応付ける。GPU 完了や非同期診断の確定は待たない。 |
| `GpuSubmissionException(completion, innerException)` | queue への受渡し開始後の同期障害を通知する。`Completion` は要求した `GpuFenceValue`、`InnerException` は元の障害を保持する。GPU へ渡った可能性があり、完了や処理成功を表さない。 |

`Submit` は実際に参照する raster/compute pipeline を解決し、未生成の実 object を生成する。必要な encode と command の終了処理を済ませてから backend queue へ渡す。使わない pipeline は生成しない。手動の pipeline 準備操作は設けない。

一つの command は生成元 queue へ一回だけ提出する。caller は recording scope を閉じてから渡す。Lumyte 自身の一回提出契約と内部 object の所有状態を管理するための確認は行うが、runtime の resource、binding、pipeline、usage の validation は複製しない。

### 完了と待機

| API | 説明 |
| --- | --- |
| `IGpuQueue.CreateSemaphore(initialValue = 0)` | この queue の完了を表す `GpuSemaphore` を作る。CPU 観測用であり queue 間 GPU wait には用いない。 |
| `GpuSemaphore` | queue と timeline の identity。単調な signal value を持つ。 |
| `IGpuQueue.IsComplete(semaphore, value)` | 指定値までの GPU 利用終了を非 block で返す。true は当該利用の回収条件を満たすことだけを示し、処理成功や出力内容を保証しない。 |
| `IGpuQueue.WaitAsync(semaphore, value, cancellationToken = default)` | 指定した提出の GPU 利用終了と runtime 診断の確定を非同期に待つ。正常復帰だけがその提出の成功を示す。待機の取消しは GPU work の取消しや完了を意味しない。 |
| `GpuSemaphore.Dispose()` | この timeline の提出と待機が終了してから同期 object を解放する。 |
| `GpuFenceValue(Semaphore, Value)` | 特定 timeline の値を運ぶ非 owning 値。別 timeline の同じ数値を同一完了点と扱わない。 |
| `GpuExecutionException` | GPU 利用終了後に報告する当該提出の失敗。`FenceValue` と runtime が返した `Diagnostics` を保持する。失敗した内容を利用できる意味ではなく、ほかの利用が残っていれば resource も回収できない。 |

signal value は同じ semaphore の予約済み値より大きくする。数値だけを再利用して古い work の completion と混同しない。`IsComplete`／`WaitAsync` の対象は initial value または queue への受渡しを開始した signal value とし、発行していない中間値を別の提出の結果として扱わない。受渡し後の失敗による受理不明の値も保持するが、対象を識別できることは GPU 利用終了の証明ではない。initial value は work を持たず成功済みとする。現在の Portable API は複数 queue 間の GPU wait を公開しない。

利用終了は timeline の進行でまとめて観測できるが、診断結果は batch ごとに確定する。値 2 の待機が成功しても値 1 の処理成功を代わりに証明しない。先行する失敗を無関係な後続 batch へ一律に伝播させず、先行出力の成功が必要な caller はその結果も確認する。resource manager／graph は自身が所有する内容世代の依存を引き継ぐ。

backend は pending の診断と待機だけを活動中の記録として保持し、確定した成功は発行済み値の区間へ集約する。確定結果を後から待つための失敗診断は semaphore の寿命まで保持する。caller は長期運用で蓄積する発行区間や失敗診断を区切る必要がある場合、全利用終了後に semaphore を更新できる。全成功 batch の object を永久に残す実装にはしない。

`IGpuQueue` は外部 backend が実装する interface、`GpuSemaphore` は public abstract 型と protected constructor から派生する。`Submit` は command の ReadOnlySpan を呼出し中に消費する。`WaitAsync` は ValueTask を返し、GPU work を同期的に待たない。`GpuExecutionException` は受け取った診断列をコピーし、呼出し側の配列変更で確定結果が変わらないようにする。

## 失敗と所有権

受理前に pipeline 作成や encode が失敗した場合、その batch は提出しない。失敗した記録は破棄し、必要なら新しく記録する。公開状態を調べて一部を復旧する手順は設けない。

queue への受渡し開始後は、同期障害があっても同じ signal point と内部記録の保持を失わない。error scope の回収や完了通知の登録で同期例外が起きた場合は `GpuSubmissionException` を返す。`Completion` は対象の識別に用い、到達保証や利用終了の証明にはしない。`GpuExecutionException` が利用終了後の診断失敗を表すことと区別する。例外通知自体が成立しない致命的な host 障害もあるため、任意の別の例外を未提出の証拠にしない。

runtime の validation、shader/pipeline 作成、encode、submit の診断が非同期の場合、同期的な `Submit` の復帰は成功を保証しない。当該 batch と、その batch が参照した実 object の生成診断を完了結果へ結び付ける。診断待ちの間に `IsComplete` が true になっても、`WaitAsync` は成功を報告しない。処理失敗は利用終了を確認してから `GpuExecutionException` として返すため、安全な回収と内容の成功を混同しない。

device loss または runtime との接続喪失で利用終了を確認できない場合は `GpuDeviceLostException` を返し、届かない値を永久に待たせない。`IsComplete` も未確認の利用終了を true にせず、同じ障害を通知する。待機の取消しとこの障害は回収の根拠にならず、device の停止処理へ所有を引き継ぐ。受理されたか不明な work を自動で再提出しない。runtime が通知した失敗を伝える契約であり、shader が意図した描画結果を意味解析して保証するものではない。

現在の backend の `Dispose` は caller が全利用を先に終了する契約を持ち、GPU 停止を確認する操作ではない。通常 completion を確認できない保持を、例外、`device.lost` や Dispose の復帰だけで再利用可能にしない。停止が確認できない障害から上位の保持を drain する契約は後続で整える。

caller は未提出の記録と全提出の利用を考慮して resource、binding、pipeline/program を保持する。`commands.Dispose()` は提出済み work を取り消さず、application resource の lifetime を変更しない。内部記録 memory は backend が完了後に回収する。

## コード配置

以下は repository root からの配置。Portable とそのテスト project、WebGPU と WebGPU.Browser に独立した提出・完了処理を置く。提出と完了は同じ project 内でも別のディレクトリに分ける。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Submission/` | `IGpuQueue.Submit`、`GpuSubmissionException` の公開契約と、一回提出の host 所有規約。 |
| `src/graphics/Lumyte.Graphics.Portable/Synchronization/` | `GpuSemaphore`、`GpuFenceValue`、`GpuExecutionException` と `CreateSemaphore`／`IsComplete`／`WaitAsync` の公開契約。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Submission/` | 提出時 pipeline 解決、encode の終了と queue への受渡し。受理前失敗と提出済み内部記録の所有を扱う。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Synchronization/` | queue の利用終了と batch ごとの診断結果、timeline 値、非同期待機、device loss による待機失敗。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser/Submission/`、`src/graphics/Lumyte.Graphics.WebGPU.Browser/Synchronization/` | queue 提出と `onSubmittedWorkDone` の promise 接続。ブラウザーを block する待機 API は追加しない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Submission/`、`src/graphics/Lumyte.Graphics.Portable.Tests/Synchronization/` | 隣接 xUnit project。一回提出と timeline identity など Lumyte 固有の契約を、device に依存せず検証する。外部 backend の consumer 試験は `Device/` にも置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Submission/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Synchronization/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Submission/` | 既存 xUnit project。fake runtime で診断と利用終了の両到着順、先行する別 batch の失敗、取消し・device loss を検証し、実 queue の提出と完了試験を隔離する。 |

## 使用例

`commands` は同じ queue で記録し、scope を閉じた command とする。resource はこの例の外で保持している。

```csharp
var queue = backend.MainQueue;
using var completed = queue.CreateSemaphore();

queue.Submit(new[] { commands }, completed, 1);
await queue.WaitAsync(completed, 1);
commands.Dispose();

// 正常復帰後は、この batch の出力を成功した内容として利用できる。
// ほかの未提出・提出済み参照もなければ resource も回収できる。
```

例は正常系を示す。`GpuExecutionException` ならこの batch の GPU 利用は終わっているが、出力は公開しない。待機が取消しや device loss で失敗した場合は同じ回収を行わず、利用終了または device の停止が確定するまで所有権を維持する。

## 参考文献

- [WebGPU: Queue Completion](https://gpuweb.github.io/gpuweb/#dom-gpuqueue-onsubmittedworkdone): queue の work 完了を非同期に観測する入口。

## 採用範囲と未実装事項

一回提出、CPU からの timeline 観測と、利用終了・処理成功の分離を採用する。native host の WebGPU に raster／compute／buffer・texture copy の提出、object／batch 診断の帰属、非同期 completion、device loss と待機取消しの接続を実装した。成功結果は発行値の区間へ集約し、過去の失敗診断は semaphore の寿命まで保持する。attachment 用の内部 view も、当該 command の GPU 利用終了後に回収する。

Browser も一つの `queue.submit` と `onSubmittedWorkDone`、独立した error scope の結果へ接続する。timeline 値は C# の ulong として管理し、JavaScript の Number へ変換しない。実機と制御した非同期結果による検証範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。

受渡し後の同期障害を通知する `GpuSubmissionException` と、その signal point・内部記録を保持する経路を実装した。Resources の token 発行、Retire／Collect と、利用終了を確認できない障害からの drain は未実装である。下位の例外は上位 Resources の token 型を参照しない。
