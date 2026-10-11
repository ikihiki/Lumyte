# 静的モデル描画

モデルの描画形式は `ModelVertex` の頂点列と `uint` の triangle-list インデックス列に統一します。
`ModelMesh` はそれらを GPU resource に変換し、`ModelMaterial` と `ModelDrawArguments` で描画します。
ファイル形式、parser、画像 decoder、scene の読み込みを扱う API は含みません。

## GPU mesh の準備

```csharp
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing;

ModelVertex[] vertices =
[
    new(new(-0.5f, -0.5f, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(0, 1), Vector4.One),
    new(new(0.5f, -0.5f, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(1, 1), Vector4.One),
    new(new(0, 0.5f, 0), Vector3.UnitZ, new(1, 0, 0, 1), new(0.5f, 0), Vector4.One),
];
uint[] indices = [0, 1, 2];
ShaderArtifact vertexArtifact = DrawingShaders.LoadModelVertex();
using ModelMesh mesh = await ModelMesh.CreateAsync(device, vertices, indices, vertexArtifact);
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

`CreateAsync` は GPU storage と staging を確保し、CPU 配列を staging に書き込みます。
GPU 上の頂点の byte 配置と stride は shader artifact の reflection に従います。
C# の struct の bytes をそのまま GPU に転送せず、各 backend の shader ABI に pack します。
`RecordInitialUpload` はコピーと host→copy→vertex/index の依存を記録し、submit／wait は行いません。
初回描画前に一度呼びます。CPU 配列は `CreateAsync` 完了後に保持する必要はありません。

## 材質・texture・描画

`ModelVertex` は座標・法線・接線・UV・頂点色を持ちます。法線と tangent.xyz は unit vector、
tangent.w は bitangent の符号（±1）、UV は画像の左上が `(0, 0)` です。
法線・接線の生成や頂点／インデックスの最適化は、この配列を用意する側で行います。

`ModelMaterial` は metallic-roughness、base color、emissive、normal scale、AO、alpha の数値を持ちます。
base color／emissive texture は sRGB、normal／metallic-roughness／AO は linear format にします。
texture と通常の sampler を argument table に登録した `SampledTexture` を使用します。
同じ元画像を色とデータに使用する場合は、それぞれの color space の GPU texture を選択してください。

欠けた texture slot には白、normal slot には linear `(0.5, 0.5, 1)` の neutral texture を渡します。
これらも利用側で upload し、sampler の LOD clamp を実際の mip chain に合わせます。

```csharp
ModelMaterial material = ModelDrawing.DefaultMaterial with
{
    BaseColor = new Vector4(0.8f, 0.4f, 0.2f, 1),
    Metallic = 0,
    Roughness = 0.6f,
};
ModelDrawArguments arguments = ModelDrawing.CreateArguments(
    vertexRef, material, whiteTexture, neutralNormalTexture) with
{
    World = world,
    NormalTransform = ModelDrawing.CreateNormalTransform(world),
    ViewProjection = view * projection,
    CameraPosition = cameraPosition,
    LightDirection = Vector3.Normalize(new Vector3(1, 2, 3)),
    LightColor = new Vector3(3),
    FogColor = new Vector3(0.4f, 0.5f, 0.6f),
    FogDensity = 0.02f,
};
// 自前の texture を使う場合は、対応する Texture／Sampler の組を置き換えます。
arguments = arguments with
{
    BaseColorTexture = baseColorTexture.Texture,
    BaseColorSampler = baseColorTexture.Sampler,
};
render.SetPipeline(pipeline);
render.SetRenderState(ModelDrawing.CreateRenderState(material, doubleSided: false, mirrored: world.GetDeterminant() < 0));
render.SetArguments(in arguments);
mesh.Draw(render);
```

この例の `whiteTexture`、`neutralNormalTexture`、`baseColorTexture` は登録済み `SampledTexture`、
`render` は color/depth attachment を持つ active render pass です。viewport／scissor／blend constant／
stencil reference、attachment の state transition、submit と GPU 完了待機は利用側で行います。

シェーダーは GGX の方向ライト、近似 ambient、normal map、AO、発光、exponential fog を計算します。
`AlphaMode` は 0=opaque、1=mask、2=straight-alpha blend。blend は遠い順に opaque の後へ描画します。
`CreateRenderState` は通常 depth（near=0／far=1）を前提にし、blend の depth write を無効にします。
projection／viewport の巻き方向反転や reverse-Z は利用側で state を変更してください。

## Instance と HotReload

instance ごとのモデル型を保持する必要はありません。同じ `ModelMesh` と材質を共有し、
draw ごとに `World`／`NormalTransform` などの `ModelDrawArguments` を変更します。
親子の world transform 計算や描画順序は利用側で管理します。

HotReload は、新しい配列から別の `ModelMesh` を作り、upload の GPU 完了を確認してから
描画側の mesh と argument table の参照を切り替える構成にします。
古い mesh を使う submission が完了するまで、古い登録・mesh・texture を生存させてください。
完了後に登録を解放し、resource を Dispose します。HotReload の監視・変換・切り替え機構自体は
このライブラリには含みません。

`ModelMesh.Dispose` は GPU 完了待機や登録解放を行いません。
