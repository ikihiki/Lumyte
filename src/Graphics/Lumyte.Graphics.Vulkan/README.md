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

このプロジェクトはデバイス生成・解放、Caps 取得と型付き buffer を扱います。[共通契約](../Lumyte.Graphics.Abstractions/README.md) と [native サンプル](../../../samples/Lumyte.Graphics.DeviceCaps.Sample/README.md) を参照してください。

## Buffer

生成済みの `IGraphicDevice` から `CreateBuffer<T>(BufferDesc<T>)` と `GetBufferLayout<T>()` を使用します。Count と SizeInBytes は指定した raw storage のサイズを保ち、alignment のために補正しません。数値型・enum・unmanaged struct は sizeof(T) の stride で格納します。shader target の layout 互換性は別途検証が必要です。

CPU access は `MemoryPreference.Upload` の MapAsync → CopyFrom → Unmap、`Readback` の MapAsync → CopyTo → Unmap で明示します。Automatic は map できません。CopyFrom／CopyTo では map、待機、GPU copy、submit を行いません。GPU copy と同期の command API は別の設計で追加します。

CPU-mapped buffer は managed byte span で扱える int.MaxValue byte までに制限します。mapping pending 中の再 map・Unmap・Dispose は拒否します。API内部では並列操作を同期しません。必要な同期は利用者が管理し、bufferが残ったdeviceのDisposeはInvalidOperationExceptionで拒否します。解放済み allocation への access は ObjectDisposedException です。

buffer usage は TransferSrcBit、TransferDstBit、StorageBufferBit、IndexBufferBit に対応付けます。Automatic は DEVICE_LOCAL、Upload／Readback は HOST_VISIBLE | HOST_COHERENT の memory type を選びます。指定 usage の VkMemoryRequirements と条件を満たす type がなければ NotSupportedException になります。

VkBuffer の size は正確な論理 SizeInBytes とし、内部の VkDeviceMemory は VkMemoryRequirements.Size に従って確保します。MapMemory／UnmapMemory で CPU access を切り替え、CopyFrom／CopyTo は pointer 上の CPU span をコピーします。HOST_COHERENT を必須にしているため、CPU copy の中に flush／invalidate を挿入しません。Vulkan の MapMemory は GPU の完了待機を行わないので、利用者が同期を保証してください。

Buffer Device Address、GPU address の公開・登録、shader binding はこの buffer API に含めません。

## TextureとView

生成済みの `IGraphicDevice.CreateTexture(TextureDesc)` から2D imageを確保します。RGBA8／BGRA8のUnorm／sRGB、単一sample、mip／array layer、CopySource／CopyDestination／Sampled／RenderAttachmentを扱います。属性は変更・丸め・暗黙変換しません。capsのMaxTextureDimension2DとMaxTextureArrayLayersを照合し、mip数とusageを検証します。

CreateViewはD2・D2Array・Cube・CubeArrayのsubresourceを選択し、Infoで解決済みのcountを返します。Viewが生きているTextureのDispose、bufferまたはtextureが残るDeviceのDisposeは拒否します。Viewはsourceを保持し、利用者による同期を前提にlive view数を管理します。Viewから先に解放してください。textureへ自動upload／readbackやGPU待機は追加していません。

Silk.NET.Vulkanでoptimal tilingのVkImageとdevice-local VkDeviceMemoryを確保し、VkImageViewでcolor aspectの範囲を選びます。GetPhysicalDeviceImageFormatPropertiesでformat・usage・寸法・mip・layerとsample count 1を照合し、不対応はNotSupportedExceptionです。native allocation失敗時はimageとmemoryをcleanupします。初期layoutはUndefinedで、barrierを挿入しません。

squareかつ6 layer以上のimageにはCubeCompatibleを指定します。imageCubeArrayはphysical deviceで対応していればlogical deviceで有効化し、不対応deviceのCubeArray viewはNotSupportedExceptionです。view、image、memoryの順に解放し、GPU完了は利用者が保証します。

DescとView範囲の検証は、このbackend assembly内のinternalなTextureValidationで行います。Abstractionsの内部型へのアクセスやInternalsVisibleToは使いません。

resource APIは並列実行の安全性を保証しません。内部lockやアトミックな所有カウンターは設けず、backendの実行制約と、生成・CPUコピー・map／unmap・解放の競合に必要な同期を利用者が管理します。状態検証はデータ競合を防止する機構ではありません。
