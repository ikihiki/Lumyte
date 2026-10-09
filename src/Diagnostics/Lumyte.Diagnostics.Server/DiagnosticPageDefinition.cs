namespace Lumyte.Diagnostics.Server;

/// <summary>A DI-registered diagnostic page and its optional subsystem requirement.</summary>
/// <param name="Id">The URL segment identifying the page.</param>
/// <param name="Title">The navigation title.</param>
/// <param name="Category">The navigation group.</param>
/// <param name="Order">The order within the group.</param>
/// <param name="RequiredSubsystem">The exact published subsystem ID, if required.</param>
/// <param name="Component">An optional custom Blazor component using ordinary DI.</param>
public sealed record DiagnosticPageDefinition(string Id, string Title, string Category, int Order, string? RequiredSubsystem = null, Type? Component = null);
