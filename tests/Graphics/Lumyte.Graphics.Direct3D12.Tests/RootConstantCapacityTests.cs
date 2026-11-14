using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks native-root capacity and immutable CPU generations without a Windows device.</summary>
public sealed class RootConstantCapacityTests
{
    /// <summary>Checks logical sizes use ceil(bytes / 4), not aligned CPU capacity.</summary>
    /// <param name="bytes">The wire byte count.</param>
    /// <param name="words">The exact native DWORD count.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(20, 5)]
    [InlineData(229, 58)]
    [InlineData(232, 58)]
    public void LogicalCapacityUsesDwords(int bytes, int words)
    {
        Assert.Equal(58, Direct3D12Device.RootConstantDwordCapacity);
        Assert.Equal(232, Direct3D12Device.RootConstantByteCapacity);
        Assert.Equal(words, Direct3D12ShaderTransfer.ValidateRootSize(bytes));
    }

    /// <summary>Rejects oversize transfers before allocating or publishing another selection.</summary>
    [Fact]
    public void OversizeFailsBeforeAllocationOrPreparation()
    {
        bool allocated = false;
        bool prepared = false;
        IShaderRootStorage Factory(int size)
        {
            allocated = true;
            throw new InvalidOperationException();
        }

        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata metadata)
        {
            prepared = true;
            return null;
        }

