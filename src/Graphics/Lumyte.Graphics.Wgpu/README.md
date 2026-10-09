# Lumyte.Graphics.Wgpu

[Ahjo.Wgpu](https://www.nuget.org/packages/Ahjo.Wgpu/0.7.0) 0.7.0 を直接使用し、native wgpu のデバイスを生成します。独自の `.Native` プロジェクトは置きません。対象は .NET 10 です。

## 生成と所有権

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Wgpu;

using WgpuDevice owner = WgpuDevice.Create();
IGraphicDevice device = owner;
Console.WriteLine(device.Caps.MaxBufferSize);
```

`WgpuDevice.Create()` は同期 API です。instance、default adapter、default limits の logical device を順に生成します。surface は作りません。adapter の探索範囲と native library の配布・ロードは Ahjo.Wgpu に従います。使用可能なドライバーとパッケージの native runtime asset が必要です。

生成に失敗すると取得済みの device、adapter、instance を解放して例外を伝播します。成功後は buffer を先に解放してから所有者が `Dispose()` を呼び、device → adapter → instance の順で解放します。再度の `Dispose()` は何もしません。生成・解放の並行呼び出しはサポートしません。`Caps` は非所有の保存済み情報なので、解放後も読み取れます。

## Caps の対応

生成時に `Device.GetLimits()` を一度呼び、adapter が持つ最大値ではなく、logical device に設定された実効上限を保存します。追加の feature や高い limits は要求しません。

| 共通の値 | 取得元 |
| --- | --- |
| MaxBufferSize | maxBufferSize |
| MaxStorageBufferBindingSize | maxStorageBufferBindingSize |
| MaxTextureDimension2D | maxTextureDimension2D |
| MaxColorAttachments | maxColorAttachments |
| MaxSampledTexturesPerStage | maxSampledTexturesPerShaderStage |
| MaxSamplersPerStage | maxSamplersPerShaderStage |
| MaxUniformBuffersPerStage | maxUniformBuffersPerShaderStage |
| MaxStorageBuffersPerStage | maxStorageBuffersPerShaderStage |
| MaxComputeInvocationsPerWorkgroup | maxComputeInvocationsPerWorkgroup |
| StorageBufferOffsetAlignment | minStorageBufferOffsetAlignment |

GPU buffer copy の offset と length は WebGPU の規定によりそれぞれ 4 byte、encoded buffer-to-texture／texture-to-buffer copy の bytesPerRow は 256 byte alignment です。queue の write 操作や texel block ごとの制約とは区別します。

`IndirectDraw` と `AnisotropicFiltering` は WebGPU の基本機能として返します。`IndirectDraw` は firstInstance が非ゼロの optional feature を保証しません。`DepthBiasClamp` と `MeshShader` は返しません。

このプロジェクトはデバイス生成・解放、Caps 取得と型付き buffer を扱います。[共通契約](../Lumyte.Graphics.Abstractions/README.md) と [native サンプル](../../../samples/Lumyte.Graphics.DeviceCaps.Sample/README.md) を参照してください。

## Buffer

生成済みの `IGraphicDevice` から `CreateBuffer<T>(BufferDesc<T>)` と `GetBufferLayout<T>()` を使用します。Count と SizeInBytes は指定した raw storage のサイズを保ち、alignment のために補正しません。数値型・enum・unmanaged struct は sizeof(T) の stride で格納します。shader target の layout 互換性は別途検証が必要です。

CPU access は `MemoryPreference.Upload` の MapAsync → CopyFrom → Unmap、`Readback` の MapAsync → CopyTo → Unmap で明示します。Automatic は map できません。CopyFrom／CopyTo では map、待機、GPU copy、submit を行いません。GPU copy と同期の command API は別の設計で追加します。

CPU-mapped buffer は managed byte span で扱える int.MaxValue byte までに制限します。mapping pending 中の再 map・Unmap・Dispose は拒否します。buffer の操作は device ごとの gate で直列化し、buffer が残った device の Dispose は InvalidOperationException で拒否します。解放済み allocation への access は ObjectDisposedException です。

WebGPU の usage は CopySource → COPY_SRC、CopyDestination → COPY_DST、ShaderRead／ShaderWrite → STORAGE、Index → INDEX に対応します。Upload は CopySource のみと MAP_WRITE、Readback は CopyDestination のみと MAP_READ の組み合わせです。CPU-mapped buffer の size は 4 byte の倍数でなければ生成時に拒否します。GPU-only buffer の論理 size はこの理由で丸めません。

Ahjo の BeginMap と device の ProcessEvents を使い、native callback の完了後に mapping 状態を公開します。キャンセルされても callback の保存先を途中で解放せず、request が完了してから Unmap と cleanup を行います。CPU copy は mapped native span に対して行います。
