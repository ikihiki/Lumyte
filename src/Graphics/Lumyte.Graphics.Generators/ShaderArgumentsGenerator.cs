using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Graphics.Generators;

/// <summary>Generates direct AOT-safe shader member readers for application structures.</summary>
[Generator]
public sealed partial class ShaderArgumentsGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor _unsupported = new(
        "LUMG001", "Unsupported shader value", "Shader arguments cannot serialize '{0}': {1}", "Lumyte.Graphics", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol?> types = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax declaration && (declaration.AttributeLists.Count != 0 || declaration.BaseList != null),
            static (syntax, token) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, token) as INamedTypeSymbol);
        context.RegisterSourceOutput(types.Collect(), static (output, candidates) =>
        {
            var emitted = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (INamedTypeSymbol? type in candidates)
            {
                if (type != null && emitted.Add(type))
                {
                    Emit(output, type);
                }
            }
        });
    }

    private static void Emit(SourceProductionContext output, INamedTypeSymbol? type)
    {
        if (type == null || type.TypeKind != TypeKind.Struct)
        {
            return;
        }

        INamedTypeSymbol? contract = type.AllInterfaces.FirstOrDefault(i => i.ToDisplayString() == "Lumyte.Graphics.Abstractions.IShaderArguments");
        if (contract == null)
        {
            return;
        }

        bool generateName = type.FindImplementationForInterfaceMember(contract.GetMembers("ShaderTypeName").Single()) == null;
        bool generateWrite = type.FindImplementationForInterfaceMember(contract.GetMembers("Write").Single()) == null;
        if (!generateName && !generateWrite)
        {
            return;
        }

        if (type.IsGenericType || type.ContainingType != null || type.DeclaredAccessibility == Accessibility.Private)
        {
            Report(output, type, "generic or nested shader argument types are not supported");
            return;
        }

        if (type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            Report(output, type, "generated shader value types must be partial");
            return;
        }

        bool generateStorage = generateWrite && type.AllInterfaces.Any(i => i.ToDisplayString() == "Lumyte.Graphics.Abstractions.IShaderData");
        if (generateStorage)
        {
            foreach (string reserved in new[] { "StorageMembers", "Storage", "CreateStorage" })
            {
                if (type.Name == reserved || type.GetMembers(reserved).Length != 0)
                {
                    Report(output, type, "generated persistent storage requires the reserved member name " + reserved);
                    return;
                }
            }
        }

        if (generateStorage && type.GetMembers().OfType<IPropertySymbol>().Any(p => p.ExplicitInterfaceImplementations.Any(i =>
            i.Name == "StorageSchema" && i.ContainingType.ToDisplayString() == "Lumyte.Graphics.Abstractions.IShaderData")))
        {
            Report(output, type, "generated persistent storage cannot replace an explicit IShaderData.StorageSchema implementation");
            return;
        }

        var body = new StringBuilder();
        StringBuilder? schema = generateStorage ? new StringBuilder() : null;
        StringBuilder? tokens = generateStorage ? new StringBuilder() : null;
        string owner = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (generateWrite && !Members(output, type, "this", string.Empty, body, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), schema, tokens, owner, "StorageMembers", 2))
        {
            return;
        }

        string storageSource = string.Empty;
        if (generateStorage)
        {
            // A read-only wrapper prevents callers from mutating the cached schema array.
            string schemaField = "__lumyteStorageSchema";
            while (type.GetMembers(schemaField).Length != 0 || schemaField == type.Name)
            {
                schemaField += "_";
            }

            storageSource = "    private static readonly global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderDataMember> " + schemaField + " =\n" +
                "        global::System.Array.AsReadOnly(new global::Lumyte.Graphics.Abstractions.ShaderDataMember[]\n        {\n" + schema + "        });\n" +
                "    static global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderDataMember> global::Lumyte.Graphics.Abstractions.IShaderData.StorageSchema => " + schemaField + ";\n" +
                "    /// <summary>Provides typed tokens for persistent shader data updates.</summary>\n    public static class StorageMembers\n    {\n" + tokens + "    }\n";
        }

        if (generateStorage)
        {
            storageSource += StorageViews(type, owner);
        }

        AttributeData? nameAttribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Lumyte.Graphics.Abstractions.ShaderTypeNameAttribute");
        string shaderName = nameAttribute?.ConstructorArguments[0].Value as string ?? type.Name;
        string id = new string(type.ToDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        string source = "// <auto-generated/>\n#nullable enable\n" +
            (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : "namespace " + type.ContainingNamespace.ToDisplayString() + ";\n") +
            (type.IsReadOnly ? "readonly " : string.Empty) + "partial " + (type.IsRecord ? "record struct " : "struct ") + "@" + type.Name + "\n{\n" +
            (generateName ? "    static string global::Lumyte.Graphics.Abstractions.IShaderArguments.ShaderTypeName => " + Quote(shaderName) + ";\n" : string.Empty) +
            (generateWrite ? "    void global::Lumyte.Graphics.Abstractions.IShaderArguments.Write<TWriter>(ref TWriter writer)\n    {\n" + body + "    }\n" : string.Empty) + storageSource + "}\n";
        output.AddSource("ShaderArguments_" + id + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static bool Members(SourceProductionContext output, INamedTypeSymbol type, string expression, string prefix, StringBuilder body, HashSet<ITypeSymbol> stack, StringBuilder? schema, StringBuilder? tokens, string owner, string tokenContainer, int tokenIndent)
    {
        if (!stack.Add(type))
        {
            Report(output, type, "inline member cycle");
            return false;
        }

        foreach (ISymbol member in type.GetMembers().Where(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public))
        {
            ITypeSymbol? memberType = member is IFieldSymbol field ? field.Type : member is IPropertySymbol property && !property.IsIndexer && property.GetMethod != null ? property.Type : null;
            if (memberType == null)
            {
                continue;
            }

            if (member is IPropertySymbol p && p.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is PropertyDeclarationSyntax syntax &&
                (syntax.ExpressionBody != null || syntax.AccessorList?.Accessors.Any(a => a.Body != null || a.ExpressionBody != null) == true)))
            {
                Report(output, member, "custom getters are not supported");
                return false;
            }

            string path = prefix + member.Name;
            string access = expression + ".@" + member.Name;
            if (tokens != null && member.Name == tokenContainer)
            {
                Report(output, member, "persistent storage member path " + path + " conflicts with its generated StorageMembers container " + tokenContainer);
                return false;
            }

            if (memberType is INamedTypeSymbol reference && reference.OriginalDefinition.ToDisplayString() == "Lumyte.Graphics.Abstractions.IGpuRef<T>")
            {
                body.Append("        writer.WriteReference(").Append(Quote(path)).Append(", ").Append(access).AppendLine(");");
                StorageMember(schema, tokens, owner, member.Name, path, reference.TypeArguments[0], true, tokenIndent);
            }
            else if (Numeric(memberType))
            {
                body.Append("        writer.WriteValue(").Append(Quote(path)).Append(", ").Append(access).AppendLine(");");
                StorageMember(schema, tokens, owner, member.Name, path, memberType, false, tokenIndent);
            }
            else if (memberType is INamedTypeSymbol nested && nested.TypeKind == TypeKind.Struct && nested.SpecialType == SpecialType.None && !nested.IsGenericType)
            {
                tokens?.Append(new string(' ', tokenIndent * 4)).AppendLine("/// <summary>Provides tokens for a nested shader value.</summary>")
                    .Append(new string(' ', tokenIndent * 4)).Append("public static class @").Append(member.Name).AppendLine()
                    .Append(new string(' ', tokenIndent * 4)).AppendLine("{");
                if (!Members(output, nested, access, path + ".", body, stack, schema, tokens, owner, member.Name, tokenIndent + 1))
                {
                    return false;
                }

                tokens?.Append(new string(' ', tokenIndent * 4)).AppendLine("}");
            }
            else
            {
                Report(output, member, "unsupported member type " + memberType.ToDisplayString());
                return false;
            }
        }

        stack.Remove(type);
        return true;
    }

    private static void StorageMember(StringBuilder? schema, StringBuilder? tokens, string owner, string name, string path, ITypeSymbol valueType, bool isReference, int indent)
    {
        if (schema == null || tokens == null)
        {
            return;
        }

        string valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        schema.Append("            new global::Lumyte.Graphics.Abstractions.ShaderDataMember(").Append(Quote(path))
            .Append(", typeof(").Append(valueTypeName).Append("), ").Append(isReference ? "true" : "false")
            .Append(isReference ? ", " + ReferenceTarget(valueType) : string.Empty).AppendLine("),");
        tokens.Append(new string(' ', indent * 4)).AppendLine("/// <summary>Gets a typed persistent member token.</summary>")
            .Append(new string(' ', indent * 4)).Append("public static global::Lumyte.Graphics.Abstractions.")
            .Append(isReference ? "ShaderDataReferenceMember<" : "ShaderDataValueMember<").Append(owner).Append(", ").Append(valueTypeName)
            .Append("> @").Append(name).Append(" { get; } = new(").Append(Quote(path)).AppendLine(");");
    }

    private static string ReferenceTarget(ITypeSymbol type)
    {
        if (type.AllInterfaces.Any(i => i.ToDisplayString() == "Lumyte.Graphics.Abstractions.IShaderArguments"))
        {
            return "global::Lumyte.Graphics.Abstractions.IShaderArguments.TypeName<" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">()";
        }

        string? name = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Lumyte.Graphics.Abstractions.ShaderTypeNameAttribute")?.ConstructorArguments[0].Value as string;
        return Quote(name ?? (type.SpecialType == SpecialType.System_Int32 ? "int" : type.SpecialType == SpecialType.System_UInt32 ? "uint" : type.SpecialType == SpecialType.System_Single ? "float" :
            type.ToDisplayString() == "System.Numerics.Vector2" ? "float2" : type.ToDisplayString() == "System.Numerics.Vector3" ? "float3" :
            type.ToDisplayString() == "System.Numerics.Vector4" ? "float4" : type.ToDisplayString() == "System.Numerics.Matrix4x4" ? "float4x4" : type.Name));
    }

    private static bool Numeric(ITypeSymbol type) => type.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single ||
        type.ToDisplayString() is "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4" or "System.Numerics.Matrix4x4";

    private static string Quote(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static void Report(SourceProductionContext output, ISymbol symbol, string reason) => output.ReportDiagnostic(Diagnostic.Create(_unsupported, symbol.Locations.FirstOrDefault(), symbol.Name, reason));
}
