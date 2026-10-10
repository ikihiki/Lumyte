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

Vulkan 1.3と `maintenance4`・`dynamicRendering`・`synchronization2` を必須とし、device生成時に有効化します。`maxBufferSize` を実際の property から取得するためで、古いドライバーで推測値を返しません。adapter がない、必要なVulkan 1.3機能に対応しない、必要な queue がない場合は `NotSupportedException`、存在しない index は `ArgumentOutOfRangeException`、Vulkan の失敗 result は `InvalidOperationException` になります。native loader のロード失敗は binding の例外に従います。失敗時は生成済みの device／instance と binding を解放します。

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

CPU access は `MemoryPreference.Upload` の MapAsync → CopyFrom → Unmap、`Readback` の MapAsync → CopyTo → Unmap で明示します。Automatic は map できません。CopyFrom／CopyTo では map、待機、GPU copy、submit を行いません。GPU copyと同期はcommand bufferへ明示的に記録します。

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

## Sampler

CreateSamplerは具象backendのIGraphicsSamplerを返し、Descを変更せずにsampling stateを確保します。enum、有限で非負のLOD、min <= max、正のanisotropyとlinear filter条件をbackend内のSamplerValidationで検証します。caps超過はNotSupportedExceptionで、暗黙補正しません。

Silk.NET.VulkanのvkCreateSampler／vkDestroySamplerを使用し、normalized coordinates、LOD bias 0のVkSamplerを確保します。SamplerAnisotropyはdeviceで有効化した機能だけを利用し、上限はphysical MaxSamplerAnisotropyの整数部分と16の小さい方、非対応時は1です。anisotropy > 1の場合だけAnisotropyEnableを有効にし、Compare未指定ならCompareEnable=falseです。生成失敗はResultに応じた例外で返し、live child数を増やしません。

samplerはtexture／Viewを所有せず、deviceのlive childとして数えます。Disposeは一度だけnative資源を解放し、samplerが残るdeviceのDisposeは拒否します。CPU／GPUの利用・解放に必要な同期は利用者の責務です。内部lock、InternalsVisibleTo、sampler cacheは設けません。

## Argument Table

CreateArgumentTableはbackendのIArgumentTableを返し、texture view・sampler・bufferを種類別のDictionaryへ疎に登録します。capacityは論理slotの上限で、巨大な事前確保やshaderのbinding数を意味しません。別tableの同じslotと、種類ごとの同じslotは独立します。Labelは診断用の指定です。

各登録instanceがidentityを持ち、`GpuReference<T>` は同じ登録の型・Count・byte offset／sizeを保持します。GetElementは登録rangeに対するoffsetをchecked計算し、内部Resolveは登録が生存することを確認してresourceを返します。公開APIへnative handleや整数IDを出しません。

登録resourceは同じdeviceの具象型に限定します。textureはSampled view、bufferはShaderRead／ShaderWrite用途を検証します。登録中resourceの解放は拒否します。置換とReleaseは古い登録を失効させ、失敗した登録で新しいleaseを残しません。tableを解放すると登録を解放してdeviceのchild数を減らし、登録resource自体はDisposeしません。同期は利用者の責務で、lock・アトミックカウンター・InternalsVisibleToを使いません。

登録はVkDescriptorSetを確保・更新せず、backendのresource instanceと選択byte rangeを保持します。descriptor set／descriptor indexingやbuffer device addressへの接続はshader／commandの設計で扱います。現在のdeviceでbuffer device addressを有効化したことを意味せず、有限bindingへの変換方式を共通APIから強制しません。

## シェーダーモジュール

共通 API は `IGraphicDevice.CreateShader(ShaderArtifact)` と `IGraphicsShader` です。`Caps.ShaderTarget` は `SpirV`。Slang の SPIR-V を Silk.NET.Vulkan の `vkCreateShaderModule` に渡し、失敗した Result を例外にします。`Dispose()` は `vkDestroyShaderModule` を呼びます。entry／stage と pipeline の互換性は pipeline 作成時に検証します。

