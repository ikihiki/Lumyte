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

CPU access は `MemoryPreference.Upload` の MapAsync → CopyFrom → Unmap、`Readback` の MapAsync → CopyTo → Unmap で明示します。Automatic は map できません。CopyFrom／CopyTo では map、待機、GPU copy、submit を行いません。GPU copyと同期はcommand bufferへ明示的に記録します。

CPU-mapped buffer は managed byte span で扱える int.MaxValue byte までに制限します。mapping pending 中の再 map・Unmap・Dispose は拒否します。API内部では並列操作を同期しません。必要な同期は利用者が管理し、bufferが残ったdeviceのDisposeはInvalidOperationExceptionで拒否します。解放済み allocation への access は ObjectDisposedException です。

WebGPU の usage は CopySource → COPY_SRC、CopyDestination → COPY_DST、ShaderRead／ShaderWrite → STORAGE、Index → INDEX に対応します。Upload は CopySource のみと MAP_WRITE、Readback は CopyDestination のみと MAP_READ の組み合わせです。CPU-mapped buffer の size は 4 byte の倍数でなければ生成時に拒否します。GPU-only buffer の論理 size はこの理由で丸めません。

Ahjo の BeginMap と device の ProcessEvents を使い、native callback の完了後に mapping 状態を公開します。キャンセルされても callback の保存先を途中で解放せず、request が完了してから Unmap と cleanup を行います。CPU copy は mapped native span に対して行います。

## TextureとView

生成済みの `IGraphicDevice.CreateTexture(TextureDesc)` から2D imageを確保します。RGBA8／BGRA8のUnorm／sRGB、単一sample、mip／array layer、CopySource／CopyDestination／Sampled／RenderAttachmentを扱います。属性は変更・丸め・暗黙変換しません。capsのMaxTextureDimension2DとMaxTextureArrayLayersを照合し、mip数とusageを検証します。

CreateViewはD2・D2Array・Cube・CubeArrayのsubresourceを選択し、Infoで解決済みのcountを返します。Viewが生きているTextureのDispose、bufferまたはtextureが残るDeviceのDisposeは拒否します。Viewはsourceを保持し、利用者による同期を前提にlive view数を管理します。Viewから先に解放してください。textureへ自動upload／readbackやGPU待機は追加していません。

Ahjo.WgpuのCreateTexture／CreateViewを直接使用し、native textureとviewを具象resourceが所有します。dimension、format、usage、mip／layer範囲をWebGPU descriptorに明示的に変換します。Disposeではview／textureの参照をReleaseします。native handleを共通APIへ公開しません。

DescとView範囲の検証は、このbackend assembly内のinternalなTextureValidationで行います。Abstractionsの内部型へのアクセスやInternalsVisibleToは使いません。

resource APIは並列実行の安全性を保証しません。内部lockやアトミックな所有カウンターは設けず、backendの実行制約と、生成・CPUコピー・map／unmap・解放の競合に必要な同期を利用者が管理します。状態検証はデータ競合を防止する機構ではありません。

## Sampler

CreateSamplerは具象backendのIGraphicsSamplerを返し、Descを変更せずにsampling stateを確保します。enum、有限で非負のLOD、min <= max、正のanisotropyとlinear filter条件をbackend内のSamplerValidationで検証します。caps超過はNotSupportedExceptionで、暗黙補正しません。

Ahjo.Wgpu.NativeのwgpuDeviceCreateSamplerとwgpuSamplerReleaseを直接使用します。Ahjo.WgpuのSamplerDescriptor経由ではLodMaxClamp=0が32へ置換されるため、0固定を保つ目的で同じ.NET bindingのraw descriptorを使用します。新しい.Native projectは追加しません。filters／address／comparisonはnative enumへ明示変換し、anisotropy上限はWebGPUの16です。

samplerはtexture／Viewを所有せず、deviceのlive childとして数えます。Disposeは一度だけnative資源を解放し、samplerが残るdeviceのDisposeは拒否します。CPU／GPUの利用・解放に必要な同期は利用者の責務です。内部lock、InternalsVisibleTo、sampler cacheは設けません。

## Argument Table

CreateArgumentTableはbackendのIArgumentTableを返し、texture view・sampler・bufferを種類別のDictionaryへ疎に登録します。capacityは論理slotの上限で、巨大な事前確保やshaderのbinding数を意味しません。別tableの同じslotと、種類ごとの同じslotは独立します。Labelは診断用の指定です。

各登録instanceがidentityを持ち、`GpuReference<T>` は同じ登録の型・Count・byte offset／sizeを保持します。GetElementは登録rangeに対するoffsetをchecked計算し、内部Resolveは登録が生存することを確認してresourceを返します。公開APIへnative handleや整数IDを出しません。

