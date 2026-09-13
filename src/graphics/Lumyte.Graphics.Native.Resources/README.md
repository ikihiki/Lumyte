# Native resource management

`Lumyte.Graphics.Native.Resources` は Native resource の所有、明示依存、GPU 使用、descriptor と転送を管理します。[ADR 0029](../../../docs/adr/0029-resource-management-api.md) の manager と、[ADR 0028](../../../docs/adr/0028-resource-utilities.md) の薄い memory arena を同じ project 内で分離しています。参照先は `Lumyte.Graphics.Native` だけで、backend、Portable、shader library、資産 loader、RenderGraph へ依存しません。

## Resource manager API

| API | 契約 |
| --- | --- |
| `GpuResourceManager(backend, options)` | backend を借用し、用途別 arena、resource record、descriptor storage、cache、非公開 timeline を所有します。 |
| `GpuResourceManagerOptions` | backing block の byte 数と resource／sampler descriptor の固定容量を設定します。 |
| `CreateScope()` | caller が所有する CPU 保持集合を作ります。同じ record の取得を繰り返しても、その scope 内では一つの保持です。 |
| `scope.CreateBuffer(GpuBufferDescription)` | `Size`、`MemoryKind`、`Alignment` から linear region を配置します。 |
| `scope.CreateTexture(description, memoryKind, defaultView)` | Native texture を配置します。明示した default view は texture と同じ owner record で保持します。 |
| `scope.GetView(texture, GpuTextureViewDescription)` | Sampled／Storage の descriptor または Attachment の render view を作成・共有します。 |
| `scope.GetView(buffer, GpuBufferViewDescription)` | 論理 byte 範囲と access から buffer descriptor を準備します。 |
| `scope.GetSampler(description)` | 不変な Native sampler description の slot を準備・共有します。 |
| `scope.AddDependency(resource, dependency)` | scope が保持する resource に明示依存を登録します。循環と別 manager の参照は拒否します。pointer/index の中身は走査しません。 |
| `scope.Release(reference)`／`scope.Dispose()` | この scope の CPU 保持を返します。別 scope、pin、use、batch の保持は残ります。 |
| `Pin(reference)`／`AcquireUse(reference)` | scope 外の保持、または raw 記録前の使用保持を取得します。返却は `Dispose` です。 |
| `use.Retire(token)` | 同じ manager の token が全利用を覆うことを caller が保証し、使用保持を manager に移します。 |
| `GetBufferRange`／`GetGpuAddress` | linear region 基準の raw range／address を返します。heap placement offset を二重に加えません。 |
| `GetTextureHandle`／`GetTextureView` | 準備済み Native handle／view を借用します。 |
| `GetShaderIndex(view or sampler)`／`GetRenderViewHandle` | 作成済み世代の slot／attachment handle を返します。取得時に割当や更新を行いません。 |
| `ResourceDescriptorHeap`／`SamplerDescriptorHeap` | manager の二つの descriptor heap を借用します。最初に必要となった時点で固定容量の storage を作ります。 |
| `Collect()` | GPU 使用終了と batch の終了要求が揃った保持、および参照されなくなった record を非 block で回収します。 |
| `Trim()` | `Collect` 後、arena の完全に空いた block を破棄します。 |
| `WaitIdleAsync(cancellationToken)` | 呼出し時点の管理された提出を待ちます。既知の提出・観測失敗は、別の未完了提出を待ち続けず伝えます。 |
| `Statistics` | record、保持中 record、未回収提出、allocation byte 数、使用 slot と view／sampler cache 数を返します。allocation 解放に失敗した場合は、解放未確認の確保も保持します。 |
| `DisposeAsync()` | 外部の scope／pin／use／batch が返却済みであることを確認し、管理 work を drain して所有物を破棄します。backend は破棄しません。 |

`GpuBufferRef`、`GpuTextureRef`、`GpuViewRef`、`GpuSamplerRef`、`GpuPackageRef` は生成時の record identity を持つ非所有参照です。CLR の参照をコピーしても保持は増えません。scope／pin／use／batch のいずれかで寿命を覆います。回収した record は再利用せず、古い参照が同じ allocation や slot の新しい resource に接続することはありません。

descriptor の index は view／sampler 世代の間は不変です。未使用 slot を回収してから次の割当に使います。default view の owner と texture の間に循環する保持を作らず、追加 view は resource を依存として保持します。cache から参照を返す場合も、回収済み世代は使用しません。

caller は manager 操作を直列化します。raw handle／address の取得は所有権の移管ではなく、下位 backend から管理対象を直接破棄しません。異なる queue、外部の手動提出、未提出記録、CPU と GPU の競合は明示的な caller 契約に従います。

