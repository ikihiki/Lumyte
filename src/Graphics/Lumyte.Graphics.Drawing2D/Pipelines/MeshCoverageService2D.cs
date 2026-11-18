using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class MeshCoverageService2D(IGraphicDevice device, MeshCoverageOptions2D options) : PipelineSharedService2D
{
    internal MeshCoverageOptions2D Options => options;

    internal DrawRecordResources2D Resources { get; } = new(device, options.NumericPoolBytes, mesh: true);

    public override void Dispose() => Resources.Dispose();

    internal bool Supports(DeviceCaps caps)
    {
        if ((caps.Features & GraphicsFeatures.MeshShader) == 0 || caps.ShaderTarget == ShaderTarget.Wgsl || caps.MeshShaderLimits is not { } limits)
        {
            return false;
        }

        foreach (string variant in new[] { string.Empty, "-basic", "-mask", "-image", "-image-small", "-image-medium", "-solid" })
        {
            var artifact = ShaderArtifact.LoadEmbedded(typeof(MeshCoverageService2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-mesh.lshader");
            JsonElement metadata = MeshShaderMetadata.Read(artifact.GetTarget(caps.ShaderTarget));
            JsonElement size = metadata.GetProperty("workgroupSize");
            ulong shared = metadata.GetProperty("sharedMemoryBytes").GetUInt64();
            ulong payload = metadata.GetProperty("payloadBytes").GetUInt64();
            ulong output = metadata.GetProperty("outputMemoryBytes").GetUInt64();
            ShaderWorkgroupLimits w = limits.Workgroup;
            uint x = size[0].GetUInt32();
            uint y = size[1].GetUInt32();
            uint z = size[2].GetUInt32();
            if (x > w.MaxSizeX || y > w.MaxSizeY || z > w.MaxSizeZ || (ulong)x * y > w.MaxInvocations / z
                || w.MaxGroupCountX == 0 || w.MaxGroupCountY == 0 || w.MaxGroupCountZ == 0 || w.MaxTotalGroupCount == 0
                || metadata.GetProperty("maxOutputVertices").GetUInt32() > limits.MaxOutputVertices
                || metadata.GetProperty("maxOutputPrimitives").GetUInt32() > limits.MaxOutputPrimitives
                || shared > limits.MaxSharedMemoryBytes || payload > limits.MaxPayloadBytes
                || payload > limits.MaxPayloadAndSharedMemoryBytes || shared > limits.MaxPayloadAndSharedMemoryBytes - payload
                || output > limits.MaxOutputMemoryBytes || payload > limits.MaxPayloadAndOutputMemoryBytes || output > limits.MaxPayloadAndOutputMemoryBytes - payload)
            {
                return false;
            }
        }

        return true;
    }
}
