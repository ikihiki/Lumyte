using Microsoft.AspNetCore.Components;

namespace Lumyte.Diagnostics.Server;

/// <summary>Validates and orders the pages registered in DI.</summary>
public sealed class DiagnosticPageRegistry
{
    /// <summary>Initializes a new instance of the <see cref="DiagnosticPageRegistry"/> class.</summary>
    /// <param name="pages">The registered definitions.</param>
    public DiagnosticPageRegistry(IEnumerable<DiagnosticPageDefinition> pages)
    {
        DiagnosticPageDefinition[] definitions = pages.ToArray();
        if (definitions.Any(page => string.IsNullOrWhiteSpace(page.Id) || page.Id.Any(character => !char.IsAsciiLetterLower(character) && character != '-')
            || string.IsNullOrWhiteSpace(page.Title) || string.IsNullOrWhiteSpace(page.Category)
            || (page.Component != null && (!typeof(IComponent).IsAssignableFrom(page.Component) || page.Component.IsAbstract || page.Component.ContainsGenericParameters)))
            || definitions.Select(page => page.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
        {
            throw new ArgumentException("Page IDs must be unique lowercase URL segments and components must be concrete Blazor components.", nameof(pages));
        }

        Pages = Array.AsReadOnly(definitions.OrderBy(page => page.Category switch { "Resources" => 0, "Game" => 1, "Engine" => 2, "Telemetry" => 3, "Operations" => 4, _ => 5 }).ThenBy(page => page.Category, StringComparer.Ordinal).ThenBy(page => page.Order).ToArray());
    }

    /// <summary>Gets the validated navigation definitions.</summary>
    public IReadOnlyList<DiagnosticPageDefinition> Pages { get; }
}
