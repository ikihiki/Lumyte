namespace Lumyte.Graphics.Portable.Tests.Shaders;

public sealed class GpuShaderProgramDescriptionTests
{
    [Fact]
    public void ProgramOwnsItsEntryAndGroupOrderAfterSourceArraysChange()
    {
        var module = new ExternalModule();
        var first = new ExternalLayout();
        var second = new ExternalLayout();
        var entry = new GpuShaderEntryPoint(module, GpuShaderStage.Compute, "computeMain");
        GpuShaderEntryPoint[] entries = [entry];
        GpuBindingLayoutHandle[] layouts = [first, second];

        var description = new GpuShaderProgramDescription(entries, layouts, 32);
        entries[0] = new(new ExternalModule(), GpuShaderStage.Compute, "replacement");
        Array.Reverse(layouts);

        Assert.Equal(entry, Assert.Single(description.EntryPoints));
        Assert.Collection(description.BindingLayouts,
            value => Assert.Same(first, value), value => Assert.Same(second, value));
        Assert.Equal(32u, description.ImmediateSize);
    }

    [Fact]
    public void ProgramCollectionsCannotBeMutatedThroughTheirCollectionInterfaces()
    {
        var entry = new GpuShaderEntryPoint(new ExternalModule(), GpuShaderStage.Compute, "main");
        var description = new GpuShaderProgramDescription([entry], [new ExternalLayout()]);

        Assert.Throws<NotSupportedException>(() => ((IList<GpuShaderEntryPoint>)description.EntryPoints)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<GpuBindingLayoutHandle>)description.BindingLayouts).Clear());
    }

    private sealed class ExternalModule : GpuShaderModuleHandle;
    private sealed class ExternalLayout : GpuBindingLayoutHandle;
}
