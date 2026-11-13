using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Graphics.Generators;

/// <summary>Generates one raw unmanaged transfer and opaque reference fixups.</summary>
[Generator]
public sealed class ShaderArgumentsGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor _unsupported = new(
        "LUMG001", "Unsupported shader transfer", "Shader transfer '{0}': {1}", "Lumyte.Graphics", DiagnosticSeverity.Error, true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol?> types = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax declaration && declaration.BaseList != null,
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

    private static void Emit(SourceProductionContext output, INamedTypeSymbol type)
    {
        INamedTypeSymbol? contract = type.AllInterfaces.FirstOrDefault(i => i.ToDisplayString() == "Lumyte.Graphics.Abstractions.IShaderArguments");
        if (contract == null || type.TypeKind is not (TypeKind.Struct or TypeKind.Class))
        {
            return;
        }

        bool generateWrite = type.FindImplementationForInterfaceMember(contract.GetMembers("Write").Single()) == null;
        if (!generateWrite)
        {
            return;
        }

        if (type.IsGenericType || type.ContainingType != null || type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax d && !d.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            Report(output, type, "generated types must be non-generic, top-level and partial");
            return;
        }

        bool isClass = type.TypeKind == TypeKind.Class;
        IFieldSymbol[] candidates = isClass
            ? type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsImplicitlyDeclared && f.DeclaredAccessibility == Accessibility.Private && f.Type.IsUnmanagedType && f.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None }).ToArray()
            : [];
        if ((isClass && candidates.Length != 1) || (!isClass && !type.IsUnmanagedType))
        {
            Report(output, type, "reference-containing values must be classes with exactly one private unmanaged storage struct");
            return;
        }

        IFieldSymbol? storage = isClass ? candidates[0] : null;
        var wire = (INamedTypeSymbol)(storage?.Type ?? type);
        var fields = new StringBuilder();
        var offsets = new StringBuilder();
        var offsetNames = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        var refs = new StringBuilder();
        var properties = new StringBuilder();
        if (isClass)
        {
            foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is PropertyDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                if (!property.Type.IsUnmanagedType || !wire.GetMembers(property.Name).OfType<IFieldSymbol>().Any(f => SymbolEqualityComparer.Default.Equals(f.Type, property.Type)))
                {
                    Report(output, property, "partial numeric properties require a same-named wire field of the same unmanaged type");
                    return;
                }

                properties.Append("    public partial ").Append(Full(property.Type)).Append(" @").Append(property.Name).Append(" { get => this.@").Append(storage!.Name).Append(".@").Append(property.Name).Append("; set => this.@").Append(storage.Name).Append(".@").Append(property.Name).AppendLine(" = value; }");
            }
        }

        // Diagnostic offsets and tables live in a separate lazy type. Only reference
        // fixup offsets are initialized on the ordinary transfer path.
        var diagnosticOffsets = new StringBuilder();
        var diagnosticNames = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
        Describe(wire, string.Empty, "0", fields, diagnosticOffsets, diagnosticNames);
        foreach (ISymbol member in type.GetMembers().Where(m => !m.IsStatic))
        {
            ITypeSymbol? mt = member is IPropertySymbol p ? p.Type : member is IFieldSymbol f && !f.IsImplicitlyDeclared ? f.Type : null;
            if (mt is not INamedTypeSymbol reference || reference.OriginalDefinition.ToDisplayString() != "Lumyte.Graphics.Abstractions.IGpuRef<T>")
            {
                continue;
            }

            IFieldSymbol? slot = wire.GetMembers(member.Name).OfType<IFieldSymbol>().SingleOrDefault();
            if (slot == null || slot.Type.ToDisplayString() != "Lumyte.Graphics.Abstractions.ShaderReferenceSlot")
            {
                Report(output, member, "each reference needs a same-named ShaderReferenceSlot in the private wire struct");
                return;
            }

            refs.Append("        writer.WriteReference(").Append(Offset(wire, slot, offsets, offsetNames)).Append(", this.@").Append(member.Name).AppendLine(");");
        }

        AttributeData? attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Lumyte.Graphics.Abstractions.ShaderTypeNameAttribute");
        string name = attr?.ConstructorArguments[0].Value as string ?? type.Name;
        bool generateName = type.FindImplementationForInterfaceMember(contract.GetMembers("ShaderTypeName").Single()) == null;
        string source = "// <auto-generated/>\n#nullable enable\n" +
            (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : "namespace " + type.ContainingNamespace.ToDisplayString() + ";\n") +
            (type.IsReadOnly ? "readonly " : string.Empty) + "partial " + (isClass ? (type.IsRecord ? "record class " : "class ") : (type.IsRecord ? "record struct " : "struct ")) + "@" + type.Name + "\n{\n" +
            (generateName ? "    static string global::Lumyte.Graphics.Abstractions.IShaderArguments.ShaderTypeName => " + Quote(name) + ";\n" : string.Empty) +
            "    static int global::Lumyte.Graphics.Abstractions.IShaderArguments.TransferSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<" + Full(wire) + ">();\n" +
            offsets + "    private static class __TransferDiagnostics\n    {\n" +
            "        static __TransferDiagnostics() { }\n" + diagnosticOffsets +
            "        internal static readonly global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderTransferField> Fields = global::System.Array.AsReadOnly(new global::Lumyte.Graphics.Abstractions.ShaderTransferField[] {\n" + fields + "        });\n    }\n" +
            "    static global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderTransferField> global::Lumyte.Graphics.Abstractions.IShaderArguments.TransferFields => __TransferDiagnostics.Fields;\n" +
            "    global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderTransferField> global::Lumyte.Graphics.Abstractions.IShaderArguments.GetTransferFields() => __TransferDiagnostics.Fields;\n" +
            "    void global::Lumyte.Graphics.Abstractions.IShaderArguments.Write<TWriter>(ref TWriter writer)\n    {\n        writer.WriteBytes(in " + (isClass ? "this.@" + storage!.Name : "this") + ");\n" + refs + "    }\n" + properties + "}\n";
        output.AddSource("ShaderArguments_" + string.Concat(type.ToDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void Describe(INamedTypeSymbol type, string prefix, string baseOffset, StringBuilder fields, StringBuilder offsets, Dictionary<IFieldSymbol, string> offsetNames)
    {
        foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            string path = prefix + (field.AssociatedSymbol?.Name ?? field.Name);
            string offset = "(" + baseOffset + " + " + Offset(type, field, offsets, offsetNames) + ")";
            bool slot = field.Type.ToDisplayString() == "Lumyte.Graphics.Abstractions.ShaderReferenceSlot";
            if (!slot && field.Type is INamedTypeSymbol nested && nested.SpecialType == SpecialType.None && !Numeric(nested))
            {
                Describe(nested, path + ".", offset, fields, offsets, offsetNames);
            }
            else
            {
                fields.Append("        new(").Append(Quote(path)).Append(", ").Append(offset).Append(", global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(Full(field.Type)).Append(">(), typeof(").Append(Full(field.Type)).Append("), ").Append(slot ? "true" : "false").AppendLine("),");
            }
        }
    }

    private static bool Numeric(ITypeSymbol type) => type.ToDisplayString() is "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4" or "System.Numerics.Matrix4x4";

    private static string Offset(INamedTypeSymbol type, IFieldSymbol field, StringBuilder offsets, Dictionary<IFieldSymbol, string> names)
    {
        if (names.TryGetValue(field, out string? name))
        {
            return name;
        }

        name = "__offset" + names.Count;
        names.Add(field, name);
        string owner = Full(type);
        string value = Full(field.Type);
        offsets.Append("    private static readonly int ").Append(name).Append(" = ").Append(name).AppendLine("Get();");
        offsets.Append("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ").Append(Quote(field.Name)).AppendLine(")]");
        offsets.Append("    private static extern ref ").Append(value).Append(" ").Append(name).Append("Field(ref ").Append(owner).AppendLine(" value);");
        offsets.Append("    private static int ").Append(name).Append("Get() { ").Append(owner).Append(" value = default; return checked((int)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<").Append(owner).Append(", byte>(ref value), ref global::System.Runtime.CompilerServices.Unsafe.As<").Append(value).Append(", byte>(ref ").Append(name).AppendLine("Field(ref value)))); }");
        return name;
    }

    private static string Full(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Quote(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static void Report(SourceProductionContext output, ISymbol symbol, string reason) => output.ReportDiagnostic(Diagnostic.Create(_unsupported, symbol.Locations.FirstOrDefault(), symbol.Name, reason));
}
