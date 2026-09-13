# Portable RenderGraph

共通の機能 pass を Portable backend へ展開する provider。設計と公開 API は
[ADR 0032](../../../docs/adr/0032-portable-render-graph-implementation.md) に記す。

内部 pass は Read／Write／ReadWrite を宣言する。Write は全範囲初期化で、ReadWrite は
先行内容を必要とする。未使用 writer を culling し、live な未初期化読取りを拒否する。
同じ description で生存区間の重ならない transient は同じ object を再利用する。
64件までの schedule／生存区間 cache は整数 index だけを保存し、過去 frame を保持しない。

```csharp
var scratch = context.CreateBuffer("scratch", new(256, GpuBufferUsage.Storage));
// 外部副作用を持つこの機能の共通契約も Preserve を宣言する。
context.AddPass("prepare", scratch, static (record, data) =>
{
    // 準備済み pipeline、binding、root を使う command を記録する。
    var range = record.GetBufferRange(data);
}).Write(scratch, PortablePassUsage.StorageWrite).Preserve();
```

frame 間の CPU 準備は `PortablePassTemplate<TState>` と
`PortablePassPreparationCache<TKey, TValue>`、GPU 内容は
`RegisterContent`／`TryUseContent` を使い分ける。ticket の Dispose は新規取得だけを終了し、
既に受理された writer／reader は GPU 利用終了まで保持する。遅延診断は利用側と派生内容へ
伝播し、先行 upload が受理済みなら後続 Build の失敗だけでは upload の内容を失効しない。

raw object を接続するときは完全な description、所有 lease と同じ manager の先行提出 token を
明示する。GPU の usage／format／binding 合法性は WebGPU に任せる。

共通 facade の package import は `images.sampled` version 1 に対応する。
Model などの upload profile と uploader の拡張口は、その機能の実装時に追加する。

CPU 検証:

```powershell
dotnet test src/graphics/Lumyte.Graphics.Portable.RenderGraph.Tests/Lumyte.Graphics.Portable.RenderGraph.Tests.csproj
```
