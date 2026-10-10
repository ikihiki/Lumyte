# Lumyte.Graphics.Browser

.NET 10 の WebAssembly と JavaScript 相互運用で、ブラウザーの WebGPU device を生成します。native graphics binding は使いません。WebGPU に対応するブラウザーと、HTTPS または localhost の secure context が必要です。

## 生成と所有権

パッケージの `wwwroot/lumyte-graphics.js` をホストが配信し、その URL を指定します。相対 URL はホストの `location.href` を基準に解決します。ビルド時は library の出力 `wwwroot/` にコピーされ、NuGet package では `contentFiles/any/net10.0/wwwroot/` に含まれます。ホスト側で実際の配信ディレクトリへ配置してください。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;

using BrowserDevice owner = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
IGraphicDevice device = owner;
Console.WriteLine(device.Caps.MaxBufferSize);
```

`BrowserDevice.CreateAsync(string moduleUrl, CancellationToken cancellationToken = default)` は module を import し、`navigator.gpu.requestAdapter()` と `adapter.requestDevice()` を実行します。default adapter と default device limits を使い、optional feature や高い limits は要求しません。canvas や surface は作りません。

WebGPU がない、adapter がない、device request が失敗した場合は JavaScript 相互運用から例外を伝播します。module の読み込み失敗も例外になります。ブラウザーの JavaScript 相互運用が利用できるスレッドで使用してください。

CancellationToken は module import と、生成済み device を保持するかどうかを制御します。WebGPU の adapter／device request 自体には abort API がないため、request 中のキャンセルは完了を待ち、生成した device を破棄してからキャンセル例外を返します。

所有者の `Dispose()` は `GPUDevice.destroy()` を呼んで JavaScript proxy を解放します。再度の呼び出しは何もしません。並行した解放はサポートしません。`Caps` は非所有の保存済み情報なので解放後も読み取れます。

## Caps の対応

生成した `GPUDevice.limits` を一度読み、[Wgpu の対応表](../Lumyte.Graphics.Wgpu/README.md#caps-の対応) と同じ WebGPU limits の意味で `DeviceCaps` に保存します。adapter の上限をそのまま返しません。System.Text.Json の source-generated context で読み込み、WebAssembly の trimming に対応します。

GPU buffer copy の offset／length alignment は 4 byte、encoded image copy の bytesPerRow alignment は 256 byte、storage binding の offset alignment は device の `minStorageBufferOffsetAlignment` です。format ごとの texel block 制約は別途必要です。

`IndirectDraw` と `AnisotropicFiltering` は WebGPU の基本機能として返します。非ゼロ firstInstance の optional feature は保証しません。`DepthBiasClamp` と `MeshShader` は返しません。

このプロジェクトはデバイス生成・解放、Caps 取得と型付き buffer を扱います。[共通契約](../Lumyte.Graphics.Abstractions/README.md) と [.NET WebAssembly サンプル](../../../samples/Lumyte.Graphics.Browser.Sample/README.md) を参照してください。

## Buffer

生成済みの `IGraphicDevice` から `CreateBuffer<T>(BufferDesc<T>)` と `GetBufferLayout<T>()` を使用します。Count と SizeInBytes は指定した raw storage のサイズを保ち、alignment のために補正しません。数値型・enum・unmanaged struct は sizeof(T) の stride で格納します。shader target の layout 互換性は別途検証が必要です。

CPU access は `MemoryPreference.Upload` の MapAsync → CopyFrom → Unmap、`Readback` の MapAsync → CopyTo → Unmap で明示します。Automatic は map できません。CopyFrom／CopyTo では map、待機、GPU copy、submit を行いません。GPU copyと同期はcommand bufferへ明示的に記録します。

CPU-mapped buffer は managed byte span で扱える int.MaxValue byte までに制限します。mapping pending 中の再 map・Unmap・Dispose は拒否します。API内部では並列操作を同期しません。必要な同期は利用者が管理し、bufferが残ったdeviceのDisposeはInvalidOperationExceptionで拒否します。解放済み allocation への access は ObjectDisposedException です。

WebGPU の usage は CopySource → COPY_SRC、CopyDestination → COPY_DST、ShaderRead／ShaderWrite → STORAGE、Index → INDEX に対応します。Upload は CopySource のみと MAP_WRITE、Readback は CopyDestination のみと MAP_READ の組み合わせです。CPU-mapped buffer の size は 4 byte の倍数でなければ生成時に拒否します。GPU-only buffer の論理 size はこの理由で丸めません。

GPUBuffer.mapAsync の Promise と getMappedRange を使います。.NET／JavaScript 間の CPU copy は MemoryView による span の同期受け渡しで行い、GPUDevice.queue.writeBuffer は呼びません。キャンセル時も native Promise の完了後に mapping を解除します。JavaScript 相互運用が利用できるスレッドで操作してください。

## TextureとView

生成済みの `IGraphicDevice.CreateTexture(TextureDesc)` から2D imageを確保します。RGBA8／BGRA8のUnorm／sRGB、単一sample、mip／array layer、CopySource／CopyDestination／Sampled／RenderAttachmentを扱います。属性は変更・丸め・暗黙変換しません。capsのMaxTextureDimension2DとMaxTextureArrayLayersを照合し、mip数とusageを検証します。

CreateViewはD2・D2Array・Cube・CubeArrayのsubresourceを選択し、Infoで解決済みのcountを返します。Viewが生きているTextureのDispose、bufferまたはtextureが残るDeviceのDisposeは拒否します。Viewはsourceを保持し、利用者による同期を前提にlive view数を管理します。Viewから先に解放してください。textureへ自動upload／readbackやGPU待機は追加していません。

JavaScript moduleはGPUDevice.createTextureとGPUTexture.createViewを呼び、具象resourceがJSObject proxyを保持します。viewの解放はproxyをDisposeし、textureの解放はGPUTexture.destroyとproxyのDisposeを行います。GPUTextureViewにdestroy APIはありません。WebGPUのvalidation／device lostはbrowserのエラー通知にも現れるため、CIのChrome検証はruntime／consoleエラーも監視します。

DescとView範囲の検証は、このbackend assembly内のinternalなTextureValidationで行います。Abstractionsの内部型へのアクセスやInternalsVisibleToは使いません。

resource APIは並列実行の安全性を保証しません。内部lockやアトミックな所有カウンターは設けず、backendの実行制約と、生成・CPUコピー・map／unmap・解放の競合に必要な同期を利用者が管理します。状態検証はデータ競合を防止する機構ではありません。
BrowserではJSObjectが属する実行contextの制約も利用者が守ります。

## Sampler

CreateSamplerは具象backendのIGraphicsSamplerを返し、Descを変更せずにsampling stateを確保します。enum、有限で非負のLOD、min <= max、正のanisotropyとlinear filter条件をbackend内のSamplerValidationで検証します。caps超過はNotSupportedExceptionで、暗黙補正しません。

source-generated JSONでDescをJavaScriptへ渡し、GPUDevice.createSamplerのdictionaryへfilter／address／comparison・LOD・anisotropyを変換します。比較未指定のcompareはdictionaryへ設定しません。GPUSamplerにはdestroyがないため、解放ではJSObject proxyをDisposeします。GPUエラーはWebGPUの通知にも現れます。JSObjectの実行context制約を利用者が守り、CIはruntime／consoleエラーも監視します。上限はWebGPUの16です。

samplerはtexture／Viewを所有せず、deviceのlive childとして数えます。Disposeは一度だけnative資源を解放し、samplerが残るdeviceのDisposeは拒否します。CPU／GPUの利用・解放に必要な同期は利用者の責務です。内部lock、InternalsVisibleTo、sampler cacheは設けません。

## Argument Table

CreateArgumentTableはbackendのIArgumentTableを返し、texture view・sampler・bufferを種類別のDictionaryへ疎に登録します。capacityは論理slotの上限で、巨大な事前確保やshaderのbinding数を意味しません。別tableの同じslotと、種類ごとの同じslotは独立します。Labelは診断用の指定です。

各登録instanceがidentityを持ち、`GpuReference<T>` は同じ登録の型・Count・byte offset／sizeを保持します。GetElementは登録rangeに対するoffsetをchecked計算し、内部Resolveは登録が生存することを確認してresourceを返します。公開APIへnative handleや整数IDを出しません。

登録resourceは同じdeviceの具象型に限定します。textureはSampled view、bufferはShaderRead／ShaderWrite用途を検証します。登録中resourceの解放は拒否します。置換とReleaseは古い登録を失効させ、失敗した登録で新しいleaseを残しません。tableを解放すると登録を解放してdeviceのchild数を減らし、登録resource自体はDisposeしません。同期は利用者の責務で、lock・アトミックカウンター・InternalsVisibleToを使いません。

登録はJavaScriptやGPUDeviceのbinding APIを呼ばず、C#のbackend resource instanceと選択byte rangeを保持します。shader binding planとroot参照から必要なGPUTextureView／GPUSampler／GPUBufferを解決し、有限bind groupへ構築する処理はshader／commandとの接続で実装します。JSObjectの実行context制約は引き続き利用者が守ります。

## シェーダーモジュール

共通 API は `IGraphicDevice.CreateShader(ShaderArtifact)` と `IGraphicsShader` です。`Caps.ShaderTarget` は `Wgsl`。DLL から取り出した UTF-8 WGSL を `GPUDevice.createShaderModule` に渡します。ブラウザーの shader validation／非同期 device error は WebGPU に従い、この同期 API は `getCompilationInfo()` を待ちません。`Dispose()` は JavaScript proxy を解放します。WebGPU の GPUShaderModule には destroy メソッドがありません。ブラウザー内で slangc を起動せず、オフライン成果物を使用します。

artifactのopaque binaryからbackendのtarget codeとmetadataを取得します。同じoffline binaryを全backendへ渡せます。必要targetを含まないonline binaryは`NotSupportedException`で拒否します。shader はデバイスの子 resource として数え、残っている間の device Dispose を拒否します。shader 解放で GPU 完了待機や暗黙の同期は行いません。artifact は GPU module を所有せず、reflection も native API に直接渡しません。pipelineとshader実行命令は別のAPIで扱います。

## CommandBuffer

ブラウザーのGPUCommandEncoder／GPUCommandBufferをJSObjectで保持します。JS moduleへbuffer／textureコピー、render／compute pass、finish、queue.submitを委譲します。JSONのregionにはmip・origin・layerとextentのみを渡し、resourceそのものはJSObjectで渡します。GPUへの書き込みにqueue.writeBuffer／writeTextureは使用せず、利用者が明示的なcopyを記録します。

GetTextureCopyLayoutはcolor texel size 4、buffer offset alignment 4、row alignment 256です。barrierの入力と論理stateを検証し、物理的な同期はWebGPUが行います。submitはqueue.onSubmittedWorkDoneのPromiseを保持し、Status／WaitAsyncからその完了・失敗を確認します。待機のキャンセルはPromiseやGPU仕事を取り消しません。JSObjectは対応するcommand／submissionのDisposeで解放します。

共通API、one-shot状態遷移、pass順序、明示的なupload／readbackの手順は [GRAPHICS-0007](../../../docs/adr/graphics/GRAPHICS-0007-command-buffers-and-submission.md) を参照してください。commandとsubmissionもdeviceの子として数えます。記録したresourceの解放、map／unmapとGPUアクセスの同期は利用者の責任です。内部lock・アトミックカウンターや暗黙の完了待機はありません。

## Pipeline

graphics programはshaderの組・topology分類・compile optionを保持し、draw時に描画状態とpassの実attachment formatからnative pipelineを解決します。完全なnative PSOを生成するため、program内でvariantをcacheし、等価なkeyの再利用で生成を繰り返しません。keyにviewport／scissor／blend constant／stencil reference、texture instance、clear値は含めません。variantはprogram Disposeまで保持します。

compute programは作成時にnative pipelineを生成します。shader moduleをprogramから保持し、保持中のshader解放を拒否します。commandは使用programを保持してsubmit時に生存を再確認しますが、GPU完了前の利用者による解放を自動的に同期しません。並列呼び出しの保証、lock、atomic counter、Slang sourceの再compileは追加しません。

reflectionでstage・location・型、vertex pulling入力、workgroup各軸と積を検証します。root-dataとresource helperのABIをartifactから検証し、通常のresource globalを直接受け取るprogramは拒否します。single-sample color/depth/stencil、direct/indexed/indirect draw、direct/indirect compute dispatchを提供します。MSAA・resolve・multi-draw・count bufferは別の拡張です。

JS moduleからGPUDevice.createRenderPipeline／createComputePipelineを使用します。AOT対応のsource-generated JSON contextで状態をsnapshotとして渡し、shader moduleと実formatからnative descriptorを構築します。writeMask／sampleMaskは0をそのまま渡し、pipelineのJSObjectはprogramのDisposeで解放します。root引数のあるprogramは明示layoutを使用し、引数のないprogramはauto layoutを使用します。dynamic値はrender passへ直接設定します。

これらの完全なnative pipelineはcacheが必要な生成単位です。dynamic stateはcommandを記録するだけで、一時state objectを生成しません。そのため今回新たに比較対象となる軽量なstate objectはありません。partial programや独立した軽量objectを導入する際は、毎draw生成・状態変更時生成・cacheのCPU時間と保持memoryを同条件で比較し、結果をこのREADMEへ記録します。今回の実装で性能計測済みとは扱いません。

共通契約は [GRAPHICS-0008](../../../docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)、共通画素検証は [PipelineExercise](../../../samples/Lumyte.Graphics.Shared/PipelineExercise.cs) を参照してください。

## Shader argumentsのbinding容量調整（ADR-0009の設計）

[ADR-GRAPHICS-0009](../../../docs/adr/graphics/GRAPHICS-0009-shader-argument-binding.md)のshader argumentsでは、artifactから取り出したWGSLをbackendが直接調整してからnative shader moduleを生成します。hardwareのlimitsを変更する機能ではなく、有効limitsに収まるresource宣言数とbinding配置を生成する機構です。

compilerはWGSLとともに、変更対象のresource宣言・参照選択helper・binding位置を識別するschemaとABI versionをartifactへ保存します。backendはcompilerが管理する領域を構造的に編集し、resource宣言、binding番号、switch等の参照解決コードを一緒に更新します。定数だけを変えて既存のresource宣言を残す置換や、任意のWGSLへ無条件の正規表現置換を行う方式は使いません。

draw／dispatchで必要な集合を収集し、textureの次元・sample type、sampler種別、buffer用途・accessなどの互換classごとに必要数を求めます。最初は必要数ぴったりの宣言を生成し、0個のclassは宣言を除去します。root uniformと参照変換表の予約枠を含め、全stageのvisibility、種類別limits、group数とgroup内binding数へ照合します。収まらない場合はdraw／dispatchを拒否し、物理上限を超える宣言は生成しません。

resourceを使うprogramのnative moduleとgraphics／compute pipeline variantは、容量が確定するdraw／dispatch時に生成できます。artifact hash、entryとstage集合、helper ABI、容量vector、実binding planをdevice内のcache keyに含めます。同じ容量でも論理型が異なれば別variantです。生成後のWGSLと実binding planを同じ変換結果から作り、artifact内の変換前binding番号を流用しません。

容量調整は数値structのlayoutと安定した参照identityを変えません。sampler共有とtexture数を独立に扱います。textureのsample helperはfragmentのderivative-uniformityを満たすように生成し、非一様な選択では明示gradient等を用いる対応可能な経路かどうか検証します。生成WGSLはDLL内artifactとruntime memoryで扱い、ソース管理へ追加しません。

shader dataはAutomaticのGPU storageとUpload stagingを別々に作ります。CopyFromはmap済みstagingへtarget ABI bytesを書き込み、利用者がunmap、commandのCopyBuffer、barrier、submitを明示します。draw／dispatchはGPU storageを直接bindし、shader dataのuploadや専用backingの生成を行いません。

WGSLだけを含むonline artifactも、このbackendの型schemaとhelper ABIが揃っていればshader data buffer生成へ使用できます。SPIR-V targetがないことだけを理由に拒否しません。全target生成の条件はoffline compilerの成果物に適用します。

## 構造体引数とWGSL binding生成

SetArgumentsは生成codecでroot値をsnapshotし、programのschemaへ照合します。shader dataは明示copyで伝播したCPU metadataを使って選択要素からIGpuRefを収集します。循環は要素の訪問済み集合で止めます。command内のpartial copyはmetadata overlayを更新し、submit成功後に後続commandへ公開します。

artifactのWGSLを直接編集し、texture・sampler・read-only buffer・writable bufferを独立して重複排除した数だけ宣言します。texture samplingとbuffer wordアクセスのABI-owned helperを生成し、論理slotを種類別の物理bindingへ変換するremapを渡します。root uniformとremap storageも含めて有効limitsを検証し、超過時は描画を分割せずNotSupportedExceptionにします。種類別resource数はprogram内のpipeline variant keyに含めます。

bufferは登録したrangeのoffsetとsizeでbindします。GetElementで作った参照も元の登録rangeを保持し、GPUへ渡す要素offsetはそのrangeの先頭から計算します。同じallocationでも登録rangeが異なれば別bindingとして扱い、同じallocationとrangeの組だけを重複排除します。storage binding sizeの上限はallocation全体ではなく登録rangeへ適用し、binding offsetのalignmentも検証します。

root値とdraw用remapだけをmappedAtCreationで初期化してunmapします。shader dataとraw bufferは確保済みのGPU resourceを直接参照します。stagingのCPU変更はGPU storageを更新しません。利用者がcopyとbarrierを記録し、GPU完了までresourceを保持します。commandはroot／remap backingとbind groupを保持し、submitで登録identityとcopy記録後のstaging revisionを検証します。

Slang側は`#include "lumyte.slang"`と、`GpuBufferRef<T>`／`GpuRWBufferRef<T>`／GpuTextureRef／GpuSamplerRefを使います。Load／StoreとLumyteSampleGradが対応helperです。最初のsampling helperはfilterableな2D float textureと非comparison samplerを扱い、用途が異なる参照はschema照合で拒否します。source generatorの導入は[Generators](../Lumyte.Graphics.Generators/README.md)を参照してください。compile時の型schema・buffer参照先型・helper ABI versionはopaque artifactに格納します。

