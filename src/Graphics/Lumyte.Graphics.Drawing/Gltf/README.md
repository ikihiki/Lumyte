# glTF 2.0 モデル描画

`GltfLoader` は静的 glTF／GLB を GPU に依存せず読み込み、`ModelMesh`、
`ModelDrawing` と組み合わせて indexed drawing に使用できます。

## 読み込み

```csharp
using Lumyte.Graphics.Drawing.Gltf;

GltfModel model = await GltfLoader.LoadFileAsync("Models/scene.glb", cancellationToken);
```

ブラウザーや独自ストレージでは `LoadAsync(bytes, resolveResource, cancellationToken)` を使います。
resolver は URI と cancellation token を受け取り、`ValueTask<ReadOnlyMemory<byte>>` を返します。
loader 自身はネットワークにアクセスしません。base64 の data URI と GLB の BIN chunk は
callback なしで読み込めます。`LoadFileAsync` はモデルのフォルダー内の相対ファイルを解決します。

- 三角形メッシュ：POSITION、NORMAL、TANGENT、TEXCOORD_0、COLOR_0、8／16／32-bit indices。
- accessor：byteOffset、interleaved byteStride、normalized UV／color、sparse override。
- NORMAL がない場合は triangle ごとに頂点を分けて flat normal を生成。
- TANGENT がない場合は UV と法線から tangent frame を生成。
- scene：既定 scene、未指定時は先頭 scene、scene 定義がない場合は node forest の root。
- node：親子関係、TRS／matrix、共有 mesh の複数 instance。world matrix は .NET の row-vector 規約。
- 材質：metallic-roughness、5 種の texture slot、OPAQUE／MASK／BLEND、doubleSided、KHR_materials_unlit。
- PNG／JPEG は **符号化された bytes** と image metadata を返します。画像デコード・GPU texture upload・
  mipmap 生成は利用側の画像処理機能で行います。

スキニング、アニメーション、morph、triangle strip/fan、TEXCOORD_1、texture transform、
Draco／Meshopt／KTX2 などの required extension は現在非対応で、例外を返します。
shader が使わない追加頂点属性や optional extension の fallback は glTF の基本表現を使用します。
IBL、shadow map、物理的な複数ライトの計算はこの shader に含みません。

## GPU mesh の準備

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;

ShaderArtifact vertexArtifact = DrawingShaders.LoadModelVertex();
GltfPrimitive primitive = model.Meshes[0].Primitives[0];
using ModelMesh mesh = await ModelMesh.CreateAsync(device, primitive, vertexArtifact);
using IGraphicsShader vertex = device.CreateShader(vertexArtifact);
using IGraphicsShader fragment = device.CreateShader(DrawingShaders.LoadModelFragment());
using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new()
{
    VertexShader = vertex,
    FragmentShader = fragment,
});
using IArgumentTable table = device.CreateArgumentTable(new()
{
    BufferCapacity = 1,
    TextureCapacity = 5,
    SamplerCapacity = 5,
});
IGpuRef<ModelVertex> vertexRef = table.WriteBuffer(0, mesh.Vertices.SliceElements(0, mesh.Vertices.Count));
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
mesh.RecordInitialUpload(commands);
```

`CreateAsync` は storage と staging を確保して CPU 値を staging に書き込みます。
`RecordInitialUpload` はコピーと host→copy→vertex/index の依存だけを記録し、submit／wait は行いません。
初回描画前に一度呼び、resource の再転送・解放・GPU 使用中の変更は利用側で同期してください。
複数 instance は同じ `ModelMesh` を共有できます。

## Texture と描画引数

model の texture index は image index と sampler の組です。image をデコードした後、
base color／emissive は sRGB、normal／metallic-roughness／AO は linear texture として upload します。
同じ image が色とデータの両方に使用される場合は、それぞれの format の texture を用意します。
`SamplerDesc.LodMaxClamp` は作成した mip 数に合わせます。mipmap filter を要求する画像では
mip chain を作り、提供しない場合は利用側が LOD を 0 に制限してください。

全 slot に有効な参照を渡します。texture がない slot は白、normal は linear `(0.5, 0.5, 1)` の
1×1 texture を使います。`SampledTexture` は table に登録した texture と sampler の参照を組にします。
`ModelDrawing.CreateArguments` が欠けた texture に neutral fallback を設定します。
texture 付き material の resolver は、引数の `ModelTextureRole` に適した色空間の `SampledTexture` を返してください。
resolver の型は `Func<int, ModelTextureRole, SampledTexture>` です。同じ texture index が
base color と normal で共有される場合でも role で別々の GPU texture を選べます。

```csharp
GltfMaterial material = model.Materials[primitive.MaterialIndex];
ModelDrawArguments arguments = ModelDrawing.CreateArguments(
    vertexRef, material, whiteTexture, neutralNormalTexture, resolveTexture);
Matrix4x4 world = instance.World;
arguments = arguments with
{
    World = world,
    NormalTransform = ModelDrawing.CreateNormalTransform(world),
    ViewProjection = view * projection,
    CameraPosition = cameraPosition,
    LightDirection = Vector3.Normalize(new Vector3(1, 2, 3)),
    LightColor = new Vector3(3),
    AmbientColor = new Vector3(0.03f),
    FogColor = new Vector3(0.4f, 0.5f, 0.6f),
    FogDensity = 0.02f,
};
render.SetPipeline(pipeline);
render.SetRenderState(ModelDrawing.CreateRenderState(material, world.GetDeterminant() < 0));
render.SetArguments(in arguments);
mesh.Draw(render);
```

この例の `whiteTexture`／`neutralNormalTexture`／`resolveTexture` は利用側が登録した参照、
`instance` は `model.Instances` の要素、`render` は depth attachment を持つ active render pass です。
`using System.Numerics;` を追加してください。viewport／scissor／blend constant／stencil reference、
render attachment の state transition は別途明示します。

シェーダーは GGX metallic-roughness の直射光、近似 ambient、AO、発光、距離に対する exponential fog を計算します。
MASK は alpha cutoff で破棄し、OPAQUE／MASK の出力 alpha は 1、BLEND は straight alpha です。
BLEND instance は利用側でカメラから遠い順に並べ、opaque／mask の後に描画します。
`CreateRenderState` は通常の depth（near=0／far=1）を前提にします。reverse-Z では変更してください。
投影や viewport が巻き方向を反転する場合は FrontFace も合わせて変更します。

pass の後は `commands.Finish()`、`device.Queue.Submit([commands])`、
`submission.WaitAsync()` で提出と完了を扱います。GPU 完了後に table の登録を解放してから
mesh／texture／shader を Dispose します。`ModelMesh.Dispose` は完了待機や登録解放を行いません。
