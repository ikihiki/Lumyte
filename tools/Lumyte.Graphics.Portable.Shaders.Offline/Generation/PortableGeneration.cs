using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lumyte.Graphics.Portable.Shaders.Offline;

internal static partial class PortableGeneration
{
    public static void RequireNames(PortableShaderCompileOptions options)
    {
        foreach (string component in options.GeneratedNamespace.Split('.')) { RequireIdentifier(component); }
        RequireIdentifier(options.GeneratedName);
    }

    public static bool IsSupportedValueType(string type) => type is "u32" or "i32" or "f32" || VectorType().IsMatch(type);

    public static IEnumerable<PortableShaderGeneratedFile> Sources(PortableShaderPackage package,
        PortableShaderCompileOptions options, IReadOnlyList<PortableShaderDataLayout> layouts)
    {
        yield return new(options.GeneratedName + ".Package.g.cs", PackageSource(package, options));
        if (layouts.Count != 0) { yield return new(options.GeneratedName + ".Host.g.cs", HostSource(layouts, options, package.GroupLayouts.Count)); }
    }

    public static IEnumerable<PortableShaderGeneratedFile> ResourceInputs(PortableShaderPackage package,
        PortableShaderCompileOptions options)
    {
        for (int group = 0; group < package.GroupLayouts.Count; group++)
        {
            string name = options.GeneratedName + "Group" + group.ToString(CultureInfo.InvariantCulture) + "Resources";
            var input = new XElement("input", new XAttribute("namespace", options.GeneratedNamespace),
                new XAttribute("name", name), new XAttribute("group", group), new XAttribute("abiHash", package.AbiHash));
            foreach (var binding in package.BindingSchema.Entries.Where(entry => entry.Group == group))
            {
                input.Add(new XElement("field", new XAttribute("name", binding.Name),
                    new XAttribute("kind", binding.Kind), new XAttribute("binding", binding.Binding)));
            }
            yield return new(name + ".portable.resources.xml", input.ToString());
        }
    }

    private static string PackageSource(PortableShaderPackage package, PortableShaderCompileOptions options)
    {
        var source = Header(options);
        source.AppendLine("using GpuFormat = global::Lumyte.Graphics.GpuFormat;")
            .AppendLine("using global::Lumyte.Graphics.Portable;")
            .AppendLine("using global::Lumyte.Graphics.Portable.Shaders;")
            .Append("public static class @").Append(options.GeneratedName).AppendLine("Package")
            .AppendLine("{")
            .Append("    public const string AbiHash = ").Append(Quote(package.AbiHash)).AppendLine(";")
            .AppendLine("    public static PortableShaderPackage Create() => new(")
            .Append("        PortableShaderPackage.CurrentVersion, ").Append(Quote(package.Module)).AppendLine(",")
            .Append("        [").AppendJoin(", ", package.EntryPoints.Select(entry => $"new PortableShaderEntryPoint((GpuShaderStage){(int)entry.Stage}, {Quote(entry.Name)})")).AppendLine("],")
            .Append("        (PortableShaderFeatures)").Append((int)package.RequiredFeatures).AppendLine(",")
            .AppendLine("        [");
        foreach (var group in package.GroupLayouts)
        {
            source.Append("            new PortableShaderGroupLayout([").AppendJoin(", ", group.Entries.Select(BindingSource)).AppendLine("]),");
        }
        source.AppendLine("        ],")
            .Append("        ").Append(LayoutSource(package.RootLayout)).AppendLine(",")
            .Append("        [").AppendJoin(", ", package.ParameterLayouts.Select(LayoutSource)).AppendLine("],")
            .Append("        new PortableShaderBindingSchema([").AppendJoin(", ", package.BindingSchema.Entries.Select(entry =>
                $"new PortableShaderBindingSchemaEntry({Quote(entry.Name)}, {entry.Group}u, {entry.Binding}u, GpuBindingLayoutKind.{entry.Kind})")).AppendLine("]),")
            .AppendLine("        AbiHash);")
            .AppendLine("}");
        return source.ToString();
    }

    private static string BindingSource(GpuBindingLayoutEntry entry)
    {
        string layout = entry.Kind switch
        {
            GpuBindingLayoutKind.Buffer => $"new GpuBufferBindingLayout(GpuBufferBindingType.{entry.BufferLayout.Type}, {entry.BufferLayout.MinBindingSize}ul, {Bool(entry.BufferLayout.HasDynamicOffset)})",
            GpuBindingLayoutKind.Texture => $"new GpuTextureBindingLayout(GpuTextureSampleType.{entry.TextureLayout.SampleType}, GpuTextureViewDimension.{entry.TextureLayout.ViewDimension}, {Bool(entry.TextureLayout.Multisampled)})",
            GpuBindingLayoutKind.StorageTexture => $"new GpuStorageTextureBindingLayout(GpuStorageTextureAccess.{entry.StorageTextureLayout.Access}, GpuFormat.{entry.StorageTextureLayout.Format}, GpuTextureViewDimension.{entry.StorageTextureLayout.ViewDimension})",
            GpuBindingLayoutKind.Sampler => $"new GpuSamplerBindingLayout(GpuSamplerBindingType.{entry.SamplerLayout.Type})",
            _ => throw new InvalidDataException("Unknown package binding kind."),
        };
        return $"new GpuBindingLayoutEntry({entry.Binding}u, (GpuShaderStage){(int)entry.Visibility}, {layout})";
    }

