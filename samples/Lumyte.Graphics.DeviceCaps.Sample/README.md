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

## Argument Table

```bash
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- wgpu arguments
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan arguments
```

共通APIだけで論理登録・要素参照・失効と所有を確認します。

## シェーダー

`mise exec -- dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- wgpu shaders` または `vulkan shaders` で、DLL のオフライン成果物と Slang のオンライン成果物から module を作成します。bootstrap だけが具体的な device／compiler を作り、検証本体は shared の共通 API を使います。オンライン側は PATH の slangc が必要です。GPU の dispatch／描画は行いません。

## GPU command

```sh
mise exec -- dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- wgpu commands
mise exec -- dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- vulkan commands
```

共有プロジェクトのCommandExerciseで、buffer・mip／layer textureのGPUコピー、clear／store、passとsubmissionの状態を共通APIのみで検証します。Vulkanはdynamic renderingとsynchronization2を必須としてdevice生成時に有効化します。
