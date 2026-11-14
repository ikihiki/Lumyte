using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Xunit;
using Xunit.Abstractions;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises native push constants through the public device and encoder contracts.</summary>
public sealed class VulkanPushConstantTests(ITestOutputHelper output)
{
    /// <summary>Supplies an immutable compile-time wire capacity.</summary>
    private interface ISize
    {
        /// <summary>Gets the native wire byte count.</summary>
        static abstract int Size { get; }
    }

    /// <summary>Checks DWORD rounding and immutable command ownership without backend API exposure.</summary>
    [GpuFact]
    public void RootPayloadUsesPhysicalPushLimit()
    {
        using var device = VulkanDevice.Create();
        output.WriteLine($"MaxPushConstantsSize={device.MaxPushConstantsSize}");
        Type constants = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanPushConstants", throwOnError: true)!;
        MethodInfo validate = constants.GetMethod("ValidateSize", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (int size in new[] { 0, 1, 3, 4, 20, checked((int)device.MaxPushConstantsSize - 1), checked((int)device.MaxPushConstantsSize) })
        {
            Assert.Equal(checked((uint)((size + 3) / 4 * 4)), Invoke(validate, device, size));
        }

        Assert.Throws<NotSupportedException>(() => Invoke(validate, device, checked((int)device.MaxPushConstantsSize + 1)));
        Assert.Throws<NotSupportedException>(() => Invoke(validate, device, checked((int)device.MaxPushConstantsSize + 4)));
        Type storageType = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanRootStorage", throwOnError: true)!;
        Assert.DoesNotContain(storageType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field => field.FieldType.GetInterfaces().Append(field.FieldType).Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IGraphicsBuffer<>)));
        using var storage = (IShaderRootStorage)Activator.CreateInstance(storageType, BindingFlags.Instance | BindingFlags.NonPublic, null, [device, 16], null)!;
        byte[] borrowed = [7, 8, 9];
        storage.Write(borrowed, 0);
        borrowed.AsSpan().Clear();
        storage.Seal();
        FieldInfo payload = storageType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(field => field.FieldType == typeof(byte[]));
        Assert.Equal(new byte[] { 7, 8, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, (byte[])payload.GetValue(storage)!);
        Assert.Throws<InvalidOperationException>(() => storage.Write(new byte[1], 0));
    }

    /// <summary>Dispatches small roots and the actual native limit with diagnostics off and on.</summary>
    /// <returns>The completed native readback checks.</returns>
    [GpuFact]
    public async Task DispatchPushesSelectedPayloadAsync()
    {
        using var probe = VulkanDevice.Create();
        output.WriteLine($"Native push range={probe.MaxPushConstantsSize}");
        await CheckAsync<Size4>(probe.MaxPushConstantsSize);
        await CheckAsync<Size20>(probe.MaxPushConstantsSize);
        if (probe.MaxPushConstantsSize == 128)
        {
            await CheckAsync<Size128>(probe.MaxPushConstantsSize);
        }
        else if (probe.MaxPushConstantsSize == 256)
        {
            await CheckAsync<Size256>(probe.MaxPushConstantsSize);
        }
        else
        {
            throw new NotSupportedException($"Add an immutable boundary marker for this adapter's {probe.MaxPushConstantsSize}-byte push limit.");
        }
    }

