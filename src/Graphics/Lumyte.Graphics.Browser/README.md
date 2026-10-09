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
