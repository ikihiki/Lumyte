# Lumyte.Graphics.Portable.Resources

Portable の明示的な resource ownership と GPU 利用の終了を管理するライブラリです。`Lumyte.Graphics.Portable` と `Lumyte.Graphics.Portable.Shaders` の公開契約を使い、backend と prepared shader program を借用します。Native の heap／placement、資産のロード処理、RenderGraph には依存しません。別 assembly の backend を追加するための production `InternalsVisibleTo` は必要ありません。

管理層の scope、pin、use、batch、completion token、binding cache、prepared package、非同期転送と、その下の whole-resource pool を実装しています。GC のように到達可能な CLR object を探索する仕組みではなく、明示した ownership と依存がなくなった resource を `Collect` で回収します。

manager は mapping、view／binding、記録中の batch と GPU 利用が必要な lease を保持し、利用終了後で pool の `Release` を呼びます。utility 自体の対象は引き続き `GpuBufferPool`／`GpuTexturePool` と、その貸出・返却・未使用 object の解放のみです。utility に token、retirement、upload や依存追跡は追加していません。

## 管理 API

| API | 動作 |
| --- | --- |
| `GpuResourceManager(backend, options)` | 借用 backend の main queue に private timeline を作り、resource record と whole-resource pool を所有します。空の options は将来の管理方針用で、GPU description を変更しません。 |
| `CreateScope()` | CPU owner を作ります。`CreateBuffer`／`CreateTexture` は記述をそのまま pool に渡します。 |
| `GpuResourceRef` と typed ref | 非所有の一意 identity です。コピーで hold は増えず、回収後は失効します。同じ raw handle の再利用でも古い ref は復活しません。 |
| `scope.Release(ref)`／`scope.Dispose()` | scope 自身の hold を落とします。pin、record dependency、batch は残ります。 |
| `scope.AddDependency(resource, dependency)` | scope が所有する resource に明示的な依存を追加します。別 manager、失効 ref、循環を拒否し、GPU 入力は探索しません。 |
| `Pin(ref)` | CPU の明示的な長期 hold を返します。`Dispose` まで対象と依存を保持します。 |
| `AcquireUse(ref)` | raw recording に渡す前の明示的な利用 hold です。未提出参照／GPU 利用の終了を caller が確認して `Dispose` するか、同じ manager の token へ `Retire` します。 |
| `BeginBatch()` | recordings、resource use、外部 lease をまとめて所有します。`Use(ref)`、`Use(scope)` は追加 hold／scope の現時点 snapshot、`Own(scope)` は scope の所有を移譲して閉じます。 |
| `batch.Retain(lease)` | 外部 lease の返却責任を移譲します。同じ manager の pin／use／mapping lease は外部 owner 登録も移譲し、caller の `Dispose`／`Retire` を拒否します。 |
| `batch.StartCommandRecording()`／`Submit()` | 所有 recording を作成し、一度だけ提出して管理 token を返します。Portable は非空の recordings が必要です。提出開始後に追加・再提出はできません。 |
| `batch.Dispose()` | batch 所有の終了を要求します。未提出なら recording と外部 lease を先に閉じます。提出後は GPU 利用終了との両方が揃ってから hold を戻します。 |
| `GpuSubmissionToken` | 発行元 manager と submission の等価 identity。default は無効・未完了です。`IsComplete` は GPU 利用終了だけを表し、`WaitAsync` は診断を含む処理結果を返します。private raw timeline／signal は公開しません。 |
| `Collect()`／`Trim()` | 完了を非同期に待たず、独立した完了登録も回収します。referrer の破棄成功後に依存 hold を落とし、`Trim` は idle pool resource も解放します。 |
| `WaitIdleAsync()` | 現時点の管理 submission を待機して回収します。CPU owner の強制解放や device 全体の idle 待機ではありません。観測不能な失敗を別の未完了 submission の後ろに無期限で隠しません。 |
| `DisposeAsync()` | 外部 scope／pin／use／batch が残れば状態を変えず拒否します。終了開始後は新規作成を止め、既知の GPU 終了を待って回収します。未知の利用や失敗した cleanup は保持し、backend は破棄しません。 |
| `Statistics` | live resource 種別数、論理 buffer byte 数、未完了提出数、破棄失敗の隔離数。Portable の物理 allocation byte 数ではありません。 |

```csharp
await using var resources = new GpuResourceManager(backend);
using var scope = resources.CreateScope();
var buffer = scope.CreateBuffer(new(4096,
    GpuBufferUsage.Storage | GpuBufferUsage.CopySource | GpuBufferUsage.CopyDestination));
await resources.UploadBufferAsync(buffer, preparedBytes);

using var batch = resources.BeginBatch();
batch.Use(buffer); // raw handle を記録する前に保持する。
var commands = batch.StartCommandRecording();
// commands に compute / raster / copy を記録する。
GpuSubmissionToken completion = batch.Submit();
batch.Dispose();
scope.Dispose();
await completion.WaitAsync();
resources.Collect();
```

