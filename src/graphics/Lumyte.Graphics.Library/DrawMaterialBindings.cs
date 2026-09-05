namespace Lumyte.Graphics.Library;

/// <summary>One sampled texture declaration used for both descriptors and graph dependencies.</summary>
public readonly record struct DrawTextureBinding(
    int Index,
    GpuTextureHandle Texture,
    GpuTextureDescription Description,
    TextureId Descriptor,
    GpuStage Stages = GpuStage.PixelShader);

/// <summary>One shader buffer declaration used for both descriptors and graph dependencies.</summary>
public readonly record struct DrawBufferBinding(
    int Index,
    GpuBufferHandle Buffer,
    GpuBufferDescription Description,
    BufferId Descriptor,
    GpuStage Stages = GpuStage.PixelShader);

/// <summary>Immutable material bindings from which descriptor and graph declarations are derived.</summary>
public sealed class DrawMaterialBindings
{
    private readonly DrawTextureBinding[] textures;
    private readonly KeyValuePair<int, SamplerId>[] samplers;
    private readonly DrawBufferBinding[] buffers;

    public DrawMaterialBindings(
        IEnumerable<DrawTextureBinding>? textures = null,
        IEnumerable<KeyValuePair<int, SamplerId>>? samplers = null,
        IEnumerable<DrawBufferBinding>? buffers = null)
    {
        this.textures = textures?.OrderBy(binding => binding.Index).ToArray() ?? [];
        this.samplers = samplers?.OrderBy(binding => binding.Key).ToArray() ?? [];
        this.buffers = buffers?.OrderBy(binding => binding.Index).ToArray() ?? [];
        ValidateIndices(this.textures.Select(binding => binding.Index), nameof(textures));
        ValidateIndices(this.samplers.Select(binding => binding.Key), nameof(samplers));
        ValidateIndices(this.buffers.Select(binding => binding.Index), nameof(buffers));
        foreach (DrawTextureBinding binding in this.textures)
        {
            _ = new DrawSampledTexture(binding.Texture, binding.Description, binding.Stages).Validate();
            if (binding.Descriptor.IsNull) { throw new ArgumentException("Texture descriptors cannot be null.", nameof(textures)); }
        }
        foreach (KeyValuePair<int, SamplerId> binding in this.samplers)
        {
            if (binding.Value.IsNull) { throw new ArgumentException("Sampler descriptors cannot be null.", nameof(samplers)); }
        }
        foreach (DrawBufferBinding binding in this.buffers)
        {
            _ = new DrawShaderBuffer(binding.Index, binding.Buffer, binding.Description, binding.Stages).Validate();
            if (binding.Descriptor.IsNull) { throw new ArgumentException("Buffer descriptors cannot be null.", nameof(buffers)); }
        }
        if (this.textures.Length + this.samplers.Length + this.buffers.Length == 0)
        {
            throw new ArgumentException("Material bindings cannot be empty.");
        }
    }

    internal GpuResourceTable CreateResourceTable()
    {
        var table = new GpuResourceTable(Length(textures.Select(x => x.Index)), Length(samplers.Select(x => x.Key)), Length(buffers.Select(x => x.Index)));
        foreach (DrawTextureBinding binding in textures) { table.SetTexture(binding.Index, binding.Descriptor); }
        foreach (KeyValuePair<int, SamplerId> binding in samplers) { table.SetSampler(binding.Key, binding.Value); }
        foreach (DrawBufferBinding binding in buffers) { table.SetBuffer(binding.Index, binding.Descriptor); }
        return table;
    }

    internal DrawSampledTexture[] CreateTextures() => textures
        .Select(binding => new DrawSampledTexture(binding.Texture, binding.Description, binding.Stages))
        .ToArray();

    internal DrawShaderBuffer[] CreateBuffers() => buffers
        .Select(binding => new DrawShaderBuffer(binding.Index, binding.Buffer, binding.Description, binding.Stages))
        .ToArray();

    private static int Length(IEnumerable<int> indices) => indices.DefaultIfEmpty(-1).Max() + 1;

    private static void ValidateIndices(IEnumerable<int> indices, string parameter)
    {
        int previous = -1;
        foreach (int index in indices)
        {
            if (index < 0 || index == previous) { throw new ArgumentException("Binding indices must be non-negative and unique.", parameter); }
            previous = index;
        }
    }
}
