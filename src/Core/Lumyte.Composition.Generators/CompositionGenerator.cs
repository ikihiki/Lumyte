using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Composition.Generators;

/// <summary>Generates delegate factories, content indexers, and target operation extensions.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class CompositionGenerator : IIncrementalGenerator
{
    private const string Prefix = "Lumyte.Composition.";
    private static readonly DiagnosticDescriptor _invalidDeclaration = new(
        "LYC001",
        "Unsupported composition declaration",
        "{0}",
        "Lumyte.Composition",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> components = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Prefix + "ComposableAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (attribute, _) => (INamedTypeSymbol)attribute.TargetSymbol)
            .Collect();
        context.RegisterSourceOutput(components.Combine(context.CompilationProvider), static (output, input) =>
            Generate(output, input.Left, input.Right));
        IncrementalValuesProvider<IMethodSymbol> actions = context.SyntaxProvider.ForAttributeWithMetadataName(
            Prefix + "ComposeActionAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attribute, _) => (IMethodSymbol)attribute.TargetSymbol);
        context.RegisterSourceOutput(actions, static (output, method) =>
        {
            if (!HasAttribute(method.ContainingType, "Composable"))
            {
                output.ReportDiagnostic(Diagnostic.Create(
                    _invalidDeclaration,
                    method.Locations.FirstOrDefault(),
                    "ComposeAction methods must be declared in a Composable class."));
            }
        });
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> components, Compilation compilation)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol component in components.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            string? failure = Validate(component, compilation, out string factoryName, out string propertyName);
            string identity = component.ContainingNamespace + "." + factoryName + "." + propertyName;
            string accessorIdentity = identity + "Factory";
            if (failure is null && (names.Contains(identity) || (component.Arity != 0 && names.Contains(accessorIdentity))))
            {
                failure = "Factory member '" + identity + "' is declared more than once.";
            }

            if (failure is null)
            {
                names.Add(identity);
                if (component.Arity != 0)
                {
                    names.Add(accessorIdentity);
                }
            }

            if (failure is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(_invalidDeclaration, component.Locations.FirstOrDefault(), failure));
                continue;
            }

            string source = Render(component, propertyName);
            context.AddSource(component.ToDisplayString().Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + ".Composition.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string? Validate(INamedTypeSymbol component, Compilation compilation, out string factoryName, out string propertyName)
    {
        AttributeData marker = component.GetAttributes().First(attribute => IsAttribute(attribute, "Composable"));
        factoryName = NamedString(marker, "Factory")
            ?? compilation.Assembly.GetAttributes().FirstOrDefault(attribute => IsAttribute(attribute, "CompositionDefaults"))
                ?.ConstructorArguments.FirstOrDefault().Value as string
            ?? "Compose";
        propertyName = NamedString(marker, "Name") ?? component.Name;
        INamedTypeSymbol? definitions = component.ContainingType;
        INamedTypeSymbol? outer = definitions?.ContainingType;
        if (!ValidName(factoryName) || !ValidName(propertyName))
        {
            return "Factory and property names must be nonempty C# identifiers.";
        }

        if (definitions?.Name != "Definitions" || outer?.Name != factoryName || outer.ContainingType is not null
            || !PublicPartial(component) || component.IsAbstract || component.IsStatic
            || !PublicPartial(definitions) || !definitions.IsStatic || definitions.Arity != 0
            || !PublicPartial(outer) || !outer.IsStatic || outer.Arity != 0)
        {
            return "Use a public partial component in public static partial " + factoryName + ".Definitions.";
        }

        if (component.TypeParameters.Any(parameter => parameter.AllowsRefLikeType || parameter.ConstraintTypes.Any(type => !PublicType(type))))
        {
            return "Component type parameters require public constraints and cannot allow ref struct.";
        }

        if (component.Arity != 0 && (outer.GetMembers(propertyName + "Factory").Length != 0
            || outer.GetMembers("__Lumyte" + component.Name + "FactoryCache").Length != 0))
        {
            return "A generic factory accessor or cache name conflicts with an existing member.";
        }

        if (!component.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0))
        {
            return "A parameterless constructor is required for '" + component.Name + "'.";
        }

        if (outer.GetMembers(propertyName).Length != 0 || outer.GetMembers(component.Name + "CompositionExtensions").Length != 0
            || definitions.GetMembers(component.Name + "Factory").Length != 0 || propertyName == outer.Name || propertyName == "Definitions")
        {
            return "A factory property or delegate name conflicts with an existing member.";
        }

        string extensionName = outer.Name + component.Name + "CompositionExtensions";
        if (component.ContainingNamespace.GetTypeMembers(extensionName).Length != 0)
        {
            return "The generated extension class conflicts with an existing namespace type.";
        }

        ISymbol[] all = Members(component).ToArray();
        ISymbol[] parameters = all.Where(member => HasAttribute(member, "ComposeParameter")).ToArray();
        ISymbol[] contents = all.Where(member => HasAttribute(member, "ComposeContent")).ToArray();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISymbol member in parameters.Concat(contents))
        {
            if (all.Count(candidate => candidate.Name == member.Name) != 1)
            {
                return "Hidden or overridden composition members are unsupported.";
            }

            if (!Writable(member) || member.IsStatic || !compilation.IsSymbolAccessibleWithin(member, component)
                || (member is IPropertySymbol property && (property.IsIndexer || property.SetMethod is null
                    || !compilation.IsSymbolAccessibleWithin(property.SetMethod, component))))
            {
                return "Composition member '" + member.Name + "' must be writable and accessible from its component.";
            }

            if (HasAttribute(member, "ComposeParameter") && HasAttribute(member, "ComposeContent"))
            {
                return "A member cannot be both a parameter and content.";
            }

            if (!PublicType(MemberType(member)))
            {
                return "Composition member types must be publicly accessible.";
            }
        }

        foreach (ISymbol member in parameters)
        {
            string name = ParameterName(member);
            if (!ValidName(name) || name == "with" || !parameterNames.Add(name))
            {
                return "Factory parameter names must be unique and cannot be 'with'.";
            }
        }

        if (all.Any(member => Required(member) && !parameters.Contains(member, SymbolEqualityComparer.Default)))
        {
            return "Every required member must be a ComposeParameter.";
        }

        if (contents.Length > 1 || (contents.Length == 1 && (ContentItem(MemberType(contents[0])) is null
            || (contents[0] is IPropertySymbol contentProperty && contentProperty.SetMethod!.IsInitOnly))))
        {
            return "Use at most one settable array or supported collection interface for content; init-only content is unsupported.";
        }

        if (contents.Length != 0 && component.GetMembers().OfType<IPropertySymbol>().Any(property => property.IsIndexer))
        {
            return "The generated content indexer conflicts with an existing indexer.";
        }

        if (component.GetMembers().Any(member => member.Name.StartsWith("__Lumyte", StringComparison.Ordinal)))
        {
            return "The __Lumyte member prefix is reserved for generated code.";
        }

        var actionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (IMethodSymbol method in component.GetMembers().OfType<IMethodSymbol>().Where(member => HasAttribute(member, "ComposeAction")))
        {
            if (!method.IsStatic || !method.ReturnsVoid || method.IsAsync || method.Arity != 0
                || method.MethodKind != MethodKind.Ordinary || method.Parameters.Length == 0
                || !method.Parameters[0].Type.IsReferenceType
                || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsOptional || parameter.IsParams
                    || !PublicType(parameter.Type) || parameter.Name is "__factory" or "__target"))
            {
                return "ComposeAction requires a synchronous non-generic static void method with a reference target first and public by-value, non-optional parameters.";
            }

            if (!actionNames.Add(method.Name))
            {
                return "Overloaded ComposeAction names are unsupported.";
            }

            if (method.Name is "Invoke" or "DynamicInvoke" or "Equals" or "GetHashCode" or "GetType" or "ToString" or "Clone" or "GetInvocationList")
            {
                return "A composition action name conflicts with a delegate instance member.";
            }
        }

        return null;
    }

    private static string Render(INamedTypeSymbol component, string propertyName)
    {
        INamedTypeSymbol outer = component.ContainingType.ContainingType;
        ISymbol[] parameters = Members(component).Where(member => HasAttribute(member, "ComposeParameter"))
            .OrderByDescending(Required).ToArray();
        ISymbol? content = Members(component).FirstOrDefault(member => HasAttribute(member, "ComposeContent"));
        IMethodSymbol[] actions = component.GetMembers().OfType<IMethodSymbol>().Where(member => HasAttribute(member, "ComposeAction"))
            .OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
        string type = TypeName(component);
        string generics = TypeParameters(component);
        string constraints = Constraints(component);
        string signature = string.Join(", ", parameters.Select(parameter => ParameterDeclaration(parameter)).Concat(new[]
        {
            "global::System.Collections.Generic.IReadOnlyList<global::System.Action<" + type + ">>? @with = null",
        }));
        var builder = new StringBuilder("// <auto-generated />\n#nullable enable\n");
        if (!component.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append("namespace ").Append(component.ContainingNamespace.ToDisplayString()).AppendLine(";");
        }

        builder.Append("public static partial class ").Append(Escape(outer.Name)).AppendLine("\n{");
        builder.AppendLine(component.Arity == 0
            ? "/// <summary>Gets the cached component factory delegate.</summary>"
            : "/// <summary>Constructs a component for the selected type arguments.</summary>");
        if (component.Arity == 0)
        {
            builder.Append("public static Definitions.").Append(Escape(component.Name + "Factory")).Append(' ').Append(Escape(propertyName))
                .Append(" { get; } = Definitions.").Append(Escape(component.Name)).AppendLine(".__LumyteCreate;");
        }
        else
        {
            builder.Append("public static ").Append(type).Append(' ').Append(Escape(propertyName)).Append(generics)
                .Append('(').Append(signature).Append(')').Append(constraints).Append(" => ")
                .Append(Escape(propertyName + "Factory")).Append(generics).Append("()(")
                .Append(string.Join(", ", parameters.Select(parameter => Escape(ParameterName(parameter))).Concat(new[] { "@with" }))).AppendLine(");");
            builder.AppendLine("/// <summary>Gets the cached factory for the selected type arguments.</summary>");
            builder.Append("public static Definitions.").Append(Escape(component.Name + "Factory")).Append(generics).Append(' ')
                .Append(Escape(propertyName + "Factory")).Append(generics).Append("()").Append(constraints)
                .Append(" => __Lumyte").Append(component.Name).Append("FactoryCache").Append(generics).AppendLine(".Value;");
            builder.Append("private static class __Lumyte").Append(component.Name).Append("FactoryCache").Append(generics).Append(constraints).AppendLine("\n{");
            builder.Append("internal static readonly Definitions.").Append(Escape(component.Name + "Factory")).Append(generics)
                .Append(" Value = ").Append(type).AppendLine(".__LumyteCreate;\n}");
        }

        builder.AppendLine("public static partial class Definitions\n{");
        builder.AppendLine("/// <summary>Constructs a component and applies additional actions.</summary>");
        foreach (ISymbol parameter in parameters)
        {
            builder.Append("/// <param name=\"").Append(ParameterName(parameter)).AppendLine("\">The component setting.</param>");
        }

        builder.AppendLine("/// <param name=\"with\">Ordered additional actions; null means none.</param>");
        builder.AppendLine("/// <returns>A new configured component.</returns>");
        builder.Append("public delegate ").Append(type).Append(' ').Append(Escape(component.Name + "Factory"))
            .Append(generics).Append('(').Append(signature).Append(')').Append(constraints).AppendLine(";");
        builder.Append("public partial class ").Append(Escape(component.Name)).Append(generics).Append(constraints).AppendLine("\n{");
        if (component.InstanceConstructors.Any(constructor => constructor.IsImplicitlyDeclared))
        {
            builder.AppendLine("/// <summary>Initializes a new component with its declared defaults.</summary>");
            builder.Append("public ").Append(Escape(component.Name)).AppendLine("() { }");
        }

        builder.AppendLine("private readonly struct __LumyteConstructor { }");
        builder.AppendLine("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        builder.Append("private ").Append(Escape(component.Name)).Append("(__LumyteConstructor marker");
        foreach (ISymbol parameter in parameters)
        {
            builder.Append(", ").Append(ParameterDeclaration(parameter, optionalDefault: false));
        }

        builder.AppendLine(") : this()\n{");
        foreach (ISymbol parameter in parameters)
        {
            string name = Escape(ParameterName(parameter));
            if (!Required(parameter))
            {
                builder.Append("if (").Append(name).AppendLine(".HasValue)\n{");
            }

            builder.Append("this.").Append(Escape(parameter.Name)).Append(" = ").Append(name)
                .AppendLine(Required(parameter) ? ";" : ".Value;\n}");
        }

        builder.AppendLine("}");
        builder.Append("internal static ").Append(type).Append(" __LumyteCreate(").Append(signature).AppendLine(")\n{");
        builder.Append("var instance = new ").Append(type).Append("(default(__LumyteConstructor)");
        foreach (ISymbol parameter in parameters)
        {
            builder.Append(", ").Append(Escape(ParameterName(parameter)));
        }

        builder.AppendLine(");\nif (@with is not null)\n{");
        builder.AppendLine("foreach (var action in @with)\n{\nif (action is null) throw new global::System.ArgumentException(\"Null composition action.\", nameof(@with));\n}");
        builder.AppendLine("foreach (var action in @with)\n{\naction(instance);\n}\n}\nreturn instance;\n}");
        if (content is not null)
        {
            builder.AppendLine("/// <summary>Replaces children and returns this same instance.</summary>");
            builder.AppendLine("/// <param name=\"content\">The ordered children.</param>");
            builder.AppendLine("/// <returns>This instance.</returns>");
            builder.Append("public ").Append(type).Append(" this[params ").Append(TypeName(ContentItem(MemberType(content))!))
                .AppendLine("[] content]\n{\nget\n{");
            builder.AppendLine("global::System.ArgumentNullException.ThrowIfNull(content);");
            builder.Append("this.").Append(Escape(content.Name)).AppendLine(" = content;\nreturn this;\n}\n}");
        }

        foreach (IMethodSymbol method in actions)
        {
            string actionSignature = ActionSignature(method);
            string arguments = ActionArguments(method);
            builder.Append("internal static global::System.Action<").Append(TypeName(method.Parameters[0].Type)).Append("> ")
                .Append(Escape("__LumyteAction" + method.Name)).Append('(').Append(actionSignature).Append(") => __target => ")
                .Append(Escape(method.Name)).Append("(__target").Append(arguments.Length == 0 ? string.Empty : ", " + arguments).AppendLine(");");
        }

        builder.AppendLine("}\n}\n}");
        if (actions.Length != 0)
        {
            builder.AppendLine("/// <summary>Provides operations for this component's factory.</summary>");
            builder.Append("public static class ").Append(Escape(outer.Name + component.Name + "CompositionExtensions")).AppendLine("\n{");
            foreach (IMethodSymbol method in actions)
            {
                builder.AppendLine("/// <summary>Creates an action invoking the declared operation on a target.</summary>");
                builder.AppendLine("/// <param name=\"__factory\">The factory identifying this extension.</param>");
                foreach (IParameterSymbol parameter in method.Parameters.Skip(1))
                {
                    builder.Append("/// <param name=\"").Append(parameter.Name).AppendLine("\">The captured operation argument.</param>");
                }

                builder.AppendLine("/// <returns>An action invoking the operation when applied.</returns>");
                builder.Append("public static global::System.Action<").Append(TypeName(method.Parameters[0].Type)).Append("> ")
                    .Append(Escape(method.Name)).Append(generics).Append("(this ").Append(TypeName(outer)).Append(".Definitions.")
                    .Append(Escape(component.Name + "Factory")).Append(generics).Append(" __factory");
                string actionSignature = ActionSignature(method);
                builder.Append(actionSignature.Length == 0 ? string.Empty : ", " + actionSignature)
                    .Append(')').Append(constraints).Append(" => ").Append(type).Append('.').Append(Escape("__LumyteAction" + method.Name))
                    .Append('(').Append(ActionArguments(method)).AppendLine(");");
            }

            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static string TypeParameters(INamedTypeSymbol type) => type.Arity == 0 ? string.Empty
        : "<" + string.Join(", ", type.TypeParameters.Select(parameter => Escape(parameter.Name))) + ">";

    private static string Constraints(INamedTypeSymbol type)
    {
        var builder = new StringBuilder();
        foreach (ITypeParameterSymbol parameter in type.TypeParameters)
        {
            var items = new List<string>();
            if (parameter.HasUnmanagedTypeConstraint)
            {
                items.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                items.Add("struct");
            }
            else if (parameter.HasReferenceTypeConstraint)
            {
                items.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            else if (parameter.HasNotNullConstraint)
            {
                items.Add("notnull");
            }

            items.AddRange(parameter.ConstraintTypes.Select(TypeName));
            if (parameter.HasConstructorConstraint)
            {
                items.Add("new()");
            }

            if (parameter.AllowsRefLikeType)
            {
                items.Add("allows ref struct");
            }

            if (items.Count != 0)
            {
                builder.Append(" where ").Append(Escape(parameter.Name)).Append(" : ").Append(string.Join(", ", items));
            }
        }

        return builder.ToString();
    }

    private static string ActionSignature(IMethodSymbol method) => string.Join(", ", method.Parameters.Skip(1)
        .Select(parameter => TypeName(parameter.Type) + " " + Escape(parameter.Name)));

    private static string ActionArguments(IMethodSymbol method) => string.Join(", ", method.Parameters.Skip(1)
        .Select(parameter => Escape(parameter.Name)));

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol component)
    {
        for (INamedTypeSymbol? type = component; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers().Where(member => member is IFieldSymbol or IPropertySymbol)
                .OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                yield return member;
            }
        }
    }

    private static bool PublicPartial(INamedTypeSymbol type) => type.DeclaredAccessibility == Accessibility.Public
        && type.DeclaringSyntaxReferences.Length != 0
        && type.DeclaringSyntaxReferences.All(reference => reference.GetSyntax() is ClassDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool PublicType(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => PublicType(array.ElementType),
        INamedTypeSymbol named => named.DeclaredAccessibility == Accessibility.Public
            && (named.ContainingType is null || PublicType(named.ContainingType)) && named.TypeArguments.All(PublicType),
        _ => false,
    };

    private static bool Writable(ISymbol member) => member switch
    {
        IFieldSymbol field => !field.IsReadOnly && !field.IsConst,
        IPropertySymbol property => property.SetMethod is not null,
        _ => false,
    };

    private static bool Required(ISymbol member) => member switch
    {
        IFieldSymbol field => field.IsRequired,
        IPropertySymbol property => property.IsRequired,
        _ => false,
    };

    private static ITypeSymbol MemberType(ISymbol member) => member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;

    private static ITypeSymbol? ContentItem(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1 ? array.ElementType : null;
        }

        if (type is INamedTypeSymbol named && named.Arity == 1 && named.OriginalDefinition.ToDisplayString() is
            "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IReadOnlyCollection<T>"
            or "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.ICollection<T>" or "System.Collections.Generic.IList<T>")
        {
            return named.TypeArguments[0];
        }

        return null;
    }

    private static string ParameterDeclaration(ISymbol parameter, bool optionalDefault = true)
    {
        string type = TypeName(MemberType(parameter));
        return Required(parameter) ? type + " " + Escape(ParameterName(parameter))
            : "global::Lumyte.Composition.Optional<" + type + "> " + Escape(ParameterName(parameter)) + (optionalDefault ? " = default" : string.Empty);
    }

    private static string ParameterName(ISymbol member)
    {
        string name = member.Name.TrimStart('_');
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

    private static bool HasAttribute(ISymbol member, string name) => member.GetAttributes().Any(attribute => IsAttribute(attribute, name));

    private static bool IsAttribute(AttributeData attribute, string name) => attribute.AttributeClass?.ToDisplayString() == Prefix + name + "Attribute";

    private static string? NamedString(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static bool ValidName(string name) => SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None;

    private static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
