# DeviceCaps サンプル

起動部分だけが選んだバックエンドでデバイスを生成・解放し、Caps の表示処理は [共有プロジェクト](../Lumyte.Graphics.Shared/README.md)を参照し、`IGraphicDevice` と `DeviceCaps` だけを使います。同じ表示処理を [Browser サンプル](../Lumyte.Graphics.Browser.Sample/README.md) でも使用します。

```sh
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- wgpu
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan
```

[Wgpu](../../src/Graphics/Lumyte.Graphics.Wgpu/README.md) または [Vulkan](../../src/Graphics/Lumyte.Graphics.Vulkan/README.md) の実行環境が必要です。実デバイスを生成できない場合は例外となり、代わりの固定 Caps は返しません。

## Buffer

```sh
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- wgpu buffers
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan buffers
```

共有プロジェクトの BufferExercise が共通 API だけで、要素のサイズ、非所有 slice、明示 mapping、CPU copy の方向・範囲・状態と寿命を確認します。GPU copy や描画は実行しません。

## Texture

```bash
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- wgpu textures
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan textures
```

shared projectのTextureExerciseが共通APIでallocationとViewの範囲・所有を確認します。画素の転送や描画は実行しません。

## Sampler

```bash
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- wgpu samplers
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan samplers
```

SamplerExerciseが共通APIでsampling stateの指定値・不正入力・capabilityと寿命を検証します。
