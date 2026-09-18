using System.Numerics;
using System.Text.Json;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

internal static class KhronosModelReference
{
    internal static ModelFixture Create(string scenario)
    {
        // This is an offline-converted upload fixture, not a glTF document or loader.
        using var stream = typeof(KhronosModelReference).Assembly.GetManifestResourceStream("Lumyte.Graphics.Conformance.BoxVertexColors.json")!;
        using var fixture = JsonDocument.Parse(stream);
        var root = fixture.RootElement;
        static Vector3 V3(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        static Vector4 V4(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle());
        var vertices = new ModelVertexData(new(new("khronos positions", 0), root.GetProperty("positions").EnumerateArray().Select(V3)),
            normals: new(new("khronos normals", 0), root.GetProperty("normals").EnumerateArray().Select(V3)),
            colors: [new(0, new(new("khronos colors", 0), root.GetProperty("colors").EnumerateArray().Select(V4)))]);
        var indices = new ModelIndexData(new("khronos indices", 0), root.GetProperty("indices").EnumerateArray().Select(value => value.GetUInt32()));
        var geometry = new ModelGeometryData(new("Khronos BoxVertexColors", 0), ModelTopology.Triangles, vertices, indices);
        var world = scenario == "khronos-box-reflected" ? Matrix4x4.CreateScale(-1, 1, 1) * Matrix4x4.CreateTranslation(1, 0, 0) : Matrix4x4.Identity;
        var draw = new ModelDrawItem(geometry, new(new("glTF default material", 0)), world);
        Vector3 outward = scenario switch
        {
            "khronos-box-back" => -Vector3.UnitZ,
            "khronos-box-right" => Vector3.UnitX,
            "khronos-box-left" => -Vector3.UnitX,
            _ => Vector3.UnitZ,
        };
        var center = new Vector3(.5f);
        var camera = ModelCamera.Orthographic(center + outward * 3, center, Vector3.UnitY, 1, .1f, 10);
        var snapshot = new ModelRenderSnapshot(camera, ModelDrawSnapshot.From([draw]), new([ModelLight.Directional(-outward, Vector3.One, MathF.PI)]));
        var expected = new Vector4[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float u = (x + .5f) / 32, v = 1 - (y + .5f) / 32;
                // The asset defines RGB = object-space XYZ. At normal incidence,
                // its default metallic roughness-one BRDF returns F0 / 4.
                var color = scenario switch
                {
                    "khronos-box-back" => new Vector3(1 - u, v, 0),
                    "khronos-box-right" => new Vector3(1, v, 1 - u),
                    "khronos-box-left" => new Vector3(0, v, u),
                    "khronos-box-reflected" => new Vector3(1 - u, v, 1),
                    _ => new Vector3(u, v, 1),
                };
                expected[y * 32 + x] = new(color / 4, 1);
            }
        }
        var graph = new GpuRenderGraph(); var input = graph.CreateInput("model", ModelRenderInputContract.Instance, snapshot);
        var colorTarget = graph.CreateTexture("color", new(32, 32, GpuFormat.Rgba16Float));
        var depth = graph.CreateTexture("depth", new(32, 32, GpuFormat.D32Float));
        graph.AddModelPass("model", new(input, colorTarget, depth)); graph.ExportTexture(colorTarget);
        return new(graph.Compile(), colorTarget, input, snapshot, default, false) { ExpectedPixels = expected };
    }
}
