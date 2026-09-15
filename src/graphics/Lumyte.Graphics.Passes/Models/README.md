# Model 描画

`AddModelPass` に、ファイル形式や ECS に依存しない描画データを渡します。
Generic Host の `AddModelRendering()` が Native／Portable の実装を登録します。
利用者はシェーダー、descriptor、GPU buffer を用意しません。

```csharp
var geometry = new ModelGeometryData(new("triangle", 0), ModelTopology.Triangles,
    new ModelVertexData(new ModelVertexAttributeData<Vector3>(new("positions", 0),
        [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)])));
var material = new ModelMaterialData(new("material", 0))
{
    Unlit = true,
    BaseColorFactor = new(1, .3f, .1f, 1),
};
var draw = new ModelDrawItem(geometry, material, Matrix4x4.Identity);
var draws = new ModelDrawList();
var moving = draws.Add(draw);
var camera = ModelCamera.Perspective(new(0, 0, 3), Vector3.Zero,
    Vector3.UnitY, MathF.PI / 3, .1f, 100);

var graph = new GpuRenderGraph();
var input = graph.CreateInput("model", ModelRenderInputContract.Instance);
var color = graph.CreateTexture("hdr", new(640, 480, GpuFormat.Rgba16Float));
var depth = graph.CreateTexture("depth", new(640, 480, GpuFormat.D32Float));
graph.AddModelPass("model", new(input, color, depth));
graph.ExportTexture(color);
var plan = graph.Compile();

// Subsequent frames reuse the plan and unchanged geometry.
draws.Set(moving, draw with { LocalToWorld = Matrix4x4.CreateRotationY(angle) });
var bindings = plan.CreateBindings();
bindings.Set(input, new(camera, draws.Snapshot(), ModelLighting.Empty));
using var execution = await runtime.SubmitAsync(plan, bindings.Build());
```

入力は constructor でコピー・固定します。編集済みの世代には新しい `GpuUploadDataKey` を付け、
変わらない属性・index・material は同じオブジェクトを共有してください。
`WithRange` は完全な新しい属性値を作り、以前の snapshot を変更しません。
`ModelDrawList` は呼出し側で直列に編集し、snapshot は提出後も利用できます。

現在は triangle list／strip／fan、indexed／non-indexed、頂点色、テクスチャなし PBR／Unlit、
directional／point／spot、CPU skin／morph、Mask／Blend と深度に対応します。
PBR に light を渡さない場合、emissive 以外は黒です。隠れた ambient light はありません。

material texture、IBL、line／point、mesh shader、部品ごとの GPU 部分転送と draw batch の差分更新は未実装です。
glTF 等のロードは `Lumyte.Resources` が担当し、このライブラリには含めません。
API の目標と未実装項目は [ADR 0035](../../../../docs/adr/0035-model-render-passes.md) を参照してください。
