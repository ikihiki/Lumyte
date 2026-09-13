# Lumyte.Graphics.Portable.Resources

Portable の Buffer／Texture object を再利用する上位 utility です。`Lumyte.Graphics.Portable` の公開契約だけを参照し、backend を借用します。Native の heap／placement、shader、資産のロード処理、RenderGraph には依存しません。

現在は `GpuBufferPool` と `GpuTexturePool` を実装しています。貸出・返却・未使用 object の解放に範囲を絞り、completion による retirement、upload utility、managed scope／pin／binding cache はまだ実装していません。

## API

| API | 動作 |
| --- | --- |
| `GpuBufferPool(backend)`／`GpuTexturePool(backend)` | 借用 backend 上に pool を作ります。作成時に GPU resource は確保しません。 |
| `Acquire(description)` | 全フィールドが一致する未使用 resource を再利用し、なければ description をそのまま backend に渡して作成します。 |
| `GpuBufferLease`／`GpuTextureLease` | 一回の貸出を識別する object です。`Handle` は返却まで借用でき、`Description` は返却後も不変の値として読めます。 |
| `Release(lease)` | 同じ pool からの未返却の貸出を戻します。未提出参照と GPU 利用の終了は caller が保証します。 |
| `Trim()` | 未使用の resource だけを破棄します。貸出中の resource は残し、GPU を待機しません。 |
| `Dispose()` | 貸出が残る場合は状態を変えず失敗します。全貸出の返却後に再度呼べます。返却済みの resource を破棄し、backend は保持します。 |

同じ native handle を再貸出する場合も、新しい lease を返します。古い lease の再返却を拒否し、現在の貸出を誤って回収しません。lease のコピーは貸出を増やさず、lease 自体は `IDisposable` を実装しません。pool の `Release` に明示的に返します。

buffer の key は size と usage、texture の key は dimension、extent、mip／layer 数、sample 数、format、usage、MutableFormat の完全な組です。大きめの object への置換、usage の拡大、heap の作成、suballocation は行いません。再利用した resource の内容と GPU state は初期化されません。次の用途の初期化と同期は caller が行います。

## 使用例

次は既存の `IPortableGpuBackend backend` を借用し、貸出 buffer を使って 4 byte をコピーする正常経路の例です。

```csharp
using Lumyte.Graphics.Portable;
using Lumyte.Graphics.Portable.Resources;

using var buffers = new GpuBufferPool(backend);
var uploadDescription = new GpuBufferDescription(
    4, GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource);
var readbackDescription = new GpuBufferDescription(
    4, GpuBufferUsage.MapRead | GpuBufferUsage.CopyDestination);
var upload = buffers.Acquire(uploadDescription);
var readback = buffers.Acquire(readbackDescription);

using (var mapped = await backend.MapBufferAsync(upload.Handle, GpuMapMode.Write, 0, 4))
{
    mapped.Memory.Span.Fill(42);
}

using var completion = backend.MainQueue.CreateSemaphore();
var commands = backend.MainQueue.StartCommandRecording();
commands.CopyBuffer(new(upload.Handle, 0, 4), new(readback.Handle, 0, 4));
backend.MainQueue.Submit([commands], completion, 1);
await backend.MainQueue.WaitAsync(completion, 1);
commands.Dispose();

using (var mapped = await backend.MapBufferAsync(readback.Handle, GpuMapMode.Read, 0, 4))
{
    Console.WriteLine(mapped.ReadOnlyMemory.Span[0]); // 42
}
buffers.Release(upload);
buffers.Release(readback);

var next = buffers.Acquire(readbackDescription); // 同条件の返却済み buffer を再利用。
buffers.Release(next);
buffers.Trim();
```

texture も `var texture = textures.Acquire(description)`、`texture.Handle`、`textures.Release(texture)` の同じ形で使います。

## 所有、失敗と実行 context

pool は作成した resource を所有し、caller は raw handle を直接 `DestroyBuffer`／`DestroyTexture` に渡しません。mapping、view／binding、未提出 recording と GPU 利用を終了してから返します。pool はこれらを探索せず、native API の usage／format／alignment／GPU state の validation を複製しません。貸出が失効しても、先に取り出した raw handle を使えなくする機構はありません。

例外や CPU の wait cancellation は GPU 利用終了を表しません。提出後に待機が失敗した場合も、使用終了が確認できるまで貸出を保持します。`Release` は completion の代理ではなく、caller の明示的な返却契約です。

`Trim`／`Dispose` は対象の未使用 object を cache から取り除いてから、それぞれの破棄を一度ずつ試みます。一つが失敗しても残りを試み、単一の例外を保持し、複数なら `AggregateException` で返します。破棄が途中まで進んだか判断できない handle を再利用したり、自動で再度破棄したりしません。破棄を開始した `Dispose` は失敗後も終了済みです。貸出が残るために拒否された `Dispose` は、この状態変更を行いません。

caller は同じ pool の操作を直列化し、backend が要求する実行 context に従います。Browser では JavaScript thread から操作し、backend とその借用 runtime を pool より長く保持します。pool が thread を切り替えたり、background task／finalizer から GPU 操作を実行したりすることはありません。
