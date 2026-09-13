namespace Lumyte.Graphics.Portable.Shaders.Tests.Programs;

public sealed class PortableShaderLoaderTests
{
    [Fact]
    public void LoadConnectsPreparedMetadataToOneStablePipelineDescription()
    {
        var backend = new TestBackend();
        var first = new GpuBindingLayoutEntry(2, GpuShaderStage.Compute, new GpuBufferBindingLayout());
        var second = new GpuBindingLayoutEntry(9, GpuShaderStage.Compute, new GpuSamplerBindingLayout());
        var root = new PortableShaderDataLayout("Root", 32, 16, [new("Value", "vec3<f32>", 16, 12, 16)]);
        var parameter = new PortableShaderDataLayout("Parameters", 64, 16);
        var schema = new PortableShaderBindingSchema([new("Values", 0, 2, GpuBindingLayoutKind.Buffer)]);
        var package = new PortableShaderPackage(1, "prepared source", [new(GpuShaderStage.Compute, "computeMain")],
            PortableShaderFeatures.ImmediateAddressSpace, [new([first]), new([second])], root, [parameter], schema, "abi");

        using PortableShaderProgram program = new PortableShaderLoader(backend).Load(package, "abi");
        GpuShaderProgramDescription description = program.Description;

        Assert.Same(package, program.Package);
        Assert.Equal(PortableShaderProgramKind.Compute, program.Kind);
        Assert.Same(description, program.Description);
        Assert.Equal(new GpuShaderEntryPoint(Assert.Single(backend.Modules), GpuShaderStage.Compute, "computeMain"),
            Assert.Single(program.Description.EntryPoints));
        Assert.Equal("prepared source", backend.Modules[0].Source);
        Assert.Collection(program.BindingLayouts,
            layout => Assert.Equal(first, Assert.Single(Assert.IsType<TestBackend.Layout>(layout).Entries)),
            layout => Assert.Equal(second, Assert.Single(Assert.IsType<TestBackend.Layout>(layout).Entries)));
        Assert.Equal(32u, program.ImmediateSize);
        Assert.Same(root, program.RootLayout);
        Assert.Same(parameter, Assert.Single(program.ParameterLayouts));
        Assert.Same(schema, program.BindingSchema);
        Assert.Equal("abi", program.AbiHash);
    }

    [Theory]
    [InlineData(GpuShaderStage.Compute, PortableShaderProgramKind.Compute)]
    [InlineData(GpuShaderStage.Vertex, PortableShaderProgramKind.Raster)]
    [InlineData(GpuShaderStage.Vertex | GpuShaderStage.Pixel, PortableShaderProgramKind.Raster)]
    public void LoadPreservesSupportedProgramStageComposition(GpuShaderStage stages, PortableShaderProgramKind kind)
    {
        var backend = new TestBackend();
        PortableShaderEntryPoint[] entries = stages == (GpuShaderStage.Vertex | GpuShaderStage.Pixel)
            ? [new(GpuShaderStage.Pixel, "pixel"), new(GpuShaderStage.Vertex, "vertex")]
            : [new(stages, "main")];

        using PortableShaderProgram program = new PortableShaderLoader(backend).Load(Package(entries: entries));

        Assert.Equal(kind, program.Kind);
        Assert.Equal(entries, program.EntryPoints.Select(entry => new PortableShaderEntryPoint(entry.Stage, entry.Name)));
    }

    [Theory]
    [InlineData(GpuShaderStage.None)]
    [InlineData(GpuShaderStage.Pixel)]
    [InlineData(GpuShaderStage.Vertex | GpuShaderStage.Compute)]
    public void InvalidPackageCompositionFailsBeforeGpuCreation(GpuShaderStage stage)
    {
        var backend = new TestBackend();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new PortableShaderLoader(backend).Load(Package(entries: [new(stage, "main")])));