artifactのopaque binaryからbackendのtarget codeとmetadataを取得します。同じoffline binaryを全backendへ渡せます。必要targetを含まないonline binaryは`NotSupportedException`で拒否します。shader はデバイスの子 resource として数え、残っている間の device Dispose を拒否します。shader 解放で GPU 完了待機や暗黙の同期は行いません。artifact は GPU module を所有せず、reflection も native API に直接渡しません。pipelineとshader実行命令は別のAPIで扱います。

## CommandBuffer

Vulkan 1.3のdynamic renderingとsynchronization2を必要機能として実装します。古いrender passやlegacy barrierへのfallbackは設けません。one-shot command bufferごとにtransient command poolとprimary VkCommandBufferを確保し、BeginCommandBufferからEndCommandBufferまで記録します。render passはCmdBeginRendering／CmdEndRenderingを使用し、compute scopeは共通APIの記録範囲として検証します。

barrierはMemoryBarrier2／BufferMemoryBarrier2／ImageMemoryBarrier2をCmdPipelineBarrier2へ渡します。stage・access、image layout、選択subresourceを明示的に変換し、queue familyは同一queueなのでIgnoredです。CopyBuffer・CopyImage・CopyBufferToImage・CopyImageToBufferを使用します。非圧縮colorのtexel sizeは4 byteで、GetTextureCopyLayoutはoffset alignmentとrow alignmentに4を返します。Capsのrow alignment 1に加え、formatのtexel block制約を反映した値です。nativeのBufferRowLengthは指定row byte数を4で割った値、BufferImageHeightはRowsPerImageです。

Submitは専用VkFenceを確保してQueueSubmitし、Status／WaitAsyncはそのfenceを非blockingに確認します。GPU全体のidle待機、CPUデータのreadback、resourceの自動解放を挿入しません。提出失敗時のout-of-memoryは未提出として扱い、device lossはFaultedにして再提出を拒否します。fenceとcommand poolはGPU使用終了後に明示的にDisposeします。

共通API、one-shot状態遷移、pass順序、明示的なupload／readbackの手順は [GRAPHICS-0007](../../../docs/adr/graphics/GRAPHICS-0007-command-buffers-and-submission.md) を参照してください。commandとsubmissionもdeviceの子として数えます。記録したresourceの解放、map／unmapとGPUアクセスの同期は利用者の責任です。内部lock・アトミックカウンターや暗黙の完了待機はありません。

## Pipeline

graphics programはshaderの組・topology分類・compile optionを保持し、draw時に描画状態とpassの実attachment formatからnative pipelineを解決します。完全なnative PSOを生成するため、program内でvariantをcacheし、等価なkeyの再利用で生成を繰り返しません。keyにviewport／scissor／blend constant／stencil reference、texture instance、clear値は含めません。variantはprogram Disposeまで保持します。

compute programは作成時にnative pipelineを生成します。shader moduleをprogramから保持し、保持中のshader解放を拒否します。commandは使用programを保持してsubmit時に生存を再確認しますが、GPU完了前の利用者による解放を自動的に同期しません。並列呼び出しの保証、lock、atomic counter、Slang sourceの再compileは追加しません。

reflectionでstage・location・型、vertex pulling入力、workgroup各軸と積を検証します。root-dataとresource helperのABIをartifactから検証し、通常のresource globalを直接受け取るprogramは拒否します。single-sample color/depth/stencil、direct/indexed/indirect draw、direct/indirect compute dispatchを提供します。MSAA・resolve・multi-draw・count bufferは別の拡張です。

Vulkanはdynamic rendering対応のGraphicsPipelineCreateInfoとroot uniformとnative descriptor arrayのPipelineLayoutを使用します。attachment formatはPipelineRenderingCreateInfo、topology・blend・coverageは完全なnative pipelineへ固定します。viewport・scissor・blend constants・stencil referenceはdynamic stateです。WebGPUと同じ画面座標へ揃えるためviewportのheightを負にし、front faceを対応させます。per-attachment blendに必要なIndependentBlendもdevice生成時に必須として有効化し、fallbackは設けません。

CreateGraphicsPipelines／CreateComputePipelinesが失敗したときは、返された部分的なnative objectを回収してcacheへ登録しません。sample mask 0も正確に渡します。optimization hintは保持しますが、partial graphics programをこの実装では使用せず、両hintともdriverの通常のpipeline生成へ渡します。

