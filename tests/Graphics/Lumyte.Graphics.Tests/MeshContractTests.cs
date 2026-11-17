using System.Text.Json.Nodes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks deterministic mesh declarations, dispatch limits and stage compatibility.</summary>
public sealed class MeshContractTests
{
    /// <summary>Preserves old stage values and old embedded aggregate semantics.</summary>
    [Fact]
    public void StageValuesAndAggregatesRemainCompatible()
    {
        Assert.Equal(0, (int)ShaderStage.Vertex);
        Assert.Equal(1, (int)ShaderStage.Fragment);
        Assert.Equal(2, (int)ShaderStage.Compute);
        Assert.Equal((PipelineStage)440, MeshShaderValidation.NormalizeStages((PipelineStage)440, GraphicsFeatures.None));
        Assert.Equal((PipelineStage)440, MeshShaderValidation.NormalizeStages(PipelineStage.AllGraphics, GraphicsFeatures.None));
        Assert.Equal(PipelineStage.AllGraphics & ~PipelineStage.TaskShader, MeshShaderValidation.NormalizeStages(PipelineStage.AllGraphics, GraphicsFeatures.MeshShader));
        Assert.Throws<NotSupportedException>(() => MeshShaderValidation.NormalizeStages(PipelineStage.MeshShader, GraphicsFeatures.None));
        Assert.Throws<NotSupportedException>(() => MeshShaderValidation.NormalizeStages(PipelineStage.TaskShader | PipelineStage.Copy, GraphicsFeatures.MeshShader));
    }

    /// <summary>Rejects axis, product and multiplication overflow without wrapping.</summary>
    [Fact]
    public void DispatchLimitsRejectZeroAndOverflow()
    {
        var limits = new ShaderWorkgroupLimits { MaxGroupCountX = uint.MaxValue, MaxGroupCountY = uint.MaxValue, MaxGroupCountZ = uint.MaxValue, MaxTotalGroupCount = ulong.MaxValue };
        MeshShaderValidation.Dispatch(limits, uint.MaxValue, uint.MaxValue, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshShaderValidation.Dispatch(limits, uint.MaxValue, uint.MaxValue, uint.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshShaderValidation.Dispatch(limits, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshShaderValidation.Dispatch(limits, 1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshShaderValidation.Dispatch(limits, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshShaderValidation.Dispatch(limits with { MaxTotalGroupCount = 10 }, 3, 2, 2));
    }

    /// <summary>Requires complete metadata and exact compilation-stage agreement.</summary>
    [Fact]
    public void MetadataCannotBeMissingOrContradictory()
    {
        ShaderTargetData data = Data();
        Assert.Equal("triangle", MeshShaderMetadata.Read(data).GetProperty("outputTopology").GetString());
        Assert.Throws<ArgumentException>(() => MeshShaderMetadata.Read(data with { ReflectionJson = "{}" }));
        Assert.Throws<ArgumentException>(() => MeshShaderMetadata.Read(data with { Stage = ShaderStage.Task }));
        Assert.Throws<ArgumentException>(() => MeshShaderMetadata.Read(data with { Target = ShaderTarget.Wgsl }));
        JsonObject root = JsonNode.Parse(data.ReflectionJson)!.AsObject();
        root["lumyteMesh"]!["workgroupSize"] = new JsonArray(0, 1, 1);
        Assert.Throws<ArgumentException>(() => MeshShaderMetadata.Read(data with { ReflectionJson = root.ToJsonString() }));
    }

    /// <summary>Checks exact memory sums, stage support and output requirements.</summary>
    [Fact]
    public void DeviceLimitsAndUnsupportedPointAreEnforced()
    {
        ShaderTargetData data = Data();
        DeviceCaps caps = Caps();
        MeshShaderValidation.Validate(data, caps);
        Assert.Throws<NotSupportedException>(() => MeshShaderValidation.Validate(data, caps with { Features = GraphicsFeatures.None }));
        Assert.Throws<NotSupportedException>(() => MeshShaderValidation.Validate(data, caps with { MeshShaderLimits = caps.MeshShaderLimits! with { MaxOutputVertices = 2 } }));
        Assert.Throws<NotSupportedException>(() => MeshShaderValidation.Validate(data, caps with { MeshShaderLimits = caps.MeshShaderLimits! with { MaxOutputMemoryBytes = 47 } }));
        JsonObject root = JsonNode.Parse(data.ReflectionJson)!.AsObject();
        root["lumyteMesh"]!["outputTopology"] = "point";
        Assert.Throws<NotSupportedException>(() => MeshShaderValidation.Validate(data with { ReflectionJson = root.ToJsonString() }, caps));
    }

    /// <summary>Rejects mismatched payloads and payloads without a producer.</summary>
    [Fact]
    public void PayloadLinkageRequiresProducerAndMatchingAbi()
    {
        ShaderTargetData mesh = Data();
        MeshShaderValidation.Program(mesh, null, null);
        JsonObject root = JsonNode.Parse(mesh.ReflectionJson)!.AsObject();
        root["lumyteMesh"]!["payloadBytes"] = 4;
        root["lumyteMesh"]!["payloadAbi"] = "uint@0";
        Assert.Throws<ArgumentException>(() => MeshShaderValidation.Program(mesh with { ReflectionJson = root.ToJsonString() }, null, null));
    }

    /// <summary>Rejects a code stream with no matching Mesh entry even when JSON claims complete requirements.</summary>
    [Fact]
    public void SpirVCodeCannotContradictMetadata()
    {
        ShaderTargetData data = Data();
        byte[] code = new byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(code, 0x07230203);
        Assert.Throws<ArgumentException>(() => MeshShaderMetadata.Read(data with { Target = ShaderTarget.SpirV, Code = code }));
    }

    private static DeviceCaps Caps() => new()
    {
        ShaderTarget = ShaderTarget.Dxil,
        Features = GraphicsFeatures.MeshShader,
        MeshShaderLimits = new()
        {
            Workgroup = new() { MaxSizeX = 128, MaxSizeY = 128, MaxSizeZ = 128, MaxInvocations = 128 },
            MaxOutputVertices = 3,
            MaxOutputPrimitives = 1,
            MaxSharedMemoryBytes = 1024,
            MaxPayloadBytes = 1024,
            MaxPayloadAndSharedMemoryBytes = 1024,
            MaxOutputMemoryBytes = 48,
            MaxPayloadAndOutputMemoryBytes = 1024,
        },
    };

    private static ShaderTargetData Data()
    {
        const string Reflection = "{\"parameters\":[],\"entryPoints\":[{\"name\":\"main\",\"stage\":\"mesh\"}],\"lumyteMesh\":{\"version\":1,\"stage\":\"mesh\",\"entryPoint\":\"main\",\"requiredFeatures\":8,\"workgroupSize\":[1,1,1],\"sharedMemoryBytes\":0,\"payloadBytes\":0,\"payloadAbi\":\"\",\"outputTopology\":\"triangle\",\"maxOutputVertices\":3,\"maxOutputPrimitives\":1,\"outputMemoryBytes\":48,\"outputs\":[]}}";
        return new(ShaderTarget.Dxil, ShaderStage.Mesh, "main", "test", "row-major", [], Reflection);
    }
}
