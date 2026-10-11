using System.Collections.Immutable;

namespace Lumyte.Input.Actions;
/// <summary>Defines input context.</summary>
/// <param name = "Id">The Id value.</param>
/// <param name = "Priority">The Priority value.</param>
/// <param name = "Exclusive">The Exclusive value.</param>
/// <param name="ParentId">The optional single parent context.</param>
/// <param name="Actions">Local definitions overriding inherited actions by identifier.</param>
public sealed record InputContext(string Id, int Priority = 0, bool Exclusive = false, string? ParentId = null, ImmutableArray<ActionDefinition> Actions = default);
