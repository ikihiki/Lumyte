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

[Generator(LanguageNames.CSharp)]
public sealed class CompositionGenerator : IIncrementalGenerator
{
    private const string ComposableAttribute = "Lumyte.Composition.ComposableAttribute";
    private const string ParameterAttribute = "Lumyte.Composition.ComposeParameterAttribute";
    private const string ContentAttribute = "Lumyte.Composition.ComposeContentAttribute";
    private const string DefaultsAttribute = "Lumyte.Composition.CompositionDefaultsAttribute";

    private static readonly DiagnosticDescriptor s_mustBePartial = new(
        "LYC001", "Composable type must be partial",
        "Composable type '{0}' must be declared partial", "Lumyte.Composition",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_constructorRequired = new(
        "LYC002", "Parameterless constructor required",
        "Composable type '{0}' must have a parameterless constructor", "Lumyte.Composition",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_memberMustBeWritable = new(
        "LYC003", "Composition member must be writable",
        "Composition member '{0}' must be writable from its declaring type", "Lumyte.Composition",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedContent = new(
        "LYC004", "Unsupported content collection",
        "Content member '{0}' must be an array or a supported generic collection interface", "Lumyte.Composition",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_multipleContent = new(
        "LYC005", "Only one content member is supported",
        "Composable type '{0}' declares more than one content member", "Lumyte.Composition",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_inaccessibleInheritedMember = new(
        "LYC006", "Inherited composition member is inaccessible",
        "Inherited composition member '{0}' must be accessible from '{1}'", "Lumyte.Composition",
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_duplicateParameter = new(
        "LYC007", "Composition parameter name is duplicated",
        "Composition parameter name '{0}' is declared more than once in the inheritance chain of '{1}'",
        "Lumyte.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> components = context.SyntaxProvider.ForAttributeWithMetadataName(
            ComposableAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        IncrementalValueProvider<(ImmutableArray<INamedTypeSymbol> Components, Compilation Compilation)> input =
            components.Collect().Combine(context.CompilationProvider);

        context.RegisterSourceOutput(input, static (spc, value) => Execute(spc, value.Components, value.Compilation));
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> components,
        Compilation compilation)
    {
        string defaultFactory = GetDefaultFactory(compilation) ?? "Compose";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol component in components)
        {
            if (!seen.Add(component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                continue;
            }

            GenerateComponent(context, component, defaultFactory);
        }
    }

    private static void GenerateComponent(
        SourceProductionContext context,
        INamedTypeSymbol component,
        string defaultFactory)
    {
        Location location = component.Locations.FirstOrDefault() ?? Location.None;
        if (!IsPartial(component))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_mustBePartial, location, component.Name));
            return;
        }

        if (!component.InstanceConstructors.Any(static ctor => ctor.Parameters.Length == 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_constructorRequired, location, component.Name));
            return;
        }

        AttributeData marker = component.GetAttributes().First(a => SymbolName(a.AttributeClass) == ComposableAttribute);
        string factory = NamedString(marker, "Factory") ?? defaultFactory;
        string method = NamedString(marker, "Name") ?? component.Name;
        var parameters = new List<ISymbol>();
        var contents = new List<ISymbol>();

        for (INamedTypeSymbol? type = component;
             type is not null && type.SpecialType != SpecialType.System_Object;
             type = type.BaseType)
        {
            ISymbol[] declared = type.GetMembers()
                .Where(static member => member is IFieldSymbol or IPropertySymbol)
                .OrderBy(static member => SourcePosition(member))
                .ToArray();
            foreach (ISymbol member in declared)
            {
                if (HasAttribute(member, ParameterAttribute))
                {
                    parameters.Add(member);
                }

                if (HasAttribute(member, ContentAttribute))
                {
                    contents.Add(member);
                }
            }
        }

        if (contents.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_multipleContent, location, component.Name));
            return;
        }

        foreach (ISymbol member in parameters.Concat(contents))
        {
            if (!IsWritable(member))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_memberMustBeWritable,
                    member.Locations.FirstOrDefault() ?? location, member.Name));
                return;
            }

            if (!SymbolEqualityComparer.Default.Equals(member.ContainingType, component)
                && !IsAccessibleFromDerived(member, component))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_inaccessibleInheritedMember,
                    member.Locations.FirstOrDefault() ?? location, member.Name, component.Name));
                return;
            }
        }

        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISymbol parameter in parameters)
        {
            string parameterName = Camel(parameter.Name.TrimStart('_'));
            if (parameterNames.Add(parameterName))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(s_duplicateParameter,
                parameter.Locations.FirstOrDefault() ?? location, parameterName, component.Name));
            return;
        }

        ITypeSymbol? contentItem = contents.Count == 0 ? null : GetContentItemType(MemberType(contents[0]));
        if (contents.Count == 1 && contentItem is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_unsupportedContent,
                contents[0].Locations.FirstOrDefault() ?? location, contents[0].Name));
            return;
        }

        string source = Render(component, factory, method, parameters, contents.FirstOrDefault(), contentItem);
        context.AddSource(SafeHint(component) + ".Composition.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string Render(
        INamedTypeSymbol component,
        string factory,
        string method,
        IReadOnlyList<ISymbol> parameters,
        ISymbol? content,
        ITypeSymbol? contentItem)
    {
        string ns = component.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : component.ContainingNamespace.ToDisplayString();
        string type = component.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        if (ns.Length > 0)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";").AppendLine();
        }

        sb.Append(Accessibility(component)).Append(" partial class ").Append(component.Name).AppendLine();
        sb.AppendLine("{");
        sb.Append("    internal static ").Append(type).Append(" __CreateComposed() => new ").Append(type).AppendLine("();");
        for (int index = 0; index < parameters.Count; index++)
        {
            ISymbol parameter = parameters[index];
            sb.Append("    internal void __SetComposed").Append(index).Append('(')
                .Append(TypeName(MemberType(parameter))).Append(" value) => this.")
                .Append(Escape(parameter.Name)).AppendLine(" = value;");
        }

        if (content is not null && contentItem is not null)
        {
            sb.AppendLine();
            sb.Append("    public ").Append(type).Append(" this[params ")
                .Append(TypeName(contentItem)).AppendLine("[] content]");
            sb.AppendLine("    {");
            sb.AppendLine("        get");
            sb.AppendLine("        {");
            sb.Append("            this.").Append(Escape(content.Name)).AppendLine(" = content;");
            sb.AppendLine("            return this;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        sb.Append("public static partial class ").Append(Escape(factory)).AppendLine();
        sb.AppendLine("{");
        sb.Append("    public static ").Append(type).Append(' ').Append(Escape(method)).Append('(');
        for (int index = 0; index < parameters.Count; index++)
        {
            if (index > 0)
            {
                sb.Append(", ");
            }

            ITypeSymbol parameterType = MemberType(parameters[index]);
            sb.Append("global::Lumyte.Composition.Optional<").Append(TypeName(parameterType)).Append("> ")
                .Append(Escape(Camel(parameters[index].Name.TrimStart('_')))).Append(" = default");
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.Append("        ").Append(type).Append(" value = ").Append(type).AppendLine(".__CreateComposed();");
        for (int index = 0; index < parameters.Count; index++)
        {
            string name = Escape(Camel(parameters[index].Name.TrimStart('_')));
            sb.Append("        if (").Append(name).Append(".HasValue) value.__SetComposed")
                .Append(index).Append('(').Append(name).AppendLine(".Value);");
        }
        sb.AppendLine("        return value;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string? GetDefaultFactory(Compilation compilation)
    {
        AttributeData? attribute = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => SymbolName(a.AttributeClass) == DefaultsAttribute);
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static bool IsPartial(INamedTypeSymbol type)
        => type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<ClassDeclarationSyntax>()
            .All(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool IsWritable(ISymbol member) => member switch
    {
        IFieldSymbol field => !field.IsReadOnly && !field.IsConst,
        IPropertySymbol property => property.SetMethod is not null && !property.SetMethod.IsInitOnly,
        _ => false,
    };

    private static bool IsAccessibleFromDerived(ISymbol member, INamedTypeSymbol derived)
    {
        return member.DeclaredAccessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => true,
            Microsoft.CodeAnalysis.Accessibility.Protected => true,
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => true,
            Microsoft.CodeAnalysis.Accessibility.Internal =>
                SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, derived.ContainingAssembly),
            Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal =>
                SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, derived.ContainingAssembly),
            _ => false,
        };
    }

    private static ITypeSymbol MemberType(ISymbol member) => member switch
    {
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => throw new InvalidOperationException(),
    };

    private static ITypeSymbol? GetContentItemType(ITypeSymbol collection)
    {
        if (collection is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }

        if (collection is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
        {
            return null;
        }

        string definition = named.ConstructedFrom.ToDisplayString();
        return definition is "System.Collections.Generic.IEnumerable<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>"
            or "System.Collections.Generic.IReadOnlyList<T>"
            or "System.Collections.Generic.ICollection<T>"
            or "System.Collections.Generic.IList<T>"
            ? named.TypeArguments[0]
            : null;
    }

    private static bool HasAttribute(ISymbol symbol, string name)
        => symbol.GetAttributes().Any(a => SymbolName(a.AttributeClass) == name);

    private static string SymbolName(INamedTypeSymbol? symbol) => symbol?.ToDisplayString() ?? string.Empty;

    private static string? NamedString(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static int SourcePosition(ISymbol symbol)
        => symbol.Locations.FirstOrDefault(static location => location.IsInSource)?.SourceSpan.Start ?? int.MaxValue;

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Accessibility(INamedTypeSymbol type)
        => type.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public ? "public" : "internal";

    private static string Camel(string value)
        => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string Escape(string value)
        => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None ? "@" + value : value;

    private static string SafeHint(INamedTypeSymbol type)
        => type.ToDisplayString().Replace('<', '_').Replace('>', '_').Replace('.', '_');
}
