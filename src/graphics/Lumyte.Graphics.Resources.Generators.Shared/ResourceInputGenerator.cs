using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

#if LUMYTE_NATIVE
namespace Lumyte.Graphics.Native.Resources.Generators;
#elif LUMYTE_PASS_GRAPH
namespace Lumyte.Graphics.Portable.RenderGraph.Generators;
#else
namespace Lumyte.Graphics.Portable.Resources.Generators;
#endif

/// <summary>Generates managed reference inputs from prepared shader metadata, without loading shaders or assets.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ResourceInputGenerator : IIncrementalGenerator
{
#if LUMYTE_NATIVE
    private const string Suffix = ".native.resources.xml";
    private const string Api = "global::Lumyte.Graphics.Native.Resources.";
    private const string DiagnosticId = "LNRG001";
#else
    private const string Suffix = ".portable.resources.xml";
#if LUMYTE_PASS_GRAPH
    private const string Api = "global::Lumyte.Graphics.Portable.RenderGraph.";
    private const string DiagnosticId = "LPPG001";
#else
    private const string Api = "global::Lumyte.Graphics.Portable.Resources.";
    private const string DiagnosticId = "LPRG001";
#endif
#endif
    private static readonly DiagnosticDescriptor InvalidSchema = new(
        DiagnosticId, "Invalid managed shader input schema", "{0}",
        "Lumyte.Graphics", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var schemas = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static input => Accepts(input.Left.Path, input.Right.GetOptions(input.Left)))
            .Select(static (input, cancellation) => new SchemaText(input.Left.Path, input.Left.GetText(cancellation)?.ToString()))
            .Collect();
        context.RegisterSourceOutput(schemas, static (output, inputs) =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (SchemaText input in inputs)
            {
                output.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Schema schema = Parse(input.Text ?? throw new FormatException("The schema could not be read."));
                    string qualifiedName = schema.Namespace + "." + schema.Name;
                    if (!names.Add(qualifiedName)) { throw new FormatException("Duplicate generated type: " + qualifiedName); }
                    output.AddSource(qualifiedName + ".g.cs", SourceText.From(Emit(schema), Encoding.UTF8));
                }
                catch (Exception error) when (error is XmlException || error is FormatException || error is OverflowException)
                {
                    var location = Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan(default, default));
                    output.ReportDiagnostic(Diagnostic.Create(InvalidSchema, location, error.Message));
                }
            }
        });
    }

    private static bool Accepts(string path, AnalyzerConfigOptions options)
    {
#if LUMYTE_NATIVE
        return path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
#else
        options.TryGetValue("build_metadata.AdditionalFiles.LumytePortableRenderGraphInput", out string? target);
        bool graphInput = string.Equals(target, "true", StringComparison.OrdinalIgnoreCase);
#if LUMYTE_PASS_GRAPH
        return path.EndsWith(".portable.pass.resources.xml", StringComparison.OrdinalIgnoreCase)
            || (graphInput && path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase));
#else
        return !graphInput && path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