登録resourceは同じdeviceの具象型に限定します。textureはSampled view、bufferはShaderRead／ShaderWrite用途を検証します。登録中resourceの解放は拒否します。置換とReleaseは古い登録を失効させ、失敗した登録で新しいleaseを残しません。tableを解放すると登録を解放してdeviceのchild数を減らし、登録resource自体はDisposeしません。同期は利用者の責務で、lock・アトミックカウンター・InternalsVisibleToを使いません。

登録はnative bind groupを生成せず、backendのresource instanceと選択byte rangeを保持します。生成codecとshader binding planがこのidentityと要素metadataを使い、rootから必要なtexture・sampler・bufferを収集して種類別に上限を照合します。物理indexと安定した登録identityを区別し、有限bind groupへの変換をshader／commandへ接続します。

## シェーダーモジュール

共通 API は `IGraphicDevice.CreateShader(ShaderArtifact)` と `IGraphicsShader` です。`Caps.ShaderTarget` は `Wgsl`。Ahjo.Wgpu の `Device.CreateShaderModule` と `ShaderSource.FromWgsl` を直接使用します。WGSL の検証や非同期 device error の扱いは wgpu に従います。shader の `Dispose()` は native module を release します。

artifactのopaque binaryからbackendのtarget codeとmetadataを取得します。同じoffline binaryを全backendへ渡せます。必要targetを含まないonline binaryは`NotSupportedException`で拒否します。shader はデバイスの子 resource として数え、残っている間の device Dispose を拒否します。shader 解放で GPU 完了待機や暗黙の同期は行いません。artifact は GPU module を所有せず、reflection も native API に直接渡しません。pipelineとshader実行命令は別のAPIで扱います。

## CommandBuffer

Ahjo.Wgpuのnative bindingを直接使用してWGPUCommandEncoder／WGPUCommandBufferを保持します。buffer／textureコピーとcolor render・compute passを記録し、Finishでnative recordingを確定します。独自のNativeプロジェクトは追加しません。barrierのstage・access・usageとtextureの論理stateは検証し、物理的なvisibilityとtexture transitionはWebGPUの順序保証で実現します。

GetTextureCopyLayoutはcolor texel size 4、buffer offset alignment 4、row alignment 256を返します。padding・行の詰め替え・staging確保は行いません。SubmitはQueueSubmitし、BeginOnSubmittedWorkDoneのrequestを保持します。Status／WaitAsyncでProcessEventsを進め、そのrequestの完了を確認します。command bufferと完了requestは別にDisposeし、pending中の解放を拒否します。

共通API、one-shot状態遷移、pass順序、明示的なupload／readbackの手順は [GRAPHICS-0007](../../../docs/adr/graphics/GRAPHICS-0007-command-buffers-and-submission.md) を参照してください。commandとsubmissionもdeviceの子として数えます。記録したresourceの解放、map／unmapとGPUアクセスの同期は利用者の責任です。内部lock・アトミックカウンターや暗黙の完了待機はありません。

## Pipeline

graphics programはshaderの組・topology分類・compile optionを保持し、draw時に描画状態とpassの実attachment formatからnative pipelineを解決します。完全なnative PSOを生成するため、program内でvariantをcacheし、等価なkeyの再利用で生成を繰り返しません。keyにviewport／scissor／blend constant／stencil reference、texture instance、clear値は含めません。variantはprogram Disposeまで保持します。

compute programは作成時にnative pipelineを生成します。shader moduleをprogramから保持し、保持中のshader解放を拒否します。commandは使用programを保持してsubmit時に生存を再確認しますが、GPU完了前の利用者による解放を自動的に同期しません。並列呼び出しの保証、lock、atomic counter、Slang sourceの再compileは追加しません。

reflectionでstage・location・型、vertex pulling入力、workgroup各軸と積を検証します。root-dataとresource helperのABIをartifactから検証し、通常のresource globalを直接受け取るprogramは拒否します。single-sample color、direct draw、compute dispatchを提供し、depth/stencil・MSAA・indexed／indirectは対応契約の拡張で追加します。

Ahjo.Wgpuのnative bindingでWGPURenderPipeline／WGPUComputePipelineを生成します。描画状態は完全なrender pipelineのkeyに入り、dynamic値はrender encoderへ直接設定します。write maskとsample maskはnative descriptorへ正確に渡すため、0をdefaultへ置き換えるwrapperは使いません。root引数のあるprogramは種類別resource集合とroot／remap用bindingから明示layoutを生成します。引数のないprogramはauto layoutを使用します。DepthClipControl等の追加featureは有効化せず、必要なstateは拒否します。

これらの完全なnative pipelineはcacheが必要な生成単位です。dynamic stateはcommandを記録するだけで、一時state objectを生成しません。そのため今回新たに比較対象となる軽量なstate objectはありません。partial programや独立した軽量objectを導入する際は、毎draw生成・状態変更時生成・cacheのCPU時間と保持memoryを同条件で比較し、結果をこのREADMEへ記録します。今回の実装で性能計測済みとは扱いません。

