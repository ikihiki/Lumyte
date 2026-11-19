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

    private static bool InheritsSlotBridge(INamedTypeSymbol component, string name, Compilation compilation)
    {
        for (INamedTypeSymbol? ancestor = component.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            // Referenced assemblies already contain their generated bridges. Internal
            // accessibility (including friend assemblies) must be checked on the symbol.
            if (ancestor.GetMembers("__LumyteSlot" + name).Any(member => compilation.IsSymbolAccessibleWithin(member, component)))
            {
                return true;
            }

            // Source generators see the input compilation, not one another's output.
            // Predict the internal bridge only for a source component in this assembly.
            if (SymbolEqualityComparer.Default.Equals(ancestor.ContainingAssembly, compilation.Assembly)
                && ancestor.DeclaringSyntaxReferences.Length != 0 && HasAttribute(ancestor, "Composable")
                && ancestor.GetMembers(name).OfType<IMethodSymbol>().Any(method => HasAttribute(method, "ComposeSlot")))
            {
                return true;
            }
        }

        return false;
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
