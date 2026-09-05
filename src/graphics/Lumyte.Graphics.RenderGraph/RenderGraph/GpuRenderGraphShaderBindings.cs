namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// Immutable mapping between bindless descriptor-array indices and resources declared by a
/// render-graph pass. Descriptor capacity belongs to each backend and is not part of the ABI.
/// </summary>
public sealed class GpuRenderGraphShaderBindings
{
    private const GpuStage SupportedTextureStages = GpuStage.VertexShader | GpuStage.PixelShader;
    private readonly GpuRenderGraphShaderTextureBinding[] textures;
    private readonly GpuRenderGraphShaderSamplerBinding[] samplers;
    private readonly GpuRenderGraphShaderBufferBinding[] buffers;

    public GpuRenderGraphShaderBindings(
        IEnumerable<GpuRenderGraphShaderTextureBinding>? textures = null,
        IEnumerable<GpuRenderGraphShaderSamplerBinding>? samplers = null,
        IEnumerable<GpuRenderGraphShaderBufferBinding>? buffers = null)
    {
        this.textures = CanonicalTextures(textures);
        this.samplers = CanonicalSamplers(samplers);
        this.buffers = CanonicalBuffers(buffers);
        if (this.textures.Length == 0 && this.samplers.Length == 0 && this.buffers.Length == 0)
        {
            throw new ArgumentException("Shader bindings require at least one texture, sampler, or buffer.");
        }
    }

    public IReadOnlyList<GpuRenderGraphShaderTextureBinding> Textures => textures;
    public IReadOnlyList<GpuRenderGraphShaderSamplerBinding> Samplers => samplers;
    public IReadOnlyList<GpuRenderGraphShaderBufferBinding> Buffers => buffers;

    internal void DeclareOn(GpuRenderGraphPassBuilder pass)
    {
        foreach (IGrouping<GpuRenderGraphTexture, GpuRenderGraphShaderTextureBinding> group
            in textures.GroupBy(static binding => binding.Texture))
        {
            GpuStage stages = GpuStage.None;
            foreach (GpuRenderGraphShaderTextureBinding binding in group) { stages |= binding.Stages; }
            pass.Read(group.Key, stages, GpuBarrierHazards.Descriptors);
        }
        foreach (IGrouping<GpuRenderGraphBuffer, GpuRenderGraphShaderBufferBinding> group
            in buffers.GroupBy(static binding => binding.Buffer))
        {
            GpuStage stages = GpuStage.None;
            foreach (GpuRenderGraphShaderBufferBinding binding in group) { stages |= binding.Stages; }
            pass.Read(group.Key, stages, GpuBarrierHazards.Descriptors);
        }
    }

    internal GpuResourceTable Resolve(GpuRenderGraphPassContextView context)
    {
        var table = new GpuResourceTable(
            RequiredLength(textures, static binding => binding.Index),
            RequiredLength(samplers, static binding => binding.Index),
            RequiredLength(buffers, static binding => binding.Index));
        foreach (GpuRenderGraphShaderTextureBinding binding in textures)
        {
            TextureId descriptor = binding.Descriptor.IsNull
                ? context.GetTextureView(binding.Texture).Id
                : binding.Descriptor;
            table.SetTexture(binding.Index, descriptor);
        }
        foreach (GpuRenderGraphShaderSamplerBinding binding in samplers)
        {
            table.SetSampler(binding.Index, binding.Sampler);
        }
        foreach (GpuRenderGraphShaderBufferBinding binding in buffers)
        {
            BufferId descriptor = binding.Descriptor.IsNull
                ? context.GetBufferView(binding.Buffer).Id
                : binding.Descriptor;
            table.SetBuffer(binding.Index, descriptor);
        }
        return table;
    }

    private static GpuRenderGraphShaderTextureBinding[] CanonicalTextures(
        IEnumerable<GpuRenderGraphShaderTextureBinding>? source)
    {
        GpuRenderGraphShaderTextureBinding[] result = source?
            .OrderBy(static binding => binding.Index)
            .ToArray() ?? [];
        for (int index = 0; index < result.Length; index++)
        {
            GpuRenderGraphShaderTextureBinding binding = result[index];
            if (binding.Index < 0 || (index != 0 && result[index - 1].Index == binding.Index))
            {
                throw new ArgumentException("Shader texture indices must be non-negative and unique.", nameof(source));
            }
            if (binding.Texture.IsNull)
            {
                throw new ArgumentException("Shader texture bindings cannot be null.", nameof(source));
            }
            if ((binding.Texture.Description.Usage & GpuTextureUsage.Sampled) == 0)
            {
                throw new ArgumentException("Shader texture bindings require Sampled usage.", nameof(source));
            }
            if (binding.Stages == GpuStage.None || (binding.Stages & ~SupportedTextureStages) != 0)
            {
                throw new ArgumentException(
                    "Shader texture bindings support vertex and pixel stages only.",
                    nameof(source));
            }
        }
        return result;
    }

    private static GpuRenderGraphShaderSamplerBinding[] CanonicalSamplers(
        IEnumerable<GpuRenderGraphShaderSamplerBinding>? source)
    {
        GpuRenderGraphShaderSamplerBinding[] result = source?
            .OrderBy(static binding => binding.Index)
            .ToArray() ?? [];
        for (int index = 0; index < result.Length; index++)
        {
            GpuRenderGraphShaderSamplerBinding binding = result[index];
            if (binding.Index < 0 || (index != 0 && result[index - 1].Index == binding.Index))
            {
                throw new ArgumentException("Shader sampler indices must be non-negative and unique.", nameof(source));
            }
            if (binding.Sampler.IsNull)
            {
                throw new ArgumentException("Shader sampler bindings cannot be null.", nameof(source));
            }
        }
        return result;
    }

    private static GpuRenderGraphShaderBufferBinding[] CanonicalBuffers(
        IEnumerable<GpuRenderGraphShaderBufferBinding>? source)
    {
        GpuRenderGraphShaderBufferBinding[] result = source?
            .OrderBy(static binding => binding.Index)
            .ToArray() ?? [];
        for (int index = 0; index < result.Length; index++)
        {
            GpuRenderGraphShaderBufferBinding binding = result[index];
            if (binding.Index < 0 || (index != 0 && result[index - 1].Index == binding.Index))
            {
                throw new ArgumentException("Shader buffer indices must be non-negative and unique.", nameof(source));
            }
            if (binding.Buffer.IsNull)
            {
                throw new ArgumentException("Shader buffer bindings cannot be null.", nameof(source));
            }
            if ((binding.Buffer.Description.Usage & GpuBufferUsage.ShaderData) == 0)
            {
                throw new ArgumentException("Shader buffer bindings require ShaderData usage.", nameof(source));
            }
            if (binding.Stages == GpuStage.None || (binding.Stages & ~SupportedTextureStages) != 0)
            {
                throw new ArgumentException(
                    "Shader buffer bindings support vertex and pixel stages only.",
                    nameof(source));
            }
        }
        return result;
    }

    private static int RequiredLength<T>(T[] bindings, Func<T, int> index)
        => bindings.Length == 0 ? 0 : checked(index(bindings[^1]) + 1);
}