## Batch と completion

`BeginBatch()` は main queue の一回の提出を作ります。`Use(reference)`／`Use(scope)` は最初の記録前に宣言し、scope の集合はその時点で固定します。`Own(scope)` は scope の所有を移し、caller の変更・終了を禁止します。`Retain(IDisposable)` は外部 lease を引き受けます。同じ manager の use／pin を渡した場合も、所有権を batch に移して manager の drain に接続します。

`StartCommandRecording()` は batch 所有の Native command を返し、二つの descriptor heap を初期設定します。初期設定に失敗した recording も batch が所有し、追加記録・提出を拒否して `Dispose()` で破棄します。`Submit()` は一度だけ提出し `GpuSubmissionToken` を返します。root data、Parameter Data、shader の意味や GPU hazard は解釈しません。caller は batch の終了要求として `Dispose()` を呼びます。未提出なら記録を破棄してから保持を返し、提出後なら GPU 利用終了を確認するまで保持します。

token の `IsValid` は発行済み identity、`IsComplete` は確認済みの GPU 利用終了です。`WaitAsync` は処理成功を待ちます。GPU 利用終了を後から確認できた失敗でも成功へ変更しません。token は manager が非公開で所有する timeline に接続し、raw semaphore や `SignalCpu` の権限を公開しません。default token の待機・retirement は拒否します。

raw queue に入った後の例外は、例外の型だけで未提出と判断しません。`GpuSubmissionException.Completion` へ同じ token を結び付け、元の診断を保持します。下位の raw completion を含む例外が入れ子になっていても、その通知権限は公開例外の連鎖から除きます。待機取消しは CPU 側の待機だけに作用し、GPU work や保持を取り消しません。

回収ではまず全 recording を破棄し、すべて成功した場合に retained lease を返します。lease の返却もすべて成功してから resource と scope の保持を返します。破棄が不明な対象を再試行せず、その依存と配置範囲を保持します。独立した完了・回収は継続し、元の障害と回収時の障害をともに伝えます。

device loss、観測失敗、backend の `Dispose` を GPU 停止の証拠にはしません。利用終了を確認できない場合、manager の drain／終了は失敗して保持を残します。この package に device の強制停止や保持の強制放棄 API はありません。

## Package と非同期転送

`GpuPackagePlan(resources, exports)` は準備済み CPU data の immutable な計画です。`GpuPackageBuffer` は description、byte 列のコピー、転送先 offset、明示依存を保持します。`GpuPackageTexture` は Native description、`GpuTextureUpload` の一覧、memory kind、default view と依存を保持します。`GpuPackageExport(Id, ResourceId)` から `package.GetExport(id)`／`GetExport<T>(id)` で typed reference を取得します。plan に file path、URI、stream、decoder は入りません。

`scope.ImportPackageAsync(plan, cancellationToken)` は `Pools` を使います。Native 専用 overload に `GpuPackagePlacement.SingleAllocation` を渡すと、各 `AllocationGroup` の全 requirements を一つの heap 確保へ渡して配置します。同じ group は一つの memory kind を持ち、複数 allocation へのフォールバックは行いません。一つの export の pin／use が残れば、その allocation group 全体を保持します。staging と descriptor storage は group の外です。

`GpuTextureUpload(data, footprint, rowCount, rowBytes, beforeLayout, afterLayout, stagingAlignment)` は byte 列をコピーし、CPU の row/image pitch と必要 byte 数、overflow を確認します。row 数と有効 row byte 数は caller の準備済み layout です。format と GPU copy 条件の合法性は backend が検証します。明示 layout を使う backend には指定した before/after を渡し、Vulkan の GENERAL model では GENERAL の初期化と明示 memory dependency を使います。

`UploadBufferAsync(destination, data, destinationOffset, cancellationToken)` と `UploadTextureAsync(destination, upload, cancellationToken)` は既存 resource を更新します。Native 用の `GpuUploadSynchronization` を追加指定し、前後の access を明示できます。GPU-only buffer／texture は staging copy、CPU-visible buffer は mapping を使います。CPU-visible buffer の更新では先行する manager main queue の利用終了を待ちます。別 queue と外部記録は caller が同期します。

`ReadBufferAsync(source, offset, length, cancellationToken)` と `ReadTextureAsync(source, footprint, rowCount, rowBytes, beforeLayout, afterLayout, cancellationToken)` は manager が readback の配置・使用・返却を保持し、成功確認後に byte 列を返します。Native 用の `GpuReadbackSynchronization` を指定できます。texture の結果は指定 footprint の配置で返し、row/image padding の内容には意味を持たせません。

