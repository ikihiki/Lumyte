# Lumyte.Graphics.Vulkan

[Silk.NET.Vulkan](https://www.nuget.org/packages/Silk.NET.Vulkan/2.23.0) 2.23.0 を使用し、Vulkan instance と logical device を生成します。対象は .NET 10 です。OS に Vulkan loader と対応ドライバーが必要です。

## 生成と所有権

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;

using VulkanDevice owner = VulkanDevice.Create(physicalDeviceIndex: 0);
IGraphicDevice device = owner;
Console.WriteLine(device.Caps.MaxBufferSize);
```

`VulkanDevice.Create(uint physicalDeviceIndex = 0)` は同期 API です。Vulkan 1.3 instance を生成し、列挙した physical device から指定 index の一つを選びます。graphics と compute の両方を扱える queue family から一つの queue を持つ logical device を作ります。surface、validation layer、device extension は有効にしません。複数 adapter の自動 fallback は行いません。

Vulkan 1.3 と `maintenance4` を必須とします。`maxBufferSize` を実際の property から取得するためで、古いドライバーで推測値を返しません。adapter がない、Vulkan 1.3／maintenance4 に対応しない、必要な queue がない場合は `NotSupportedException`、存在しない index は `ArgumentOutOfRangeException`、Vulkan の失敗 result は `InvalidOperationException` になります。native loader のロード失敗は binding の例外に従います。失敗時は生成済みの device／instance と binding を解放します。

所有者の `Dispose()` は logical device、instance、binding を解放します。再度の呼び出しは何もしません。生成・解放の並行呼び出しはサポートしません。`Caps` は非所有の保存済み情報なので解放後も読み取れます。

## Caps の対応

physical device の properties と features を生成時に取得し、有効にした機能と実際の上限を保存します。

| 共通の値 | Vulkan の取得元 |
| --- | --- |
| MaxBufferSize | VkPhysicalDeviceVulkan13Properties.maxBufferSize |
| MaxStorageBufferBindingSize | maxStorageBufferRange |
| MaxTextureDimension2D | maxImageDimension2D |
| MaxColorAttachments | maxColorAttachments |
| MaxSampledTexturesPerStage | maxPerStageDescriptorSampledImages |
| MaxSamplersPerStage | maxPerStageDescriptorSamplers |
| MaxUniformBuffersPerStage | maxPerStageDescriptorUniformBuffers |
| MaxStorageBuffersPerStage | maxPerStageDescriptorStorageBuffers |
| MaxComputeInvocationsPerWorkgroup | maxComputeWorkGroupInvocations |
| StorageBufferOffsetAlignment | minStorageBufferOffsetAlignment |

`VkBufferCopy` の offset と size は byte 単位で、copy offset／size alignment は `1` を返します。image copy の row も一律の追加 byte alignment はないため `1` です。image の texel block、aspect、queue family による追加制約は別途検証が必要です。`optimalBufferCopyOffsetAlignment`／`optimalBufferCopyRowPitchAlignment` は性能上の推奨値なので、必須 alignment として返しません。

`IndirectDraw` は基本機能として返します。`SamplerAnisotropy`／`DepthBiasClamp` が対応していれば device で有効にし、それぞれ `AnisotropicFiltering`／`DepthBiasClamp` を返します。`MeshShader` は extension を有効にしないため返しません。

このプロジェクトのデバイス API は生成・解放と Caps 取得を扱います。[共通契約](../Lumyte.Graphics.Abstractions/README.md) と [native サンプル](../../../samples/Lumyte.Graphics.DeviceCaps.Sample/README.md) を参照してください。
