# 静的モデル描画

モデルの描画形式は `ModelVertex` の頂点列と `ushort` または `uint` の triangle-list インデックス列に統一します。
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
IGpuRef<ModelVertex> vertexRef = table.WriteBuffer(0, mesh.Vertices.Slice(0, mesh.Vertices.Count));
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
mesh.RecordInitialUpload(commands);
```

`CreateAsync` は GPU storage と staging を確保し、CPU 配列を staging に書き込みます。
頂点は C# と shader 共通の 64-byte 配置を検証し、raw buffer に一括コピーします。
頂点ごとの shader snapshot の生成・pack・描画時の参照走査を行いません。
16bit インデックスはそのまま保持します。奇数個の場合は転送用 storage の末尾だけを
4-byte 境界まで padding し、draw には元の個数を使用します。
`RecordInitialUpload` はコピーと host→copy→vertex/index の依存を記録し、submit／wait は行いません。
初回描画前に一度呼びます。CPU 配列は `CreateAsync` 完了後に保持する必要はありません。
upload submission の実完了後に `mesh.ReleaseUploadResources()` を呼ぶと、頂点・index の staging だけを解放できます。
このメソッドは待機を行わず、繰り返し呼べます。以後の `RecordInitialUpload` は拒否しますが、描画と import は引き続き可能です。
Resources 経由では公開前に staging を解放するため、常駐 mesh は転送用の複製を保持しません。

## glTF と接続する byte 配置

`ModelVertex.AsVertices(ReadOnlySpan<byte>)` は `MemoryMarshal.Cast` による再解釈です。
`ModelMesh.CreatePackedAsync(device, vertexBytes, indices, vertexArtifact)` なら、変換済みの
interleaved bytes から頂点配列を追加確保せず staging にコピーできます。CPU→GPU 転送自体は必要です。

| 属性 | 型 | byte offset |
| --- | --- | ---: |
| Position | float3 | 0 |
| Normal | float3 | 12 |
| Tangent | float4 | 24 |
| TexCoord | float2 | 40 |
| Color | float4 | 48 |

stride は 64、little-endian、全属性 float32 です。glTF の accessor がこの配置と一致する場合のみ
直接再解釈できます。属性の分離、別の stride、normalized 整数、sparse accessor、圧縮、
欠けた法線・接線・頂点色は外部の変換工程でこの配置に揃えます。ファイル全体を cast する API ではありません。
mesh 作成時に `ValidateShaderLayout` を実行します。shader だけを HotReload する場合も、
既存 mesh と組み合わせる前に呼び出してください。ABI の変更は拒否されます。

## 材質・texture・描画

`ModelVertex` は座標・法線・接線・UV・頂点色を持ちます。法線と tangent.xyz は unit vector、
tangent.w は bitangent の符号（±1）、UV は画像の左上が `(0, 0)` です。
法線・接線の生成や頂点／インデックスの最適化は、この配列を用意する側で行います。
インデックスが登録頂点範囲内であることも利用側の契約で、毎回の全要素検証は行いません。

`ModelMaterial` は metallic-roughness、base color、emissive、normal scale、AO、alpha の数値を持ちます。
base color／emissive texture は sRGB、normal／metallic-roughness／AO は linear format にします。
各 texture の `BaseColorUv`／`MetallicRoughnessUv`／`NormalUv`／`OcclusionUv`／`EmissiveUv` は
`KHR_texture_transform` と同じ offset + rotation(scale × UV) を指定できます。既定値は identity。
normal texture の軸変換時には shader が微分から tangent frame を再構成します。
`EmissiveStrength` は `KHR_materials_emissive_strength` 相当の倍率で、既定値は 1 です。

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
render.SetArgumentTable(table);
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

共有 GPU mesh の再構築には、任意の [Lumyte.Resources.Graphics.Drawing](../Lumyte.Resources.Graphics.Drawing/README.md)
の `ModelMeshDefinition` を使用できます。変換済み CPU データを Build 依存とし、GPU 完了後の mesh を
汎用 `GraphicsResource<ModelMesh>` として公開します。インポーターは含みません。

ResourceReference は論理 mesh を保持し、graph.ImportResources(manager).Pin(reference) が
フレームで使う世代を固定します。HotReload の新世代を次の graph で取得し、旧 graph／plan は GPU 完了まで
旧世代を保持します。device loss 後は旧 epoch の退役を確認し、device を復旧してから
`manager.RebuildRuntimeAsync([graphicsRuntime.Id])` で GPU 出力を再構築します。

Resources を使わない場合は、利用側で新 mesh の upload 完了、参照切替、旧 submission 完了、旧資源解放を管理します。
`ModelMesh.Dispose` は GPU 完了待機や登録解放を行いません。

## RenderGraph への接続

初期 upload が完了した mesh を graph ごとに一度 import します。前の command／graph の実際の scope を渡してください。

```csharp
ModelMeshGraphResources meshResources = mesh.Import(graph,
    new(PipelineStage.VertexShader, ResourceAccess.ShaderRead),
    new(PipelineStage.IndexInput, ResourceAccess.IndexRead));
RenderGraphPass pass = graph.AddPass("model", context =>
{
    // attachment views は context.CreateTextureView(handle) で作成する。
    // render pass を開始し、pipeline／table／state／arguments を設定して mesh.Draw(render)。
    // render.End() して返す。
});
meshResources.Use(pass);
// 材質が参照する全 texture の UseTexture と、color/depth attachment の UseTexture も宣言する。
```

`Use` は raw 頂点と元の幅の index buffer の read を宣言します。
texture、attachment、他の draw 入力は利用側が宣言します。Graph が依存順と barrier を作り、
一時 HDR scene から共有 ImageEffects へ接続できます。import は mesh の所有権を移しません。
Resources を使う場合は graph が取得した Lease が、GPU 完了後の plan 解放まで mesh を保持します。

## glTF 機能との対応範囲

metallic-roughness、5 種の texture、頂点色、alpha mask／blend、両面、unlit、UV 変換、
HDR 発光強度は描画側で扱います。node の transform は draw arguments に渡し、
primitive／material ごとに mesh と draw を用意します。材質は GPU texture／sampler への参照が必要なため、
glTF の texture index を含むデータをそのまま cast できません。

現在の UV は TEXCOORD_0 のみです。TEXCOORD_1 を使う lightmap／AO、skin、morph、animation、
IBL、clearcoat／transmission／volume／sheen／anisotropy 等は未対応です。
UV や変形を追加する場合は固定の `ModelVertex` を肥大化させず、必要な mesh だけに補助 stream と
対応 shader を追加する方針が適しています。圧縮や画像形式は外部の importer／decoder の担当です。
