namespace Lumyte.Graphics.Planning;
/// <summary>Declares graphics planning budget.</summary>
/// <param name = "MaximumPasses">The maximum passes.</param>
/// <param name = "MaximumResources">The maximum resources.</param>
/// <param name = "CleanupAttempts">The cleanup attempts.</param>
public sealed record GraphicsPlanningBudget(int MaximumPasses = 4096, int MaximumResources = 4096, int CleanupAttempts = 2)
{
    /// <summary>Gets maximum allocation bytes.</summary>
    public ulong MaximumAllocationBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>Gets maximum argument bytes.</summary>
    public ulong MaximumArgumentBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets the finite number of immutable successor versions admitted before compilation.</summary>
    public int MaximumResourceVersions { get; init; } = 65536;

    /// <summary>Gets the finite number of shader-data elements admitted to one declared allocation or transfer.</summary>
    public int MaximumShaderDataElements { get; init; } = 65536;

    /// <summary>Gets the finite count of declared semantic resource uses.</summary>
    public int MaximumResourceUses { get; init; } = 131072;

    /// <summary>Gets the finite count of whole-table physical exposures across operations.</summary>
    public int MaximumPhysicalBindingUses { get; init; } = 1048576;

    /// <summary>Gets the finite count of prepared native resource and release barriers.</summary>
    public int MaximumBarriers { get; init; } = 1048576;

    /// <summary>Gets maximum commands.</summary>
    public int MaximumCommands { get; init; } = 65536;

    /// <summary>Gets maximum table references.</summary>
    public int MaximumTableReferences { get; init; } = 65536;

    /// <summary>Gets maximum in flight.</summary>
    public int MaximumInFlight { get; init; } = 1;
}