## View、binding、shader inputs

`scope.GetView(texture, description)` は正規化した description と resource identity で再利用します。default view は texture owner record に含め、所有の循環を作りません。このため同じ scope の texture とその default view は一つの所有 identity です。追加 view は texture を依存として保持します。`scope.GetSampler(description)` は記述が一致する論理 sampler を再利用します。引数なしの `GetSampler()` は `new GpuSamplerDescription()` を使い、明示的な `default` の値を補正しません。Portable には独立した raw view／sampler object 作成はなく、backend が binding 内で実体を作ります。

`scope.GetBindings(program, group, inputs)` は `IGpuBindingInputs.Write(GpuBindingWriter)` を呼び、`Buffer(binding, ref, offset, length)`、`Texture(binding, viewRef)`、`Sampler(binding, samplerRef)` を raw entries へ解決します。生成器は prepared shader metadata に沿った typed inputs を生成できます。buffer の length 省略は残りの範囲です。layout の種類・usage・不足や重複 binding の検証は backend へ委ねます。manager は別 owner、失効、CPU range arithmetic と、配列選択に必要な group 範囲だけを確認します。

binding cache は layout identity、resource/view/sampler identity、buffer range と実際の entries を key とし、入力順序を binding 番号で安定化します。bind group 自体が依存 resource を保持し、cache は永久 owner にはなりません。program／layout は caller が binding と全 GPU 利用より長く保持してください。manager は program を破棄せず、その寿命を自動推論しません。

`GetBufferRange`、`GetTextureHandle`、`GetTextureView`、`GetBindingsHandle` は hold を追加しない raw 借用アクセスです。raw recording を直接使う場合、caller が事前に use／batch へ対象を登録します。すでに渡した raw handle や GPU bytes を追跡し直す機能はありません。

## Prepared package と転送

`GpuPackagePlan` は immutable な `GpuPackageBuffer`／`GpuPackageTexture` と `GpuPackageExport`、local dependency ID を保持します。buffer は description・owned bytes・destination offset、texture は description と `GpuTextureUpload` の列を持ちます。`GpuTextureUpload` は owned bytes と `GpuTextureCopyFootprint` の row/image pitches を持ち、GPU allocation layout は表しません。constructor で CPU bytes と列をコピーし、重複／欠落 ID、循環、bytes が覆う CPU 範囲を確認します。URI、stream、GLTF の解釈や資産ロードは行いません。

`scope.ImportPackageAsync(plan)` は resource 作成と upload 完了を待ち、成功後に `GpuPackageRef` を公開します。`GetExport(name)`／`GetExport<T>(name)` で借用 ref を取得し、必要なら `Pin` します。package は全構成 resource を保持しますが、export の pin は対象とその明示的な依存だけを保持します。Portable では無関係な resource を物理 allocation group としてまとめません。

`UploadBufferAsync`、`ReadBufferAsync`、`UploadTextureAsync`、`ReadTextureAsync` は private batch と pool staging を使います。map 前の ownership、unmap、GPU 完了、readback 公開を順に行い、診断失敗時の bytes は返しません。MapWrite buffer への upload は直接 mapping し、WebGPU で両立しない CopyDestination usage を追加しません。MapWrite path と caller が直接使う `MapBufferAsync` の外部 GPU 同期は caller の責任です。manager は raw buffer mapping lease を返し、その成功した unmap まで resource を保持します。Native の CPU pointer/span と異なり、この Portable mapping API は非同期 raw mapping を包みます。

transfer の CPU byte count と pitch arithmetic を確認しますが、GPU の format／usage／copy alignment 検証は重複しません。転送の await は backend execution context を保持し、GPU 操作を worker thread へ移しません。CPU wait の cancellation は提出済み GPU work をキャンセルしません。

texture readback は footprint が覆う byte span を返します。row／image pitch の padding は texel data ではなく、値は未定義です。必要な行と slice だけを読みます。

## 提出と cleanup の失敗

manager 内で確実に提出前と判定できる失敗（例: 空 batch）はそのまま返します。raw Submit へ入った後の例外は、任意の host 例外から rejection を推測せず、`Resources.GpuSubmissionException` と管理 token を返します。元の原因を保持しますが、raw fence を持つ例外は private signal authority が漏れない形へ変換します。token の発行は受理／完了証明ではなく、観測失敗は hold を残します。後から raw completion が終了を確認できれば `Collect` は回収できます。