これらの完全なnative pipelineはcacheが必要な生成単位です。dynamic stateはcommandを記録するだけで、一時state objectを生成しません。そのため今回新たに比較対象となる軽量なstate objectはありません。partial programや独立した軽量objectを導入する際は、毎draw生成・状態変更時生成・cacheのCPU時間と保持memoryを同条件で比較し、結果をこのREADMEへ記録します。今回の実装で性能計測済みとは扱いません。

共通契約は [GRAPHICS-0008](../../../docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)、共通画素検証は [PipelineExercise](../../../samples/Lumyte.Graphics.Shared/PipelineExercise.cs) を参照してください。

### Graphics PSO cacheの無効化

`VulkanDevice.Create(cacheGraphicsPipelines: false)` で、状態が同じdrawでも毎回 `vkCreateGraphicsPipelines` を呼ぶ比較用経路を選べます。既定値は `true` です。これはライブラリのvariant再利用を無効にする設定で、driver内部のcacheを無効にするものではありません。`VkPipelineCache` はどちらの経路も渡していません。

生成したPSOは記録済みcommandやGPUが参照するため、draw直後には破棄せずprogramの `Dispose` まで保持します。利用者はGPU完了後にprogramを解放してください。無効時はdraw数に比例してnative PSOの保持数と生成費用が増えます。compute pipelineはprogram生成時の一個を使う契約を維持します。

共通APIの同じ画素検証をcache有効・無効の両方で既存CIへ流します。これは描画の正しさの比較で、実GPUの性能比較結果を示すものではありません。

### PSO生成費用の比較方法

`dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan pipeline-benchmark` で共通APIの同一workloadを比較します。各試行は独立したdeviceとprogramを生成し、8×8の単一attachmentに初回drawと同じ状態の200 drawを記録します。8 roundで有効・無効の順を交互に入れ替え、最初の2 roundをwarmupとして除外します。

測定項目は初回drawのCPU時間、200 draw記録のCPU時間、submitから完了までのwall-clock時間、GPU完了後のprogram破棄時間です。device／shader／resource作成と状態設定は測定対象に含めません。完了時間はGPU timestampではなくCPU schedulingを含みます。PSO cache無効時のnative保持数は201個、有効時は1個です。毎drawで状態を再設定する費用や異なるstate variantのworkloadはこの測定に含めません。

既存Linux x64 CIのlavapipeで測定し、raw JSONLをtest-results artifactに保存します。software VulkanのCPU費用を比較するための結果であり、hardware GPUや実シーンのFPSへ一般化しません。median・最小・最大を報告し、速度比は200 draw記録のmedianから計算します。

### CI測定結果

