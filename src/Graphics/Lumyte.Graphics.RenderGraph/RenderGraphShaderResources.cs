using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes named logical shader inputs consumed directly by graph encoders.</summary>
public sealed class RenderGraphShaderResources
{
    private readonly GraphShaderResourceOwner _owner;
    private readonly ShaderResources _resources;
    private readonly ShaderArtifact _artifact;
    private readonly ShaderDataLayout _layout;
    private readonly string _root;
    private readonly Dictionary<string, Func<ShaderValue>> _values = new(StringComparer.Ordinal);

    internal RenderGraphShaderResources(GraphShaderResourceOwner owner, ShaderResources resources, ShaderArtifact artifact, ShaderTarget target, string root)
    {
        _owner = owner;
        _resources = resources;
        _artifact = artifact;
        _root = root;
        _layout = ShaderDataLayout.Root(artifact.GetTarget(target), root);
    }

    internal ShaderResources Core => _resources;

    internal bool Graphics => _artifact.GetTarget(_owner.Target).Stage != ShaderStage.Compute;

    /// <summary>Declares a sampled graph image at its compiled logical member path.</summary>
    /// <param name="path">The compiled root member path.</param>
    /// <param name="texture">The pass-declared sampled image.</param>
    /// <param name="desc">The requested view range.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources ReadTexture(string path, RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        Check(path);
        _layout.ValidateReference(path, "GpuTextureRef");
        IGpuRef<IGraphicsTextureView>? reference = null;
        _resources.RegisterTexture(() => _owner.Texture(_resources, texture, desc), value => reference = value);
        _values.Add(path, () => new(path, typeof(IGraphicsTextureView), default, reference, true));
        return this;
    }

    /// <summary>Declares an immutable graph-owned sampler by logical member name.</summary>
    /// <param name="path">The compiled root member path.</param>
    /// <param name="desc">The sampler configuration.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources Sampler(string path, SamplerDesc desc)
    {
        Check(path);
        _layout.ValidateReference(path, "GpuSamplerRef");
        IGpuRef<IGraphicsSampler>? reference = null;
        _resources.RegisterSampler(_owner.Sampler(desc), value => reference = value);
        _values.Add(path, () => new(path, typeof(IGraphicsSampler), default, reference, true));
        return this;
    }

    /// <summary>Uses a borrowed immutable sampler without coalescing its identity.</summary>
    /// <param name="path">The compiled root member path.</param>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources Sampler(string path, IGraphicsSampler sampler)
    {
        Check(path);
        _layout.ValidateReference(path, "GpuSamplerRef");
        IGpuRef<IGraphicsSampler>? reference = null;
        _resources.RegisterSampler(sampler, value => reference = value);
        _values.Add(path, () => new(path, typeof(IGraphicsSampler), default, reference, true));
        return this;
    }

    /// <summary>Declares a typed graph buffer range with its actual shader access.</summary>
    /// <typeparam name="T">The unmanaged storage element.</typeparam>
    /// <param name="path">The compiled root member path.</param>
    /// <param name="buffer">The declared logical buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The positive element count.</param>
    /// <param name="access">The actual shader access.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources ReadBuffer<T>(string path, RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        Check(path);
        ArgumentOutOfRangeException.ThrowIfZero(count);
        string kind = access == ArgumentBufferAccess.Read ? "GpuBufferRef" : "GpuRWBufferRef";
        _layout.ValidateReference(path, kind, RawName<T>());
        IGpuRef<T>? reference = null;
        _resources.RegisterBuffer(() => _owner.Buffer(_resources, buffer, access).SliceElements(offset, count), value => reference = value, access);
        _values.Add(path, () => new(path, typeof(T), default, reference, true));
        return this;
    }

    /// <summary>Declares a shader-data range with its static shader element schema.</summary>
    /// <typeparam name="T">The shader-data element.</typeparam>
    /// <param name="path">The compiled root member path.</param>
    /// <param name="buffer">The declared logical buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The positive element count.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources ReadBuffer<T>(string path, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderData
    {
        Check(path);
        ArgumentOutOfRangeException.ThrowIfZero(count);
        _layout.ValidateReference(path, "GpuBufferRef", T.ShaderTypeName);
        IGpuRef<T>? reference = null;
        _resources.RegisterShaderData(() => _owner.Buffer(_resources, buffer).SliceElements(offset, count), value => reference = value);
        _values.Add(path, () => new(path, typeof(T), default, reference, true));
        return this;
    }

    /// <summary>Sets an explicitly supported numeric shader member.</summary>
    /// <typeparam name="T">The numeric member type.</typeparam>
    /// <param name="path">The compiled member path.</param>
    /// <param name="value">The numeric value copied into this description.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources Value<T>(string path, T value)
        where T : unmanaged => Value(path, () => value);

    /// <summary>Evaluates a numeric member once for each encoder SetArguments call.</summary>
    /// <typeparam name="T">The numeric member type.</typeparam>
    /// <param name="path">The compiled member path.</param>
    /// <param name="value">The synchronous value source.</param>
    /// <returns>This input description.</returns>
    public RenderGraphShaderResources Value<T>(string path, Func<T> value)
        where T : unmanaged
    {
        Check(path);
        ArgumentNullException.ThrowIfNull(value);
        _layout.ValidateValue(path, typeof(T));
        _values.Add(path, () =>
        {
            T captured = value();
            return new(path, typeof(T), MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref captured, 1)).ToArray(), null, false);
        });
        return this;
    }

    internal void Bind(IRenderEncoder encoder) => _owner.Bind(_resources, encoder);

    internal void Bind(IComputeEncoder encoder) => _owner.Bind(_resources, encoder);

    internal ShaderValueSnapshot Snapshot()
    {
        if (!_resources.IsReady)
        {
            throw new InvalidOperationException("The graph inputs are not ready for execution.");
        }

        var snapshot = new ShaderValueSnapshot(_root, _layout.Name, _values.Values.Select(read => read()).ToArray());
        _layout.ValidateArguments(snapshot);
        return snapshot;
    }

    internal void ValidateRender(RenderGraphContext context, IGraphicsPipeline pipeline, IReadOnlyList<RenderGraphTexture> colorAttachments, RenderGraphTexture? depthAttachment)
    {
        _owner.ValidateExecution(_resources, context.Pass, colorAttachments, graphics: true);
        if (!ReferenceEquals(pipeline.Desc.VertexShader.Artifact, _artifact) && !ReferenceEquals(pipeline.Desc.FragmentShader?.Artifact, _artifact))
        {
            throw new ArgumentException("The selected pipeline does not contain the pinned shader artifact.", nameof(pipeline));
        }

        if (!Graphics || (colorAttachments.Count == 0 && depthAttachment is null))
        {
            throw new InvalidOperationException("The shader inputs do not describe this render execution.");
        }
    }

    internal void ValidateCompute(RenderGraphContext context, IGraphicsComputePipeline pipeline)
    {
        _owner.ValidateExecution(_resources, context.Pass, [], graphics: false);
        if (Graphics || !ReferenceEquals(pipeline.Desc.ComputeShader.Artifact, _artifact))
        {
            throw new ArgumentException("The compute program does not contain the pinned shader artifact.", nameof(pipeline));
        }
    }

    private static string RawName<T>()
        where T : unmanaged => typeof(T) == typeof(int) ? "int" : typeof(T) == typeof(uint) ? "uint" : typeof(T) == typeof(float) ? "float" : typeof(T) == typeof(Vector2) ? "float2" : typeof(T) == typeof(Vector3) ? "float3" : typeof(T) == typeof(Vector4) ? "float4" : typeof(T) == typeof(Matrix4x4) ? "float4x4" : throw new NotSupportedException("The raw buffer element is not a supported shader numeric type.");

    private void Check(string path)
    {
        _owner.ValidateBuilding();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_values.ContainsKey(path))
        {
            throw new ArgumentException("A shader member is declared exactly once.", nameof(path));
        }
    }
}