    /// <summary>Rejects an oversized artifact before any Set call or native pipeline creation.</summary>
    /// <returns>The completed actual-shader boundary checks.</returns>
    [GpuFact]
    public async Task ShaderCapacityIsCheckedBeforeNativeCreationAsync()
    {
        using var device = VulkanDevice.Create();
        int limit = checked((int)device.MaxPushConstantsSize);
        output.WriteLine($"Artifact boundary={limit}/{limit + 4}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        foreach (int bytes in new[] { limit, limit + 4 })
        {
            int words = bytes / 4;
            string fields = string.Join(" ", Enumerable.Range(0, words - 4).Select(i => $"uint V{i};"));
            string source = "#include \"lumyte.slang\"\n" + $"struct Root {{ GpuRWBufferRef<uint> Output; {fields} }}; ConstantBuffer<Root> customPayload; [numthreads(1,1,1)] void main() {{ customPayload.Output.Store(0, customPayload.V{words - 5}); }}";
            ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = ShaderTarget.SpirV }, timeout.Token);
            if (bytes > limit)
            {
                Assert.Throws<NotSupportedException>(() => device.CreateShader(artifact));
                continue;
            }

            using IGraphicsShader shader = device.CreateShader(artifact);
            using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        }
    }

    /// <summary>Checks all root names while excluding draw information and schema probes.</summary>
    [GpuFact]
    public void ArtifactValidationChecksEveryApplicationRoot()
    {
        using var device = VulkanDevice.Create();
        Type constants = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanPushConstants", throwOnError: true)!;
        MethodInfo validate = constants.GetMethod("ValidateStorage", BindingFlags.Static | BindingFlags.NonPublic)!;
        string Parameter(string name, int size) => $$$$"""
            {"name":"{{{{name}}}}","type":{"kind":"constantBuffer","elementType":{"kind":"struct","name":"Root","sizes":[{"kind":"uniform","value":{{{{size}}}}}],"fields":[]}}}
            """;
        ShaderTargetData Target(params string[] parameters) => new(ShaderTarget.SpirV, ShaderStage.Compute, "main", "test", "row-major", [], "{\"lumyteRootStorage\":\"VkPushConstants\",\"parameters\":[" + string.Join(',', parameters) + "]}");
        Invoke(validate, device, Target(Parameter("lumyteDrawInfo", 4096), Parameter("__lumyte_schema_Root", 4096), Parameter("custom", 4)));
        Assert.Throws<NotSupportedException>(() => Invoke(validate, device, Target(Parameter("first", 4), Parameter("second", checked((int)device.MaxPushConstantsSize + 4)))));
    }

    private static object? Invoke(MethodInfo method, params object[] arguments)
    {
        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private async Task CheckAsync<TSize>(uint limit)
        where TSize : struct, ISize
    {
        int words = TSize.Size / 4;
        string fields = string.Join(" ", Enumerable.Range(0, TSize.Size == 4 ? 1 : words - 4).Select(index => $"uint Value{index};"));
        string reference = TSize.Size == 4 ? string.Empty : "GpuRWBufferRef<uint> Output;";
        string body = TSize.Size == 4 ? "if (arguments.Value0 == 123) return;" : string.Join(" ", Enumerable.Range(0, words - 4).Select(index => $"arguments.Output.Store({index}, arguments.Value{index});"));
        string source = $"#include \"lumyte.slang\"\nstruct Payload {{ {fields} {reference} }}; ConstantBuffer<Payload> arguments; [numthreads(1,1,1)] void main() {{ {body} }}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = ShaderStage.Compute, Target = ShaderTarget.SpirV }, timeout.Token);
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            if (TSize.Size == 20)
            {
                ShaderTargetData targetData = artifact.GetTarget(ShaderTarget.SpirV);
                var legacy = ShaderArtifact.PackTargets([targetData with { ReflectionJson = targetData.ReflectionJson.Replace("VkPushConstants", "UniformBuffer", StringComparison.Ordinal) }]);
                Assert.Throws<NotSupportedException>(() => device.CreateShader(legacy));
            }

            using IGraphicsShader shader = device.CreateShader(artifact);
            using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
            uint count = checked((uint)Math.Max(1, words - 4));
            using IGraphicsBuffer<uint> result = device.CreateBuffer<uint>(new() { Count = count, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
            using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = count, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
            IGpuRef<uint> target = table.WriteBuffer(0, result.SliceElements(0, count), ShaderBufferAccess.ReadWrite);
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            IComputeEncoder encoder = commands.BeginComputePass(new());
            encoder.SetPipeline(pipeline);
            encoder.SetArgumentTable(table);
            var value = new BoundaryRoot<TSize>(7, target);
            encoder.SetArguments(in value);
            value.Number = 999;
            if (limit == 128)
            {
                var rejected = new BoundaryRoot<Size132>(11, target);
                Assert.Throws<NotSupportedException>(() => encoder.SetArguments(in rejected));
                Assert.Equal(0, rejected.WriteCount);
            }
            else
            {
                var rejected = new BoundaryRoot<Size260>(11, target);
                Assert.Throws<NotSupportedException>(() => encoder.SetArguments(in rejected));
                Assert.Equal(0, rejected.WriteCount);
            }

            encoder.SetArgumentValidation(_ => throw new InvalidOperationException("admission rejected"));
            var replacement = new BoundaryRoot<TSize>(17, target);
            Assert.Equal("admission rejected", Assert.Throws<InvalidOperationException>(() => encoder.SetArguments(in replacement)).Message);
            encoder.SetArgumentValidation(null);
            encoder.Dispatch(1);
            encoder.End();
            if (TSize.Size > 4)
            {
                commands.Barrier(new BufferBarrierDesc<uint> { Buffer = result.SliceElements(0, count), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
                commands.CopyBuffer(result.SliceElements(0, count), readback.SliceElements(0, count));
                commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.SliceElements(0, count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            }

            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync(timeout.Token);
            Assert.Equal(SubmissionStatus.Completed, submission.Status);
            if (TSize.Size > 4)
            {
                await readback.MapAsync(timeout.Token);
                uint[] actual = new uint[count];
                readback.CopyTo(actual);
                readback.Unmap();
                Assert.Equal(Enumerable.Range(0, (int)count).Select(index => checked(7u + (uint)index)), actual);
            }

            output.WriteLine($"wire={TSize.Size}; validation={validation}; rejected={limit + 4}; previous selection dispatched{(TSize.Size > 4 ? " and read back" : string.Empty)}");
        }
    }

    private readonly struct Size4 : ISize
    {
        public static int Size => 4;
    }

    private readonly struct Size20 : ISize
    {
        public static int Size => 20;
    }

    private readonly struct Size128 : ISize
    {
        public static int Size => 128;
    }

    private readonly struct Size132 : ISize
    {
        public static int Size => 132;
    }

    private readonly struct Size256 : ISize
    {
        public static int Size => 256;
    }

    private readonly struct Size260 : ISize
    {
        public static int Size => 260;
    }

    private sealed class BoundaryRoot<TSize>(uint number, IGpuRef<uint> target) : IShaderArguments
        where TSize : struct, ISize
    {
        public static string ShaderTypeName => "Payload";

        public static int TransferSize => TSize.Size;

        internal uint Number { get; set; } = number;

        internal int WriteCount { get; private set; }

        public IReadOnlyList<ShaderTransferField> GetTransferFields()
        {
            int count = TSize.Size == 4 ? 1 : (TSize.Size - 16) / 4;
            var fields = Enumerable.Range(0, count).Select(index => new ShaderTransferField($"Value{index}", index * 4, 4, typeof(uint))).ToList();
            if (TSize.Size > 4)
            {
                fields.Add(new("Output", count * 4, 16, typeof(IGpuRef<uint>), true));
            }

            return fields;
        }

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            WriteCount++;
            int count = TSize.Size == 4 ? 1 : (TSize.Size - 16) / 4;
            for (int index = 0; index < count; index++)
            {
                uint value = checked(Number + (uint)index);
                writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            }

            if (TSize.Size > 4)
            {
                writer.WriteReference(target);
            }
        }
    }
}
