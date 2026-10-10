namespace Lumyte.Input;

/// <summary>Limits retained history by count or monotonic age; null limits mean manual retention.</summary>
/// <param name="MaxRecords">Maximum retained count, or null for no count limit.</param>
/// <param name="MaxAge">Maximum retained age, or null for no age limit.</param>
public sealed record InputRetentionPolicy(int? MaxRecords = null, TimeSpan? MaxAge = null);
