using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal static class PipelineValidation
{
    internal static ShaderTargetData Shader(IGraphicsShader shader, ShaderTarget target, ShaderStage stage, DeviceCaps caps)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ShaderTargetData data = shader.Artifact.GetTarget(target);
        if (data.Stage != stage)
        {
            throw new ArgumentException("Shader stage does not match the program.");
        }

        using var document = JsonDocument.Parse(data.ReflectionJson);
        JsonElement root = document.RootElement;
        JsonElement entry = root.GetProperty("entryPoints").EnumerateArray().Single(e => e.GetProperty("name").GetString() == data.EntryPoint);
        if (root.TryGetProperty("parameters", out JsonElement globals))
        {
            int roots = globals.EnumerateArray().Count(p => p.GetProperty("type").GetProperty("kind").GetString() == "constantBuffer");
            if (roots > 1)
            {
                throw new NotSupportedException("A stage accepts one logical root structure.");
            }

            if (globals.GetArrayLength() != 0 && (!root.TryGetProperty("lumyteAbi", out JsonElement abi) || abi.GetInt32() != 1))
            {
                throw new NotSupportedException("The shader artifact has no supported root-data helper ABI.");
            }

            foreach (JsonElement parameter in globals.EnumerateArray())
            {
                string name = parameter.GetProperty("name").GetString()!;
                string kind = parameter.GetProperty("type").GetProperty("kind").GetString()!;
                if (kind != "constantBuffer" && !name.StartsWith("lumyte", StringComparison.Ordinal) && !name.StartsWith("__lumyte_schema_", StringComparison.Ordinal))
                {
                    throw new NotSupportedException("Shader resources must use the generated root-data and reference ABI.");
                }
            }
        }

        if (stage == ShaderStage.Compute)
        {
            JsonElement size = entry.GetProperty("threadGroupSize");
            uint x = size[0].GetUInt32();
            uint y = size[1].GetUInt32();
            uint z = size[2].GetUInt32();
            if (x == 0 || y == 0 || z == 0 || x > caps.MaxComputeWorkgroupSizeX || y > caps.MaxComputeWorkgroupSizeY || z > caps.MaxComputeWorkgroupSizeZ || checked((ulong)x * y * z) > caps.MaxComputeInvocationsPerWorkgroup)
            {
                throw new NotSupportedException("Shader workgroup exceeds enabled device limits.");
            }
        }

        if (stage == ShaderStage.Vertex && Inputs(entry).Count != 0)
        {
            throw new NotSupportedException("Vertex input layouts are not part of vertex pulling.");
        }

        return data;
    }

    internal static void Program(GraphicsPipelineDesc desc, ShaderTargetData vertex, ShaderTargetData? fragment)
    {
        ShaderDataLayout.ValidateProgram(vertex, fragment);
        if (!Enum.IsDefined(desc.TopologyClass) || !Enum.IsDefined(desc.Optimization) || (desc.AlphaToCoverageEnable && fragment == null))
        {
            throw new ArgumentException("Invalid compilation options.");
        }

        if (fragment == null)
        {
            return;
        }

        using var v = JsonDocument.Parse(vertex.ReflectionJson);
        using var f = JsonDocument.Parse(fragment.ReflectionJson);
        Dictionary<uint, string> outputs = Outputs(v.RootElement.GetProperty("entryPoints")[0], false);
        foreach ((uint location, string type) in Inputs(f.RootElement.GetProperty("entryPoints")[0]))
        {
            if (!outputs.TryGetValue(location, out string? previous) || previous != type)
            {
                throw new ArgumentException("Vertex and fragment location or type linkage differs.");
            }
        }
    }

    internal static RenderStateSnapshot State(GraphicsRenderStateDesc state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Rasterization);
        ArgumentNullException.ThrowIfNull(state.DepthStencil);
        ArgumentNullException.ThrowIfNull(state.ColorTargets);
        RasterizationStateDesc r = state.Rasterization;
        DepthStencilStateDesc d = state.DepthStencil;
        if (!Enum.IsDefined(state.Topology) || (state.StripIndexFormat is { } index && !Enum.IsDefined(index)) ||
            (state.StripIndexFormat != null && state.Topology is not (PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip)) ||
            !Enum.IsDefined(r.Cull) || !Enum.IsDefined(r.FrontFace) || r.DepthBiasConstant is < -16777216 or > 16777216 || !float.IsFinite(r.DepthBiasSlope) || !float.IsFinite(r.DepthBiasClamp) ||
            !Enum.IsDefined(d.DepthCompare) || d.StencilReadMask > 255 || d.StencilWriteMask > 255)
        {
            throw new ArgumentException("Invalid draw state.");
        }

        Stencil(d.Front);
        Stencil(d.Back);
        ColorBlendStateDesc[] colors = state.ColorTargets.ToArray();
        foreach (ColorBlendStateDesc color in colors)
        {
            ArgumentNullException.ThrowIfNull(color);
            if ((color.WriteMask & ~ColorWriteMask.All) != 0)
            {
                throw new ArgumentException("Unknown color mask.");
            }

            Blend(color.Color, true);
            Blend(color.Alpha, false);
        }

        GraphicsRenderStateDesc snapshot = state with { ColorTargets = Array.AsReadOnly(colors) };
        return new(snapshot, JsonSerializer.Serialize(snapshot, PipelineJsonContext.Default.GraphicsRenderStateDesc));
    }

    internal static void Draw(GraphicsPipelineDesc program, RenderStateSnapshot state, TextureFormat[] formats, IReadOnlyDictionary<uint, string>? fragment, TextureFormat? depthFormat = null, IndexFormat? indexFormat = null)
    {
        PrimitiveTopologyClass topology = state.Desc.Topology switch
        {
            PrimitiveTopology.PointList => PrimitiveTopologyClass.Point,
            PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => PrimitiveTopologyClass.Line,
            _ => PrimitiveTopologyClass.Triangle,
        };
        if (program.TopologyClass != topology || state.Desc.ColorTargets.Count != formats.Length || (fragment == null && formats.Length != 0) ||
            (state.Desc.StripIndexFormat != null && state.Desc.StripIndexFormat != indexFormat) ||
            (indexFormat != null && state.Desc.Topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip && state.Desc.StripIndexFormat != indexFormat))
        {
            throw new ArgumentException("Program, index topology or color targets do not match the pass.");
        }

        DepthStencilStateDesc depth = state.Desc.DepthStencil;
        if (((depth.DepthTestEnable || depth.DepthWriteEnable || depth.StencilTestEnable) && depthFormat == null) ||
            (depth.StencilTestEnable && depthFormat != TextureFormat.Depth24Stencil8))
        {
            throw new ArgumentException("Depth/stencil state requires a compatible attachment.");
        }

        if (program.AlphaToCoverageEnable || !state.Desc.Rasterization.DepthClipEnable ||
            state.Desc.Rasterization.DepthBiasClamp != 0 ||
            (depthFormat == null && (state.Desc.Rasterization.DepthBiasConstant != 0 || state.Desc.Rasterization.DepthBiasSlope != 0)))
        {
            throw new NotSupportedException("Requested state requires depth/stencil, MSAA or additional enabled rasterization features.");
        }

        if (fragment == null)
        {
            return;
        }

        foreach ((uint location, string type) in fragment)
        {
            if (location >= (uint)formats.Length || type != "vector:4:scalar:float32")
            {
                throw new ArgumentException("Fragment output does not match the color attachment slots.");
            }
        }
    }

    internal static IReadOnlyDictionary<uint, string>? FragmentOutputs(ShaderTargetData? fragment)
    {
        if (fragment == null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(fragment.ReflectionJson);
        return Outputs(document.RootElement.GetProperty("entryPoints")[0], true);
    }

    internal static void Viewport(Viewport v)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Width) || !float.IsFinite(v.Height) || v.Width <= 0 || v.Height <= 0 ||
            !float.IsFinite(v.MinDepth) || !float.IsFinite(v.MaxDepth) || v.MinDepth < 0 || v.MaxDepth > 1 || v.MinDepth > v.MaxDepth)
        {
            throw new ArgumentException("Invalid viewport.");
        }
    }

    internal static void Scissor(ScissorRect s, uint width, uint height)
    {
        if (s.Width == 0 || s.Height == 0 || s.X > width || s.Width > width - s.X || s.Y > height || s.Height > height - s.Y)
        {
            throw new ArgumentException("Scissor is outside the attachment.");
        }
    }

    private static void Stencil(StencilFaceDesc face)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (!Enum.IsDefined(face.Compare) || !Enum.IsDefined(face.Fail) || !Enum.IsDefined(face.DepthFail) || !Enum.IsDefined(face.Pass))
        {
            throw new ArgumentException("Unknown stencil state.");
        }
    }

    private static void Blend(BlendComponentDesc c, bool color)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!Enum.IsDefined(c.Source) || !Enum.IsDefined(c.Destination) || !Enum.IsDefined(c.Operation) ||
            (c.Operation is BlendOperation.Min or BlendOperation.Max && (c.Source != BlendFactor.One || c.Destination != BlendFactor.One)) ||
            c.Destination == BlendFactor.SourceAlphaSaturated || (!color && c.Source == BlendFactor.SourceAlphaSaturated))
        {
            throw new ArgumentException("Invalid blend equation.");
        }
    }

    private static Dictionary<uint, string> Inputs(JsonElement entry)
    {
        Dictionary<uint, string> values = [];
        if (!entry.TryGetProperty("parameters", out JsonElement parameters))
        {
            return values;
        }

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            Varyings(parameter, values, "varyingInput", false);
        }

        return values;
    }

    private static Dictionary<uint, string> Outputs(JsonElement entry, bool targets)
    {
        Dictionary<uint, string> values = [];
        if (entry.TryGetProperty("result", out JsonElement result))
        {
            Varyings(result, values, "varyingOutput", targets);
        }

        return values;
    }

    private static void Varyings(JsonElement node, Dictionary<uint, string> values, string kind, bool targets)
    {
        JsonElement type = node.GetProperty("type");
        if (type.GetProperty("kind").GetString() == "struct")
        {
            foreach (JsonElement field in type.GetProperty("fields").EnumerateArray())
            {
                Varyings(field, values, kind, targets);
            }

            return;
        }

        string? semantic = node.TryGetProperty("semanticName", out JsonElement s) ? s.GetString() : null;
        if (semantic?.StartsWith("SV_", StringComparison.Ordinal) == true && !(targets && semantic == "SV_TARGET"))
        {
            return;
        }

        if (node.TryGetProperty("binding", out JsonElement b) && b.GetProperty("kind").GetString() == kind)
        {
            values.Add(b.GetProperty("index").GetUInt32(), Type(type));
        }
    }

    private static string Type(JsonElement type) => type.GetProperty("kind").GetString() switch
    {
        "scalar" => "scalar:" + type.GetProperty("scalarType").GetString(),
        "vector" => "vector:" + type.GetProperty("elementCount").GetInt32() + ":" + Type(type.GetProperty("elementType")),
        _ => throw new NotSupportedException("Unsupported stage linkage type."),
    };
}
