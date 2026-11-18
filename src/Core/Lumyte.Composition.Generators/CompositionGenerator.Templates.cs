using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lumyte.Composition.Generators;

/// <summary>Recognizes runtime template contracts and renders their slot entry types.</summary>
public sealed partial class CompositionGenerator
{
    private static INamedTypeSymbol? TemplateType(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, Arity: >= 2 and <= 17 } named)
        {
            return null;
        }

        // Resolve through the runtime contract assembly, not the consumer's namespace.
        // A source declaration with the same metadata name must not become a template.
        INamedTypeSymbol? marker = compilation.GetTypeByMetadataName(Prefix + "ComposableAttribute");
        INamedTypeSymbol? definition = marker?.ContainingAssembly.GetTypeByMetadataName(
            Prefix + "CompositionTemplate`" + named.Arity);
        return definition is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, definition)
            ? named : null;
    }

    private static string SlotType(IMethodSymbol slot, Compilation compilation)
    {
        string target = TypeName(slot.Parameters[0].Type);
        INamedTypeSymbol? template = TemplateType(slot.Parameters[1].Type, compilation);
        return template is not null
            ? "global::Lumyte.Composition.CompositionTemplateSlot<" + target + ", "
                + string.Join(", ", template.TypeArguments.Select(TypeName)) + ">"
            : "global::Lumyte.Composition.CompositionSlot<" + target + ", "
                + TypeName(ContentItem(slot.Parameters[1].Type)!) + ">";
    }
}
