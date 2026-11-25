using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class MeshCoverageService2D(IGraphicDevice device, MeshCoverageOptions2D options, Func<string, ShaderArtifact>? loadArtifact = null) : PipelineSharedService2D
{
    // The production source is this loaded assembly's immutable embedded artifact set.
    // Keep only validated scalars; neither target code nor mutable payloads escape here.
    private readonly object _requirementsGate = new();
    private readonly Dictionary<ShaderTarget, Requirements[]> _requirements = [];

    internal MeshCoverageOptions2D Options => options;

    internal DrawRecordResources2D Resources { get; } = new(device, options.NumericPoolBytes, mesh: true);

    public override void Dispose() => Resources.Dispose();

    internal bool Supports(DeviceCaps caps)
    {
        if ((caps.Features & GraphicsFeatures.MeshShader) == 0 || caps.ShaderTarget == ShaderTarget.Wgsl || caps.MeshShaderLimits is not { } limits)
        {
            return false;
        }

        foreach (Requirements requirement in GetRequirements(caps.ShaderTarget))
        {
            (uint x, uint y, uint z, uint vertices, uint primitives, ulong shared, ulong payload, ulong output) = requirement;
            ShaderWorkgroupLimits w = limits.Workgroup;
            if (x > w.MaxSizeX || y > w.MaxSizeY || z > w.MaxSizeZ || (ulong)x * y > w.MaxInvocations / z
                || w.MaxGroupCountX == 0 || w.MaxGroupCountY == 0 || w.MaxGroupCountZ == 0 || w.MaxTotalGroupCount == 0
                || vertices > limits.MaxOutputVertices
                || primitives > limits.MaxOutputPrimitives
                || shared > limits.MaxSharedMemoryBytes || payload > limits.MaxPayloadBytes
                || payload > limits.MaxPayloadAndSharedMemoryBytes || shared > limits.MaxPayloadAndSharedMemoryBytes - payload
                || output > limits.MaxOutputMemoryBytes || payload > limits.MaxPayloadAndOutputMemoryBytes || output > limits.MaxPayloadAndOutputMemoryBytes - payload)
            {
                return false;
            }
        }

        return true;
    }

    private static ShaderArtifact LoadEmbedded(string name) => ShaderArtifact.LoadEmbedded(typeof(MeshCoverageService2D).Assembly, name);

    private Requirements[] GetRequirements(ShaderTarget target)
    {
        lock (_requirementsGate)
        {
            if (_requirements.TryGetValue(target, out Requirements[]? existing))
            {
                return existing;
            }

            // Publish only a complete validated snapshot. Failed acquisition is retried
            // on the next admission, matching the previous non-cached failure behavior.
            string[] variants = [string.Empty, "-basic", "-mask", "-image", "-image-small", "-image-medium", "-solid"];
            var requirements = new Requirements[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                ShaderArtifact artifact = (loadArtifact ?? LoadEmbedded)("Lumyte.Drawing2D.draw-records" + variants[i] + "-mesh.lshader");
                JsonElement metadata = MeshShaderMetadata.Read(artifact.GetTarget(target));
                JsonElement size = metadata.GetProperty("workgroupSize");
                requirements[i] = new(
                    size[0].GetUInt32(),
                    size[1].GetUInt32(),
                    size[2].GetUInt32(),
                    metadata.GetProperty("maxOutputVertices").GetUInt32(),
                    metadata.GetProperty("maxOutputPrimitives").GetUInt32(),
                    metadata.GetProperty("sharedMemoryBytes").GetUInt64(),
                    metadata.GetProperty("payloadBytes").GetUInt64(),
                    metadata.GetProperty("outputMemoryBytes").GetUInt64());
            }

            _requirements.Add(target, requirements);
            return requirements;
        }
    }

    private readonly record struct Requirements(uint X, uint Y, uint Z, uint Vertices, uint Primitives, ulong Shared, ulong Payload, ulong Output);
}