        Oversize value = default;
        Assert.Throws<NotSupportedException>(() => Direct3D12ShaderTransfer.CreateRoot(null!, value, Factory, prepare: Prepare));
        OversizeWire raw = default;
        Assert.Throws<NotSupportedException>(() => Direct3D12ShaderTransfer.CreateUnmanaged(null!, raw, "arguments", null, Factory, prepare: Prepare));
        Assert.False(allocated);
        Assert.False(prepared);
        Assert.Throws<NotSupportedException>(() => Direct3D12ShaderTransfer.ValidateRootSize(int.MaxValue));
    }

    /// <summary>Checks source mutation, repeat sealing, tail zeroing and independent generations.</summary>
    /// <param name="bytes">The logical wire byte count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(232)]
    public void CpuPayloadIsIndependentAndTailIsZero(int bytes)
    {
        using var device = new Direct3D12Device(null!, false);
        byte[] source = Enumerable.Repeat((byte)37, bytes).ToArray();
        int capacity = ((bytes + 3) / 4) * 4;
        using var first = new Direct3D12RootStorage(device, bytes);
        first.Write(source, 0);
        source.AsSpan().Fill(99);
        first.Seal();
        first.Seal();
        Assert.Throws<InvalidOperationException>(() => first.Write(source, 0));
        using var second = new Direct3D12RootStorage(device, capacity);
        second.Write(source, 0);
        second.Seal();
        Assert.All(first.Payload[..bytes].ToArray(), value => Assert.Equal((byte)37, value));
        Assert.All(second.Payload[..bytes].ToArray(), value => Assert.Equal((byte)99, value));
        Assert.All(first.Payload[bytes..].ToArray(), value => Assert.Equal((byte)0, value));
        int words = Direct3D12ShaderTransfer.ValidateRootSize(bytes);
        Assert.Equal(words * 4, first.Size);
        Assert.Equal(words * 4, capacity);
        if (bytes == 232)
        {
            Assert.Equal(232, capacity);
            Assert.Equal(58, words);
        }
    }

    /// <summary>Validates compiled logical schemas before pipeline creation, without a Windows device.</summary>
    /// <param name="bytes">The logical root size.</param>
    /// <returns>The completed compiler and managed validation checks.</returns>
    [Theory]
    [InlineData(232)]
    [InlineData(236)]
    public async Task ShaderLogicalCapacityIsCheckedAsync(int bytes)
    {
        int words = bytes / 4;
        string fields = string.Join(" ", Enumerable.Range(0, words).Select(i => $"uint V{i};"));
        string source = "#include \"lumyte.slang\"\n" + $"struct Root {{ {fields} }}; ConstantBuffer<Root> customPayload; RWByteAddressBuffer output; [numthreads(1,1,1)] void main() {{ output.Store(0, customPayload.V{words - 1}); }}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = ShaderTarget.Dxil }, timeout.Token);
        ShaderTargetData target = artifact.GetTarget(ShaderTarget.Dxil);
        Assert.Equal(bytes, ShaderDataLayout.Root(target, "customPayload").Size);
        if (bytes == 232)
        {
            PipelineValidation.RootCapacity(target);
            Assert.Equal(58, Direct3D12ShaderTransfer.ValidateRootSize(bytes));
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => PipelineValidation.RootCapacity(target));
        }
    }

    /// <summary>Every application constant buffer is checked, but internal declarations are not roots.</summary>
    [Fact]
    public void CapacityChecksAllApplicationNamesAndSkipsInternals()
    {
        string Parameter(string name, int size) => $$$$"""
            {"name":"{{{{name}}}}","type":{"kind":"constantBuffer","elementType":{"kind":"struct","name":"Root","sizes":[{"kind":"uniform","value":{{{{size}}}}}],"fields":[]}}}
            """;
        ShaderTargetData Target(params string[] parameters) => new(ShaderTarget.Dxil, ShaderStage.Compute, "main", "test", "row-major", [], "{\"parameters\":[" + string.Join(',', parameters) + "]}");
        PipelineValidation.RootCapacity(Target(Parameter("lumyteDrawInfo", 4096), Parameter("__lumyte_schema_Root", 4096), Parameter("custom", 232)));
        Assert.Throws<NotSupportedException>(() => PipelineValidation.RootCapacity(Target(Parameter("first", 4), Parameter("second", 236))));
    }

    /// <summary>Sealing and disposal require no native allocation or native lifetime operations.</summary>
    [Fact]
    public void CpuStorageRejectsUseAfterDisposal()
    {
        using var device = new Direct3D12Device(null!, false);
        var storage = new Direct3D12RootStorage(device, 16);
        Assert.Throws<InvalidOperationException>(() => storage.Payload.ToArray());
        storage.Write([1, 2, 3], 0);
        storage.Seal();
        storage.Dispose();
        storage.Dispose();
        Assert.Throws<ObjectDisposedException>(() => storage.Payload.ToArray());
        Assert.Throws<ObjectDisposedException>(() => storage.Write([4], 0));
        Assert.Throws<ObjectDisposedException>(storage.Seal);
    }

    /// <summary>Guards the native command transport and full fixed root-signature budget.</summary>
    [Fact]
    public void NativeSourceUsesRootConstantsWithoutUploadFallback()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "native", "lumyte_graphics_d3d12")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string native = Path.Combine(directory.FullName, "native", "lumyte_graphics_d3d12");
        string binding = string.Join(" ", File.ReadAllText(Path.Combine(native, "binding.inl")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string rendering = File.ReadAllText(Path.Combine(native, "rendering.inl"));
        Assert.Contains("SetGraphicsRoot32BitConstants(0, static_cast<UINT>(binding->root_words.size()), binding->root_words.data(), 0)", binding);
        Assert.Contains("SetComputeRoot32BitConstants(0, static_cast<UINT>(binding->root_words.size()), binding->root_words.data(), 0)", binding);
        Assert.DoesNotContain("RootConstantBufferView", binding);
        Assert.DoesNotContain("buffer_create(", binding);
        Assert.DoesNotContain("root_create(", binding);
        Assert.Contains("std::vector<DWORD> root_words", binding);
        Assert.Contains("word_count > app_root_dword_capacity", binding);
        Assert.Contains("parameters[0].Constants = {8, 0, app_root_dword_capacity}", rendering);
        Assert.Contains("app_root_dword_capacity + fixed_root_dword_cost == 64", rendering);
        Assert.Contains("parameters[3].Constants = {9, 0, draw_info_dword_cost}", rendering);
        Assert.Contains("parameters[4].Descriptor = {0, 2}", rendering);
        Assert.DoesNotContain("D3D12_ROOT_PARAMETER_TYPE_CBV", rendering);
        string production = Path.Combine(directory.FullName, "src", "Graphics", "Lumyte.Graphics.Direct3D12");
        string compute = File.ReadAllText(Path.Combine(production, "Direct3D12ComputePipeline.Native.cs"));
        Assert.True(compute.IndexOf("PipelineValidation.RootCapacity(Data)", StringComparison.Ordinal) < compute.IndexOf("NativeMethods.ComputeCreate(", StringComparison.Ordinal));
        string graphics = File.ReadAllText(Path.Combine(production, "Direct3D12GraphicsPipeline.Native.cs"));
        int vertexCapacity = graphics.IndexOf("PipelineValidation.RootCapacity(VertexData)", StringComparison.Ordinal);
        int fragmentCapacity = graphics.IndexOf("PipelineValidation.RootCapacity(FragmentData)", StringComparison.Ordinal);
        int partialMode = graphics.IndexOf("if (!_owner.PartialGraphicsProgramsEnabled)", StringComparison.Ordinal);
        int partialCreate = graphics.IndexOf("NativeMethods.PartialCreate(", StringComparison.Ordinal);
        Assert.True(vertexCapacity >= 0 && fragmentCapacity > vertexCapacity && partialMode > fragmentCapacity && partialCreate > partialMode);
        foreach (string encoder in new[] { "Direct3D12RenderEncoder.cs", "Direct3D12ComputeEncoder.cs" })
        {
            string source = File.ReadAllText(Path.Combine(production, encoder));
            int capture = source.IndexOf("ShaderBindingDependencies.Collect(", StringComparison.Ordinal);
            int pipeline = source.IndexOf("NativeMethods.Pipeline(", capture, StringComparison.Ordinal);
            int bindingCommand = source.IndexOf("NativeMethods.Binding(", pipeline, StringComparison.Ordinal);
            Assert.True(capture >= 0 && pipeline > capture && bindingCommand > pipeline);
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 233)]
    private struct OversizeWire
    {
    }

    private readonly struct Oversize : IShaderArguments
    {
        /// <inheritdoc />
        public static string ShaderTypeName => "Oversize";

        /// <inheritdoc />
        public static int TransferSize => 233;

        /// <inheritdoc />
        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => throw new InvalidOperationException("Must reject before writing.");
    }
}
