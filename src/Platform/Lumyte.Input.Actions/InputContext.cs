namespace Lumyte.Input.Actions;
/// <summary>Defines input context.</summary>
/// <param name = "Id">The Id value.</param>
/// <param name = "Priority">The Priority value.</param>
/// <param name = "Exclusive">The Exclusive value.</param>
public sealed record InputContext(string Id, int Priority = 0, bool Exclusive = false);
