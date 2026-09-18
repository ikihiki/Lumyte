# Khronos BoxVertexColors の描画 fixture

出所は [Khronos glTF-Sample-Assets の固定版](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/c6a6bd13ab2b3c685c7903d03561b8a9392f38b8/Models/BoxVertexColors)。
作者は Marco Hutter、ライセンスは CC0-1.0（© 2023, Public）。
原文の帰属情報は `source/README.source.md` に保持する。

`source/BoxVertexColors.gltf` と `source/buffer.bin` は変更していない。
SHA-256 はそれぞれ次のとおり。

- glTF: `53d15f2357d410d0cc9e798065709054474db25ba52274f012bb7fb96a2eaddf`
- buffer: `b508427bea091cc29cbe68d8b08e75db27e33559f72104a37343c7e808e72d26`

2026-09-18 に公式 `gltf-validator` **2.0.0-dev.3.10** の `validateBytes` を実行し、
external buffer をローカルから解決して **errors 0、warnings 0、infos 0、hints 0** を確認した。
この検証は fixture 準備時だけ行い、通常の xUnit／GPU suite にネットワークや Validator を要求しない。

`model.json` は GPU 転送型へ渡すため、属性と index をオフラインで展開したデータである。
生成は repository root から次のコマンドで再現できる。

```powershell
python tools/model-conformance/prepare_box_vertex_colors.py src/graphics/Lumyte.Graphics.RenderGraph.Conformance/Models/Fixtures/BoxVertexColors/source src/graphics/Lumyte.Graphics.RenderGraph.Conformance/Models/Fixtures/BoxVertexColors/model.json
```

変換 script はこの固定資産専用であり、production の glTF loader ではない。
原文の byte offset、stride、16-bit index、RGB 属性を読み、RGB に alpha 1 を補う。
Graphics の描画試験は元 glTF を読まず、展開済みの `model.json` だけを embedded resource から読む。

資産が定義する `RGB = object-space XYZ` を利用し、前後左右からの正射影と負の X scale を試験する。
元の glTF の既定材質を使い、正面の directional light、roughness 1、metallic 1 の閉形式解 `F0 / 4` と比較する。
source geometry／material は変更しない。camera・light と reflected ケースの配置は試験側の設定である。
比較は同じ consumer を用いる DirectX 12／Vulkan／Dawn／Browser の HDR 画素値で行う。

これは一資産の描画適合試験であり、glTF のファイル読込み、animation／node 評価、全 core／拡張の完全適合を表さない。
