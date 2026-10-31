# Native Graphics host

Vulkan / wgpu を生成して、共通の `GraphicsExercises` を実行する薄い host です。
入口とサンプル全体の役割は [共通ランチャー](../README.md) を参照してください。

```sh
dotnet run --project samples/Lumyte.Samples -- --sample graphics --backend vulkan -- caps
dotnet run --project samples/Lumyte.Samples -- --sample graphics --backend wgpu -- commands
dotnet run --project samples/Lumyte.Samples -- --sample graphics --backend vulkan -- suite
```

個別 host の `dotnet run --project samples/Lumyte.Graphics.DeviceCaps.Sample -- vulkan caps` も利用できます。
backend / exercise が不足すると Spectre.Console で選びます。非対話実行では両方を指定してください。

| 選択 | 役割 |
| --- | --- |
| `caps` | デバイス能力の表示 |
| `buffers`, `textures`, `samplers`, `arguments` | 資源生成・範囲・所有権 |
| `shaders` | offline module と Slang による online compile |
| `commands`, `pipelines`, `bindings` | GPU コピー・描画・生成 codec |
| `advanced`, `semaphores`, `render-graph` | depth / stencil・間接実行・同期・グラフ |
| `suite` | 共通の全契約検証（online compile は含まない） |
| `pipeline-benchmark` | Vulkan 専用の pipeline cache 比較 |

検証内容は [共有 exercise](../Lumyte.Graphics.Shared/README.md) に集約しています。
[Wgpu](../../src/Graphics/Lumyte.Graphics.Wgpu/README.md) または [Vulkan](../../src/Graphics/Lumyte.Graphics.Vulkan/README.md) の実行環境と、ビルド用の Slang / DXC が必要です。
実デバイスの生成失敗はエラーになり、固定値や別 backend へ置き換えません。
ベンチマークは `--sample pipeline-benchmark --backend vulkan` でも実行できます。