        Assert.Equal("package", error.ParamName);
        Assert.Empty(backend.Modules);
    }

    [Fact]
    public void RepeatedEntryStageFailsBeforeGpuCreation()
    {
        var backend = new TestBackend();

        Assert.Throws<ArgumentException>(() => new PortableShaderLoader(backend).Load(
            Package(entries: [new(GpuShaderStage.Compute, "first"), new(GpuShaderStage.Compute, "second")])));

        Assert.Empty(backend.Modules);
    }

    [Theory]
    [InlineData(PortableShaderFeatures.ImmediateAddressSpace)]
    [InlineData(PortableShaderFeatures.DualSourceBlend)]
    [InlineData((PortableShaderFeatures)128)]
    public void UnsupportedRequiredFeaturesFailBeforeGpuCreation(PortableShaderFeatures features)
    {
        var backend = new TestBackend { Capabilities = default };

        Assert.Throws<NotSupportedException>(() => new PortableShaderLoader(backend).Load(Package(features: features)));

        Assert.Empty(backend.Modules);
    }

    [Fact]
    public void RootMetadataCannotBypassDirectInputRequirement()
    {
        var backend = new TestBackend { Capabilities = default };

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new PortableShaderLoader(backend).Load(Package(root: new("Root", 4, 4))));

        Assert.Contains("direct root data", error.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Modules);
    }

    [Fact]
    public void RootSizeUsesEffectiveDeviceLimitWithoutAllocatingFallbackStorage()
    {
        var backend = new TestBackend { Limits = new() { MaxImmediateSize = 16 } };

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new PortableShaderLoader(backend).Load(Package(root: new("Root", 32, 16))));

        Assert.Contains("device limit 16", error.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Modules);
    }

    [Fact]
    public void RootlessProgramNeedsNoDirectInputCapability()
    {
        var backend = new TestBackend { Capabilities = default, Limits = new() };

        using PortableShaderProgram program = new PortableShaderLoader(backend).Load(Package());

        Assert.Equal(0u, program.ImmediateSize);
        Assert.Null(program.RootLayout);
    }

    [Fact]
    public void UnsupportedPackageVersionFailsBeforeGpuCreation()
    {
        var backend = new TestBackend();

        Assert.Throws<NotSupportedException>(() => new PortableShaderLoader(backend).Load(Package(version: 99)));

        Assert.Empty(backend.Modules);
    }

    [Fact]
    public void MismatchedGeneratedAbiFailsBeforeGpuCreation()
    {
        var backend = new TestBackend();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new PortableShaderLoader(backend).Load(Package(), "different-abi"));

        Assert.Equal("package", error.ParamName);
        Assert.Contains("ABI hash", error.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Modules);
    }

    [Fact]
    public void ShaderAndBindingLegalityAreForwardedToBackendWithoutOwnValidation()
    {
        var backend = new TestBackend();
        var package = new PortableShaderPackage(1, "not legal WGSL", [new(GpuShaderStage.Compute, "missingEntry")],
            PortableShaderFeatures.None, [new([default, default])], null, [], new([]), "abi");

        using PortableShaderProgram program = new PortableShaderLoader(backend).Load(package);

        Assert.Equal("not legal WGSL", Assert.Single(backend.Modules).Source);
        Assert.Equal(new GpuBindingLayoutEntry[] { default, default }, Assert.Single(backend.Layouts).Entries);
    }

    [Fact]
    public void InconsistentSemanticBindingMetadataFailsBeforeGpuCreation()
    {
        var backend = new TestBackend();
        var package = new PortableShaderPackage(1, "", [new(GpuShaderStage.Compute, "main")], PortableShaderFeatures.None,
            [new([new(2, GpuShaderStage.Compute, new GpuBufferBindingLayout())])], null, [],
            new([new("Input", 0, 2, GpuBindingLayoutKind.Texture)]), "abi");

        ArgumentException error = Assert.Throws<ArgumentException>(() => new PortableShaderLoader(backend).Load(package));

        Assert.Equal("package", error.ParamName);
        Assert.Contains("Input", error.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Modules);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedGroupCreationReleasesOnlyPreviouslyCreatedObjects(int failedGroup)
    {
        var backend = new TestBackend { FailingLayout = failedGroup };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PortableShaderLoader(backend).Load(Package(groups: [new([]), new([]), new([])])));

        Assert.Same(backend.LayoutCreationError, error);
        TestBackend.Release[] expected = Enumerable.Range(0, failedGroup).Reverse()
            .Select(index => new TestBackend.Release("layout", index)).Append(new("module", 0)).ToArray();
        Assert.Equal(expected, backend.Releases);
        Assert.False(backend.Disposed);
    }

    [Fact]
    public void FailedModuleCreationDoesNotDestroyUncreatedObjects()
    {
        var failure = new InvalidOperationException("Module creation failed.");
        var backend = new TestBackend { ModuleCreationError = failure };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new PortableShaderLoader(backend).Load(Package()));

        Assert.Same(failure, error);
        Assert.Empty(backend.Releases);
    }

    [Fact]
    public void RollbackFailurePreservesInitializationErrorAndStillReleasesModule()
    {
        var backend = new TestBackend { FailingLayout = 1, FailingLayoutRelease = 0 };

        AggregateException error = Assert.Throws<AggregateException>(() =>
            new PortableShaderLoader(backend).Load(Package(groups: [new([]), new([])])));

        Assert.Collection(error.InnerExceptions,
            value => Assert.Same(backend.LayoutCreationError, value), value => Assert.Same(backend.LayoutReleaseError, value));
        Assert.Equal(new TestBackend.Release[] { new("layout", 0), new("module", 0) }, backend.Releases);
    }

    internal static PortableShaderPackage Package(uint version = 1, PortableShaderFeatures features = PortableShaderFeatures.None,
        PortableShaderDataLayout? root = null, PortableShaderEntryPoint[]? entries = null, PortableShaderGroupLayout[]? groups = null)
        => new(version, "source", entries ?? [new(GpuShaderStage.Compute, "main")], features, groups ?? [], root, [], new([]), "abi");
}
