# Lumyte.Graphics.Drawing

既存の Graphics API 上で使える描画用の構造体・形状生成・Slang シェーダーのライブラリです。
.NET 10、Graphics.Abstractions、既存の source generator と offline shader pipeline を使用し、
Wgpu／Browser／Vulkan の具象 backend には依存しません。

## 提供する型

- `DrawingVertex`：3D 座標、linear／straight-alpha の頂点色、UV。`IShaderData` を実装します。
- `ColorDrawArguments`：頂点範囲、object-to-clip 行列、色の乗算値。
- `TextureDrawArguments`：上記に加えて 2D texture view と通常の sampler の GPU 参照。
- `DrawingPrimitives.Rectangle`／`Circle`：XY 平面の反時計回り triangle list。
- `DrawingPrimitives.Line`：任意の 3D 座標を結ぶ line list。
- `DrawingShaders`：頂点カラーと texture 描画の vertex／fragment artifact をロードします。

色と UV を自由に指定した `DrawingVertex` 配列を使えば任意の三角形やメッシュも描画できます。
矩形は XY の最小座標と正のサイズ、円は中心・正の半径・3 以上の分割数を指定します。
UV は画像の左上が `(0, 0)`。生成した形状は CPU 配列で、GPU resource を所有しません。

## 使用例

```csharp
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;

DrawingVertex[] vertices = DrawingPrimitives.Rectangle(
    new(-0.5f, -0.5f), new(1, 1), Vector4.One);
ShaderArtifact vertexArtifact = DrawingShaders.LoadColorVertex();
using IGraphicsShader vertexShader = device.CreateShader(vertexArtifact);
using IGraphicsShader fragmentShader = device.CreateShader(DrawingShaders.LoadColorFragment());
using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new()
{
    VertexShader = vertexShader,
    FragmentShader = fragmentShader,
});
using IGraphicsShaderDataBuffer<DrawingVertex> upload = device.CreateBuffer<DrawingVertex>(
    vertexArtifact, (ulong)vertices.Length, MemoryPreference.Upload);
using IGraphicsShaderDataBuffer<DrawingVertex> storage = device.CreateBuffer<DrawingVertex>(
    vertexArtifact, (ulong)vertices.Length);
await upload.MapAsync();
upload.CopyFrom(vertices);
upload.Unmap();
```

続いて、既存の command API の `CopyBuffer` と `ShaderDataBufferBarrierDesc` で
upload から storage へ明示転送します。argument table に
`storage.SliceElements(0, storage.Count)` を登録すると `IGpuRef<DrawingVertex>` を取得できます。
記録中の render encoder に `SetPipeline(pipeline)`、描画状態・viewport・scissor・blend constant・stencil reference を設定し、
`SetArguments(new ColorDrawArguments(vertexRef, Matrix4x4.Identity, Vector4.One))` と
`Draw((uint)vertices.Length)` で描画します。上のコードは resource 準備の例であり、
転送・pass 記録・submit・完了待機までを利用側で行ってください。

シェーダーは `SV_VertexID` で登録した範囲の頂点を読みます。`firstVertex` や index buffer の値は
登録範囲内で指定してください。行列は `System.Numerics` と同じ row vector／row-major 方針で、
`model * view * projection` を渡します。

texture 描画では両 shader を `LoadTextureVertex`／`LoadTextureFragment` に切り替え、
`TextureDrawArguments` を渡します。fragment は texture × 頂点色 × tint を返します。
straight-alpha の画像を使い、透明度に合わせた blend state を利用側で指定します。
線の pipeline は `TopologyClass = PrimitiveTopologyClass.Line`、render state は line list に設定します。
線幅は backend の line rasterization に従います。

resource の寿命、同期、upload、render state、depth／blend、submit は利用側で管理します。
反時計回りは object space の規約なので、投影・viewport に応じて culling を指定してください。

## ビルドと検証

リポジトリの開発環境を activate してから実行します。

```sh
dotnet build src/Graphics/Lumyte.Graphics.Drawing
dotnet test tests/Graphics/Lumyte.Graphics.Drawing.Tests
```

`Slang.targets` が WGSL と SPIR-V、ABI reflection を `.lshader` にまとめて DLL に埋め込みます。
実行時の Slang installation は不要です。テストは形状の巻き方向・閉じ方・不正値、
生成 serializer と全 shader の両 target を GPU なしで確認します。
