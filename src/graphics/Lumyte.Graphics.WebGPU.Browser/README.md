# Browser WebGPU backend

`Lumyte.Graphics.WebGPU.Browser.WebGpuBackend` は、browser の `navigator.gpu` に接続する `IPortableGpuBackend` の実装である。production の参照先は `Lumyte.Graphics.Portable` とし、resource、明示 binding、raw WGSL、raster／compute、copy と CPU timeline の契約を実装する。[Portable API](../Lumyte.Graphics.Portable/README.md) と [WebGPU backend ADR](../../../docs/adr/0027-webgpu-backend-implementation.md) を設計の参照先とする。

## 起動と所有

host は配布する `lumyte-webgpu.js` の公開 URL を決め、`WebGpuBrowserRuntime.LoadAsync(moduleUrl)` に渡す。戻り値は呼出し元が所有する JavaScript module である。`WebGpuBackend.CreateAsync(runtime, options)` は runtime を借用し、作成した WebGPU device を所有する。一つの runtime を複数 device で利用でき、全 backend の終了後に runtime を破棄する。

device、resource、command の操作と mapped memory へのアクセスは、runtime を作成した JavaScript thread で行う。非同期処理から戻る際もその thread を維持し、map と GPU 完了は `await` で待つ。CPU timeline の値は .NET の `ulong` として保持し、JavaScript の number に変換しない。

WGSL の `immediate_address_space`、非ゼロの `maxImmediateSize`、render／compute encoder の `setImmediates` を初期化要件とする。未対応なら `CreateAsync` は失敗する。`RequireDualSourceBlend` と `RequireIndirectFirstInstance` は必要な場合に options で要求し、`Capabilities` と `Limits` は作成した device の有効値を返す。

## 小さな使用例

以下は browser の .NET host で動かす正常系の例である。`/lumyte-webgpu.js` を公開済みとし、4 byte の値を upload、GPU copy、readback の順に読み戻す。

```csharp
using System.Buffers.Binary;
using Lumyte.Graphics.WebGPU.Browser;
using P = Lumyte.Graphics.Portable;

using var runtime = await WebGpuBrowserRuntime.LoadAsync("/lumyte-webgpu.js");
using var backend = await WebGpuBackend.CreateAsync(runtime);
var upload = backend.CreateBuffer(new P.GpuBufferDescription(
    4, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
var readback = backend.CreateBuffer(new P.GpuBufferDescription(
    4, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination));

using (var mapped = await backend.MapBufferAsync(upload, P.GpuMapMode.Write, 0, 4))
{
    BinaryPrimitives.WriteUInt32LittleEndian(mapped.Memory.Span, 42);
}

var queue = backend.MainQueue;
using var completed = queue.CreateSemaphore();
using var commands = queue.StartCommandRecording();
commands.CopyBuffer(new(upload), new(readback));
queue.Submit([commands], completed, 1);
await queue.WaitAsync(completed, 1);

using (var mapped = await backend.MapBufferAsync(readback, P.GpuMapMode.Read, 0, 4))
{
    Console.WriteLine(BinaryPrimitives.ReadUInt32LittleEndian(mapped.ReadOnlyMemory.Span)); // 42
}
backend.DestroyBuffer(readback);
backend.DestroyBuffer(upload);
```

caller は resource、binding、module と pipeline を、それらを参照する記録と GPU 利用が終わるまで保持する。`commands.Dispose()` は提出済み work を取り消さず、内部 command と attachment view は GPU 利用終了後に解放する。待機の cancellation は resource の回収許可ではない。失敗時の所有と診断は [Portable の提出・完了契約](../../../docs/adr/0026-command-submission-and-synchronization.md) に従う。

## Mapping と JavaScript の数値

