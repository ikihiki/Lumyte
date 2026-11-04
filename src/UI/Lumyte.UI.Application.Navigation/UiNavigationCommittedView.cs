namespace Lumyte.UI.Application.Navigation;

/// <summary>Identifies a published navigation surface without argument or editor content.</summary>
/// <param name="NavigatorId">The navigator identity.</param>
/// <param name="EntryId">The history entry identity.</param>
/// <param name="ActivationId">The activation identity.</param>
/// <param name="Route">The canonical route.</param>
/// <param name="SurfaceId">The nonreused surface identity.</param>
/// <param name="TargetId">The optional diagnostic target identity.</param>
public sealed record UiNavigationCommittedView(Guid NavigatorId, Guid EntryId, Guid ActivationId, string Route, Guid SurfaceId, Guid? TargetId);
