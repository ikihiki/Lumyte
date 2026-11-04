using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.UI.Automation.Contracts;
/// <summary>A closed versioned action or observation plan.</summary>
/// <param name = "SchemaVersion">The SchemaVersion value.</param>
/// <param name = "Kind">The Kind value.</param>
/// <param name = "Locator">The Locator value.</param>
/// <param name = "Key">The Key value.</param>
/// <param name = "Modifiers">The Modifiers value.</param>
/// <param name = "Position">The Position value.</param>
/// <param name = "Predicate">The Predicate value.</param>
/// <param name = "Count">The Count value.</param>
/// <param name="Text">The explicit Text value.</param>
/// <param name="Candidate">The explicit Candidate value.</param>
/// <param name="CandidateIndex">The explicit CandidateIndex value.</param>
public sealed record UiRemotePlan(int SchemaVersion, string Kind, UiRemoteLocatorStep[] Locator, string? Key = null, string[]? Modifiers = null, UiRemotePoint? Position = null, string? Predicate = null, int? Count = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UiRemoteCandidateToken? Candidate = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CandidateIndex = null);