browser の `GPUBuffer.getMappedRange()` は JavaScript の `ArrayBuffer` を返す。backend は map ごとに一度だけこれを取得し、内容を managed byte 配列へコピーして `Memory<byte>`／`ReadOnlyMemory<byte>` に接続する。Write mapping の `Dispose` は編集後の内容を元の ArrayBuffer に書き戻してから unmap する。Read mapping は書き戻さない。これにより、一部だけ編集した Write mapping でも未編集の byte を保持する。

mapping の length は `int.MaxValue` 以下とする。保存済み Memory からの Span 取得や Pin も、lease の Dispose 後は拒否する。既に取得した Span／pointer は失効させられないため、caller は unmap 前に全アクセスを終える。二回目の map を browser が拒否した場合、その失敗処理は既存の成功した mapping を unmap しない。

JavaScript の整数引数に渡す Buffer size、offset、binding size 等の `ulong` は、正確に表せる `0` から `2^53 - 1` の範囲に限定する。限度を超える値は丸めず拒否する。これは bridge の表現条件であり、device limit、usage、alignment、format の検証は browser に委ねる。省略した値は JavaScript descriptor でも省略する。native C API の未指定 sentinel を JavaScript の明示値に流用しない。

## Shader と直接 root

`CreateShaderModule(string wgsl)` には完成した WGSL を渡し、shader と pipeline の使い方は [Portable の compute／raster の例](../Lumyte.Graphics.Portable/README.md#compute-と完了) と同じである。pipeline は実際の draw／dispatch を含む最初の Submit で生成し、論理 handle を再利用する。

`SetRootData`／`SetComputeRootData` は記録時に byte 列をコピーする。Submit の JavaScript bridge はその byte 列を `setImmediates` に渡し、shader の `var<immediate>` が直接読む。GPU buffer への退避、root の解析、Parameter Data の生成・自動 upload は行わない。mapping で行う CPU と ArrayBuffer 間のコピーは、この shader 入力経路とは別の resource 操作である。

Chrome for Testing Dev 155.0.8048.0 で、間接 dispatch を含む実 Browser の適合試験 20 件と timeline 試験 21 件の成功を確認した。Edge 153.0.4234.32 では、間接 dispatch の内部検証が root の値 37 を上書きし、出力が 65535／65536 になる不具合を確認している。Dawn の [修正となる revert](https://dawn.googlesource.com/dawn/+/c4e47b5eddc06f271cb07c3108cfccb1bb4704ec) を含む runtime を使用する。backend は browser 名／version による判定や buffer fallback を行わず、直接入力を使う適合試験で実行環境を確認する。

buffer／texture／sampler は explicit binding で shader に渡す。内部 view／sampler は同じ値を使う生存中の binding 間で再利用し、最後の参照が終わると解放する。binding の破棄は元の application resource を破棄しない。

## 配布 asset と host 設定

[wwwroot/lumyte-webgpu.js](wwwroot/lumyte-webgpu.js) は build output の `wwwroot/` と NuGet package の `contentFiles/any/any/wwwroot/` に含める。host がこの asset を公開する位置を選び、その URL を LoadAsync に渡す。assembly と JavaScript asset は同じ版を配布する。

現段階の browser host は .NET WebAssembly の interpreter、trimming 無効、reflection による JSON serialization 有効を使う。対応する host 設定は次のとおりである。

```xml
<PropertyGroup>
  <PublishTrimmed>false</PublishTrimmed>
  <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
  <RunAOTCompilation>false</RunAOTCompilation>
</PropertyGroup>
```

実際の asset 配置と host の構成は [conformance host](../Lumyte.Graphics.WebGPU.Browser.Tests/Integration/BrowserHost/BrowserHost.csproj) を参照する。AOT、trimming と JSON metadata の生成対応は後続作業とする。shader package／loader、RenderGraph provider、canvas presentation もこの段階には含めない。

動作確認は隣接する [Browser conformance](../Lumyte.Graphics.WebGPU.Browser.Tests/Integration/README.md) で、C# backend を実際の browser WebAssembly 上で実行する。対応環境と実行結果は [進捗記録](../../../docs/designs/graphics-implementation-progress.md) に記録する。