#endif
#endif
    }

    private static Schema Parse(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XElement root = XElement.Load(reader);
        if (root.Name != "input") { throw new FormatException("The root element must be input."); }
        string ns = Required(root, "namespace");
        if (ns.Split('.').Any(part => !Identifier(part))) { throw new FormatException("Invalid generated namespace."); }
        string name = Required(root, "name");
        if (!Identifier(name)) { throw new FormatException("Invalid generated type name."); }
        string? abiHash = (string?)root.Attribute("abiHash");
        var fields = new List<Field>();
#if LUMYTE_NATIVE
        var members = new HashSet<string>(StringComparer.Ordinal) { "Write", "Retain", "ByteSize" };
#else
        var members = new HashSet<string>(StringComparer.Ordinal) { "Write", "Group" };
#endif
        if (abiHash is not null) { members.Add("AbiHash"); }
        if (!members.Add(name)) { throw new FormatException("The generated type name conflicts with a generated member: " + name); }
        var bindings = new HashSet<uint>();
#if LUMYTE_NATIVE
        uint size = Number(root, "size");
        if (size > int.MaxValue) { throw new FormatException("Input size exceeds the CPU span range."); }
        uint group = 0;
#else
        uint size = 0;
        uint group = Number(root, "group");
#endif
        foreach (XElement element in root.Elements())
        {
            if (element.Name != "field") { throw new FormatException("Only field elements are supported."); }
            string kind = Required(element, "kind");
#if LUMYTE_NATIVE
            if (kind is "Scalar" or "Vector" or "Matrix") { continue; }
#endif
            string fieldName = Required(element, "name");
            if (!Identifier(fieldName) || !members.Add(fieldName))
            { throw new FormatException("Invalid or duplicate member: " + fieldName); }
#if LUMYTE_NATIVE
            string resource = kind == "GpuAddress" ? "Buffer" : (string?)element.Attribute("resource") ?? "View";
            if ((kind != "GpuAddress" && kind != "DescriptorIndex") ||
                (resource != "Buffer" && resource != "View" && resource != "Sampler") ||
                (kind == "DescriptorIndex" && resource == "Buffer"))
            { throw new FormatException("Native fields must be GpuAddress or a View/Sampler DescriptorIndex."); }
            uint offset = Number(element, "offset");
            uint width = kind == "GpuAddress" ? 8u : 4u;
            if (element.Attribute("size") is not null && Number(element, "size") != width)
            { throw new FormatException("Reference width does not match the prepared ABI: " + fieldName); }
            if ((ulong)offset + width > size || fields.Any(field => offset < (ulong)field.Offset + field.Width && field.Offset < (ulong)offset + width))
            { throw new FormatException("Reference fields must fit the input and must not overlap: " + fieldName); }
            fields.Add(new Field(fieldName, resource, offset, width));
#else
            string resource = kind switch
            {
                "Buffer" => "Buffer", "Texture" or "StorageTexture" => "View", "Sampler" => "Sampler",
                _ => throw new FormatException("Unsupported Portable binding kind: " + kind),
            };
            uint binding = Number(element, "binding");
            if (!bindings.Add(binding)) { throw new FormatException("Duplicate binding in the input group."); }
            if (resource == "Buffer" && (!members.Add(fieldName + "Offset") || !members.Add(fieldName + "Length")))
            { throw new FormatException("Buffer range member conflicts with another input: " + fieldName); }
            fields.Add(new Field(fieldName, resource, binding, 0));
#endif
        }
        return new Schema(ns, name, size, group, fields, abiHash);
    }

    private static string Emit(Schema schema)
    {
        var code = new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace ");
        code.Append(string.Join(".", schema.Namespace.Split('.').Select(Escape))).Append(";\npublic readonly struct ").Append(Escape(schema.Name));
#if LUMYTE_PASS_GRAPH
        code.Append(" : ").Append(Api).Append("IPortablePassBindingInputs");
#elif !LUMYTE_NATIVE
        code.Append(" : ").Append(Api).Append("IGpuBindingInputs");
#endif
        code.Append("\n{\n");
        if (schema.AbiHash is not null)
        {
            code.Append("    public const string AbiHash = ")
                .Append(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(schema.AbiHash, quote: true)).Append(";\n");
        }
        foreach (Field field in schema.Fields)
        {
            code.Append("    public ").Append(ResourceType(field)).Append(' ').Append(Escape(field.Name)).Append(" { get; }\n");
#if !LUMYTE_NATIVE
            if (field.Resource == "Buffer")
            {
                code.Append("    public ulong ").Append(Escape(field.Name + "Offset")).Append(" { get; init; }\n");
                code.Append("    public ulong ").Append(Escape(field.Name + "Length")).Append(" { get; init; }\n");
            }
#endif
        }
        code.Append("    public ").Append(Escape(schema.Name)).Append('(')
            .Append(string.Join(", ", schema.Fields.Select(field => ResourceType(field) + " " + Escape(field.Name))))
            .Append(")\n    {\n");
        foreach (Field field in schema.Fields)
        {
            string member = Escape(field.Name);
            code.Append("        this.").Append(member).Append(" = ").Append(member)
                .Append(" ?? throw new global::System.ArgumentNullException(nameof(").Append(member).Append("));\n");
#if !LUMYTE_NATIVE
            if (field.Resource == "Buffer")
            {
                code.Append("        ").Append(Escape(field.Name + "Offset")).Append(" = 0;\n");
                code.Append("        ").Append(Escape(field.Name + "Length")).Append(" = ulong.MaxValue;\n");
            }
#endif
        }
        code.Append("    }\n");
#if LUMYTE_NATIVE
        code.Append("    public const int ByteSize = ").Append(schema.Size.ToString(CultureInfo.InvariantCulture)).Append(";\n")
            .Append("    public void Retain(").Append(Api).Append("GpuResourceBatch batch)\n    {\n        global::System.ArgumentNullException.ThrowIfNull(batch);\n");
        foreach (Field field in schema.Fields) { code.Append("        batch.Use(this.").Append(Escape(field.Name)).Append(");\n"); }
        code.Append("    }\n    public void Write(").Append(Api).Append("GpuResourceManager manager, global::System.Span<byte> destination)\n    {\n")
            .Append("        global::System.ArgumentNullException.ThrowIfNull(manager);\n")
            .Append("        if (destination.Length < ByteSize) { throw new global::System.ArgumentException(\"The destination must cover the prepared input layout.\", nameof(destination)); }\n");
        foreach (Field field in schema.Fields)
        {
            code.Append("        global::System.Buffers.Binary.BinaryPrimitives.WriteUInt").Append(field.Width == 8 ? "64" : "32")
                .Append("LittleEndian(destination.Slice(").Append(field.Offset.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(field.Width)
                .Append("), manager.").Append(field.Width == 8 ? "GetGpuAddress" : "GetShaderIndex").Append("(this.").Append(Escape(field.Name)).Append("));\n");
        }
#else
        code.Append("    public const uint Group = ").Append(schema.Group.ToString(CultureInfo.InvariantCulture)).Append("u;\n")
#if LUMYTE_PASS_GRAPH
            .Append("    public void Write(").Append(Api).Append("PortablePassBindingWriter writer)\n    {\n        global::System.ArgumentNullException.ThrowIfNull(writer);\n");
#else
            .Append("    public void Write(").Append(Api).Append("GpuBindingWriter writer)\n    {\n        global::System.ArgumentNullException.ThrowIfNull(writer);\n");
#endif
        foreach (Field field in schema.Fields)
        {
            code.Append("        writer.").Append(field.Resource == "View" ? "Texture" : field.Resource).Append('(')
                .Append(field.Offset.ToString(CultureInfo.InvariantCulture)).Append("u, this.").Append(Escape(field.Name));
            if (field.Resource == "Buffer")
            { code.Append(", this.").Append(Escape(field.Name + "Offset")).Append(", this.").Append(Escape(field.Name + "Length")); }
            code.Append(");\n");
        }
#endif
        return code.Append("    }\n}\n").ToString();
    }

    private static string ResourceType(Field field)
    {
#if LUMYTE_PASS_GRAPH
        return field.Resource == "Sampler"
            ? "global::Lumyte.Graphics.Portable.Resources.GpuSamplerRef"
            : Api + "PortablePass" + (field.Resource == "View" ? "View" : "Buffer");
#else
        return Api + "Gpu" + field.Resource + "Ref";
#endif
    }

    private static string Required(XElement element, string name)
        => (string?)element.Attribute(name) ?? throw new FormatException("Missing schema attribute: " + name);
    private static uint Number(XElement element, string name)
        => uint.TryParse(Required(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out uint value)
            ? value : throw new FormatException("Invalid unsigned schema value: " + name);
    private static bool Identifier(string name)
        => name.Length != 0 && name[0] != '@' && (SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None);
    private static string Escape(string name) => "@" + name;

    private sealed class SchemaText(string path, string? text)
    { public string Path { get; } = path; public string? Text { get; } = text; }
    private sealed class Schema(string ns, string name, uint size, uint group, List<Field> fields, string? abiHash)
    {
        public string Namespace { get; } = ns;
        public string Name { get; } = name;
        public uint Size { get; } = size;
        public uint Group { get; } = group;
        public List<Field> Fields { get; } = fields;
        public string? AbiHash { get; } = abiHash;
    }
    private sealed class Field(string name, string resource, uint offset, uint width)
    {
        public string Name { get; } = name;
        public string Resource { get; } = resource;
        public uint Offset { get; } = offset;
        public uint Width { get; } = width;
    }
}