共通契約は [GRAPHICS-0008](../../../docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)、共通画素検証は [PipelineExercise](../../../samples/Lumyte.Graphics.Shared/PipelineExercise.cs) を参照してください。

## Shader argumentsのbinding容量調整（ADR-0009の設計）

[ADR-GRAPHICS-0009](../../../docs/adr/graphics/GRAPHICS-0009-shader-argument-binding.md)のshader argumentsでは、artifactから取り出したWGSLをbackendが直接調整してからnative shader moduleを生成します。hardwareのlimitsを変更する機能ではなく、有効limitsに収まるresource宣言数とbinding配置を生成する機構です。

compilerはWGSLとともに、変更対象のresource宣言・参照選択helper・binding位置を識別するschemaとABI versionをartifactへ保存します。backendはcompilerが管理する領域を構造的に編集し、resource宣言、binding番号、switch等の参照解決コードを一緒に更新します。定数だけを変えて既存のresource宣言を残す置換や、任意のWGSLへ無条件の正規表現置換を行う方式は使いません。

draw／dispatchで必要な集合を収集し、textureの次元・sample type、sampler種別、buffer用途・accessなどの互換classごとに必要数を求めます。最初は必要数ぴったりの宣言を生成し、0個のclassは宣言を除去します。root uniformと参照変換表の予約枠を含め、全stageのvisibility、種類別limits、group数とgroup内binding数へ照合します。収まらない場合はdraw／dispatchを拒否し、物理上限を超える宣言は生成しません。

resourceを使うprogramのnative moduleとgraphics／compute pipeline variantは、容量が確定するdraw／dispatch時に生成できます。artifact hash、entryとstage集合、helper ABI、容量vector、実binding planをdevice内のcache keyに含めます。同じ容量でも論理型が異なれば別variantです。生成後のWGSLと実binding planを同じ変換結果から作り、artifact内の変換前binding番号を流用しません。

容量調整は数値structのlayoutと安定した参照identityを変えません。sampler共有とtexture数を独立に扱います。textureのsample helperはfragmentのderivative-uniformityを満たすように生成し、非一様な選択では明示gradient等を用いる対応可能な経路かどうか検証します。生成WGSLはDLL内artifactとruntime memoryで扱い、ソース管理へ追加しません。

shader dataのCopyFromはCPU値だけを設定します。draw／dispatchは参照先の推移的なversionをsnapshotし、そのsnapshot用にGPU bufferとbindingを用意します。必要なuploadは実行前の転送処理としてsubmissionへ関連付け、render／compute pass内へcopyを挿入しません。記録済みcommandが使用するbufferを別versionで上書きせず、GPU完了まで保持します。

WGSLだけを含むonline artifactも、このbackendの型schemaとhelper ABIが揃っていればshader data buffer生成へ使用できます。SPIR-V targetがないことだけを理由に拒否しません。全target生成の条件はoffline compilerの成果物に適用します。

## 構造体引数とWGSL binding生成

SetArgumentsは生成codecでCPU値をsnapshotし、programのroot schemaへ照合します。shader dataのCopyFromはCPU値だけを更新します。draw／dispatchで選択要素から推移的にIGpuRefを収集し、要素の既訪問集合で循環を止めます。再帰callは使わず、直接resourceだけの場合は追加の探索stackを確保しません。

artifactのWGSLを直接編集し、texture・sampler・read-only buffer・writable bufferを独立して重複排除した数だけ宣言します。texture samplingとbuffer wordアクセスのABI-owned helperを生成し、論理slotを種類別の物理bindingへ変換するremapを渡します。root uniformとremap storageも含めて有効limitsを検証し、超過時は描画を分割せずNotSupportedExceptionにします。種類別resource数はprogram内のpipeline variant keyに含めます。

root値、remap、使用するshader data要素の固定backingはmappedAtCreationで初期化してunmapし、consumerの命令を記録する前に用意します。利用者のpass内へcopyを追加しません。raw bufferは元のresourceを参照し、その内容をsnapshotしません。commandは内部backingとbind groupをGPU完了後のDisposeまで保持します。submitでは登録identityを再検証します。

Slang側は`#include "lumyte.slang"`と、`GpuBufferRef<T>`／`GpuRWBufferRef<T>`／GpuTextureRef／GpuSamplerRefを使います。Load／StoreとLumyteSampleGradが対応helperです。最初のsampling helperはfilterableな2D float textureと非comparison samplerを扱い、用途が異なる参照はschema照合で拒否します。source generatorの導入は[Generators](../Lumyte.Graphics.Generators/README.md)を参照してください。compile時の型schema・buffer参照先型・helper ABI versionはopaque artifactに格納します。

共有の型配置・参照追跡処理は [Lumyte.Graphics.Shared](../Lumyte.Graphics.Shared/README.md) ライブラリを参照します。バックエンド実装用の契約は Graphics.Abstractions にあり、ソースのリンクコンパイルや InternalsVisibleTo は使用しません。
