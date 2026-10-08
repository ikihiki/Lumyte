# Lumyte.Graphics.Shared

Native と Browser のサンプルが ProjectReference で参照する共有ライブラリです。`Lumyte.Graphics.Abstractions` のみを参照し、`CapsDisplay.Describe(IGraphicDevice)` で Caps を検証・表示し、`BufferExercise.RunAsync(IGraphicDevice)` で buffer の共通契約を検証します。

デバイスの生成・解放は各サンプルの起動部分が担当します。このプロジェクトは `Lumyte.slnx` に登録し、単独でもビルドできます。
