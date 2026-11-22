using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Checks CPU-only graphics foundation contracts without emulating a GPU.</summary>
public sealed class FoundationTests
{
    /// <summary>Checks all explicit policy boundaries.</summary>
    [Fact]
    public void PolicyRejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsPipelinePolicy(-1, 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsPipelinePolicy(0, -1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsPipelinePolicy(0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsPipelinePolicy(0, 0, 0, 0));
        Assert.Equal(1, new GraphicsPipelinePolicy(0, 0, 0, 1).MaxEpochs);
    }

    /// <summary>Checks immutable validated artifact keys.</summary>
    [Fact]
    public void ArtifactKeysRejectInvalidIdentity()
    {
        Assert.Throws<ArgumentNullException>(() => new StageArtifactKey(null!, 1, 0));
        Assert.Throws<ArgumentException>(() => new StageArtifactKey(typeof(List<>), 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageArtifactKey(typeof(FoundationTests), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageArtifactKey(typeof(FoundationTests), 1, -1));
        Assert.Equal(new StageArtifactKey(typeof(FoundationTests), 1, 0), new StageArtifactKey(typeof(FoundationTests), 1, 0));
    }

    /// <summary>Checks target and hash declaration validation.</summary>
    [Fact]
    public void DescriptionRejectsInvalidIdentity()
    {
        var key = new StageArtifactKey(typeof(FoundationTests), 1, 0);
        string hash = new('A', 64);
        Assert.Throws<ArgumentException>(() => new StageArtifactDescription(key, "1", hash, hash));
        Assert.Throws<ArgumentException>(() => new StageArtifactDescription(key, "spirv", hash, hash));
        Assert.Throws<ArgumentException>(() => new StageArtifactDescription(key, "SpirV", "hash", hash));
        Assert.Throws<ArgumentException>(() => new StageArtifactDescription(key, "SpirV", hash, new string('a', 64)));
        Assert.Equal(hash, new StageArtifactDescription(key, "SpirV", hash, hash).ProgramHash);
    }

    /// <summary>Checks requirements snapshot their caller-owned collections.</summary>
    [Fact]
    public void RequirementsCaptureImmutableLists()
    {
        var key = new StageArtifactKey(typeof(FoundationTests), 1, 0);
        StageArtifactKey[] keys = [key];
        StageInputAccess[] accesses = [new(0, ResourceAccess.ShaderRead)];
        var requirements = new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 32, keys, accesses);
        keys[0] = new(typeof(string), 1, 0);
        accesses[0] = new(1, ResourceAccess.ShaderWrite);
        Assert.Equal(key, requirements.Artifacts[0]);
        Assert.Equal(0, requirements.InputAccesses[0].InputIndex);
    }

    /// <summary>Checks duplicate, conflicting and invalid accesses.</summary>
    [Fact]
    public void RequirementsRejectInvalidContracts()
    {
        var key = new StageArtifactKey(typeof(FoundationTests), 1, 0);
        Assert.Throws<ArgumentException>(() => new GraphicsStageRequirements((GraphicsExecutionDomain)99, 0, [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, -1, [], []));
        Assert.Throws<ArgumentException>(() => new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 0, [key, key], []));
        Assert.Throws<ArgumentException>(() => new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 0, [], [new(0, ResourceAccess.ShaderRead), new(0, ResourceAccess.ShaderWrite)]));
        Assert.Throws<ArgumentException>(() => new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 0, [], [new(0, ResourceAccess.ColorWrite)]));
        Assert.Throws<ArgumentException>(() => new GraphicsStageRequirements(GraphicsExecutionDomain.Upload, 0, [key], []));
        Assert.Throws<ArgumentException>(() => new StageInputAccess(0, ResourceAccess.HostRead));
        Assert.Throws<ArgumentException>(() => new StageInputAccess(0, ResourceAccess.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StageInputAccess(6, ResourceAccess.ShaderRead));
    }

    /// <summary>Checks proof issuance remains outside the public caller surface.</summary>
    [Fact]
    public void TrustBoundaryHasNoPublicProofIssuance()
    {
        Assert.Empty(typeof(GraphicsDeviceGeneration).GetConstructors());
        Assert.Empty(typeof(GraphicsDeviceRetirementProof).GetConstructors());
        Assert.Empty(typeof(GraphicsDeviceRetirementProof).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(StageArtifactBuildContext).GetConstructors());
    }
}
