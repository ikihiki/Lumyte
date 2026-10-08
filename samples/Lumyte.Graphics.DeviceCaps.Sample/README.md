# DeviceCaps サンプル

起動部分だけが選んだバックエンドでデバイスを生成・解放し、Caps の表示処理は [共有プロジェクト](../Lumyte.Graphics.Shared/README.md)を参照し、`IGraphicDevice` と `DeviceCaps` だけを使います。同じ表示処理を [Browser サンプル](../Lumyte.Graphics.Browser.Sample/README.md) でも使用します。

```sh
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- wgpu
dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -c Release -- vulkan
```

[Wgpu](../../src/Graphics/Lumyte.Graphics.Wgpu/README.md) または [Vulkan](../../src/Graphics/Lumyte.Graphics.Vulkan/README.md) の実行環境が必要です。実デバイスを生成できない場合は例外となり、代わりの固定 Caps は返しません。
