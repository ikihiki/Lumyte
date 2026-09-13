namespace Lumyte.Graphics.Portable.Shaders.Tests.Packages;

public sealed class PortableShaderPackageTests
{
    [Fact]
    public void PackageRetainsNestedInputSnapshotsAfterArraysChange()
    {
        var entry = new PortableShaderEntryPoint(GpuShaderStage.Compute, "main");
        var field = new PortableShaderFieldLayout("Index", "u32", 0, 4, 4);
        var binding = new GpuBindingLayoutEntry(7, GpuShaderStage.Compute, new GpuBufferBindingLayout());
        var semantic = new PortableShaderBindingSchemaEntry("Values", 0, 7, GpuBindingLayoutKind.Buffer);
        PortableShaderEntryPoint[] entries = [entry];
        PortableShaderFieldLayout[] fields = [field];
        GpuBindingLayoutEntry[] bindings = [binding];
        PortableShaderBindingSchemaEntry[] semantics = [semantic];
        var root = new PortableShaderDataLayout("Root", 4, 4, fields);
        var parameters = new PortableShaderDataLayout("Parameters", 4, 4, fields);
        var group = new PortableShaderGroupLayout(bindings);
        PortableShaderGroupLayout[] groups = [group];
        PortableShaderDataLayout[] parameterLayouts = [parameters];
        var package = new PortableShaderPackage(1, "WGSL", entries, PortableShaderFeatures.ImmediateAddressSpace,
            groups, root, parameterLayouts, new(semantics), "abi-one");

        entries[0] = default;
        fields[0] = default;
        bindings[0] = default;
        semantics[0] = default;
        groups[0] = new([]);
        parameterLayouts[0] = new("Replacement", 0, 0);

        Assert.Equal(entry, Assert.Single(package.EntryPoints));
        Assert.Equal(binding, Assert.Single(Assert.Single(package.GroupLayouts).Entries));
        Assert.Equal(semantic, Assert.Single(package.BindingSchema.Entries));
        Assert.Equal(field, Assert.Single(package.RootLayout!.Fields));
        Assert.Same(parameters, Assert.Single(package.ParameterLayouts));
        Assert.Equal(field, Assert.Single(parameters.Fields));
    }

    [Fact]
    public void PackageCollectionsRejectMutationThroughCollectionInterfaces()
    {
        var root = new PortableShaderDataLayout("Root", 4, 4, [new("Index", "u32", 0, 4, 4)]);
        var group = new PortableShaderGroupLayout([default]);
        var schema = new PortableShaderBindingSchema([new("Input", 0, 0, GpuBindingLayoutKind.Buffer)]);
        var package = new PortableShaderPackage(1, "", [new(GpuShaderStage.Compute, "main")],
            PortableShaderFeatures.None, [group], root, [root], schema, "abi");

        Assert.Throws<NotSupportedException>(() => ((IList<PortableShaderEntryPoint>)package.EntryPoints).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PortableShaderGroupLayout>)package.GroupLayouts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GpuBindingLayoutEntry>)group.Entries).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PortableShaderDataLayout>)package.ParameterLayouts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PortableShaderFieldLayout>)root.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PortableShaderBindingSchemaEntry>)schema.Entries).Clear());
    }
}
