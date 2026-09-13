using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumyte.Graphics.Portable.Shaders.Offline;

/// <summary>Reads fixed-version official Tint output. It never reads or parses WGSL source.</summary>
internal static partial class TintReflection
{
    internal sealed record Reflection(PortableShaderEntryPoint[] EntryPoints, PortableShaderFeatures Features,
        PortableShaderGroupLayout[] Groups, PortableShaderDataLayout? Root, PortableShaderDataLayout[] Parameters,
        PortableShaderBindingSchemaEntry[] Bindings, IReadOnlyList<PortableShaderDataLayout> HostLayouts);

    public static Reflection Read(string json, string text, string ir, IReadOnlyList<string> selectedEntries,
        PortableShaderCompileOptions options)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var types = ReadTypes(text);
        var layouts = ReadLayouts(root.GetProperty("structures"), types);
        string? rootName = ReadImmediateType(ir);
        if (options.RootTypeName is not null && !StringComparer.Ordinal.Equals(options.RootTypeName, rootName))
        { throw new InvalidDataException($"Selected root type '{options.RootTypeName}' differs from the official Tint immediate type '{rootName ?? "<none>"}'."); }
        var samplerKinds = ReadSamplerKinds(text);
        var entries = new List<PortableShaderEntryPoint>();
        var bindings = new SortedDictionary<(uint Group, uint Binding), GpuBindingLayoutEntry>();
        var requested = new HashSet<string>(selectedEntries, StringComparer.Ordinal);
        if (requested.Count != selectedEntries.Count) { throw new ArgumentException("Entry point selection contains duplicates.", nameof(selectedEntries)); }
        foreach (JsonElement entry in root.GetProperty("entry_points").EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString()!;
            if (selectedEntries.Count != 0 && !requested.Remove(name)) { continue; }
            GpuShaderStage stage = entry.GetProperty("stage").GetString() switch
            {
                "compute" => GpuShaderStage.Compute, "vertex" => GpuShaderStage.Vertex, "fragment" => GpuShaderStage.Pixel,
                string value => throw new NotSupportedException($"Tint entry stage '{value}' is not supported by Portable packages."),
                _ => throw new InvalidDataException("Tint entry has no stage."),
            };
            entries.Add(new(stage, name));
            foreach (JsonElement binding in entry.GetProperty("bindings").EnumerateArray())
            {
                uint group = binding.GetProperty("group").GetUInt32();
                uint slot = binding.GetProperty("binding").GetUInt32();
                var layout = ReadBinding(binding, stage, samplerKinds.GetValueOrDefault((name, group, slot)));
                if (bindings.TryGetValue((group, slot), out var existing))
                {
                    var comparison = WithVisibility(layout, existing.Visibility);
                    if (comparison != existing) { throw new InvalidDataException($"Tint entry points disagree on group {group} binding {slot}."); }
                    layout = WithVisibility(layout, layout.Visibility | existing.Visibility);
                }
                bindings[(group, slot)] = layout;
            }
        }
        if (requested.Count != 0) { throw new ArgumentException($"Entry point '{requested.First()}' was not returned by Tint.", nameof(selectedEntries)); }
        GpuShaderStage stages = GpuShaderStage.None;
        foreach (var entry in entries)
        {
            if ((stages & entry.Stage) != 0) { throw new NotSupportedException("Select at most one entry point per stage for one Portable package."); }
            stages |= entry.Stage;
        }
        if (stages is not (GpuShaderStage.Compute or GpuShaderStage.Vertex or (GpuShaderStage.Vertex | GpuShaderStage.Pixel)))
        { throw new NotSupportedException("A Portable package requires compute, vertex, or vertex and fragment entry points."); }
        PortableShaderFeatures features = rootName is null ? PortableShaderFeatures.None : PortableShaderFeatures.ImmediateAddressSpace;
        foreach (var extension in root.GetProperty("extensions").EnumerateArray())
        {
            features |= extension.GetString() switch
            {
                "dual_source_blending" => PortableShaderFeatures.DualSourceBlend,
                "immediate_address_space" => PortableShaderFeatures.ImmediateAddressSpace,
                string value => throw new NotSupportedException($"WGSL extension '{value}' has no feature contract in the Portable runtime."),
                _ => throw new InvalidDataException("Tint extension has no name."),
            };
        }
        var selectedLayouts = new Dictionary<string, PortableShaderDataLayout>(StringComparer.Ordinal);
        PortableShaderDataLayout? rootLayout = rootName is null ? null : SelectLayout(rootName, layouts, selectedLayouts);
        var parameters = options.ParameterTypeNames.Select(name => SelectLayout(name, layouts, selectedLayouts)).ToArray();
        uint groupCount = bindings.Count == 0 ? 0 : checked(bindings.Keys.Max(key => key.Group) + 1);
        if (groupCount > int.MaxValue) { throw new InvalidDataException("Tint group number cannot be represented as a package list."); }
        var groups = Enumerable.Range(0, (int)groupCount).Select(group => new PortableShaderGroupLayout(
            bindings.Where(pair => pair.Key.Group == group).Select(pair => pair.Value).ToArray())).ToArray();
        var schema = bindings.Select(pair => new PortableShaderBindingSchemaEntry(
            $"Group{pair.Key.Group}Binding{pair.Key.Binding}", pair.Key.Group, pair.Key.Binding, pair.Value.Kind)).ToArray();
        return new(entries.ToArray(), features, groups, rootLayout, parameters, schema, selectedLayouts.Values.ToArray());
    }

    private static Dictionary<string, Dictionary<(string Name, uint Offset), string>> ReadTypes(string text)
    {
        var result = new Dictionary<string, Dictionary<(string Name, uint Offset), string>>(StringComparer.Ordinal);
        Dictionary<(string Name, uint Offset), string>? current = null;
        foreach (string line in text.Split('\n'))
        {
            Match header = StructureHeader().Match(line);
            if (header.Success)
            { current = new(); result.Add(header.Groups[1].Value, current); continue; }
            Match member = StructureMember().Match(line);
            if (member.Success && current is not null)
            { current.Add((member.Groups[2].Value, uint.Parse(member.Groups[1].Value, CultureInfo.InvariantCulture)), member.Groups[3].Value.Trim()); }
        }
        return result;
    }

    private static Dictionary<string, PortableShaderDataLayout> ReadLayouts(JsonElement structures,
        Dictionary<string, Dictionary<(string Name, uint Offset), string>> types)
    {
        var result = new Dictionary<string, PortableShaderDataLayout>(StringComparer.Ordinal);
        foreach (JsonElement structure in structures.EnumerateArray())
        {
            string name = structure.GetProperty("name").GetString()!;
            if (!types.TryGetValue(name, out var fields)) { throw new InvalidDataException($"Tint text has no type information for '{name}'."); }
            var members = new List<PortableShaderFieldLayout>();
            foreach (JsonElement member in structure.GetProperty("members").EnumerateArray())
            {
                string memberName = member.GetProperty("name").GetString()!;
                uint memberOffset = member.GetProperty("offset").GetUInt32();
                if (!fields.TryGetValue((memberName, memberOffset), out string? type))
                {
                    // Padding has no declaration in Tint's semantic structure display.
                    if (memberName == "implicit_padding") { continue; }
                    throw new InvalidDataException($"Tint text has no type for '{name}.{memberName}'.");
                }
                members.Add(new(memberName, type, memberOffset,
                    member.GetProperty("size").GetUInt32(), member.GetProperty("align").GetUInt32()));
            }
            result.Add(name, new(name, structure.GetProperty("size").GetUInt32(), structure.GetProperty("align").GetUInt32(), members.ToArray()));
        }
        return result;
    }

    private static PortableShaderDataLayout SelectLayout(string name, Dictionary<string, PortableShaderDataLayout> layouts,
        Dictionary<string, PortableShaderDataLayout> selected)
    {
        if (selected.TryGetValue(name, out var previous)) { return previous; }
        if (!layouts.TryGetValue(name, out var layout)) { throw new NotSupportedException($"Tint does not expose a structure layout for '{name}'. Only structure root and selected parameter types are currently supported."); }
        selected.Add(name, layout);
        foreach (var field in layout.Fields)
        {
            if (layouts.ContainsKey(field.TypeName)) { SelectLayout(field.TypeName, layouts, selected); }
            else if (!PortableGeneration.IsSupportedValueType(field.TypeName))
            { throw new NotSupportedException($"Field '{name}.{field.Name}' uses '{field.TypeName}'. Typed host generation currently supports 32-bit scalar/vector and nested structure fields; matrix and array stride reflection requires the Tint bridge."); }
        }
        return layout;
    }

    private static string? ReadImmediateType(string ir)
    {
        const string marker = "== IR dump before wgsl.Lower:";
        int start = ir.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { throw new InvalidDataException("The fixed Tint frontend did not return the initial WGSL IR snapshot. Refusing to infer immediate layout from source."); }
        int next = ir.IndexOf("== IR dump before", start + marker.Length, StringComparison.Ordinal);
        string first = next < 0 ? ir[start..] : ir[start..next];
        var names = ImmediateVariable().Matches(first).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length == 0 && first.Contains("ptr<immediate,", StringComparison.Ordinal))
        { throw new InvalidDataException("Tint returned immediate pointers without a recognized root variable declaration. Refusing to treat an unknown IR representation as a rootless module."); }
        return names.Length switch { 0 => null, 1 => names[0], _ => throw new InvalidDataException("Tint returned multiple immediate root types.") };
    }

    private static Dictionary<(string Entry, uint Group, uint Binding), string> ReadSamplerKinds(string text)
    {
        var result = new Dictionary<(string, uint, uint), string>();
        string entry = "";
        uint group = 0, binding = 0;
        foreach (string line in text.Split('\n'))
        {
            var point = EntryHeader().Match(line);
            if (point.Success) { entry = point.Groups[1].Value; continue; }
            var slot = BindingHeader().Match(line);
            if (slot.Success) { group = uint.Parse(slot.Groups[1].Value, CultureInfo.InvariantCulture); binding = uint.Parse(slot.Groups[2].Value, CultureInfo.InvariantCulture); continue; }
            var kind = SamplerKind().Match(line);
            if (kind.Success) { result[(entry, group, binding)] = kind.Groups[1].Value; }
        }
        return result;
    }

    private static GpuBindingLayoutEntry ReadBinding(JsonElement binding, GpuShaderStage stage, string? samplerKind)
    {
        uint slot = binding.GetProperty("binding").GetUInt32();
        string kind = binding.GetProperty("resource_type").GetString()!;
        if (kind is "UniformBuffer" or "StorageBuffer" or "ReadOnlyStorageBuffer")
        {
            var type = kind switch { "UniformBuffer" => GpuBufferBindingType.Uniform, "ReadOnlyStorageBuffer" => GpuBufferBindingType.ReadOnlyStorage, _ => GpuBufferBindingType.Storage };
            return new(slot, stage, new GpuBufferBindingLayout(type, binding.GetProperty("size").GetUInt64()));
        }
        if (kind == "Sampler")
        {
            var type = samplerKind switch
            {
                "comparison" => GpuSamplerBindingType.Comparison, "filtering" => GpuSamplerBindingType.Filtering,
                "non-filtering" => GpuSamplerBindingType.NonFiltering,
                // Tint leaves a plain sampler's filtering category open when usage imposes none.
                "unknown-filtering" => GpuSamplerBindingType.Filtering,
                _ => throw new NotSupportedException($"Tint sampler category '{samplerKind}' is not supported."),
            };
            return new(slot, stage, new GpuSamplerBindingLayout(type));
        }
        GpuTextureViewDimension dimension = binding.GetProperty("dimensions").GetString() switch
        {
            "1d" => GpuTextureViewDimension.Texture1D, "2d" => GpuTextureViewDimension.Texture2D,
            "2dArray" => GpuTextureViewDimension.Texture2DArray, "3d" => GpuTextureViewDimension.Texture3D,
            "Cube" or "cube" => GpuTextureViewDimension.Cube, "CubeArray" or "cubeArray" => GpuTextureViewDimension.CubeArray,
            string value => throw new NotSupportedException($"Tint texture dimension '{value}' is not supported."),
            _ => throw new InvalidDataException("Tint binding has no texture dimension."),
        };
        if (kind is "WriteOnlyStorageTexture" or "ReadOnlyStorageTexture" or "ReadWriteStorageTexture")
        {
            var access = kind switch { "WriteOnlyStorageTexture" => GpuStorageTextureAccess.WriteOnly, "ReadOnlyStorageTexture" => GpuStorageTextureAccess.ReadOnly, _ => GpuStorageTextureAccess.ReadWrite };
            string formatName = binding.GetProperty("image_format").GetString()!;
            if (!Enum.TryParse<GpuFormat>(formatName, ignoreCase: true, out var format))
            { throw new NotSupportedException($"Tint storage format '{formatName}' has no Portable format."); }
            return new(slot, stage, new GpuStorageTextureBindingLayout(access, format, dimension));
        }
        if (kind is not ("SampledTexture" or "MultisampledTexture" or "DepthTexture" or "DepthMultisampledTexture"))
        { throw new NotSupportedException($"Tint binding resource type '{kind}' is not supported."); }
        var sample = kind.StartsWith("Depth", StringComparison.Ordinal) ? GpuTextureSampleType.Depth : binding.GetProperty("sampled_kind").GetString() switch
        {
            "Float" or "filterable" or "unknown-filterable" => GpuTextureSampleType.Float,
            "unfilterable" => GpuTextureSampleType.UnfilterableFloat, "SInt" => GpuTextureSampleType.Sint, "UInt" => GpuTextureSampleType.Uint,
            string value => throw new NotSupportedException($"Tint sampled kind '{value}' is not supported."),
            _ => throw new InvalidDataException("Tint binding has no sampled kind."),
        };
        return new(slot, stage, new GpuTextureBindingLayout(sample, dimension, kind.Contains("Multisampled", StringComparison.Ordinal)));
    }

    private static GpuBindingLayoutEntry WithVisibility(GpuBindingLayoutEntry layout, GpuShaderStage stages) => layout.Kind switch
    {
        GpuBindingLayoutKind.Buffer => new(layout.Binding, stages, layout.BufferLayout),
        GpuBindingLayoutKind.Texture => new(layout.Binding, stages, layout.TextureLayout),
        GpuBindingLayoutKind.StorageTexture => new(layout.Binding, stages, layout.StorageTextureLayout),
        GpuBindingLayoutKind.Sampler => new(layout.Binding, stages, layout.SamplerLayout),
        _ => throw new InvalidDataException("Invalid reflected binding kind."),
    };

    [GeneratedRegex(@"^/\*.*?\*/\s+struct\s+(\S+)\s+\{")]
    private static partial Regex StructureHeader();
    [GeneratedRegex(@"^/\* offset\(\s*(\d+)\).*?\*/\s+(\S+)\s+:\s+(.+),\s*$")]
    private static partial Regex StructureMember();
    [GeneratedRegex(@"^\s*%[^:\r\n]+:ptr<immediate, (.+), read> = var\b", RegexOptions.Multiline)]
    private static partial Regex ImmediateVariable();
    [GeneratedRegex(@"^Entry Point = (\S+) \(")]
    private static partial Regex EntryHeader();
    [GeneratedRegex(@"^\s*\[(\d+)\]\[(\d+)\]:")]
    private static partial Regex BindingHeader();
    [GeneratedRegex(@"^\s*sampler_type = (\S+)")]
    private static partial Regex SamplerKind();
}
