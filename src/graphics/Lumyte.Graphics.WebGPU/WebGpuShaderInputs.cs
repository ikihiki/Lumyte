using System.Text.RegularExpressions;
using Silk.NET.WebGPU;

namespace Lumyte.Graphics.WebGPU;

public sealed unsafe partial class WebGpuDevice
{
    // Shaders using immediate root data need an explicit pipeline layout.
    private ShaderInputLayout? CreateShaderInputLayout(string source)
    {
        if (!source.Contains("var<immediate>", StringComparison.Ordinal)) { return null; }
        var resources = new List<BindGroupLayoutEntry>();
        foreach (Match match in ShaderResourcePattern().Matches(source))
        {
            if (match.Groups["group"].Value != "0") { continue; }
            uint binding = uint.Parse(match.Groups["binding"].Value, System.Globalization.CultureInfo.InvariantCulture);
            string type = match.Groups["type"].Value.Trim();
            string access = match.Groups["access"].Value;
            var entry = new BindGroupLayoutEntry { Binding = binding, Visibility = ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute };
            if (access.Contains("storage", StringComparison.Ordinal))
            {
                entry.Buffer = new BufferBindingLayout { Type = access.Contains("read_write", StringComparison.Ordinal)
                    ? BufferBindingType.Storage : BufferBindingType.ReadOnlyStorage };
            }
            else if (type == "sampler" || type == "sampler_comparison")
            {
                entry.Sampler = new SamplerBindingLayout { Type = type == "sampler" ? SamplerBindingType.Filtering : SamplerBindingType.Comparison };
            }
            else if (type.StartsWith("texture_storage_2d<", StringComparison.Ordinal))
            {
                string format = type["texture_storage_2d<".Length..].Split(',')[0].Trim();
                entry.StorageTexture = new StorageTextureBindingLayout
                {
                    Access = StorageTextureAccess.WriteOnly, ViewDimension = TextureViewDimension.Dimension2D,
                    Format = format switch
                    {
                        "rgba8unorm" => TextureFormat.Rgba8Unorm,
                        "rgba16float" => TextureFormat.Rgba16float,
                        "r32float" => TextureFormat.R32float,
                        "r32uint" => TextureFormat.R32Uint,
                        _ => throw new NotSupportedException($"Storage format {format} is outside the common shader ABI."),
                    },
                };
            }
            else if (type.StartsWith("texture_2d<", StringComparison.Ordinal) || type == "texture_depth_2d")
            {
                entry.Texture = new TextureBindingLayout
                {
                    ViewDimension = TextureViewDimension.Dimension2D,
                    SampleType = type == "texture_depth_2d" ? TextureSampleType.Depth
                        : type.Contains("u32", StringComparison.Ordinal) ? TextureSampleType.Uint
                        : type.Contains("i32", StringComparison.Ordinal) ? TextureSampleType.Sint : TextureSampleType.Float,
                };
            }
            else { throw new NotSupportedException($"Shader resource {type} is outside the common shader ABI."); }
            if (entry.StorageTexture.Format != TextureFormat.Undefined || entry.Buffer.Type == BufferBindingType.Storage)
            { entry.Visibility = ShaderStage.Fragment | ShaderStage.Compute; }
            if (!resources.Any(existing => existing.Binding == binding)) { resources.Add(entry); }
        }
        BindGroupLayout* resourceLayout;
        BindGroupLayoutEntry[] resourceEntries = resources.ToArray();
        fixed (BindGroupLayoutEntry* entries = resourceEntries)
        {
            var description = new BindGroupLayoutDescriptor { EntryCount = (nuint)resourceEntries.Length, Entries = entries };
            resourceLayout = api.DeviceCreateBindGroupLayout(device, in description);
        }
        try
        {
            BindGroupLayout** layouts = stackalloc BindGroupLayout*[1] { resourceLayout };
            var pipelineDescription = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = layouts };
            PipelineLayout* layout = api.DeviceCreatePipelineLayout(device, in pipelineDescription);
            return new((nint)layout, (nint)resourceLayout, resources.Count == 0);
        }
        catch
        {
            api.BindGroupLayoutRelease(resourceLayout);
            throw;
        }
    }

    [GeneratedRegex(@"@binding\((?<binding>\d+)\)\s+@group\((?<group>\d+)\)\s+var(?:<(?<access>[^>]+)>)?\s+\w+\s*:\s*(?<type>[^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex ShaderResourcePattern();

    private sealed class ShaderInputLayout(nint pipeline, nint resources, bool empty)
    {
        public nint Pipeline { get; } = pipeline;
        public nint Resources { get; } = resources;
        public bool Empty { get; } = empty;
        private nint emptyGroup;

        public nint GetEmptyGroup(WebGpuDevice owner)
        {
            if (emptyGroup == 0)
            {
                var description = new BindGroupDescriptor { Layout = (BindGroupLayout*)Resources };
                emptyGroup = (nint)owner.api.DeviceCreateBindGroup(owner.device, in description);
            }
            return emptyGroup;
        }

        public void Dispose(ModernWebGpuApi api)
        {
            if (emptyGroup != 0) { api.BindGroupRelease((BindGroup*)emptyGroup); }
            api.PipelineLayoutRelease((PipelineLayout*)Pipeline);
            api.BindGroupLayoutRelease((BindGroupLayout*)Resources);
        }
    }
}
