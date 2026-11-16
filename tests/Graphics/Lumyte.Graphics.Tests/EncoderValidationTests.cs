using System.Reflection;
using System.Runtime.ExceptionServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Validation;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises the same validation boundary called by native draw and dispatch recording.</summary>
public sealed class EncoderValidationTests
{
    private static readonly BindingFlags _hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ResourceHolder> _resources = new();

    /// <summary>Checks repeated dispatch accesses, rather than only binding-time lifetime retention.</summary>
    /// <param name="enabled">Whether graphics validation is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageWritesRequireAnExecutionDependency(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        object command = enabled ? CreateCommand(validation) : new object();
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadWrite);
        IShaderReference[] references = [reference];
        ShaderAccesses(validation, command, references, PipelineStage.ComputeShader);
        void DispatchAgain() => ShaderAccesses(validation, command, references, PipelineStage.ComputeShader);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(DispatchAgain);
        }
        else
        {
            DispatchAgain();
            Assert.Equal(0, reference.Probes);
        }
    }

    /// <summary>Checks that disabled diagnostics neither enumerate metadata nor probe registrations on the first execution.</summary>
    [Fact]
    public void DisabledShaderAccessReportsDoNotEnumerateMetadata()
    {
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly) { Stale = true };
        var references = new TrackingReferences(reference);
        object command = new();

        ShaderAccesses(null, command, references, PipelineStage.ComputeShader);

        Assert.Equal(0, references.CountReads);
        Assert.Equal(0, references.Enumerations);
        Assert.Equal(0, reference.Probes);
    }

    /// <summary>Checks released registration identities at the actual execution access boundary.</summary>
    [Fact]
    public void ExecutionRejectsStaleRegistration()
    {
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly) { Stale = true };
        Assert.Throws<InvalidOperationException>(() => ShaderAccesses(new(), CreateCommand(new()), [reference], PipelineStage.VertexShader | PipelineStage.FragmentShader));
    }

    /// <summary>Checks that sampled access uses exactly the selected mip and layer range.</summary>
    [Fact]
    public void SamplingUsesTheViewSubresources()
    {
        var texture = (IGraphicsTexture)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Vulkan.VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanTexture")!);
        var view = new FakeView(texture);
        var reference = new FakeReference(view, ShaderBufferAccess.ReadOnly);
        var validation = new GraphicsValidation();
        IGraphicsCommandBuffer command = CreateCommand(validation);
        var scope = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        validation.TextureAccess(command, texture, new(0, 1, 0, 1), TextureState.CopyDestination, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        ShaderAccesses(validation, command, [reference], PipelineStage.FragmentShader);
        Assert.Throws<InvalidOperationException>(() => validation.TextureAccess(command, texture, new(2, 1, 3, 1), TextureState.CopyDestination, scope));
    }

    /// <summary>Checks copy-to-shader hazards through the canonical decorated allocation identity.</summary>
    [Fact]
    public void ShaderDataUsesTheBackingAllocation()
    {
        var validation = new GraphicsValidation();
        IGraphicsCommandBuffer command = CreateCommand(validation);
        var device = (ValidationDevice)command.GetType().GetProperty("Device", _hidden)!.GetValue(command)!;

        // No native member is used by ResourceIdentity; leave the borrowed raw
        // field empty rather than manufacture an inaccessible generic backend.
        object data = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(ValidationType("ValidationShaderDataBuffer`1").MakeGenericType(typeof(EmptyArguments)));
        Type resourceBase = data.GetType().BaseType!;
        resourceBase.GetField("<Device>k__BackingField", _hidden)!.SetValue(data, device);
        var reference = new FakeReference(data, ShaderBufferAccess.ReadOnly);
        validation.Access(command, data, new(PipelineStage.Copy, ResourceAccess.CopyWrite), 16, 16);
        Assert.Throws<InvalidOperationException>(() => ShaderAccesses(validation, command, [reference], PipelineStage.ComputeShader));
    }

    /// <summary>Registration callbacks remain live until submission rather than only being checked at capture.</summary>
    [Fact]
    public void SubmissionRevalidatesCapturedRegistration()
    {
        var validation = new GraphicsValidation();
        IGraphicsCommandBuffer command = CreateCommand(validation);
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly);
        ShaderAccesses(validation, command, [reference], PipelineStage.ComputeShader);
        reference.Stale = true;
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission([command]));
        Assert.Equal(2, reference.Probes);
        validation.Discard(command);
        validation.PrepareSubmission([command]);
        Assert.Equal(2, reference.Probes);
    }

    /// <summary>Per-execution tracking retains both table and resource lifetimes until discard.</summary>
    [Fact]
    public void ExecutionRetainsTableAndResource()
    {
        var validation = new GraphicsValidation();
        IGraphicsCommandBuffer command = CreateCommand(validation);
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly);
        ShaderAccesses(validation, command, [reference], PipelineStage.ComputeShader);
        Assert.Throws<InvalidOperationException>(() => validation.Release(reference.Table));
        Assert.Throws<InvalidOperationException>(() => validation.Release(ResourceIdentity(command, reference.Resource)));
        validation.Discard(command);
        validation.Release(reference.Table);
        validation.Release(ResourceIdentity(command, reference.Resource));
    }

    /// <summary>Aliased bindings in one shader execution are not mistaken for consecutive dispatches.</summary>
    [Fact]
    public void AliasedRegistrationsAreCombinedWithinOneExecution()
    {
        object storage = new();
        var validation = new GraphicsValidation();
        IGraphicsCommandBuffer command = CreateCommand(validation);
        IShaderReference[] references = [new FakeReference(storage, ShaderBufferAccess.ReadOnly), new FakeReference(storage, ShaderBufferAccess.ReadWrite)];
        ShaderAccesses(validation, command, references, PipelineStage.ComputeShader);
        Assert.Throws<InvalidOperationException>(() => ShaderAccesses(validation, command, references, PipelineStage.ComputeShader));
    }

    /// <summary>Depth-only and stage-local roots do not acquire fictitious fragment-stage accesses.</summary>
    [Fact]
    public void RenderAccessStagesFollowRootDeclarations()
    {
        const string Root = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer"}}]}
            """;
        const string Empty = """
            {"parameters":[]}
            """;
        var vertex = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Vertex, "main", "test", "row-major", [], Root);
        var fragment = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Fragment, "main", "test", "row-major", [], Empty);
        Assert.Equal(PipelineStage.VertexShader, RenderStages(vertex, null, "arguments"));
        Assert.Equal(PipelineStage.VertexShader, RenderStages(vertex, fragment, "arguments"));
        var fragmentRoot = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Fragment, "main", "test", "row-major", [], Root);
        Assert.Equal(PipelineStage.VertexShader | PipelineStage.FragmentShader, RenderStages(vertex, fragmentRoot, "arguments"));
    }

    // Invoke the production decorator boundary; reflection is confined to this test
    // because these implementation details deliberately are not public API.
    private static Type ValidationType(string name) => typeof(ValidationDevice).Assembly.GetType("Lumyte.Graphics.Validation." + name, true)!;

    private static object? Invoke(MethodInfo method, object? target, params object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?> call)
        where T : class
    {
        T value = DispatchProxy.Create<T, BackendProxy>();
        ((BackendProxy)(object)value).Call = call;
        return value;
    }

    private static void ShaderAccesses(GraphicsValidation? validation, object command, IEnumerable<IShaderReference> references, PipelineStage stages)
    {
        // Opt-out means no ValidationDevice exists. This is only composition, not
        // a replacement implementation of any validation algorithm.
        if (validation == null)
        {
            return;
        }

        var device = (ValidationDevice)command.GetType().GetProperty("Device", _hidden)!.GetValue(command)!;
        var wrapped = new List<IShaderReference>();
        foreach (IShaderReference reference in references)
        {
            if (reference.Resource is IGraphicsTextureView)
            {
                wrapped.Add(reference);
                continue;
            }

            object resource = reference.Resource.GetType().Assembly == typeof(ValidationDevice).Assembly
                ? reference.Resource
                : _resources.GetValue(reference.Resource, _ => new ResourceHolder()).Get(device);
            wrapped.Add(new ResourceReference(reference, resource));
        }

        Invoke(ValidationType("ValidationEncoderChecks").GetMethod("ShaderAccesses", _hidden)!, null, command, wrapped, stages, true);
    }

    private static object ResourceIdentity(object command, object resource)
    {
        if (resource.GetType().Assembly == typeof(ValidationDevice).Assembly)
        {
            return resource;
        }

        var device = (ValidationDevice)command.GetType().GetProperty("Device", _hidden)!.GetValue(command)!;
        return _resources.GetValue(resource, _ => new ResourceHolder()).Get(device);
    }

    private static IGraphicsCommandBuffer CreateCommand(GraphicsValidation? validation)
    {
        IGraphicDevice backend = Proxy<IGraphicDevice>(method => method.Name switch
        {
            "get_Queue" => Proxy<IGraphicsQueue>(_ => throw new NotSupportedException()),
            "add_DeviceLost" or "remove_DeviceLost" => null,
            _ => throw new NotSupportedException(method.Name),
        });
        var device = new ValidationDevice(backend);
        if (validation != null)
        {
            typeof(ValidationDevice).GetField("<Tracker>k__BackingField", _hidden)!.SetValue(device, validation);
        }

        IGraphicsCommandBuffer raw = Proxy<IGraphicsCommandBuffer>(_ => throw new NotSupportedException());
        return (IGraphicsCommandBuffer)Activator.CreateInstance(ValidationType("ValidationCommandBuffer"), _hidden, null, [device, raw, new CommandBufferDesc()], null)!;
    }

    private static PipelineStage RenderStages(ShaderTargetData vertex, ShaderTargetData? fragment, string root)
    {
        IGraphicDevice backend = Proxy<IGraphicDevice>(method => method.Name switch
        {
            "get_Queue" => Proxy<IGraphicsQueue>(_ => throw new NotSupportedException()),
            "get_Caps" => new DeviceCaps { ShaderTarget = ShaderTarget.Wgsl },
            "add_DeviceLost" or "remove_DeviceLost" => null,
            _ => throw new NotSupportedException(method.Name),
        });
        using var device = new ValidationDevice(backend);
        IGraphicsShader Shader(ShaderTargetData target) => Proxy<IGraphicsShader>(_ => ShaderArtifact.PackTarget(ShaderTarget.Wgsl, target.Stage, "main", "test", [32], target.ReflectionJson));
        var desc = new GraphicsPipelineDesc { VertexShader = Shader(vertex), FragmentShader = fragment == null ? null : Shader(fragment) };
        IGraphicsPipeline pipeline = Proxy<IGraphicsPipeline>(_ => desc);
        return (PipelineStage)Invoke(ValidationType("ValidationEncoderChecks").GetMethod("RenderStages", _hidden)!, null, device, pipeline, root)!;
    }

    private readonly struct EmptyArguments : IShaderArguments
    {
        public static string ShaderTypeName => "Empty";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => throw new NotSupportedException();
    }

    /// <summary>A raw backend stub only; all diagnostics run in the production validation assembly.</summary>
    public class BackendProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?> Call { get; set; } = _ => throw new NotSupportedException();

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!);
    }

    private sealed class ResourceHolder
    {
        private readonly Dictionary<ValidationDevice, object> _wrappers = [];

        public object Get(ValidationDevice device)
        {
            if (!_wrappers.TryGetValue(device, out object? wrapper))
            {
                wrapper = Activator.CreateInstance(ValidationType("ValidationBuffer`1").MakeGenericType(typeof(byte)), _hidden, null, [device, Proxy<IGraphicsBuffer<byte>>(_ => throw new NotSupportedException())], null)!;
                _wrappers.Add(device, wrapper);
            }

            return wrapper;
        }
    }

    private sealed class ResourceReference(IShaderReference source, object resource) : IShaderReference, IValidatedShaderReference
    {
        public object Table => source.Table;

        public object Resource => resource;

        public ShaderBufferAccess BufferAccess => source.BufferAccess;

        public uint Slot => source.Slot;

        public ulong OffsetInBytes => source.OffsetInBytes;

        public ulong SizeInBytes => source.SizeInBytes;

        public ulong Count => source.Count;

        public ulong RegistrationOffsetInBytes => source.RegistrationOffsetInBytes;

        public ulong RegistrationSizeInBytes => source.RegistrationSizeInBytes;

        public void ValidateRegistration() => ((IValidatedShaderReference)source).ValidateRegistration();
    }

    private sealed class TrackingReferences(IShaderReference reference) : IReadOnlyCollection<IShaderReference>
    {
        public int Count
        {
            get
            {
                CountReads++;
                return 1;
            }
        }

        public int CountReads { get; private set; }

        public int Enumerations { get; private set; }

        public IEnumerator<IShaderReference> GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable<IShaderReference>)new[] { reference }).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FakeView(IGraphicsTexture texture) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => texture;

        public TextureViewInfo Info { get; } = new() { Format = TextureFormat.Rgba8Unorm, Dimension = TextureViewDimension.D2, BaseMipLevel = 2, MipLevelCount = 1, BaseArrayLayer = 3, ArrayLayerCount = 1 };

        public void Dispose()
        {
        }
    }

    private sealed class FakeReference(object resource, ShaderBufferAccess access) : IShaderReference, IValidatedShaderReference
    {
        public object Table { get; } = new();

        public object Resource => resource;

        public ShaderBufferAccess BufferAccess => access;

        public uint Slot => 0;

        public ulong OffsetInBytes => 16;

        public ulong SizeInBytes => 16;

        public ulong RegistrationOffsetInBytes => 16;

        public ulong RegistrationSizeInBytes => 16;

        public ulong Count => 1;

        public int Probes { get; private set; }

        public bool Stale { get; set; }

        public void ValidateRegistration()
        {
            Probes++;
            if (Stale)
            {
                throw new InvalidOperationException("Stale registration.");
            }
        }
    }
}
