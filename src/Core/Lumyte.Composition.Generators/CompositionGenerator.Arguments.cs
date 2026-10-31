using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lumyte.Composition.Generators;

/// <summary>Selects host-defined adapters without interpreting parameter or input types.</summary>
public sealed partial class CompositionGenerator
{
    private static IEnumerable<AttributeData> ArgumentAdapters(INamedTypeSymbol component)
    {
        for (INamedTypeSymbol? type = component; type is not null; type = type.BaseType)
        {
            foreach (AttributeData attribute in type.GetAttributes().Where(value => IsAttribute(value, "ComposeArgumentAdapter")))
            {
                yield return attribute;
            }
        }
    }

    private static bool HasArgumentAdapters(INamedTypeSymbol component) => ArgumentAdapters(component).Any();

    private static INamedTypeSymbol? ParameterAdapter(INamedTypeSymbol component, ISymbol parameter)
    {
        if (IsDelegateParameter(parameter) || parameter is IPropertySymbol { SetMethod.IsInitOnly: true })
        {
            return null;
        }

        ITypeSymbol value = MemberType(parameter);
        INamedTypeSymbol? fallback = null;
        INamedTypeSymbol? generic = null;
        foreach (AttributeData attribute in ArgumentAdapters(component))
        {
            var adapter = (INamedTypeSymbol)attribute.ConstructorArguments.Last().Value!;
            if (attribute.ConstructorArguments.Length == 1)
            {
                fallback = adapter.ConstructedFrom.Construct(value);
            }
            else if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol match)
            {
                if (SymbolEqualityComparer.Default.Equals(match, value))
                {
                    return adapter;
                }

                if (match.IsUnboundGenericType && value is INamedTypeSymbol named
                    && SymbolEqualityComparer.Default.Equals(match.OriginalDefinition, named.OriginalDefinition))
                {
                    generic = adapter.ConstructedFrom.Construct(named.TypeArguments.ToArray());
                }
            }
        }

        return generic ?? fallback;
    }

    private static string? ValidateAdapter(INamedTypeSymbol component, Compilation compilation)
    {
        var selections = new List<(ITypeSymbol? Match, INamedTypeSymbol Adapter)>();
        foreach (AttributeData attribute in ArgumentAdapters(component))
        {
            if (attribute.ConstructorArguments.LastOrDefault().Value is not INamedTypeSymbol adapter
                || adapter.TypeKind != TypeKind.Struct || adapter.IsRefLikeType || adapter.ContainingType?.IsGenericType == true
                || !PublicType(adapter.IsUnboundGenericType ? adapter.ConstructedFrom : adapter))
            {
                return "An argument adapter must be a public non-ref struct.";
            }

            ITypeSymbol? match = attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;
            bool generic = match is INamedTypeSymbol { IsUnboundGenericType: true };
            if (match is null && attribute.ConstructorArguments.Length != 1)
            {
                return "A parameter type mapping cannot be null.";
            }

            if (match is null || generic)
            {
                if (!adapter.IsUnboundGenericType || adapter.Arity != 1
                    || (generic && (match is not INamedTypeSymbol namedMatch || namedMatch.Arity != 1 || namedMatch.ContainingType?.IsGenericType == true)))
                {
                    return "Fallback and generic mappings require public unbound generic structs with one type parameter.";
                }
            }
            else if (adapter.IsUnboundGenericType)
            {
                return "An exact parameter type mapping requires a closed adapter struct.";
            }

            foreach ((ITypeSymbol? previousMatch, INamedTypeSymbol previousAdapter) in selections)
            {
                if (SymbolEqualityComparer.Default.Equals(match, previousMatch)
                    && !SymbolEqualityComparer.Default.Equals(adapter, previousAdapter))
                {
                    return "Conflicting inherited argument adapters for the same parameter type.";
                }
            }

            selections.Add((match, adapter));
        }

        if (selections.Count == 0)
        {
            return null;
        }

        if (component.GetMembers().Any(member => member.Name.StartsWith("__LumyteArgumentKey", StringComparison.Ordinal)
            || member.Name.StartsWith("__LumyteArgumentSet", StringComparison.Ordinal)))
        {
            return "An argument adapter generated member name conflicts with an existing member.";
        }

        foreach (ISymbol member in Members(component).Where(value => HasAttribute(value, "ComposeParameter")))
        {
            INamedTypeSymbol? adapter = ParameterAdapter(component, member);
            if (adapter is not null && ValidateClosedAdapter(adapter, MemberType(member), compilation) is { } failure)
            {
                return failure;
            }
        }

        return null;
    }

    private static string? ValidateClosedAdapter(INamedTypeSymbol adapter, ITypeSymbol value, Compilation compilation)
    {
        for (int index = 0; index < adapter.Arity; index++)
        {
            ITypeParameterSymbol constraint = adapter.OriginalDefinition.TypeParameters[index];
            ITypeSymbol argument = adapter.TypeArguments[index];
            if ((constraint.HasValueTypeConstraint && (!argument.IsValueType || argument.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
                || (constraint.HasReferenceTypeConstraint && !argument.IsReferenceType) || (constraint.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
                || (constraint.HasNotNullConstraint && argument.NullableAnnotation == NullableAnnotation.Annotated)
                || (constraint.HasConstructorConstraint && !argument.IsValueType && !(argument is ITypeParameterSymbol parameter && parameter.HasConstructorConstraint)
                    && !(argument is INamedTypeSymbol named && !named.IsAbstract && named.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 0)))
                || constraint.ConstraintTypes.Any(required => !compilation.ClassifyCommonConversion(argument, CloseConstraint(required, adapter, compilation)).IsImplicit))
            {
                return "The member type does not satisfy argument adapter constraints.";
            }
        }

        return adapter.AllInterfaces.Any(contract => contract.OriginalDefinition.ToDisplayString() == Prefix + "IComposeArgument<T>"
            && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], value))
            ? null : "The adapter must implement IComposeArgument for the exact declared parameter type.";
    }

    private static ITypeSymbol CloseConstraint(ITypeSymbol type, INamedTypeSymbol adapter, Compilation compilation)
    {
        for (int index = 0; index < adapter.Arity; index++)
        {
            type = SubstituteConstraint(type, adapter.OriginalDefinition.TypeParameters[index], adapter.TypeArguments[index], compilation);
        }

        return type;
    }
}