`Resources.GpuExecutionException` は GPU 利用終了後の診断失敗です。出力を成功扱いしませんが、利用が終わった resource の回収は可能です。未観測の診断失敗は drain で報告し、観測済みの完了履歴は manager から除去します。外部 token は引き続き同じ結果を保持します。

recording／外部 lease の cleanup は全件分のエラー領域を事前に確保し、独立した recording を一度ずつ試みます。recording が参照し得る retained lease は、全 recording の終了が確認できた後だけ返却を試みます。失敗した batch は依存を保持します。binding などの破棄が失敗した record も隔離し、依存 resource を pool へ戻しません。失敗した対象を自動で再破棄しません。未知の利用終了を backend `Dispose`、device loss、wait cancellation、CLR GC から推測することもありません。停止保証がない device failure を強制回収する機構は提供しません。

## Utility API

| API | 動作 |
| --- | --- |
| `GpuBufferPool(backend)`／`GpuTexturePool(backend)` | 借用 backend 上に pool を作ります。作成時に GPU resource は確保しません。 |
| `Acquire(description)` | 全フィールドが一致する未使用 resource を再利用し、なければ description をそのまま backend に渡して作成します。 |
| `GpuBufferLease`／`GpuTextureLease` | 一回の貸出を識別する object です。`Handle` は返却まで借用でき、`Description` は返却後も不変の値として読めます。 |
| `Release(lease)` | 同じ pool からの未返却の貸出を戻します。manager または直接利用する caller が、未提出参照と GPU 利用の終了を保証します。 |
| `Trim()` | 未使用の resource だけを破棄します。貸出中の resource は残し、GPU を待機しません。 |
| `Dispose()` | 貸出が残る場合は状態を変えず失敗します。全貸出の返却後に再度呼べます。返却済みの resource を破棄し、backend は破棄しません。 |

同じ native handle を再貸出する場合も、新しい lease を返します。古い lease の再返却を拒否し、現在の貸出を誤って回収しません。lease のコピーは貸出を増やさず、lease 自体は `IDisposable` を実装しません。pool の `Release` に明示的に返します。

buffer の key は size と usage、texture の key は dimension、extent、mip／layer 数、sample 数、format、usage、MutableFormat の完全な組です。大きめの object への置換、usage の拡大、heap の作成、suballocation は行いません。再利用した resource の内容と GPU state は初期化されません。次の用途の初期化と同期は caller が行います。

## 使用例

次は既存の `IPortableGpuBackend backend` を借用し、同じ description の buffer を再利用する例です。

```csharp
using Lumyte.Graphics.Portable;
using Lumyte.Graphics.Portable.Resources;

using var buffers = new GpuBufferPool(backend);
var description = new GpuBufferDescription(4096, GpuBufferUsage.Storage);
var loan = buffers.Acquire(description);
GpuBufferHandle buffer = loan.Handle;

// この例では GPU に提出しない。利用した場合は manager が全利用終了後に返す。
buffers.Release(loan);

var next = buffers.Acquire(description); // 同条件の返却済み buffer を再利用。
buffers.Release(next);
buffers.Trim();
```

texture も `var texture = textures.Acquire(description)`、`texture.Handle`、`textures.Release(texture)` の同じ形で使います。

## 所有、失敗と実行 context

pool は作成した resource を所有し、caller は raw handle を直接 `DestroyBuffer`／`DestroyTexture` に渡しません。manager は mapping、view／binding、未提出 recording と GPU 利用を終了してから返します。pool を直接使う場合は caller が同じ責任を持ちます。pool はこれらを探索せず、native API の usage／format／alignment／GPU state の validation を複製しません。貸出が失効しても、先に取り出した raw handle を使えなくする機構はありません。

例外や CPU の wait cancellation は GPU 利用終了を表しません。提出後に待機が失敗した場合も、使用終了が確認できるまで貸出を保持します。`Release` は completion の代理ではなく、caller の明示的な返却契約です。

`Trim`／`Dispose` は対象の snapshot と全件分のエラー保存領域を確保してから、未使用 object を cache から取り除き、それぞれの破棄を一度ずつ試みます。準備が失敗した場合は cache と pool の状態を変えません。一つの破棄が失敗しても残りを試み、単一の例外を保持し、複数なら `AggregateException` で返します。破棄が途中まで進んだか判断できない handle を再利用したり、自動で再度破棄したりしません。破棄を開始した `Dispose` は失敗後も終了済みです。貸出が残るために拒否された `Dispose` は、この状態変更を行いません。

caller は同じ pool の操作を直列化し、backend が要求する実行 context に従います。Browser では JavaScript thread から操作し、backend とその借用 runtime を pool より長く保持します。pool が thread を切り替えたり、background task／finalizer から GPU 操作を実行したりすることはありません。