初期 upload の成功前に package を公開しません。取消しや提出失敗で一部の生成物が不要になっても、提出済みの staging と転送先は利用終了まで保持します。

```csharp
using Lumyte.Graphics.Native.Resources;

// native は caller が作成し、最後に破棄する INativeGpuBackend。
await using var resources = new GpuResourceManager(native);
using var scope = resources.CreateScope();
var plan = new GpuPackagePlan(
    [new GpuPackageBuffer("values", new(4), new byte[] { 1, 2, 3, 4 })],
    [new GpuPackageExport("Values", "values")]);
var package = await scope.ImportPackageAsync(plan);
var buffer = package.GetExport<GpuBufferRef>("Values");
await resources.UploadBufferAsync(buffer, new byte[] { 7, 9 }, destinationOffset: 1);
byte[] result = await resources.ReadBufferAsync(buffer); // 1, 7, 9, 4
scope.Release(package);
resources.Collect();
```

## コード配置

`Management/` は record、scope／pin／use／batch、statistics と回収を、`Management/Submission/` は非公開 timeline と token を持ちます。`Descriptors/` は slot と view／sampler cache、`Packages/` は CPU plan と配置 group、`Upload/` は初期転送・更新・readback を担当します。`Utilities/Arena/` は以下の明示的な arena に限定し、completion や retirement を追加しません。隣接 `.Tests/Unit/Management/` と `.Tests/Unit/Packages/` に GPU 不要の失敗・寿命・転送試験を置き、実 GPU の適合試験は DirectX12／Vulkan の test project に置きます。

shader schema からの managed input 生成は別 project `Lumyte.Graphics.Native.Resources.Generators` が担当します。生成入力の `Retain(batch)` が使用を宣言し、`Write(manager, destination)` が宣言された pointer/index field だけを埋めます。下位 shader runtime とこの manager に上位 Graph 依存はありません。

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

`NativeGpuMemoryRequirements.Size` は logical な Buffer byte 数ではなく、配置境界の余白を含む予約量です。backend は返却する alignment と size に Vulkan の buffer/image granularity 等を含めています。arena はこの予約量と指定 alignment を使い、追加の native 制約を照会・分類しません。mixed allocation では同じ全 token 集合と、全 resource の alignment の倍数となる共通 alignment を指定します。alignment がすべて 2 の累乗なら、その最大値を使えます。

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
NativeGpuLinearRegion? region = null;
try
{
    region = native.CreateLinearRegion(logicalSize, slice.Heap, slice.Offset);
    // CPU mapping は region.CpuAddress を使用する。
    // GPU に提出した場合は、その利用を終了してから次の finally へ進む。
}
finally
{
    if (region is not null) { native.DestroyLinearRegion(region); }
    arena.Release(slice);
}

arena.Trim();
```

heap 全体の破棄権限は arena にあります。caller は `slice.Heap` を手動で破棄しません。配置時は `slice.Offset + localOffset` を一度だけ加え、配置後の `NativeGpuRange.Offset` と混同しないでください。

返却後も slice の数値は読めますが、使用権限は失効しています。同じ heap／offset が再貸出しされても、古い slice の `Release` は新しい貸出を返しません。再利用した memory の内容、resource state、alias の初期化状態を生成直後と同じとはみなしません。必要な GPU 同期と再初期化は caller が行います。

配置 resource の破棄が失敗したときは slice を返却しません。上位の manager が resource と allocation の所有権を保持して対処します。

`Trim`／正常に開始した `Dispose` は対象とエラー保持領域を用意してから block をすべて切り離し、各 `DestroyGpuHeap` を一度ずつ試みます。一つが失敗しても残りを試み、一件なら元の例外、複数なら `AggregateException` を返します。native 解放の副作用は例外だけでは判定できないため、失敗した heap の解放を再試行したり pool へ戻したりしません。arena の終了後や backend 破棄後の操作を続けないでください。

## 実装範囲

この utility が担当する heap 確保、alignment、空き範囲の分割・結合、貸出 identity、`Release`／`Trim`／`Dispose` は実装済みです。隣接 `.Tests` の `Unit/Utilities/` に、GPU 不要の fake backend を用いた範囲計算、断片化後の全領域再利用、opaque identity、失敗時の保持・解放の試験を置きます。実 GPU の配置・転送は DirectX12／Vulkan の適合試験で確認します。

配置 resource の生成・破棄、参照関係、GPU 完了と返却タイミング、descriptor 管理、転送、package upload、RenderGraph との接続は上位 resource manager の責務です。completion token、`Retire`／`Collect`、非同期 upload は、この utility に追加する API ではありません。manager が利用終了を確認した範囲を `Release` へ渡します。
