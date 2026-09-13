# Portable GPU 基盤

WebGPU の resource と実行モデルに対応する独立した低レベル契約。[設計の正本](../../../docs/adr/0016-portable-api.md) は Portable ADR 群である。Native の backend を経由せず、Buffer／Texture は内部 memory を含めて生成・破棄する。

現在の範囲は device、要求 feature／limits、Buffer／Texture の生成・破棄、非同期 map/unmap、runtime 診断である。binding、shader、pipeline、copy を含む command、queue completion、Browser runtime の接続は後続段階とする。

## Device と直接入力

native host では `Lumyte.Graphics.WebGPU.WebGpuBackend.CreateAsync(options)` が、同梱 Dawn runtime の instance／adapter／device を所有する。旧 `IGpuBackend` 用 factory は `Lumyte.Graphics.WebGPU.Legacy.WebGpuBackend` へ移し、新しい Portable device と相互変換しない。

native host の callback と GPU 完了通知は、instance ごとの内部イベント処理が進行させる。未完了の native future がない間は休止する。利用者が polling する必要はなく、`CreateAsync`／`MapBufferAsync` の公開呼出しは非同期に復帰する。

WGSL の `immediate_address_space` と非ゼロの直接入力 capacity は初期化要件である。`MaxImmediateSize` を未指定なら adapter が提供する capacity を要求し、明示指定した場合はその値を要求する。有効な limits は作成した device から取得する。64 byte の固定 root ABI や、root を GPU buffer に退避する経路は設けない。この段階では、Portable shader の作成・実行はまだ提供しない。

`GpuBackendOptions.RequireDualSourceBlend` と `RequireIndirectFirstInstance` は任意 feature の要求で、`Capabilities` は有効になった機能だけを返す。`RequiredLimits` の nullable member は null と0を区別する。Max limits は必要容量の下限、Min offset alignments は許容制約の上限として runtime へ渡す。native C API の未指定 sentinel と衝突する明示値は表現不能として拒否する。

## Buffer の作成と mapping

```csharp
using Lumyte.Graphics.WebGPU;
using P = Lumyte.Graphics.Portable;

using var backend = await WebGpuBackend.CreateAsync(new P.GpuBackendOptions
{
    RequiredLimits = new() { MaxImmediateSize = 16 }
});
var upload = backend.CreateBuffer(new P.GpuBufferDescription(
    256, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
try
{
    using var mapped = await backend.MapBufferAsync(upload, P.GpuMapMode.Write, 64, 16);
    mapped.Memory.Span.Fill(42);
} // unmap してから Buffer を破棄する。
finally
{
    backend.DestroyBuffer(upload);
}
```

offset は Buffer 先頭からの byte offset である。Write mapping は `Memory<byte>`、Read mapping は `ReadOnlyMemory<byte>` を使う。Write mapping でも `ReadOnlyMemory` を取得できる。`Dispose` は unmap し、Buffer 自体を破棄しない。

unmap 後は保存した Memory から Span を取得し直す操作も拒否する。ただし、すでに取得済みの Span／pointer を失効させることはできないため、caller は unmap 前に全アクセスを終了し、Dispose 後にそれらを使用しない。mapping の length は `Memory<byte>` の int 長とホストの pointer 幅で表現できる必要がある。native の map alignment／usage／resource size の検証は runtime に委ねる。

## Texture と所有

```csharp
var texture = backend.CreateTexture(new P.GpuTextureDescription(
    P.GpuTextureDimension.Texture2D,
    Width: 64, Height: 64, Depth: 1,
    MipCount: 1, LayerCount: 1, SampleCount: 1,
    Format: Lumyte.Graphics.GpuFormat.Rgba8Unorm,
    Usage: P.GpuTextureUsage.Sampled | P.GpuTextureUsage.CopyDestination,
    MutableFormat: true));
try
{
    // view、binding、転送と描画は後続の API で接続する。
}
finally
{
    backend.DestroyTexture(texture);
}
```

事前の heap、allocation、配置要件は不要である。`MutableFormat` は runtime が許す互換 view format の意図であり、公開の許可 format 列を要求しない。Texture handle から配置情報や GPU address を取得する API は設けない。

caller は全利用終了後に resource を一度だけ破棄し、その後に backend を破棄する。handle のコピーは resource を延命しない。backend に全 resource の registry、自動 staging、GC や暗黙の GPU wait は置かない。

## Runtime 診断

resource の同期生成は runtime の非同期診断の成功を保証しない。validation／out-of-memory／internal scope を生成操作に対応付け、診断結果をその object に保持する。map は buffer の生成診断と native map の双方を確認してから成功する。失敗時は `GpuOperationException` に操作名とコピー済み診断列を渡し、device loss は `GpuDeviceLostException` で通知する。

scope を開いた native 呼出し区間は同じ device で直列化し、全 scope を pop してから非同期結果を待つ。無関係な object の診断を別の操作へ付け替えず、runtime の validator を複製しない。Texture を利用する binding／command への生成診断の接続と、GPU 利用終了・batch 成功の分離は後続段階で実装する。

## Backend の追加

外部 assembly は `IPortableGpuBackend` を実装し、`GpuBufferHandle`、`GpuTextureHandle`、`GpuMappedBufferRange` の public abstract 基底型と protected constructor から非公開実装を派生させる。Portable assembly の `InternalsVisibleTo` は不要である。

動作確認は隣接する [Portable.Tests](../Lumyte.Graphics.Portable.Tests/Lumyte.Graphics.Portable.Tests.csproj) の consumer test と、[WebGPU の適合試験](../Lumyte.Graphics.WebGPU.Tests/Integration/PORTABLE.md) に分ける。試験結果と後続作業は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) を参照する。
