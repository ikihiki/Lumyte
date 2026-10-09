# Lumyte.Graphics.Shared

Native と Browser のサンプルが ProjectReference で参照する共有ライブラリです。`Lumyte.Graphics.Abstractions` のみを参照し、`CapsDisplay.Describe(IGraphicDevice)` で Caps を検証・表示し、`BufferExercise.RunAsync(IGraphicDevice)` で buffer の共通契約を検証します。

デバイスの生成・解放は各サンプルの起動部分が担当します。このプロジェクトは `Lumyte.slnx` に登録し、単独でもビルドできます。

`TextureExercise.Run(IGraphicDevice)` は同じ共通APIだけでtextureの確保、4形式、mip・layerとcube viewの範囲・正規化、不正入力、解放順序を確認します。画素の転送や描画はcommand APIの対象です。

SamplerExercise.Runは共通APIだけでfilter／address／comparison、LOD 0、anisotropy、入力拒否と所有を確認します。実際のsampling結果はshader／binding／描画の後続検証で扱います。

ArgumentTableExercise.Runは共通APIで種類別slot、buffer要素参照、世代の失効、用途・範囲と寿命を検証します。CheckForeignDeviceは別deviceのresourceを登録できないことを確認します。shaderやGPU bindingの生成は行いません。

## シェーダー

`Shaders/increment.slang` をビルド時に WGSL と SPIR-V へ変換し、反射情報・entry・stage・compiler version・行列layoutと一つのbinaryにpackし、このDLLに埋め込みます。利用側はtargetを選ばず同じbinaryを渡します。生成 code はコミットせず、実行時に外部ファイルを読みません。オンライン検証用の Slang source も DLL に埋め込みます。

`ShaderExercise.Run(IGraphicDevice)` は共通 API だけで成果物をロードし、全targetの収録・コンパイルmetadata・module作成・所有権を確認します。`RunOnlineAsync` は bootstrap から渡された `IShaderCompiler` を使用します。module の作成検証であり、compute dispatch は行いません。このプロジェクトは引き続き Abstractions のみ参照します。