[2026-10-09の測定ログ](https://github.com/ikihiki/Lumyte/actions/runs/37933131170/job/113828542234)。Ubuntu 24.04 x64、Release、Mesa 25.2.8、llvmpipe LLVM 20.1.2。warmup 2回を除いた各6試行。値はmsの中央値（最小–最大）です。

| 測定項目 | cache有効 | cache無効 |
| --- | ---: | ---: |
| 初回draw記録 | 0.274 (0.253–0.336) | 0.295 (0.243–0.323) |
| 200 draw記録 | 0.221 (0.142–0.350) | 21.456 (18.133–28.571) |
| submitから完了 | 4.704 (4.608–5.848) | 65.970 (63.083–105.730) |
| program破棄 | 0.020 (0.017–0.069) | 0.078 (0.054–0.100) |
| 上記の合計（各試行の合計から算出） | 5.251 (5.069–6.471) | 90.184 (82.813–130.980) |

200 draw記録の中央値は無効時に約97倍、測定した区間の合計は約17.2倍でした。同じPSOの再利用が多いこのworkloadではcacheを維持します。submit完了時間の差にはsoftware driverの実行時処理やCPU schedulingも含まれるため、hardware GPUの描画時間差とは解釈しません。必要な拡張を用いたdynamic state化や部分PSO生成との比較は別のworkloadです。

## Shader dataと構造体引数

[ADR-GRAPHICS-0009](../../../docs/adr/graphics/GRAPHICS-0009-shader-argument-binding.md)に従い、AutomaticでGPU storage、Uploadでstagingを作ります。map済みstagingのCopyFromが値をpackし、利用者がcommandのCopyBufferとbarrierを明示します。shader dataへのraw byte aliasとShaderWriteは提供しません。

GPU storageはshader data作成時に確保します。stagingへ値を書き込む時に参照先の既存GPU buffer device addressを解決し、明示copyでGPUへ転送します。先にbufferを確保・登録できるため循環参照をpackできます。draw／dispatchは転送済みCPU metadataから依存を収集し、shader dataを再転送しません。

Upload stagingは明示mapでCPU書き込みを行い、Automatic storageへVkCmdCopyBufferで転送します。shader dataのbarrierは同じallocationのbyte rangeへVkBufferMemoryBarrier2を記録します。root値だけはcommand所有のhost-visible coherent backingで渡します。stagingとGPU storageの有効期間・copy前後の依存は利用者が管理します。

SPIR-Vだけを含むonline artifactも、このbackendの型schemaとhelper ABIが揃っていれば使用できます。WGSL targetがないことだけを理由に拒否しません。

## 構造体引数とnative参照

Vulkan 1.2のBufferDeviceAddress、RuntimeDescriptorArray、DescriptorBindingPartiallyBound、ShaderSampledImageArrayNonUniformIndexingとShaderInt64を有効化します。shader用途のraw bufferはShaderDeviceAddress usageとDeviceAddress allocation flagを持ちます。buffer helperは64-bit addressを直接参照し、portableな有限binding用のremapやswitchを使いません。

textureとsamplerは種類別の論理slotをnative descriptor array indexとして使用します。到達slotの最大値に対応したarray layoutとroot uniformを使い、PSOをcacheします。物理arrayの上限を超えるslotは拒否します。drawごとにdescriptorを設定しますが、shader data bytesは確保済みGPU bufferのまま使います。commandはroot backingとdescriptor poolを保持し、submitで登録identityとstaging revisionを再検証します。

Slang側は`#include "lumyte.slang"`と、`GpuBufferRef<T>`／`GpuRWBufferRef<T>`／GpuTextureRef／GpuSamplerRefを使います。Load／StoreとLumyteSampleGradが対応helperです。最初のsampling helperはfilterableな2D float textureと非comparison samplerを扱い、用途が異なる参照はschema照合で拒否します。source generatorの導入は[Generators](../Lumyte.Graphics.Generators/README.md)を参照してください。compile時の型schema・buffer参照先型・helper ABI versionはopaque artifactに格納します。

shader dataの配置は、offline／onlineとも`StructuredBuffer<Ptr<T>>`のpointee reflectionから取得します。これによりGPU addressの`T*`が使うnatural layoutとCPUのpackを一致させます。たとえば`uint`と`float3`を持つ構造体はoffset 0／4・stride 16、`float3`だけの構造体はstride 12です。root uniformの配置は、そのconstant bufferのreflectionから別に取得します。

共有の型配置・参照追跡処理は [Lumyte.Graphics.Shared](../Lumyte.Graphics.Shared/README.md) ライブラリを参照します。バックエンド実装用の契約は Graphics.Abstractions にあり、ソースのリンクコンパイルや InternalsVisibleTo は使用しません。

## Depth／Stencil、Indexed／Indirect実行

`RenderPassDesc.DepthStencilAttachment`へ単一mip・単一layerのD2 viewを指定します。colorは省略でき、depth-only passではfragment shaderのないprogramを使えます。depth/stencilのload/storeは独立で、利用者が`DepthStencilAttachment`へのbarrierを記録します。Depth32Floatはdepthのみ、Depth24Stencil8はdepthと8bit stencilです。今回のdepth textureはRenderAttachment用途に限定し、sampling/copyは生成時に拒否します。

`SetIndexBuffer`はushort／uintのsliceを受け取り、`DrawIndexed`のfirstIndexはsliceからの相対offsetです。stripは`StripIndexFormat`を一致させ、最大index値でprimitive restartします。indexの値はCPUで走査しません。

`DrawIndirect`／`DrawIndexedIndirect`／`DispatchIndirect`はそれぞれ16／20／12byteの1要素sliceをGPU命令として実行します。Indirect usage、4byte alignment、device、寿命、非mappedを検証します。命令内容はCPUで読み出さず、portable drawのFirstInstance=0、index範囲、workgroup limitsは利用者が守ります。GPU生成後の`ShaderWrite`から`IndirectRead`へのbarrier、転送とsubmitも利用者が明示します。命令bufferの参照とshader root引数は独立で、引数からの資源収集はdirect実行と同じです。

`VkImage`ではD32_SFLOAT／D24_UNORM_S8_UINTとDepthStencilAttachment usageを使い、image/view/barrierのaspectにdepthと必要なstencilを設定します。D24_UNORM_S8_UINTの対応はimage format propertiesで検証し、不対応なら生成を拒否します。dynamic renderingのdepth/stencil attachment、pipelineのdepth/stencil state・format、CmdBindIndexBuffer／CmdDrawIndexed／CmdDrawIndirect／CmdDrawIndexedIndirect／CmdDispatchIndirectを使用します。depth formatはキャッシュキーへ含め、キャッシュ無効時も同じ状態から生成します。IndexInput、DrawIndirect、DepthStencilをvertex input、draw indirect、early/late fragment testsへ変換し、対応するaccessをsynchronization2 barrierへ渡します。

Slangの`SV_VertexID`はSPIR-VでVertexIndexからBaseVertexを引きます。vertex pullingでbaseVertex適用後のindexを使う場合は`SV_VulkanVertexID`を指定します。firstInstance適用後は`SV_VulkanInstanceID`です。これらのSlang semanticはWGSL targetでも対応します。SV_VertexID等が要求するDrawParameters capabilityのため、deviceではVulkan 1.1のshaderDrawParametersを検証・有効化します。

## 追加カラー形式

`R8Unorm`, `Rg8Unorm`, `R16Float`, `Rg16Float`, `Rgba16Float`, `Rgb10A2Unorm`をサンプリング・カラーattachment・コピーへ対応付けます。コピーは形式ごとのtexelサイズを使い、オフセットは4 byteとtexelサイズの両方に整列させます。

ネイティブの形式とusage対応を生成時に問い合わせ、非対応は拒否します。

## Surface・Swapchain・Present

`VulkanDevice.CreateForPresentation(VulkanSurfaceSource source, out IGraphicsSurface surface, ...)`へ、platformのinstance extension名と`Func<Instance, SurfaceKHR>`を渡します。
callbackは渡されたinstanceに属する新しいnative surfaceを作成し、返却時にその所有権をGraphicsへ移します。実ウインドウのハンドル取得やplatform別のsurface生成は別PRの連携側が担当します。
追加targetは`device.CreateSurface(createSurface)`へ渡します。同じinstanceで有効化済みのplatform extensionと、既存general queueによるpresent対応が必要です。
`Create()`のheadless経路にはWSI拡張を要求しません。

提示経路ではVK_KHR_surface、VK_KHR_get_surface_capabilities2、VK_EXT_surface_maintenance1、VK_KHR_swapchain、VK_EXT_swapchain_maintenance1とswapchainMaintenance1 featureを要求します。
同じgraphics／compute／present queue familyを選び、別queue familyへのownership transferは行いません。
形式はsurfaceが返すnonlinear-sRGB color spaceの組み合わせを公開します。HDR color spaceの選択は別の契約です。
画像数はnative limits内でバックエンドが選び、複数frameの取得を共通の単一frame制約で制限しません。

取得はtimeout 0のAcquireNextImageです。返却画像の準備をCPUで待たず、acquire semaphoreをframeを指定したQueue.Submitのwaitへ関連付けます。
同じsubmitでrender-finished semaphoreをsignalし、QueuePresentKHRがwaitします。利用側はPresent layoutへのbarrierを明示します。
acquire fenceとswapchain maintenance1のpresent fenceを使って、image leaseとbinary semaphoreの安全な解放を非ブロッキングで照会します。
`WaitForReleaseAsync`だけが明示的にその完了を待ち、DeviceWaitIdle／QueueWaitIdleや自動submitは行いません。
未提示の画像はGPU使用完了後にReleaseSwapchainImagesEXTで返却します。
古いswapchainのleaseが残る再構成を拒否し、作成失敗でoldSwapchainがretireされた場合は取得結果をOutdatedとして再構成を要求します。

Native WSIはビルドとハンドル受け取り口の検証を行い、実ウインドウの提示検証はハンドル取得を実装する後続PRで行います。
