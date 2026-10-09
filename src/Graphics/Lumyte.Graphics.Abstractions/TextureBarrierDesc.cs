namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes an explicit texture dependency and transition.</summary>
public sealed record TextureBarrierDesc
{
    /// <summary>Gets the allocation.</summary>
    public required IGraphicsTexture Texture { get; init; }

    /// <summary>Gets the subresource range.</summary>
    public required TextureSubresourceRange Range { get; init; }

    /// <summary>Gets the preceding state; Undefined discards contents.</summary>
    public required TextureState BeforeState { get; init; }

    /// <summary>Gets the required subsequent state.</summary>
    public required TextureState AfterState { get; init; }

    /// <summary>Gets the preceding accesses.</summary>
    public required BarrierScope Before { get; init; }

    /// <summary>Gets the subsequent accesses.</summary>
    public required BarrierScope After { get; init; }
}
