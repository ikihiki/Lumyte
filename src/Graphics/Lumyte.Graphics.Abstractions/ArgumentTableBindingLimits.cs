namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes effective shader-helper slot spans and per-draw binding budgets, not logical table capacities.</summary>
public sealed record ArgumentTableBindingLimits
{
    /// <summary>Gets the representable texture slot span.</summary>
    public uint TextureSlots { get; init; } = uint.MaxValue;

    /// <summary>Gets the representable sampler slot span.</summary>
    public uint SamplerSlots { get; init; } = uint.MaxValue;

    /// <summary>Gets the representable buffer slot span; device-address buffers need no descriptor slot limit.</summary>
    public uint BufferSlots { get; init; } = uint.MaxValue;

    /// <summary>Gets a value indicating whether draw binding budgets consume reachable slot spans instead of compact resource counts.</summary>
    public bool DirectSlotBindings { get; init; }

    /// <summary>Gets the texture descriptor floor in a nonempty helper layout.</summary>
    public uint MinimumTextureBindings { get; init; }

    /// <summary>Gets the sampler descriptor floor in a nonempty helper layout.</summary>
    public uint MinimumSamplerBindings { get; init; }

    /// <summary>Gets the maximum texture bindings for one draw.</summary>
    public uint TexturesPerDraw { get; init; } = uint.MaxValue;

    /// <summary>Gets the maximum sampler bindings for one draw.</summary>
    public uint SamplersPerDraw { get; init; } = uint.MaxValue;

    /// <summary>Gets the maximum buffer bindings for one draw, excluding reserved helper storage.</summary>
    public uint BuffersPerDraw { get; init; } = uint.MaxValue;

    /// <summary>Gets the combined binding budget; the weights specify which descriptor classes consume it.</summary>
    public uint CombinedBindings { get; init; } = uint.MaxValue;

    /// <summary>Gets root/remap descriptors reserved from the combined budget.</summary>
    public uint ReservedBindings { get; init; }

    /// <summary>Gets the combined cost per texture binding.</summary>
    public uint TextureBindingWeight { get; init; } = 1;

    /// <summary>Gets the combined cost per sampler binding.</summary>
    public uint SamplerBindingWeight { get; init; } = 1;

    /// <summary>Gets the combined cost per buffer binding; zero represents device-address buffers.</summary>
    public uint BufferBindingWeight { get; init; } = 1;

    /// <summary>Gets the combined cost per active color attachment.</summary>
    public uint ColorAttachmentBindingWeight { get; init; }

    /// <summary>Gets the maximum registered storage range for finite binding backends.</summary>
    public ulong MaximumBufferRangeBytes { get; init; } = ulong.MaxValue;

    /// <summary>Gets the required registered storage range size alignment.</summary>
    public uint BufferRangeSizeAlignment { get; init; } = 1;

    /// <summary>Gets the required storage range offset alignment.</summary>
    public uint BufferRangeAlignment { get; init; } = 1;
}
