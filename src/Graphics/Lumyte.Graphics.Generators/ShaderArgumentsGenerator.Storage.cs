using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Lumyte.Graphics.Generators;

/// <summary>Generates direct AOT-safe shader member readers for application structures.</summary>
public sealed partial class ShaderArgumentsGenerator
{
    private static string StorageViews(INamedTypeSymbol type, string owner)
    {
        const string Api = "global::Lumyte.Graphics.Abstractions.";
        string data = Api + "ShaderDataStorage<" + owner + ">";
        var result = new StringBuilder();
        result.AppendLine("    /// <summary>Creates typed persistent views over backend-owned transfer storage.</summary>")
            .AppendLine("    /// <param name=\"buffer\">The backend buffer that certifies the storage schema.</param>")
            .AppendLine("    /// <param name=\"initial\">The initial logical values.</param>")
            .AppendLine("    /// <returns>Independent persistent storage and its cached element views.</returns>")
            .Append("    public static Storage CreateStorage(").Append(Api).Append("IGraphicsShaderDataBuffer<").Append(owner)
            .Append("> buffer, global::System.ReadOnlySpan<").Append(owner).AppendLine("> initial)")
            .AppendLine("    {")
            .AppendLine("        if (buffer is null) throw new global::System.ArgumentNullException(nameof(buffer));")
            .AppendLine("        return new Storage(buffer.CreateStorage(initial));")
            .AppendLine("    }")
            .AppendLine("    /// <summary>Owns cached write-through views of persistent shader data.</summary>")
            .AppendLine("    public sealed class Storage")
            .AppendLine("    {")
            .AppendLine("        private readonly Element[] __elements;")
            .AppendLine("        /// <summary>Wraps backend-certified storage and creates each element view once.</summary>")
            .AppendLine("        /// <param name=\"data\">The backend-certified storage.</param>")
            .Append("        public Storage(").Append(data).AppendLine(" data)")
            .AppendLine("        {")
            .AppendLine("            if (data is null) throw new global::System.ArgumentNullException(nameof(data));")
            .AppendLine("            int count = data.Count;")
            .AppendLine("            if (count < 0) throw new global::System.ArgumentOutOfRangeException(nameof(data), \"Storage count cannot be negative.\");")
            .AppendLine("            Data = data;")
            .AppendLine("            __elements = new Element[count];")
            .AppendLine("            for (int index = 0; index < count; index++) __elements[index] = new Element(data, index);")
            .AppendLine("        }")
            .AppendLine("        /// <summary>Gets the number of cached element views.</summary>")
            .AppendLine("        public int Count => __elements.Length;")
            .AppendLine("        /// <summary>Gets the underlying backend storage for explicit transfers.</summary>")
            .Append("        public ").Append(data).AppendLine(" Data { get; }")
            .AppendLine("        /// <summary>Gets a readonly view whose properties write through to persistent storage.</summary>")
            .AppendLine("        /// <param name=\"index\">The element index.</param>")
            .AppendLine("        /// <returns>The cached element view, never a reference to transfer bytes.</returns>")
            .AppendLine("        public ref readonly Element this[int index]")
            .AppendLine("        {")
            .AppendLine("            get")
            .AppendLine("            {")
            .AppendLine("                if ((uint)index >= (uint)__elements.Length) throw new global::System.ArgumentOutOfRangeException(nameof(index));")
            .AppendLine("                return ref __elements[index];")
            .AppendLine("            }")
            .AppendLine("        }")
            .AppendLine("        /// <summary>Replaces one complete logical value.</summary>")
            .AppendLine("        /// <param name=\"index\">The element index.</param>")
            .AppendLine("        /// <param name=\"value\">The complete replacement value.</param>")
            .Append("        public void Set(int index, in ").Append(owner).AppendLine(" value) => Data.Set(index, in value);")
            .AppendLine("        /// <summary>Unwraps typed views for backend storage transfers.</summary>")
            .AppendLine("        /// <param name=\"storage\">The non-null typed storage.</param>")
            .AppendLine("        /// <returns>The underlying backend storage.</returns>")
            .Append("        public static implicit operator ").Append(data).AppendLine("(Storage storage)")
            .AppendLine("        {")
            .AppendLine("            if (storage is null) throw new global::System.ArgumentNullException(nameof(storage));")
            .AppendLine("            return storage.Data;")
            .AppendLine("        }");

        // Accessors live alongside Element, not inside their parent's member scope. Allocate
        // all type names against root member names so even a property named Element is safe.
        var names = new HashSet<string>(ViewMembers(type).Select(m => m.Name))
        {
            "Storage", "Element", "Count", "Data", "Set", "__elements", "Item",
        };
        string element = UniqueViewName("Element", new HashSet<string>(ViewMembers(type).Select(m => m.Name)) { "Storage" });

        // The wrapper's indexer and array use the chosen element name as well.
        if (element != "Element")
        {
            result.Replace("Element[]", element + "[]").Replace("new Element[", "new " + element + "[")
                .Replace("new Element(", "new " + element + "(").Replace("ref readonly Element this", "ref readonly " + element + " this");
        }

        names.Add(element);
        EmitStorageView(result, type, owner, data, element, "StorageMembers", names);
        result.AppendLine("    }");
        return result.ToString();
    }