shader dataの配置は、offline／onlineとも`StructuredBuffer<Ptr<T>>`のpointee reflectionから取得します。WGSLのraw buffer load／storeと`sizeof(T)`が使うnatural layoutに合わせ、`float3`だけの構造体はstride 12、`uint`と`float3`を持つ構造体はoffset 0／4・stride 16でpackします。root uniformの配置は、そのconstant bufferのreflectionから別に取得します。

共有の型配置・参照追跡処理は [Lumyte.Graphics.Shared](../Lumyte.Graphics.Shared/README.md) ライブラリを参照します。バックエンド実装用の契約は Graphics.Abstractions にあり、ソースのリンクコンパイルや InternalsVisibleTo は使用しません。

## Depth／Stencil、Indexed／Indirect実行

`RenderPassDesc.DepthStencilAttachment`へ単一mip・単一layerのD2 viewを指定します。colorは省略でき、depth-only passではfragment shaderのないprogramを使えます。depth/stencilのload/storeは独立で、利用者が`DepthStencilAttachment`へのbarrierを記録します。Depth32Floatはdepthのみ、Depth24Stencil8はdepthと8bit stencilです。今回のdepth textureはRenderAttachment用途に限定し、sampling/copyは生成時に拒否します。

`SetIndexBuffer`はushort／uintのsliceを受け取り、`DrawIndexed`のfirstIndexはsliceからの相対offsetです。stripは`StripIndexFormat`を一致させ、最大index値でprimitive restartします。indexの値はCPUで走査しません。

