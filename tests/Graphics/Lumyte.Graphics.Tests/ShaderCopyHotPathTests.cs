using System.Reflection;
using System.Reflection.Emit;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Deterministic count, ownership, direct-span and callback lifetime regressions.</summary>
public sealed class ShaderCopyHotPathTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Source = """
        #include "lumyte.slang"
        struct HotPathValue { float4 Padding; GpuBufferRef<uint> First; GpuBufferRef<uint> Second; };
        struct CopyNumeric { uint Value; };
        struct Root { GpuBufferRef<HotPathValue> Values; GpuBufferRef<CopyNumeric> Numeric; };
        ConstantBuffer<Root> arguments;
        [shader("compute")] [numthreads(1,1,1)]
        void main() { uint x = arguments.Values.Load(0).First.Load(arguments.Numeric.Load(0).Value); }
        """;

    /// <summary>Borrows an already mapped native upload range for test-only byte inspection.</summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="length">The byte length.</param>
    /// <returns>The existing mapped bytes.</returns>
    private delegate Span<byte> MappedUpload(ulong offset, int length);

    private delegate ShaderRootArguments VulkanRoot<T>(VulkanDevice owner, in T value, Span<byte> bytes)
        where T : struct, IShaderArguments;

    private delegate IDisposable WgpuRoot<T>(WgpuDevice owner, in T value)
        where T : struct, IShaderArguments;

    /// <summary>Provides a deliberately declared physical slot total.</summary>
    private interface ICount
    {
        /// <summary>Gets the declared total.</summary>
        static abstract int Count { get; }
    }

    /// <summary>Static counts describe physical slots without reading instances or getters.</summary>
    [Fact]
    public void GeneratedCountsMatchPhysicalWriterCalls()
    {
        CheckCount(new ReadonlyOuterArguments(17), 0);
        CheckCount(default(UnifiedTransferValue), 1);
        CheckCount(new WireCursorValue(31), 2);
        CheckCount(new CopyEightReferences(), 8);
        var value = new WireCursorGetterValue { Number = 17 };
        Assert.Equal(1, Count<WireCursorGetterValue>());
        Assert.Equal(0, value.GetterCalls);
        CheckCount(value, 1);
        Assert.Equal(1, value.GetterCalls);
        Assert.Equal(17u, value.Number);
        MethodInfo getter = typeof(IShaderArguments).GetProperty(nameof(IShaderArguments.ReferenceCount))!.GetMethod!;
        Assert.True(getter.IsStatic);
        Assert.True(getter.IsAbstract);
    }

    /// <summary>Dependency snapshots own immutable copies of reference identities.</summary>
    [Fact]
    public void MetadataOwnsImmutableDependencySnapshot()
    {
        IShaderReference reference = new Reference();
        IShaderReference[] slots = [reference, reference];
        var metadata = new ShaderDependencyMetadata(slots);
        slots[0] = new Reference();
        Assert.Equal(2, metadata.References.Count);
        Assert.All(metadata.References, item => Assert.Same(reference, item));
        Assert.False(metadata.References is IShaderReference[]);
        Assert.Throws<NotSupportedException>(() => ((IList<IShaderReference>)metadata.References)[0] = slots[0]);
        Assert.Empty(new ShaderDependencyMetadata([]).References);
    }

    /// <summary>Raw writers have no semantic count fields, constructor parameters or guards.</summary>
    /// <param name="backend">The backend under test.</param>
    [Theory]
    [InlineData("Wgpu")]
    [InlineData("Vulkan")]
    public void RawWritersHaveNoReferenceCountContract(string backend)
    {
        Type writer = Assembly.Load("Lumyte.Graphics." + backend).GetType($"Lumyte.Graphics.{backend}.{backend}ShaderValueWriter", true)!;
        Assert.Null(writer.GetMethod("ValidateExpectedCount", BindingFlags.Static | BindingFlags.NonPublic));
        Assert.DoesNotContain(writer.GetFields(Hidden), field => field.Name.Contains("referenceCount", StringComparison.OrdinalIgnoreCase) || field.Name.Contains("expectedCount", StringComparison.OrdinalIgnoreCase));
        Assert.All(writer.GetConstructors(Hidden), constructor => Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.Name == "expectedCount"));
    }

    /// <summary>Leases reject every overlapping writer and release safely on exception unwinding.</summary>
    [Fact]
    public void ValidationLeaseProtectsMappedAllocationAndCleansUp()
    {
        var validation = new GraphicsValidation();
        object buffer = new();
        var original = new ApplicationException("original codec failure");
        Exception? error = Record.Exception((Action)(() =>
        {
            using GraphicsValidation.HostWriteLease lease = validation.BeginHostWrite(buffer, 16, 32);
            Assert.Throws<InvalidOperationException>(() => validation.BeginHostWrite(buffer, 32, 1));
            Assert.Throws<InvalidOperationException>(() => validation.HostAccess(buffer, ResourceAccess.HostWrite, 16, 1));
            Assert.Throws<InvalidOperationException>(() => validation.ValidateUnmap(buffer));
            Assert.Throws<InvalidOperationException>(() => validation.Map(buffer));
            Assert.Throws<InvalidOperationException>(() => validation.ValidateRelease(buffer));
            using GraphicsValidation.HostWriteLease disjoint = validation.BeginHostWrite(buffer, 48, 16);
            validation.HostAccess(buffer, ResourceAccess.HostWrite, 0, 16);
            throw original;
        }));
        Assert.Same(original, error);
        validation.ValidateUnmap(buffer);
        validation.Map(buffer);
        validation.ValidateRelease(buffer);
        GraphicsValidation.HostWriteLease later = validation.BeginHostWrite(buffer, 16, 32);
        later.Dispose();
        later.Dispose();
        validation.Release(buffer);
    }

    /// <summary>Disabled validation does not create a tracker or execute unsafe native callbacks.</summary>
    [Fact]
    public void DisabledValidationDoesNotAllocateLeaseOrInvokeCallback()
    {
        GraphicsValidation? validation = null;
        int callbacks = 0;
        object Buffer()
        {
            callbacks++;
            return new();
        }

        using GraphicsValidation.HostWriteLease? lease = validation?.BeginHostWrite(Buffer(), 0, 4);
        Assert.Null(validation);
        Assert.Null(lease);
        Assert.Equal(0, callbacks);
    }

    /// <summary>Raw backends borrow once; only the decorator leases and collects dependencies.</summary>
    /// <param name="backend">The backend under test.</param>
    [Theory]
    [InlineData("Wgpu")]
    [InlineData("Vulkan")]
    [InlineData("Direct3D12")]
    [InlineData("Browser")]
    public void BackendSourcesKeepSingleBorrowAndDirectWrites(string backend)
    {
        string transfer = ReadSource($"src/Graphics/Lumyte.Graphics.{backend}/{backend}ShaderTransfer.cs");
        string copy = transfer[transfer.IndexOf("internal static void CopyFrom", StringComparison.Ordinal)..];
        string borrow = backend == "Browser" ? "AcquireMappedUploadRange(" : "GetMappedUploadSpan(";
        Assert.Equal(1, Occurrences(copy, borrow));
        Assert.True(copy.IndexOf(borrow, StringComparison.Ordinal) < copy.IndexOf("for (int i", StringComparison.Ordinal));
        Assert.DoesNotContain("BeginHostWrite", transfer);
        Assert.DoesNotContain("GraphicsValidation", transfer);
        string device = ReadSource($"src/Graphics/Lumyte.Graphics.{backend}/{backend}Device.cs");
        Assert.DoesNotContain("GraphicsValidation", device);
        Assert.DoesNotContain("EnableValidation(", device);
        string writer = ReadSource($"src/Graphics/Lumyte.Graphics.{backend}/{backend}ShaderValueWriter.cs");
        Assert.DoesNotContain("CopyBytesFrom(", writer);
        Assert.Contains(backend == "Browser" ? "BrowserInterop.CopyMappedUploadRange(" : backend == "Wgpu" ? "value.CopyTo(_upload.Slice(" : "value.CopyTo(_destination.Slice(", writer);
        Assert.DoesNotContain("_referenceCount", writer);
        Assert.DoesNotContain("_expectedCount", writer);
        Assert.DoesNotContain("T.ReferenceCount", transfer);
        Assert.DoesNotContain("IShaderReferenceMetadata", writer);
        Assert.DoesNotContain("new IShaderReference[", writer);
        Assert.DoesNotContain("ValidateExpectedCount", writer);
        Assert.Equal(backend == "Browser" ? 1 : 0, Occurrences(writer, "stackalloc byte[16]"));
        string referenceWrite = writer[writer.IndexOf("public void WriteReference", StringComparison.Ordinal)..];
        referenceWrite = referenceWrite[..referenceWrite.IndexOf("internal void Complete", StringComparison.Ordinal)];
        Assert.DoesNotContain("WriteValue(", referenceWrite);
        Assert.Equal(1, Occurrences(referenceWrite, "ValidateWrite("));
        string decorator = ReadSource("src/Graphics/Lumyte.Graphics.Validation/ValidationShaderDataBuffer.cs");
        Assert.Contains("Tracker.BeginHostWrite(this, offset, length)", decorator);
        Assert.Contains("new ShaderDependencyMetadata(", decorator);
        Assert.Contains("Raw.CopyFrom(native, elementOffset)", decorator);
        string unmanaged = transfer[transfer.IndexOf("CreateUnmanaged", StringComparison.Ordinal)..];
        unmanaged = unmanaged[..unmanaged.IndexOf("internal static void CopyFrom", StringComparison.Ordinal)];
        Assert.DoesNotContain("T.ReferenceCount", unmanaged);
    }

    /// <summary>Native compiled transfer entrypoints perform exactly one borrow without a diagnostic lease.</summary>
    /// <param name="backend">The native backend under test.</param>
    [Theory]
    [InlineData("Wgpu")]
    [InlineData("Vulkan")]
    public void NativeILBorrowsOnceAndWriterDoesNotCallBufferCopies(string backend)
    {
        var assembly = Assembly.Load("Lumyte.Graphics." + backend);
        Type transfer = assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ShaderTransfer", true)!;
        MethodInfo copy = transfer.GetMethod("CopyFrom", BindingFlags.Static | BindingFlags.NonPublic)!;
        (System.Reflection.Emit.OpCode Code, MemberInfo? Member)[] instructions = ImmediateRootCommandTests.Instructions(copy).ToArray();
        Assert.Single(instructions, item => item.Member?.Name == "GetMappedUploadSpan");
        Assert.DoesNotContain(instructions, item => item.Member?.Name == "BeginHostWrite");
        Assert.DoesNotContain(instructions, item => item.Member?.Name == "get_ReferenceCount");
        Type writer = assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ShaderValueWriter", true)!;
        MethodInfo write = writer.GetMethod("WriteValue")!;
        Assert.DoesNotContain(ImmediateRootCommandTests.Instructions(write), item => item.Member?.Name == "CopyBytesFrom");
        (OpCode Code, MemberInfo? Member)[] referenceInstructions = ImmediateRootCommandTests.Instructions(writer.GetMethod("WriteReference")!).ToArray();
        Assert.DoesNotContain(referenceInstructions, item => item.Member?.Name is "GetBufferDeviceAddress" or "WriteValue");
        Assert.DoesNotContain(referenceInstructions, item => item.Code == OpCodes.Localloc);
        Assert.Single(referenceInstructions, item => item.Member?.Name == "ValidateWrite");
    }

    /// <summary>The Vulkan native address query is constructor-only; D3D caches at allocation.</summary>
    [Fact]
    public void NativeAddressQueriesRemainAllocationOnly()
    {
        Type buffer = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanBuffer`1", true)!;
        FieldInfo address = buffer.GetField("_shaderAddress", Hidden)!;
        Assert.Equal(typeof(ulong), address.FieldType);
        Assert.True(address.IsInitOnly);
        ConstructorInfo constructor = Assert.Single(buffer.GetConstructors(Hidden));
        Assert.Single(ImmediateRootCommandTests.Instructions(constructor), item => item.Member?.Name == "GetBufferDeviceAddress");
        foreach (MethodInfo method in buffer.GetMethods(Hidden | BindingFlags.Public))
        {
            Assert.DoesNotContain(ImmediateRootCommandTests.Instructions(method), item => item.Member?.Name == "GetBufferDeviceAddress");
        }

        string source = ReadSource("src/Graphics/Lumyte.Graphics.Vulkan/VulkanBuffer.cs");
        Assert.Equal(1, Occurrences(source, ".GetBufferDeviceAddress("));
        Assert.True(source.IndexOf(".GetBufferDeviceAddress(", StringComparison.Ordinal) < source.IndexOf("ulong IVulkanShaderBuffer.ShaderAddress", StringComparison.Ordinal));
        Assert.DoesNotContain("GetBufferDeviceAddress", ReadSource("src/Graphics/Lumyte.Graphics.Vulkan/VulkanShaderValueWriter.cs"));
        string native = ReadSource("native/lumyte_graphics_d3d12/d3d12.cpp");
        Assert.Equal(1, Occurrences(native, "GetGPUVirtualAddress("));
        Assert.DoesNotContain("GetGPUVirtualAddress(", ReadSource("native/lumyte_graphics_d3d12/rendering.inl"));
    }

    /// <summary>Real Wgpu transfers cover exact counts, failure invalidation and active-span callbacks.</summary>
    /// <returns>The native test completion.</returns>
    [GpuFact]
    public async Task WgpuNativeCopyContractAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.Wgsl);
        foreach (bool enabled in new[] { false, true })
        {
            using var raw = WgpuDevice.Create();
            using ValidationDevice? layer = enabled ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;

            await CheckNativeAsync(device, artifact, enabled);
        }
    }

    /// <summary>Real Vulkan transfers cover exact counts, failure invalidation and active-span callbacks.</summary>
    /// <returns>The native test completion.</returns>
    [GpuFact]
    public async Task VulkanNativeCopyContractAsync()
    {
        ShaderArtifact artifact = await CompileAsync(ShaderTarget.SpirV);
        foreach (bool enabled in new[] { false, true })
        {
            using var raw = VulkanDevice.Create();
            using ValidationDevice? layer = enabled ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;

            await CheckNativeAsync(device, artifact, enabled);
        }
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target) => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Target = target, Stage = ShaderStage.Compute });

    private static async Task CheckNativeAsync(IGraphicDevice device, ShaderArtifact artifact, bool enabled)
    {
        Assert.Equal(enabled, device.ValidationEnabled);
        using IGraphicsBuffer<uint> numbers = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.ShaderRead });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 4 });
        IGpuRef<uint> reference = table.WriteBuffer(0, numbers.SliceElements(1, 4));
        IGpuRef<uint> replacement = table.WriteBuffer(1, numbers.SliceElements(6, 1));
        using IGraphicsShaderDataBuffer<HotPathValue> upload = device.CreateShaderDataBuffer<HotPathValue>(artifact, 5, MemoryPreference.Upload);
        IShaderDataSource data = Assert.IsAssignableFrom<IShaderDataSource>(upload);
        if (!enabled)
        {
            CheckRoots(device, reference);
        }
        else
        {
            await CheckValidatedRootsAsync(device, reference);
        }

        var valid = new HotPathValue(reference);
        Assert.Throws<InvalidOperationException>(() => upload.CopyFrom([valid]));
        await upload.MapAsync();
        try
        {
            upload.CopyFrom([valid, valid, valid, valid, valid]);
            Assert.Equal(5, valid.Calls[0]);
            IShaderReferenceMetadata? first = QueryDependencies(data, enabled);
            if (enabled)
            {
                Assert.Same(reference, Assert.Single(first!.References));
                Assert.Same(first, QueryDependencies(data, enabled));
            }

            var changed = new HotPathValue(replacement);
            upload.CopyFrom([changed], 2);
            IShaderReferenceMetadata? union = QueryDependencies(data, enabled);
            if (enabled)
            {
                Assert.NotSame(first, union);
                Assert.Same(reference, Assert.Single(first!.References));
                Assert.Contains((IShaderReference)reference, union!.References);
                Assert.Contains((IShaderReference)replacement, union.References);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => upload.CopyFrom([valid], ulong.MaxValue));
            Assert.Same(union, QueryDependencies(data, enabled)); // Bounds rejection does not change bytes.
            upload.CopyFrom([valid, valid, valid, valid, valid]);
            first = QueryDependencies(data, enabled);
            var original = new ApplicationException("codec failure");
            var bad = new HotPathValue(reference, () => throw original);
            Assert.Same(original, Record.Exception(() => upload.CopyFrom([valid, bad, valid], 1)));
            AssertUnknownDependencies(data, enabled);
            upload.CopyFrom([valid], 1);
            AssertUnknownDependencies(data, enabled); // A partial success cannot repair unknown bytes elsewhere.
            if (enabled)
            {
                Assert.Same(reference, Assert.Single(first!.References)); // Old revisions remain immutable.
            }

            upload.CopyFrom([valid, valid, valid, valid, valid]);
            QueryDependencies(data, enabled);

            if (enabled)
            {
                foreach (Action action in new Action[]
                {
                    upload.Unmap,
                    upload.Dispose,
                    () => upload.CopyFrom([valid], 1),
                })
                {
                    int attempts = 0;
                    var guarded = new HotPathValue(reference, () =>
                    {
                        attempts++;
                        Assert.Throws<InvalidOperationException>(action);
                    });
                    upload.CopyFrom([guarded], 1);
                    Assert.Equal(1, attempts);
                    upload.CopyFrom([valid, valid, valid, valid, valid]);
                    Assert.Same(reference, Assert.Single(QueryDependencies(data, enabled)!.References));
                }

                var disjoint = new HotPathValue(reference, () => upload.CopyFrom([valid], 4));
                upload.CopyFrom([disjoint], 1);
                Assert.Same(reference, Assert.Single(QueryDependencies(data, enabled)!.References));
            }
            else
            {
                int safeCallbacks = 0;
                var safe = new HotPathValue(reference, () => safeCallbacks++);
                upload.CopyFrom([safe], 1);
                Assert.Equal(1, safeCallbacks);
                Assert.False(device.ValidationEnabled);
            }
        }
        finally
        {
            upload.Unmap();
        }

        if (!enabled)
        {
            await CheckMappedBoundsAsync(device); // Native span seam is raw-only.
        }

        await CheckEmptyAsync(device, artifact, enabled);
        await CheckCountMismatchAsync<DeclaredOne>(device, artifact, reference, enabled);
        await CheckCountMismatchAsync<DeclaredThree>(device, artifact, reference, enabled);
        await CheckCountMismatchAsync<DeclaredNegative>(device, artifact, reference, enabled);
        await CheckCountMismatchAsync<DeclaredHuge>(device, artifact, reference, enabled);
    }

    private static void CheckRoots(IGraphicDevice device, IGpuRef<uint> reference)
    {
        CheckRoot(device, new CopyNumeric(17), 0);
        CheckRoot(device, new UnifiedTransferValue { Value = 17, Output = reference }, 1);
        CheckRoot(device, new WireCursorValue(31) { A = reference, B = reference }, 2);
        CheckRoot(device, new CopyEightReferences { R0 = reference, R1 = reference, R2 = reference, R3 = reference, R4 = reference, R5 = reference, R6 = reference, R7 = reference }, 8);
        var getter = new WireCursorGetterValue { Number = 17, Reference = reference };
        Assert.Equal(1, Count<WireCursorGetterValue>());
        Assert.Equal(0, getter.GetterCalls);
        CheckRoot(device, getter, 1);
        Assert.Equal(1, getter.GetterCalls);
        var once = new HotPathValue(reference);
        CheckRoot(device, once, 2);
        Assert.Equal(1, once.Calls[0]);
        var unpreparedValue = new HotPathValue(reference);
        using (CreateRoot(device, in unpreparedValue, out ShaderRootArguments unprepared))
        {
            Assert.Null(unprepared.References);
        }

        Assert.Equal(1, unpreparedValue.Calls[0]);
        CheckBadRoot(device, new CountedValue<DeclaredOne>(reference));
        CheckBadRoot(device, new CountedValue<DeclaredThree>(reference));
        CheckBadRoot(device, new CountedValue<DeclaredNegative>(reference));
        CheckBadRoot(device, new CountedValue<DeclaredHuge>(reference));
        var throwing = new CountedValue<DeclaredThrowing>(reference);
        using (CreateRoot(device, in throwing, out ShaderRootArguments throwingRoot))
        {
            Assert.Equal(48, throwingRoot.WireSize);
        }

        Assert.Equal(1, throwing.Calls[0]);
        var pair = new TwoReferences(reference);
        using (CreateRoot(device, in pair, out ShaderRootArguments pairRoot))
        {
            Assert.Equal(32, pairRoot.WireSize);
        }
    }

    private static void CheckRoot<T>(IGraphicDevice device, T value, int expected)
        where T : struct, IShaderArguments
    {
        using IDisposable? storage = CreateRoot(device, in value, out ShaderRootArguments arguments);
        Assert.Equal(expected, T.ReferenceCount);
        Assert.Null(arguments.References);
        Assert.Equal(T.TransferSize, arguments.WireSize);
    }

    private static void CheckBadRoot<TCount>(IGraphicDevice device, CountedValue<TCount> value)
        where TCount : struct, ICount
    {
        Exception? failure = Record.Exception(() =>
        {
            using IDisposable? root = CreateRoot(device, in value, out _);
        });
        Assert.Null(failure); // Semantic declarations belong exclusively to validation.
        Assert.Equal(1, value.Calls[0]);
    }

    private static IDisposable? CreateRoot<T>(IGraphicDevice device, in T value, out ShaderRootArguments arguments)
        where T : struct, IShaderArguments
    {
        string backend = device is WgpuDevice ? "Wgpu" : "Vulkan";
        Type transfer = device.GetType().Assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ShaderTransfer", true)!;
        MethodInfo create = transfer.GetMethod("CreateRoot", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T));
        if (device is WgpuDevice wgpu)
        {
            WgpuRoot<T> write = create.CreateDelegate<WgpuRoot<T>>();
            IDisposable storage = write(wgpu, in value);
            arguments = (ShaderRootArguments)storage.GetType().GetProperty("Arguments", Hidden)!.GetValue(storage)!;
            return storage;
        }

        var adapter = new DynamicMethod(
            "CreateVulkanRootArguments",
            typeof(ShaderRootArguments),
            [typeof(VulkanDevice), typeof(T).MakeByRefType(), typeof(Span<byte>)],
            typeof(ShaderCopyHotPathTests).Module,
            skipVisibility: true);
        ILGenerator il = adapter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, create);
        il.Emit(OpCodes.Ldfld, create.ReturnType.GetField("Item1")!);
        il.Emit(OpCodes.Ret);
        VulkanRoot<T> vulkanWrite = adapter.CreateDelegate<VulkanRoot<T>>();
        arguments = vulkanWrite((VulkanDevice)device, in value, new byte[T.TransferSize]);
        return null;
    }

    private static IGraphicsBuffer<byte> Storage(object upload)
    {
        object native = upload.GetType().GetProperty("RawObject")?.GetValue(upload) ?? upload;
        return (IGraphicsBuffer<byte>)native.GetType().GetProperty("Storage", Hidden)!.GetValue(native)!;
    }

    private static async Task CheckValidatedRootsAsync(IGraphicDevice device, IGpuRef<uint> reference)
    {
        const string RootSource = """
            #include "lumyte.slang"
            struct HotPathValue { float4 Padding; GpuBufferRef<uint> First; GpuBufferRef<uint> Second; };
            ConstantBuffer<HotPathValue> arguments;
            [shader("compute")] [numthreads(1,1,1)]
            void main() { uint x = arguments.First.Load(0) + arguments.Second.Load(0); }
            """;
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = RootSource, Target = device.Caps.ShaderTarget, Stage = ShaderStage.Compute });
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IComputeEncoder encoder = commands.BeginComputePass(new());
        try
        {
            encoder.SetPipeline(pipeline);
            int prepared = 0;
            IShaderReferenceMetadata? snapshot = null;
            encoder.SetArgumentValidation(metadata =>
            {
                prepared++;
                snapshot = metadata;
                Assert.Same(reference, Assert.Single(metadata.References)); // Physical slots aggregate by identity.
                return null;
            });
            var value = new HotPathValue(reference);
            encoder.SetArguments(in value);
            Assert.Equal(1, value.Calls[0]);
            Assert.Equal(1, prepared);
            encoder.SetArgumentValidation(null);
            var unprepared = new HotPathValue(reference);
            encoder.SetArguments(in unprepared);
            Assert.Equal(1, unprepared.Calls[0]);
            Assert.Equal(1, prepared);
            Assert.Same(reference, Assert.Single(snapshot!.References));
            encoder.SetArgumentValidation(_ =>
            {
                prepared++;
                return null;
            });
            CheckRejectedRoot<DeclaredOne>(encoder, reference);
            CheckRejectedRoot<DeclaredThree>(encoder, reference);
            CheckRejectedRoot<DeclaredNegative>(encoder, reference);
            CheckRejectedRoot<DeclaredHuge>(encoder, reference);
            Assert.Equal(1, prepared);
        }
        finally
        {
            encoder.End();
        }
    }

    private static void CheckRejectedRoot<TCount>(IComputeEncoder encoder, IGpuRef<uint> reference)
        where TCount : struct, ICount
    {
        var value = new CountedValue<TCount>(reference);
        Exception? failure = Record.Exception(() => encoder.SetArguments(in value));
        if (TCount.Count < 0 || TCount.Count > 3)
        {
            Assert.IsType<ArgumentOutOfRangeException>(failure);
            Assert.Equal(0, value.Calls[0]); // The decorator rejects impossible declarations before the application codec.
        }
        else
        {
            if (TCount.Count == 1)
            {
                Assert.IsType<InvalidOperationException>(failure); // The decorator rejects the undeclared slot before native encoding.
            }
            else
            {
                Assert.IsType<InvalidOperationException>(failure); // Decorator detects the missing declared slot on completion.
            }

            Assert.Equal(1, value.Calls[0]);
        }
    }

    private static async Task CheckMappedBoundsAsync(IGraphicDevice device)
    {
        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = 16, Memory = MemoryPreference.Upload, Usage = BufferUsage.CopySource });
        MappedUpload borrow = upload.GetType().GetMethod("GetMappedUploadSpan", Hidden)!.CreateDelegate<MappedUpload>(upload);
        Assert.Throws<InvalidOperationException>(() => { _ = borrow(0, 4); });
        await upload.MapAsync();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = borrow(ulong.MaxValue, 4); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = borrow(15, 2); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = borrow(0, -1); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = borrow(0, 0); });
            Assert.Equal(16, borrow(0, 16).Length);
        }
        finally
        {
            upload.Unmap();
        }

        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 16, Memory = MemoryPreference.Readback, Usage = BufferUsage.CopyDestination });
        MappedUpload wrongDirection = readback.GetType().GetMethod("GetMappedUploadSpan", Hidden)!.CreateDelegate<MappedUpload>(readback);
        await readback.MapAsync();
        try
        {
            Assert.Throws<InvalidOperationException>(() => { _ = wrongDirection(0, 4); });
        }
        finally
        {
            readback.Unmap();
        }
    }

    private static async Task CheckEmptyAsync(IGraphicDevice device, ShaderArtifact artifact, bool enabled)
    {
        using IGraphicsShaderDataBuffer<CopyNumeric> upload = device.CreateShaderDataBuffer<CopyNumeric>(artifact, 2, MemoryPreference.Upload);
        await upload.MapAsync();
        try
        {
            var first = new CopyNumeric(17);
            var second = new CopyNumeric(31);
            upload.CopyFrom([first, second]);
            var source = (IShaderDataSource)upload;
            IShaderReferenceMetadata? empty = QueryDependencies(source, enabled);
            if (enabled)
            {
                Assert.Empty(empty!.References);
            }

            upload.CopyFrom([second, first]);
            IShaderReferenceMetadata? next = QueryDependencies(source, enabled);
            if (enabled)
            {
                Assert.Empty(next!.References);
                Assert.Empty(empty!.References);
            }
        }
        finally
        {
            upload.Unmap();
        }
    }

    private static async Task CheckCountMismatchAsync<TCount>(IGraphicDevice device, ShaderArtifact artifact, IGpuRef<uint> reference, bool enabled)
        where TCount : struct, ICount
    {
        if (enabled)
        {
            Assert.Throws<ArgumentException>(() => device.CreateShaderDataBuffer<CountedValue<TCount>>(artifact, 1, MemoryPreference.Upload));
            return; // Compiled-schema mismatch is rejected before allocation or writes.
        }

        using IGraphicsShaderDataBuffer<CountedValue<TCount>> upload = device.CreateShaderDataBuffer<CountedValue<TCount>>(artifact, 1, MemoryPreference.Upload);
        var value = new CountedValue<TCount>(reference);
        IGraphicsBuffer<byte> raw = Storage(upload);
        MappedUpload borrow = raw.GetType().GetMethod("GetMappedUploadSpan", Hidden)!.CreateDelegate<MappedUpload>(raw);
        await upload.MapAsync();
        raw.CopyFrom(Enumerable.Repeat((byte)0xCD, 48).ToArray());
        try
        {
            Exception? error = Record.Exception(() => upload.CopyFrom([value]));
            Assert.Null(error);
            Assert.Equal(1, value.Calls[0]);
            AssertUnknownDependencies((IShaderDataSource)upload, enabled);
            byte[] bytes = borrow(0, 48).ToArray();
            Assert.All(bytes[..16], item => Assert.Equal((byte)0, item));
            Assert.Equal(bytes[16..32], bytes[32..48]);
            Assert.All(bytes[28..32], item => Assert.Equal((byte)0, item));
            Assert.All(bytes[44..48], item => Assert.Equal((byte)0, item));
        }
        finally
        {
            upload.Unmap();
        }
    }

    private static IShaderReferenceMetadata? QueryDependencies(IShaderDataSource source, bool enabled)
    {
        Assert.Equal(enabled, source.TryGetDependencies(out IShaderReferenceMetadata? dependencies));
        if (enabled)
        {
            Assert.NotNull(dependencies);
        }
        else
        {
            Assert.Null(dependencies);
        }

        return dependencies;
    }

    private static void AssertUnknownDependencies(IShaderDataSource source, bool enabled)
    {
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => source.TryGetDependencies(out _));
        }
        else
        {
            QueryDependencies(source, enabled);
        }
    }

    private static int Count<T>()
        where T : struct, IShaderArguments => T.ReferenceCount;

    private static void CheckCount<T>(T value, int expected)
        where T : struct, IShaderArguments
    {
        Assert.Equal(expected, T.ReferenceCount);
        CountingWriter writer = default;
        value.Write(ref writer);
        Assert.Equal(expected, writer.References);
        Assert.Equal(T.TransferSize, writer.Bytes);
    }

    private static string ReadSource(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Lumyte.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, path));
    }

    private static int Occurrences(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;

    private struct CountingWriter : IShaderValueWriter
    {
        internal int Bytes;
        internal int References;

        public void WriteValue(scoped ReadOnlySpan<byte> value) => Bytes += value.Length;

        public void WriteReference<T>(IGpuRef<T>? reference)
        {
            References++;
            Bytes += 16;
        }
    }

    private readonly struct DeclaredOne : ICount
    {
        public static int Count => 1;
    }

    private readonly struct DeclaredThree : ICount
    {
        public static int Count => 3;
    }

    private readonly struct DeclaredNegative : ICount
    {
        public static int Count => -1;
    }

    private readonly struct DeclaredThrowing : ICount
    {
        public static int Count => throw new ApplicationException("Raw writers must never read semantic counts.");
    }

    private readonly struct DeclaredHuge : ICount
    {
        public static int Count => int.MaxValue;
    }

    private readonly struct TwoReferences(IGpuRef<uint> reference) : IShaderArguments
    {
        public static string ShaderTypeName => "TwoReferences";

        public static int TransferSize => 32;

        public static int ReferenceCount => 1;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            writer.WriteReference(reference);
            writer.WriteReference(reference);
        }
    }

    private struct CountedValue<TCount>(IGpuRef<uint> reference) : IShaderArguments
        where TCount : struct, ICount
    {
        public static string ShaderTypeName => "HotPathValue";

        public static int TransferSize => 48;

        public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Padding", 0, 16, typeof(System.Numerics.Vector4)), new("First", 16, 16, typeof(uint), true), new("Second", 32, 16, typeof(uint), true)];

        public static int ReferenceCount => TCount.Count;

        internal int[] Calls { get; } = [0];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Calls[0]++;
            writer.WriteValue(new byte[16]);
            writer.WriteReference(reference);
            writer.WriteReference(reference);
        }
    }

    private struct HotPathValue(IGpuRef<uint> reference, Action? callback = null) : IShaderArguments
    {
        public static string ShaderTypeName => "HotPathValue";

        public static int TransferSize => 48;

        public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Padding", 0, 16, typeof(System.Numerics.Vector4)), new("First", 16, 16, typeof(uint), true), new("Second", 32, 16, typeof(uint), true)];

        public static int ReferenceCount => 2;

        internal int[] Calls { get; } = [0];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Calls[0]++;
            writer.WriteValue(new byte[16]);
            callback?.Invoke();
            writer.WriteReference(reference);
            writer.WriteReference(reference);
        }
    }

    private sealed class Reference : IShaderReference
    {
        public object Table { get; } = new();

        public object Resource { get; } = new();

        public ShaderBufferAccess BufferAccess => default;

        public uint Slot => 0;

        public ulong Count => 1;

        public ulong OffsetInBytes => 0;

        public ulong SizeInBytes => 4;

        public ulong RegistrationOffsetInBytes => 0;

        public ulong RegistrationSizeInBytes => 4;
    }
}
