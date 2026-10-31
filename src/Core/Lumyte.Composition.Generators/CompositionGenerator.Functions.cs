using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Composition.Generators;

/// <summary>Generates host-managed wrappers for static construction functions.</summary>
public sealed partial class CompositionGenerator
{
    private static void GenerateFunctions(SourceProductionContext context, ImmutableArray<IMethodSymbol> functions, Compilation compilation, HashSet<string> names)
    {
        foreach (IMethodSymbol method in functions.Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AttributeData marker = method.GetAttributes().First(attribute => IsAttribute(attribute, "Composable"));
            string factoryName = NamedString(marker, "Factory")
                ?? compilation.Assembly.GetAttributes().FirstOrDefault(attribute => IsAttribute(attribute, "CompositionDefaults"))?.ConstructorArguments.FirstOrDefault().Value as string
                ?? "Compose";
            string name = NamedString(marker, "Name") ?? method.Name;
            string identity = method.ContainingNamespace + "." + factoryName + "." + name;
            INamedTypeSymbol? adapter = FunctionAdapter(method.ReturnType);
            string? failure = ValidateFunction(method, factoryName, name, adapter, compilation);
            if (failure is null && !names.Add(identity))
            {
                failure = "Factory member '" + identity + "' is declared more than once.";
            }

            if (failure is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(_invalidDeclaration, method.Locations.FirstOrDefault(), failure));
                continue;
            }

            context.AddSource(method.ContainingType.ToDisplayString() + "." + name + ".Function.g.cs", SourceText.From(RenderFunction(method, name, adapter!), Encoding.UTF8));
        }
    }

    private static INamedTypeSymbol? FunctionAdapter(ITypeSymbol result)
    {
        for (var type = result as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            AttributeData? attribute = type.GetAttributes().FirstOrDefault(value => IsAttribute(value, "ComposeFunctionAdapter"));
            if (attribute?.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol adapter)
            {
                return adapter;
            }
        }

        return null;
    }

    private static string? ValidateFunction(IMethodSymbol method, string factoryName, string name, INamedTypeSymbol? adapter, Compilation compilation)
    {
        INamedTypeSymbol definitions = method.ContainingType;
        INamedTypeSymbol? outer = definitions.ContainingType;
        if (!ValidName(factoryName) || !ValidName(name) || definitions.Name != "Definitions" || outer?.Name != factoryName || outer.ContainingType is not null
            || !PublicPartial(definitions) || !definitions.IsStatic || definitions.Arity != 0
            || !PublicPartial(outer) || !outer.IsStatic || outer.Arity != 0
            || !method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.IsAsync || method.IsAbstract || method.IsExtern
            || method.ReturnsByRef || method.ReturnsByRefReadonly || method.ReturnNullableAnnotation == NullableAnnotation.Annotated)
        {
            return "Use a public synchronous static function in public static partial " + factoryName + ".Definitions.";
        }

        if (name == outer.Name || name == "Definitions" || name.StartsWith("__Lumyte", StringComparison.Ordinal)
            || outer.GetMembers(name).Length != 0 || outer.GetMembers("__LumyteFunction" + name).Length != 0)
        {
            return "A function factory name conflicts with an existing member.";
        }

        if (adapter is null || !PublicType(adapter) || adapter.IsGenericType || !adapter.IsAbstract || adapter.TypeKind != TypeKind.Class
            || !adapter.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected)
            || !adapter.GetMembers("Build").OfType<IMethodSymbol>().Any(build => build.IsAbstract && !build.IsStatic && build.Arity == 0
                && build.Parameters.Length == 0 && build.DeclaredAccessibility == Accessibility.Protected
                && compilation.ClassifyCommonConversion(method.ReturnType, build.ReturnType).IsImplicit
                && compilation.ClassifyCommonConversion(adapter, build.ReturnType).IsImplicit))
        {
            return "The return type needs a ComposeFunctionAdapter selecting a public abstract base with a protected parameterless constructor and protected abstract Build().";
        }

        if (adapter.GetMembers().Any(member => member.IsAbstract && member.Name != "Build"))
        {
            return "A function adapter cannot require abstract members other than Build().";
        }

        if (!Members(adapter).OfType<IPropertySymbol>().Any(property => property.Name == "Key" && property.Type.SpecialType == SpecialType.System_String
            && property.SetMethod?.DeclaredAccessibility == Accessibility.Public && !property.SetMethod.IsInitOnly))
        {
            return "A function adapter must expose a public writable string Key for declaration identity.";
        }

        if (method.TypeParameters.Any(parameter => parameter.AllowsRefLikeType || parameter.Name == "key" || parameter.Name.StartsWith("__", StringComparison.Ordinal) || parameter.ConstraintTypes.Any(type => !PublicType(type)))
            || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams || parameter.Type.IsRefLikeType || parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer
                || !PublicType(parameter.Type) || parameter.Name == "key" || parameter.Name.StartsWith("__", StringComparison.Ordinal)))
        {
            return "Function parameters require public value types, no ref/params/ref struct, and cannot use reserved names 'key' or '__...'.";
        }

        // Constraints and IComposeArgument<T> contracts are the same as for class factories.
        string? argumentFailure = ValidateAdapter((INamedTypeSymbol)method.ReturnType, compilation);
        if (argumentFailure is not null)
        {
            return argumentFailure;
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            INamedTypeSymbol? argumentAdapter = ParameterAdapter((INamedTypeSymbol)method.ReturnType, parameter);
            if (argumentAdapter is not null && ValidateClosedAdapter(argumentAdapter, parameter.Type, compilation) is { } failure)
            {
                return failure;
            }
        }

        return null;
    }

    private static string RenderFunction(IMethodSymbol method, string name, INamedTypeSymbol adapter)
    {
        INamedTypeSymbol outer = method.ContainingType.ContainingType;
        var argumentOwner = (INamedTypeSymbol)method.ReturnType;
        string generic = method.Arity == 0 ? string.Empty : "<" + string.Join(", ", method.TypeParameters.Select(parameter => Escape(parameter.Name))) + ">";
        string wrapper = "__LumyteFunction" + name;
        string constraints = Constraints(method.TypeParameters);
        string result = TypeName(adapter.GetMembers("Build").OfType<IMethodSymbol>().First(build => build.IsAbstract).ReturnType);
        string signature = string.Join(", ", method.Parameters.Select(parameter => FunctionParameter(parameter, ParameterAdapter(argumentOwner, parameter))).Concat(new[] { "string? key = null" }));
        string arguments = string.Join(", ", method.Parameters.Select(parameter => Escape(parameter.Name)).Concat(new[] { "key" }));
        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!outer.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append("namespace ").Append(outer.ContainingNamespace.ToDisplayString()).AppendLine(";");
        }

        builder.Append("public static partial class ").Append(Escape(outer.Name)).AppendLine("\n{");
        builder.AppendLine("/// <summary>Creates a host-managed declaration of the construction function.</summary>");
        builder.Append("public static ").Append(result).Append(' ').Append(Escape(name)).Append(generic).Append('(').Append(signature).Append(')').Append(constraints).AppendLine("\n{");
        for (int index = 0; index < method.Parameters.Length; index++)
        {
            IParameterSymbol parameter = method.Parameters[index];
            string value = Escape(parameter.Name);
            if (ParameterAdapter(argumentOwner, parameter) is not null)
            {
                value = parameter.HasExplicitDefaultValue
                    ? value + ".IsSpecified ? " + value + ".GetInitialValue() : " + FunctionDefault(parameter)
                    : value + ".GetInitialValue()";
            }

            builder.Append(TypeName(parameter.Type)).Append(" __initial").Append(index).Append(" = ").Append(value).AppendLine(";");
        }

        builder.Append("var __instance = new ").Append(wrapper).Append(generic).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, method.Parameters.Length).Select(index => "__initial" + index))).AppendLine(") { Key = key };");
        for (int index = 0; index < method.Parameters.Length; index++)
        {
            IParameterSymbol parameter = method.Parameters[index];
            if (ParameterAdapter(argumentOwner, parameter) is not null)
            {
                builder.Append(Escape(parameter.Name)).Append(".AttachTo(__instance, ").Append(wrapper).Append(generic).Append(".__key").Append(index)
                    .Append(", __initial").Append(index).Append(", static (__target, __value) => __target.__arg").Append(index).AppendLine(" = __value);");
            }
        }

        builder.Append("if ((object)__instance is global::Lumyte.Composition.ICompositionRebuildable __rebuildable) __rebuildable.SetCompositionFactory(() => ")
            .Append(Escape(name)).Append(generic).Append('(').Append(arguments).AppendLine("));\nreturn __instance;\n}");
        builder.Append("private sealed class ").Append(wrapper).Append(generic).Append(" : ").Append(TypeName(adapter)).Append(constraints).AppendLine("\n{");
        for (int index = 0; index < method.Parameters.Length; index++)
        {
            builder.Append("internal static readonly object __key").Append(index).AppendLine(" = new object();");
            builder.Append("internal ").Append(TypeName(method.Parameters[index].Type)).Append(" __arg").Append(index).AppendLine(";");
        }

        builder.Append("internal ").Append(wrapper).Append('(')
            .Append(string.Join(", ", method.Parameters.Select((parameter, index) => TypeName(parameter.Type) + " __value" + index))).AppendLine(")\n{");
        for (int index = 0; index < method.Parameters.Length; index++)
        {
            builder.Append("__arg").Append(index).Append(" = __value").Append(index).AppendLine(";");
        }

        builder.AppendLine("}");
        builder.Append("protected override ").Append(result).Append(" Build() => Definitions.").Append(Escape(method.Name)).Append(generic).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, method.Parameters.Length).Select(index => "__arg" + index))).AppendLine(");\n}\n}");
        return builder.ToString();
    }

    private static string FunctionParameter(IParameterSymbol parameter, INamedTypeSymbol? adapter)
    {
        bool adapted = adapter is not null;
        return (adapted ? TypeName(adapter!) : TypeName(parameter.Type)) + " " + Escape(parameter.Name)
            + (parameter.HasExplicitDefaultValue ? " = " + (adapted ? "default" : FunctionDefault(parameter)) : string.Empty);
    }

    private static string FunctionDefault(IParameterSymbol parameter)
    {
        if (parameter.ExplicitDefaultValue is not { } value)
        {
            return "default!";
        }

        string literal = value switch
        {
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            float number when float.IsNaN(number) => "global::System.Single.NaN",
            float number when float.IsPositiveInfinity(number) => "global::System.Single.PositiveInfinity",
            float number when float.IsNegativeInfinity(number) => "global::System.Single.NegativeInfinity",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
            double number when double.IsNaN(number) => "global::System.Double.NaN",
            double number when double.IsPositiveInfinity(number) => "global::System.Double.PositiveInfinity",
            double number when double.IsNegativeInfinity(number) => "global::System.Double.NegativeInfinity",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            _ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
        };
        ITypeSymbol type = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : parameter.Type;
        return type.TypeKind == TypeKind.Enum ? "(" + TypeName(type) + ")" + literal : literal;
    }
}
