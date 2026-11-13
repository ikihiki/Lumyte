using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Lumyte.Graphics.Generators;

/// <summary>Generates synchronous wire cursors and physically ordered opaque references.</summary>
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
        var referenceMembers = new List<ISymbol>();
        var referenceOffsets = new List<string>();
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

            string referenceOffset = Offset(wire, slot, offsets, offsetNames);
            referenceMembers.Add(member);
            referenceOffsets.Add(referenceOffset);
        }

        bool hasReferences = referenceMembers.Count != 0;
        string emitter = hasReferences ? ReferenceEmitter(type, wire, referenceMembers, referenceOffsets) : string.Empty;
        string wireExpression = isClass ? "this.@" + storage!.Name : "this";
        string writeBody = hasReferences
            ? "        global::System.Runtime.CompilerServices.Unsafe.SkipInit(out " + Full(wire) + " wire);\n" +
                "        global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(global::System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in " + wireExpression + ", 1)).CopyTo(global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref wire, 1)));\n" +
                "        var emitter = new __ReferenceEmitter(this);\n        global::Lumyte.Graphics.Abstractions.ShaderWire.Write(in wire, ref emitter, ref writer);\n"
            : "        global::Lumyte.Graphics.Abstractions.ShaderWire.Write(in " + wireExpression + ", ref writer);\n";
        AttributeData? attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Lumyte.Graphics.Abstractions.ShaderTypeNameAttribute");
        string name = attr?.ConstructorArguments[0].Value as string ?? type.Name;
        bool generateName = type.FindImplementationForInterfaceMember(contract.GetMembers("ShaderTypeName").Single()) == null;
        string source = "// <auto-generated/>\n#nullable enable\n" +
            (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : "namespace " + type.ContainingNamespace.ToDisplayString() + ";\n") +
            (type.IsReadOnly ? "readonly " : string.Empty) + "partial " + (isClass ? (type.IsRecord ? "record class " : "class ") : (type.IsRecord ? "record struct " : "struct ")) + "@" + type.Name + "\n{\n" +
            (generateName ? "    static string global::Lumyte.Graphics.Abstractions.IShaderArguments.ShaderTypeName => " + Quote(name) + ";\n" : string.Empty) +
            "    static int global::Lumyte.Graphics.Abstractions.IShaderArguments.TransferSize => global::System.Runtime.CompilerServices.Unsafe.SizeOf<" + Full(wire) + ">();\n" +
            offsets + emitter + "    private static class __TransferDiagnostics\n    {\n" +
            "        static __TransferDiagnostics() { }\n" + diagnosticOffsets +
            "        internal static readonly global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderTransferField> Fields = global::System.Array.AsReadOnly(new global::Lumyte.Graphics.Abstractions.ShaderTransferField[] {\n" + fields + "        });\n    }\n" +
            "    static global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderTransferField> global::Lumyte.Graphics.Abstractions.IShaderArguments.TransferFields => __TransferDiagnostics.Fields;\n" +
            "    global::System.Collections.Generic.IReadOnlyList<global::Lumyte.Graphics.Abstractions.ShaderTransferField> global::Lumyte.Graphics.Abstractions.IShaderArguments.GetTransferFields() => __TransferDiagnostics.Fields;\n" +
            "    void global::Lumyte.Graphics.Abstractions.IShaderArguments.Write<TWriter>(ref TWriter writer)\n    {\n" + writeBody + "    }\n" + properties + "}\n";
        output.AddSource("ShaderArguments_" + string.Concat(type.ToDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string ReferenceEmitter(INamedTypeSymbol owner, INamedTypeSymbol wire, List<ISymbol> members, List<string> offsets)
    {
        var source = new StringBuilder();
        source.AppendLine("    private static class __ReferencePlan");
        source.AppendLine("    {");
        source.AppendLine("    static __ReferencePlan() { }");
        source.AppendLine("    internal static readonly int[] Order = __CreateReferenceOrder();");
        source.AppendLine("    private static int[] __CreateReferenceOrder()");
        source.AppendLine("    {");
        source.Append("        int[] offsets = new int[] { ").Append(string.Join(", ", offsets)).AppendLine(" };");
        source.Append("        int[] order = new int[] { ").Append(string.Join(", ", Enumerable.Range(0, members.Count))).AppendLine(" };");
        source.AppendLine("        global::System.Array.Sort(offsets, order);");

        // Invalid layouts are cached as data, never as a poisoned type initializer.
        source.AppendLine("        long end = 0;");
        source.AppendLine("        foreach (int offset in offsets)");
        source.AppendLine("        {");
        source.Append("            if (offset < 0 || offset < end || (long)offset + 16 > global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(Full(wire)).AppendLine(">())");
        source.AppendLine("            {");
        source.AppendLine("                return global::System.Array.Empty<int>();");
        source.AppendLine("            }");
        source.AppendLine("            end = (long)offset + 16;");
        source.AppendLine("        }");
        source.AppendLine("        return order;");
        source.AppendLine("    }");
        source.AppendLine("    }");
        source.AppendLine("    private readonly struct __ReferenceEmitter : global::Lumyte.Graphics.Abstractions.IShaderReferenceEmitter");
        source.AppendLine("    {");
        source.Append("        private readonly ").Append(Full(owner)).AppendLine(" _owner;");
        source.Append("        internal __ReferenceEmitter(").Append(Full(owner)).AppendLine(" owner) { _owner = owner; }");
        source.AppendLine("        public void Emit<TCursor>(ref TCursor cursor)");
        source.AppendLine("            where TCursor : global::Lumyte.Graphics.Abstractions.IShaderReferenceCursor, allows ref struct");
        source.AppendLine("        {");
        source.Append("            if (__ReferencePlan.Order.Length != ").Append(members.Count).AppendLine(")");
        source.AppendLine("            {");
        source.AppendLine("                throw new global::System.ArgumentException(\"Reference slots must be non-overlapping and inside the wire value.\");");
        source.AppendLine("            }");
        source.AppendLine("            foreach (int index in __ReferencePlan.Order)");
        source.AppendLine("            {");
        source.AppendLine("                switch (index)");
        source.AppendLine("                {");
        for (int i = 0; i < members.Count; i++)
        {
            source.Append("                    case ").Append(i).Append(": cursor.WriteReference(").Append(offsets[i]).Append(", _owner.@").Append(members[i].Name).AppendLine("); break;");
        }

        source.AppendLine("                }");
        source.AppendLine("            }");
        source.AppendLine("        }");
        source.AppendLine("    }");
        return source.ToString();
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
