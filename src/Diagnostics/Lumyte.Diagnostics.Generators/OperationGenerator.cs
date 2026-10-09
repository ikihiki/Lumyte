using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Diagnostics.Generators;

/// <summary>Generates explicitly opted-in, typed diagnostic operations without reflection.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class OperationGenerator : IIncrementalGenerator
{
    private const string Prefix = "Lumyte.Diagnostics.";
    private static readonly DiagnosticDescriptor _error1 = new("LMDIAG001", "Invalid diagnostic operation", "{0}", "Lumyte.Diagnostics", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor _error2 = new("LMDIAG002", "Invalid diagnostic operation", "{0}", "Lumyte.Diagnostics", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor _error3 = new("LMDIAG003", "Invalid diagnostic operation", "{0}", "Lumyte.Diagnostics", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor _error4 = new("LMDIAG004", "Invalid diagnostic operation", "{0}", "Lumyte.Diagnostics", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor _error5 = new("LMDIAG005", "Invalid diagnostic operation", "{0}", "Lumyte.Diagnostics", DiagnosticSeverity.Error, isEnabledByDefault: true);
    private static readonly DiagnosticDescriptor[] _errors = [_error1, _error2, _error3, _error4, _error5];

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> operations = context.SyntaxProvider.ForAttributeWithMetadataName(
            Prefix + "DiagnosticOperationAttribute", static (node, _) => node is MethodDeclarationSyntax, static (attribute, _) => (IMethodSymbol)attribute.TargetSymbol).Collect();
        context.RegisterSourceOutput(operations, static (output, methods) => Generate(output, methods));
    }

    private static void Generate(SourceProductionContext output, ImmutableArray<IMethodSymbol> methods)
    {
        var groups = new Dictionary<INamedTypeSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (IMethodSymbol method in methods)
        {
            if (!groups.TryGetValue(method.ContainingType, out List<IMethodSymbol>? group))
            {
                group = new List<IMethodSymbol>();
                groups.Add(method.ContainingType, group);
            }

            group.Add(method);
        }

        foreach (KeyValuePair<INamedTypeSymbol, List<IMethodSymbol>> group in groups)
        {
            Emit(output, group.Key, group.Value);
        }
    }

    private static void Emit(SourceProductionContext output, INamedTypeSymbol type, List<IMethodSymbol> methods)
    {
        bool valid = true;
        void Error(int index, ISymbol symbol, string message)
        {
            output.ReportDiagnostic(Diagnostic.Create(_errors[index - 1], symbol.Locations.FirstOrDefault(), message));
            valid = false;
        }

        if (type.TypeKind != TypeKind.Class || type.IsRecord || type.IsAbstract || type.IsStatic || type.Arity != 0
            || type.ContainingType != null || type.DeclaredAccessibility != Accessibility.Public
            || type.BaseType?.SpecialType != SpecialType.System_Object || type.GetMembers("Configure").Length != 0
            || type.GetMembers().Any(member => member.Name.StartsWith("__Lumyte", StringComparison.Ordinal))
            || type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is not ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            Error(1, type, "Use a public, non-generic partial class without a base class or manual Configure.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var registrations = new List<(string Id, string Code)>();
        foreach (IMethodSymbol method in methods)
        {
            AttributeData attribute = Attribute(method, "DiagnosticOperation")!;
            string id = NamedString(attribute, "Id") ?? Kebab(method.Name);
            int permission = attribute.ConstructorArguments.Length == 0 ? 1 : (int)attribute.ConstructorArguments[0].Value!;
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || type.GetMembers(method.Name).OfType<IMethodSymbol>().Count() != 1)
            {
                Error(3, method, "Operation IDs must be unique and overloads are unsupported.");
            }

            if (permission < 0 || permission > 2)
            {
                Error(5, method, "Unknown permission.");
            }

            bool hasContext = method.Parameters.Length > 0 && method.Parameters[0].Type.ToDisplayString() == Prefix + "DiagnosticOperationContext";
            if (method.IsStatic || method.IsAsync || method.IsGenericMethod || method.MethodKind != MethodKind.Ordinary
                || method.IsAbstract || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsOptional || parameter.IsParams)
                || method.ReturnType is not INamedTypeSymbol result || result.OriginalDefinition.ToDisplayString() != Prefix + "DiagnosticResult<T>"
                || result.TypeArguments[0] is not INamedTypeSymbol shape || shape.TypeKind != TypeKind.Class || shape.Arity != 0
                || shape.DeclaredAccessibility != Accessibility.Public || shape.BaseType?.SpecialType != SpecialType.System_Object)
            {
                Error(2, method, "Use a synchronous instance method returning DiagnosticResult<public scalar record/class>.");
                continue;
            }

            var inputFields = new List<string>();
            var calls = new List<string>();
            if (hasContext)
            {
                calls.Add("context");
            }

            var argumentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (IParameterSymbol parameter in method.Parameters.Skip(hasContext ? 1 : 0))
            {
                AttributeData? argument = Attribute(parameter, "DiagnosticArgument");
                string name = ConstructorString(argument) ?? Kebab(parameter.Name);
                string? kind = Kind(parameter.Type);
                if (kind == null || parameter.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    Error(4, parameter, "Only non-null bool, long, double and string arguments are supported.");
                    continue;
                }

                if (!argumentIds.Add(name) || string.IsNullOrWhiteSpace(name))
                {
                    Error(3, parameter, "Duplicate or empty argument ID.");
                }

                string bounds = Constraints(argument, kind, parameter, Error);
                inputFields.Add("new(" + Literal(name) + ", global::Lumyte.Diagnostics.DiagnosticValueKind." + kind + bounds + ")");
                calls.Add("arguments.Get" + (kind == "Boolean" ? "Boolean" : kind) + "(" + Literal(name) + ")");
            }

            var resultFields = new List<string>();
            var encoded = new List<string>();
            var outputIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (IPropertySymbol property in shape.GetMembers().OfType<IPropertySymbol>())
            {
                if (Attribute(property, "DiagnosticIgnore") != null || property.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                string? kind = Kind(property.Type);
                if (kind == null || property.IsStatic || property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                    || property.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    Error(4, property, "Result properties must be readable, non-null scalars or explicitly ignored.");
                    continue;
                }

                AttributeData? member = Attribute(property, "DiagnosticMember");
                string name = ConstructorString(member) ?? Kebab(property.Name);
                if (!outputIds.Add(name) || string.IsNullOrWhiteSpace(name))
                {
                    Error(3, property, "Duplicate or empty result ID.");
                }

                resultFields.Add("new(" + Literal(name) + ", global::Lumyte.Diagnostics.DiagnosticValueKind." + kind + Constraints(member, kind, property, Error) + ")");
                encoded.Add("[" + Literal(name) + "] = global::Lumyte.Diagnostics.DiagnosticValue.From(value.@" + property.Name + ")");
            }

            bool requiresRevision = NamedBool(attribute, "RequiresRevision");
            string code = "builder.Operation(new(" + Literal(id) + ", " + Literal(NamedString(attribute, "DisplayName") ?? method.Name)
                + ", (global::Lumyte.Diagnostics.DiagnosticPermission)" + permission
                + ", [" + string.Join(",", inputFields) + "], [" + string.Join(",", resultFields) + "]"
                + ", RequiresRevision: " + (requiresRevision ? "true" : "false") + "), (context, arguments) => @" + method.Name
                + "(" + string.Join(",", calls) + ").ToOperationResult(static value => new global::System.Collections.Generic.Dictionary<string, global::Lumyte.Diagnostics.DiagnosticValue> {"
                + string.Join(",", encoded) + "}));";
            registrations.Add((id, code));
        }

        if (!valid)
        {
            return;
        }

        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            source.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).Append(";\n");
        }

        source.Append("public ").Append(type.IsSealed ? "sealed " : string.Empty).Append("partial class @").Append(type.Name)
            .Append(" : global::Lumyte.Diagnostics.IDiagnosticContributor {\nvoid global::Lumyte.Diagnostics.IDiagnosticContributor.Configure(global::Lumyte.Diagnostics.DiagnosticBuilder builder) {\n");
        foreach ((string _, string code) in registrations.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            source.AppendLine(code);
        }

        source.Append("}\n}\n");
        output.AddSource(type.ToDisplayString() + ".Diagnostics.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static string Constraints(AttributeData? attribute, string kind, ISymbol symbol, Action<int, ISymbol, string> error)
    {
        double min = NamedDouble(attribute, "Minimum", double.NegativeInfinity);
        double max = NamedDouble(attribute, "Maximum", double.PositiveInfinity);
        int length = (int)(attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == "MaxLength").Value.Value ?? 0);
        if (double.IsNaN(min) || double.IsNaN(max) || min > max || length < 0
            || ((Finite(min) || Finite(max)) && kind is not "Int64" and not "Double")
            || (length != 0 && kind != "String")
            || (kind == "Int64" && ((Finite(min) && (Math.Abs(min) > 9007199254740991 || min != Math.Truncate(min)))
                || (Finite(max) && (Math.Abs(max) > 9007199254740991 || max != Math.Truncate(max))))))
        {
            error(5, symbol, "Invalid bounds or constraints for this scalar type.");
        }

        return (Finite(min) ? ", Minimum: " + min.ToString("R", CultureInfo.InvariantCulture) + "D" : string.Empty)
            + (Finite(max) ? ", Maximum: " + max.ToString("R", CultureInfo.InvariantCulture) + "D" : string.Empty)
            + (length != 0 ? ", MaxLength: " + length : string.Empty);
    }

    private static AttributeData? Attribute(ISymbol symbol, string name)
        => symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == Prefix + name + "Attribute");

    private static string? ConstructorString(AttributeData? attribute)
        => attribute != null && attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;

    private static string? NamedString(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static bool NamedBool(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is true;

    private static double NamedDouble(AttributeData? attribute, string name, double fallback)
        => attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is double value ? value : fallback;

    private static string? Kind(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Boolean => "Boolean",
        SpecialType.System_Int64 => "Int64",
        SpecialType.System_Double => "Double",
        SpecialType.System_String => "String",
        _ => null,
    };

    private static string Kebab(string name)
    {
        string split = Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1-$2", RegexOptions.CultureInvariant);
        return Regex.Replace(split, "([a-z0-9])([A-Z])", "$1-$2", RegexOptions.CultureInvariant).Replace('_', '-').ToLowerInvariant();
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