    private static IEnumerable<ISymbol> ViewMembers(INamedTypeSymbol type) => type.GetMembers().Where(m =>
        !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public &&
        (m is IFieldSymbol || (m is IPropertySymbol p && !p.IsIndexer && p.GetMethod != null)));

    private static string UniqueViewName(string candidate, HashSet<string> names)
    {
        while (!names.Add(candidate))
        {
            candidate += "_";
        }

        return candidate;
    }

    private static void EmitStorageView(StringBuilder result, INamedTypeSymbol type, string owner, string data, string name, string tokens, HashSet<string> typeNames)
    {
        ISymbol[] members = ViewMembers(type).ToArray();
        var localNames = new HashSet<string>(members.Select(m => m.Name)) { name };
        string ownerField = UniqueViewName("__owner", localNames);
        string indexField = UniqueViewName("__index", localNames);
        string ownerProperty = UniqueViewName("__Owner", localNames);
        var children = new List<(INamedTypeSymbol Type, string Name, string Field, string Token)>();
        var properties = new StringBuilder();
        foreach (ISymbol member in members)
        {
            ITypeSymbol memberType = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
            string token = owner + "." + tokens + ".@" + member.Name;
            bool reference = memberType is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "Lumyte.Graphics.Abstractions.IGpuRef<T>";
            properties.AppendLine("            /// <summary>Gets or updates this persistent shader member.</summary>");
            if (reference || Numeric(memberType))
            {
                string valueType = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string method = reference ? "Reference" : "Value";
                properties.Append("            public ").Append(valueType).Append(" @").Append(member.Name).AppendLine()
                    .AppendLine("            {")
                    .Append("                get => ").Append(ownerProperty).Append(".Get").Append(method).Append("(").Append(indexField).Append(", ").Append(token).AppendLine(");")
                    .Append("                set => ").Append(ownerProperty).Append(".Set").Append(method).Append("(").Append(indexField).Append(", ").Append(token).AppendLine(", value);")
                    .AppendLine("            }");
            }
            else
            {
                // Serialization validation has already rejected all non-inline member types.
                var nested = (INamedTypeSymbol)memberType;

                // A sibling accessor must also avoid its own logical property names.
                var forbidden = new HashSet<string>(typeNames
                    .Concat(ViewMembers(nested).Select(m => m.Name)).Concat(localNames));
                string childName = UniqueViewName(member.Name + "Accessor", forbidden);
                typeNames.Add(childName);
                string childField = UniqueViewName("__child" + children.Count, localNames);
                children.Add((nested, childName, childField, tokens + ".@" + member.Name));
                properties.AppendLine("            [global::System.Diagnostics.CodeAnalysis.UnscopedRef]")
                    .Append("            public ref readonly ").Append(childName).Append(" @").Append(member.Name).AppendLine()
                    .AppendLine("            {")
                    .AppendLine("                get")
                    .AppendLine("                {")
                    .Append("                    _ = ").Append(ownerProperty).AppendLine(";")
                    .Append("                    return ref ").Append(childField).AppendLine(";")
                    .AppendLine("                }")
                    .AppendLine("            }");
            }
        }

        result.AppendLine("        /// <summary>A readonly write-through property view; default views are invalid.</summary>")
            .Append("        public readonly struct ").Append(name).AppendLine()
            .AppendLine("        {")
            .Append("            private readonly ").Append(data).Append("? ").Append(ownerField).AppendLine(";")
            .Append("            private readonly int ").Append(indexField).AppendLine(";");
        foreach ((INamedTypeSymbol Type, string Name, string Field, string Token) child in children)
        {
            result.Append("            private readonly ").Append(child.Name).Append(' ').Append(child.Field).AppendLine(";");
        }

        result.Append("            private ").Append(data).Append(' ').Append(ownerProperty).Append(" => ").Append(ownerField)
            .AppendLine(" ?? throw new global::System.InvalidOperationException(\"A default shader data view has no storage owner.\");")
            .Append("            internal ").Append(name).Append('(').Append(data).AppendLine(" owner, int index)")
            .AppendLine("            {")
            .Append("                ").Append(ownerField).AppendLine(" = owner;")
            .Append("                ").Append(indexField).AppendLine(" = index;");
        foreach ((INamedTypeSymbol Type, string Name, string Field, string Token) child in children)
        {
            result.Append("                ").Append(child.Field).Append(" = new ").Append(child.Name).AppendLine("(owner, index);");
        }

        result.AppendLine("            }").Append(properties).AppendLine("        }");
        foreach ((INamedTypeSymbol Type, string Name, string Field, string Token) child in children)
        {
            EmitStorageView(result, child.Type, owner, data, child.Name, child.Token, typeNames);
        }
    }
}
