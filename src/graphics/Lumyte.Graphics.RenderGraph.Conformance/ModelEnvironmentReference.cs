using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

internal static class ModelEnvironmentReference
{
    internal static ModelFixture Changed(ModelFixture original)
    {
        var environment = original.Snapshot.Lighting.Environment! with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI), Intensity = 2 };
        var color = Create("ibl-rotation").Expected;
        return original with { Snapshot = original.Snapshot with { Lighting = new([], environment) }, Expected = new(color.X * 2, color.Y * 2, color.Z * 2, 1) };
    }
    internal static ModelFixture Create(string scenario)
    {
        var original = ModelRenderConsumer.Create("indexed");
        var draw = original.Snapshot.Draws.Items.Single();
        bool gradient = scenario is "ibl-gradient" or "ibl-rotation" or "ibl-retained";
        int width = gradient ? 64 : 1, height = gradient ? 32 : 1;
        var bytes = new byte[width * height * 8];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float z = MathF.Sin((y + .5f) / height * MathF.PI) * MathF.Sin(((x + .5f) / width - .5f) * MathF.Tau);
                Vector4 color = gradient ? new(1 + .5f * z, .5f, .25f, 1) : scenario == "ibl-hdr" ? new(4, 2, 1, 1) : Vector4.One;
                for (int channel = 0; channel < 4; channel++)
                { BitConverter.TryWriteBytes(bytes.AsSpan((y * width + x) * 8 + channel * 2, 2), (Half)color[channel]); }
            }
        }
        var image = new GpuImageUploadData(new("environment", 0), new((uint)width, (uint)height, GpuFormat.Rgba16Float),
            GpuImageColorEncoding.Linear, GpuImageAlphaMode.Opaque, [new(0, 0, (ulong)width * 8, (ulong)bytes.Length, bytes)]);
        float intensity = scenario == "ibl-disabled" ? 0 : scenario == "ibl-intensity" ? 2 : 1;
        var rotation = scenario == "ibl-rotation" ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) : Quaternion.Identity;
        var material = draw.Material with { Unlit = scenario == "ibl-unlit", MetallicFactor = scenario == "ibl-metal" ? 1 : 0, RoughnessFactor = 1 };
        bool occluded = scenario is "ibl-occlusion" or "ibl-ao-strength" or "ibl-ao-direct";
        if (occluded)
        {
            var ao = new GpuImageUploadData(new("occlusion", 0), new(1, 1, GpuFormat.Rgba8Unorm), GpuImageColorEncoding.Srgb,
                GpuImageAlphaMode.Opaque, [new(0, 0, 4, 4, new byte[] { 0, 255, 255, 255 })]);
            material = material with { OcclusionTexture = new(new(ao, new())), OcclusionStrength = scenario == "ibl-ao-strength" ? .5f : 1 };
            draw = draw with { Geometry = draw.Geometry with { Vertices = new(draw.Geometry.Vertices.Positions,
                texCoords: [new(0, new(new("uv", 0), Enumerable.Repeat(Vector2.Zero, 4)))]) } };
        }
        if (scenario == "ibl-ao-direct") { material = material with { EmissiveFactor = new(.1f, .2f, .3f) }; }
        draw = draw with { Material = material };
        ModelLight[] lights = scenario == "ibl-ao-direct" ? [ModelLight.Directional(-Vector3.UnitZ, Vector3.One, MathF.PI)] : [];
        var snapshot = original.Snapshot with { Draws = ModelDrawSnapshot.From([draw]), Lighting = new(lights, new(image, rotation, intensity)) };

        // Independent hemisphere integral for correlated Smith GGX at roughness one.
        // A + B = 1 - ln(2); integrate only Schlick's B term using uniform solid angle.
        double fresnelIntegral = 0;
        for (int i = 0; i < 65536; i++)
        {
            double nl = (i + .5) / 65536, vh = Math.Sqrt((1 + nl) / 2);
            fresnelIntegral += nl / (1 + nl) * Math.Pow(1 - vh, 5) / 65536;
        }
        float b = (float)fresnelIntegral, a = 1 - MathF.Log(2) - b;
        var baseRgb = new Vector3(material.BaseColorFactor.X, material.BaseColorFactor.Y, material.BaseColorFactor.Z);
        var f0 = Vector3.Lerp(new(.04f), baseRgb, material.MetallicFactor);
        var environment = gradient ? new Vector3(1 + (scenario == "ibl-rotation" ? -1 : 1) / 3f, .5f, .25f)
            : scenario == "ibl-hdr" ? new Vector3(4, 2, 1) : Vector3.One;
        var expected = ((Vector3.One - f0) * (1 - material.MetallicFactor) * baseRgb + f0 * a + new Vector3(b)) * environment * intensity;
        if (occluded) { expected *= 1 - material.OcclusionStrength; }
        if (scenario == "ibl-ao-direct") { expected += baseRgb * .96f + new Vector3(.01f) + material.EmissiveFactor; }
        if (material.Unlit) { expected = baseRgb; }
        var graph = new GpuRenderGraph(); var input = graph.CreateInput("model", ModelRenderInputContract.Instance, snapshot);
        var colorTarget = graph.CreateTexture("color", new(32, 32, GpuFormat.Rgba16Float));
        var depth = graph.CreateTexture("depth", new(32, 32, GpuFormat.D32Float));
        graph.AddModelPass("model", new(input, colorTarget, depth)); graph.ExportTexture(colorTarget);
        return new(graph.Compile(), colorTarget, input, snapshot, new(expected, 1), false);
    }
}