`DrawIndirect`／`DrawIndexedIndirect`／`DispatchIndirect`はそれぞれ16／20／12byteの1要素sliceをGPU命令として実行します。Indirect usage、4byte alignment、device、寿命、非mappedを検証します。命令内容はCPUで読み出さず、portable drawのFirstInstance=0、index範囲、workgroup limitsは利用者が守ります。GPU生成後の`ShaderWrite`から`IndirectRead`へのbarrier、転送とsubmitも利用者が明示します。命令bufferの参照とshader root引数は独立で、引数からの資源収集はdirect実行と同じです。

WebGPUのdepth32float／depth24plus-stencil8、render passのdepthStencilAttachment、pipelineのdepthStencil stateを使用します。depth24plus-stencil8のdepth storageはWebGPU実装が決めます。depth formatはpipeline variantのキーへ含めます。setIndexBuffer／drawIndexed／drawIndirect／drawIndexedIndirect／dispatchWorkgroupsIndirectへ直接対応付け、物理的なbarrierはWebGPUのusage管理へ任せます。indirect-first-instance optional featureは要求しません。command bufferが同じpassでwritable storage bindingにも使われる構成はWebGPUのusage競合になるため、生成passと実行passを分け、実行側のrootには必要な資源だけを渡します。

## 追加カラー形式

