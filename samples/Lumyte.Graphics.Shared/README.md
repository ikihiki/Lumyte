# Lumyte.Graphics.Shared

Native と Browser のサンプルが ProjectReference で参照する共有ライブラリです。`Lumyte.Graphics.Abstractions` のみを参照し、`CapsDisplay.Describe(IGraphicDevice)` で Caps を検証・表示し、`BufferExercise.RunAsync(IGraphicDevice)` で buffer の共通契約を検証します。

デバイスの生成・解放は各サンプルの起動部分が担当します。このプロジェクトは `Lumyte.slnx` に登録し、単独でもビルドできます。

`TextureExercise.Run(IGraphicDevice)` は同じ共通APIだけでtextureの確保、4形式、mip・layerとcube viewの範囲・正規化、不正入力、解放順序を確認します。画素の転送や描画はcommand APIの対象です。

SamplerExercise.Runは共通APIだけでfilter／address／comparison、LOD 0、anisotropy、入力拒否と所有を確認します。実際のsampling結果はshader／binding／描画の後続検証で扱います。

ArgumentTableExercise.Runは共通APIで種類別slot、buffer要素参照、世代の失効、用途・範囲と寿命を検証します。CheckForeignDeviceは別deviceのresourceを登録できないことを確認します。shaderやGPU bindingの生成は行いません。

## シェーダー

`Shaders/increment.slang` をビルド時に WGSL と SPIR-V へ変換し、反射情報・entry・stage・compiler version・行列layoutと一つのbinaryにpackし、このDLLに埋め込みます。利用側はtargetを選ばず同じbinaryを渡します。生成 code はコミットせず、実行時に外部ファイルを読みません。オンライン検証用の Slang source も DLL に埋め込みます。

`ShaderExercise.Run(IGraphicDevice)` は共通 API だけで成果物をロードし、全targetの収録・コンパイルmetadata・module作成・所有権を確認します。`RunOnlineAsync` は bootstrap から渡された `IShaderCompiler` を使用します。module の作成検証であり、compute dispatch は行いません。このプロジェクトは引き続き Abstractions のみ参照します。

## GPU command検証

`CommandExercise.RunAsync(IGraphicDevice)` は共通APIだけでupload → buffer部分コピー → readback、mip／layerとrow paddingを指定したtexture往復・texture間コピー、render passのclear／storeを検証します。GPU完了後にmapし、コピーした全texelのbytesを照合します。padding部分の内容は検証対象にしません。compute passの開始・終了、pass内操作の拒否、二重submit・mapped bufferの拒否とキャンセル後の再待機も確認します。

NativeのGPUテストとBrowserの既存Wasm検証が同じ処理を呼びます。backend固有deviceの生成はそれぞれのbootstrapで行います。

## Pipeline検証

`PipelineExercise.RunAsync(IGraphicDevice)` は同じgraphics programをRGBA／BGRAと複数描画状態で使い、readbackの全画素を検証します。blend、write mask 0、sample mask 0、setter失敗後の状態維持、元list変更後のsnapshot、未設定／終了後のdraw拒否とshader保持を確認します。compute programのdispatchとstage／device不一致の拒否も共通APIで確認します。

fullscreen vertex・color fragment・empty computeのSlang sourceを全targetへoffline compileし、metadataと一つのbinaryとしてDLLに埋め込みます。生成WGSL／SPIR-Vはコミットしません。native GPUテストと既存Browser CIから同じ検証を呼びます。空のcompute shaderはdispatch・完了経路の検証であり、storage bufferへの書き込み検証はbinding接続時に追加します。

ShaderBindingExerciseは生成codecと共通APIだけを使います。root matrix、循環したshader data参照、異なるstagingからの明示copyを挟んだdispatch、未転送draw・未map書き込み・未submit copyのmetadata非公開、slot再利用後のsubmit拒否、5枚のtextureでsamplerを共有するdraw、20個のtextureとmaterialの全画素readbackを検証します。native GPU testsとBrowser sampleから同じexerciseを呼び出します。

shader dataの追加検証では、float3だけの構造体、uintとfloat3を並べた構造体、入れ子構造体の要素0・1をreadbackしてmember offsetとstrideを照合します。別allocationを結ぶ循環参照、同じrootの背後にある型のstage間不一致の拒否、互換な循環型とraw buffer型の受理も確認します。bufferの別々の登録範囲からGetElementで選んだ値を読み、Capsが許す場合はstorage binding上限を超えるallocation内の小さな範囲も検証します。この追加allocationは256 MiB以下に制限します。

`AdvancedCommandExercise.RunAsync`は共通APIのみでdepth遮蔽、depth-only passからのload、stencil mask、16/32bit indexのsliceとsigned baseVertex、indirect draw/indexed draw、GPU生成dispatchを検証します。Slang shaderは全targetをoffline compileしてDLLへ埋め込み、WGSLをコミットしません。

AdvancedCommandExerciseのvertex pullingはSlangの`SV_VulkanVertexID`でbaseVertex適用後のindexを受け取ります。このsemanticはWGSL／SPIR-V両targetで同じ用途に使えます。

`SurfaceExercise.RunAsync(device, surface)`は外部から渡されたsurfaceへ共通APIだけで描画し、pixel readback、明示的なsubmit／Present、lease寿命、discard、resizeを確認します。
ウインドウやcanvasの生成・取得はこのライブラリに含めません。BrowserのテストbootstrapがOffscreenCanvas contextを提供します。

`SemaphoreExercise.RunAsync`は利用側のsignal／waitチェーン、待機stage、semaphore-only Submit、消費後の再利用、重複・未signal・失効・異なるdeviceの拒否を共通APIで検証します。
SurfaceExerciseも取得signal、Submit wait／signal、Present waitを利用側から明示し、自動同期に依存しません。
