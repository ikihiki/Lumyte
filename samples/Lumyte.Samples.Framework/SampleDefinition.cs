namespace Lumyte.Samples.Framework;

/// <summary>A logical sample with interchangeable runtime adapters.</summary>
/// <param name="Id">Stable command-line identifier.</param>
/// <param name="Role">Purpose shown in the catalog and selector.</param>
/// <param name="Backends">Supported backend adapters.</param>
public sealed record SampleDefinition(string Id, string Role, params SampleBackend[] Backends);
