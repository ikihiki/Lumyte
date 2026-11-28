namespace Lumyte.Graphics.Planning;
/// <summary>Declares plan barrier.</summary>
/// <param name = "Before">The before.</param>
/// <param name = "After">The after.</param>
/// <param name = "Source">The source.</param>
/// <param name = "Destination">The destination.</param>
public sealed record PlanBarrier(PlanPass Before, PlanPass After, PlanResourceUse Source, PlanResourceUse Destination);
