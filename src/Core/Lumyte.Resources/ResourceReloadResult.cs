namespace Lumyte.Resources;

/// <summary>Reports the publication boundary reached by a reload operation.</summary>
public sealed record ResourceReloadResult
{
    /// <summary>Gets the outcome.</summary>
    public required ResourceReloadStatus Status { get; init; }

    /// <summary>Gets the publication version observed when the operation completed.</summary>
    public required ulong PublicationVersion { get; init; }

    /// <summary>Gets the number of published logical outputs; zero for unchanged or conflicting operations.</summary>
    public required int UpdatedResources { get; init; }
}