`R8Unorm`, `Rg8Unorm`, `R16Float`, `Rg16Float`, `Rgba16Float`, `Rgb10A2Unorm`をサンプリング・カラーattachment・コピーへ対応付けます。コピーは形式ごとのtexelサイズを使い、オフセットは4 byteとtexelサイズの両方に整列させます。

WebGPUの標準filterable形式を使い、追加のオプションfeatureは要求しません。

## Surface・Swapchain・Present

`device.CreateSurface(JSObject context)`へ、既存のHTMLCanvasElementまたはOffscreenCanvasの`GPUCanvasContext`を渡します。
ライブラリはDOM検索、canvas生成、ウインドウからのハンドル取得を行いません。contextとcanvasは利用側がSurfaceの解放まで保持します。
同じcontextの二重接続を拒否し、外部canvasの所有権は取得しません。

canvas configureでRGBA8／BGRA8／RGBA16 float、usage、pixel size、opaque／premultiplied alphaを設定します。
ブラウザーのcomposition schedulingを利用するため、共通のPresentModeはFifoだけを公開します。
`Present(waitSemaphores)`は明示的なsubmit後に論理frameを閉じる操作です。WebGPUには独立したnative present呼び出しがなく、実際のcompositionはブラウザーが行うため、その時刻や同期intervalをこのAPIで制御しません。
取得から記録・submit・Presentまでは同じブラウザー描画turn内に行い、その間に任意の非同期処理へyieldしないでください。
取得画像を解放するときも`GPUTexture.destroy()`は呼ばず、canvas contextの画像所有権とcompositionを維持します。
GPU完了とframeの解放待機は、その後に明示的に行えます。

既存Browser CIへ外部OffscreenCanvas contextを渡し、共通SurfaceExerciseでclear結果の読み戻し、lease失効、二重Present、discard、resizeを検証します。
実ウインドウ／DOMから表示先を取得する連携は別PRです。

## 明示的なGPU semaphore

`device.CreateSemaphore()`は単一device queueの発行順序を表すbinary semaphoreを生成します。
WebGPUにはnative semaphoreがないため、GPUへ発行済みのsignalと一回のwaitを検証し、同じqueueの順序保証へ対応付けます。
取得に渡されたsemaphoreだけをsignal済みにし、Submit／Presentに渡されたwaitだけを消費します。
wait stageはGPUの非空stageを検証しますが、WebGPUにはstage別のnative待機操作がなく、queue順序へ対応付けます。
CPU待機、追加submit、queue idleは挿入しません。commandのないwait／signal Submitも明示的に発行できます。
外部queue・device間の同期や未発行signalへの将来waitは公開しません。使用中のDisposeを拒否し、再利用時点と利用者の同期は呼び出し側が管理します。
