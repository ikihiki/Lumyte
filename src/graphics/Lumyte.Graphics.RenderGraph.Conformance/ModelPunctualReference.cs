using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

internal static class ModelPunctualReference
{
    internal static ModelFixture Create(string scenario)
    {
        var original = ModelRenderConsumer.Create("indexed");
        var draw = original.Snapshot.Draws.Items.Single();
        draw = draw with { Material = draw.Material with { Unlit = false, MetallicFactor = 0, RoughnessFactor = 1 } };
        bool spot = scenario == "punctual-spot";
        float? range = scenario == "punctual-range" ? 2.5f : null;
        var light = spot ? ModelLight.Spot(new(0, 0, 2), -Vector3.UnitZ, Vector3.One, 4 * MathF.PI, innerAngle: .2f, outerAngle: .5f)
            : ModelLight.Point(new(0, 0, 2), Vector3.One, 4 * MathF.PI, range);
        var snapshot = original.Snapshot with { Draws = ModelDrawSnapshot.From([draw]), Lighting = new([light]) };
        var expected = new Vector4[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                double px = 2 * (x + .5) / 32 - 1, py = 1 - 2 * (y + .5) / 32;
                double distance = Math.Sqrt(px * px + py * py + 4), cosine = 2 / distance;
                double fresnel = .04 + .96 * Math.Pow(1 - Math.Sqrt((1 + cosine) / 2), 5);
                double diffuse = 1 - fresnel, specular = fresnel / (2 * (1 + cosine));
                double attenuation = 4 * cosine / (distance * distance);
                if (range is { } radius) { attenuation *= Math.Max(0, 1 - Math.Pow(distance / radius, 4)); }
                if (spot) { attenuation *= Math.Pow(Math.Clamp((cosine - Math.Cos(.5)) / (Math.Cos(.2) - Math.Cos(.5)), 0, 1), 2); }
                var rgb = new Vector3(.25f, .5f, .75f) * (float)(diffuse * attenuation) + new Vector3((float)(specular * attenuation));
                expected[y * 32 + x] = new(rgb, 1);
            }
        }
        var graph = new GpuRenderGraph(); var input = graph.CreateInput("model", ModelRenderInputContract.Instance, snapshot);
        var color = graph.CreateTexture("color", new(32, 32, GpuFormat.Rgba16Float));
        var depth = graph.CreateTexture("depth", new(32, 32, GpuFormat.D32Float));
        graph.AddModelPass("model", new(input, color, depth)); graph.ExportTexture(color);
        return new(graph.Compile(), color, input, snapshot, default, false) { ExpectedPixels = expected };
    }
}