    private static string LayoutSource(PortableShaderDataLayout? layout) => layout is null ? "null" :
        $"new PortableShaderDataLayout({Quote(layout.Name)}, {layout.Size}u, {layout.Alignment}u, [" +
        string.Join(", ", layout.Fields.Select(field => $"new PortableShaderFieldLayout({Quote(field.Name)}, {Quote(field.TypeName)}, {field.Offset}u, {field.Size}u, {field.Alignment}u, {field.ArrayStride}u, {field.MatrixStride}u)")) + "])";

    private static string HostSource(IReadOnlyList<PortableShaderDataLayout> layouts, PortableShaderCompileOptions options, int groupCount)
    {
        var source = Header(options);
        source.AppendLine("using global::System.Runtime.InteropServices;");
        var emittedVectors = new HashSet<string>(StringComparer.Ordinal);
        var declaredNames = new HashSet<string>(StringComparer.Ordinal) { options.GeneratedName + "Package" };
        for (int group = 0; group < groupCount; group++) { declaredNames.Add(options.GeneratedName + "Group" + group + "Resources"); }
        foreach (var layout in layouts)
        {
            RequireIdentifier(layout.Name);
            string generatedType = options.GeneratedName + layout.Name;
            if (!declaredNames.Add(generatedType)) { throw new InvalidDataException($"Generated host type '{generatedType}' is duplicated."); }
            foreach (var field in layout.Fields)
            {
                RequireIdentifier(field.Name);
                if (field.Name == generatedType) { throw new NotSupportedException($"Field '{field.Name}' collides with its generated containing type."); }
            }
            source.Append("[StructLayout(LayoutKind.Explicit, Size = ").Append(layout.Size).AppendLine(", Pack = 1)]")
                .Append("public struct @").Append(generatedType).AppendLine()
                .AppendLine("{");
            foreach (var field in layout.Fields)
            {
                string fieldType = HostType(field.TypeName, options.GeneratedName, emittedVectors);
                source.Append("    [FieldOffset(").Append(field.Offset).Append(")] public ").Append(fieldType)
                    .Append(" @").Append(field.Name).AppendLine(";");
            }
            source.AppendLine("}");
        }
        foreach (string vector in emittedVectors.Order(StringComparer.Ordinal))
        {
            Match match = VectorType().Match(vector);
            int count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            string scalar = ScalarType(match.Groups[2].Value);
            string generatedType = VectorName(options.GeneratedName, count, match.Groups[2].Value);
            if (!declaredNames.Add(generatedType)) { throw new InvalidDataException($"Generated vector type '{generatedType}' collides with a structure."); }
            source.AppendLine("[StructLayout(LayoutKind.Sequential, Pack = 4)]")
                .Append("public struct @").Append(generatedType).AppendLine()
                .AppendLine("{");
            foreach (char component in "XYZW"[..count])
            { source.Append("    public ").Append(scalar).Append(' ').Append(component).AppendLine(";"); }
            source.AppendLine("}");
        }
        return source.ToString();
    }

    private static string HostType(string type, string prefix, HashSet<string> vectors)
    {
        if (type is "u32" or "i32" or "f32") { return ScalarType(type); }
        var match = VectorType().Match(type);
        if (!match.Success) { RequireIdentifier(type); return "@" + prefix + type; }
        vectors.Add(type);
        return "@" + VectorName(prefix, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value);
    }

    private static string VectorName(string prefix, int count, string scalar) => prefix + "Vector" + count + scalar;
    private static string ScalarType(string type) => type switch { "u32" => "uint", "i32" => "int", "f32" => "float", _ => throw new NotSupportedException(type) };
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static StringBuilder Header(PortableShaderCompileOptions options) => new StringBuilder("// <auto-generated/>\n#nullable enable\nnamespace ")
        .AppendJoin('.', options.GeneratedNamespace.Split('.').Select(part => "@" + part)).AppendLine(";");

    internal static void RequireIdentifier(string value)
    {
        if (!Identifier().IsMatch(value)) { throw new ArgumentException($"Generated C# identifier '{value}' must use ASCII letters, digits and underscores and begin with a letter or underscore."); }
    }

    [GeneratedRegex(@"^vec([234])<(u32|i32|f32)>$")]
    private static partial Regex VectorType();
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
