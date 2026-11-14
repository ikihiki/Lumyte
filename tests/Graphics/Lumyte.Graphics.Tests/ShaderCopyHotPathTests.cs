using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
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

    private delegate ShaderRootArguments VulkanRoot<T>(VulkanDevice owner, in T value, Span<byte> bytes, ShaderDataLayout? diagnostic, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
        where T : struct, IShaderArguments;

    private delegate IDisposable WgpuRoot<T>(WgpuDevice owner, in T value, ShaderDataLayout? diagnostic, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
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

    /// <summary>Backend metadata owns an exact array and is itself the immutable indexed view.</summary>
    /// <param name="backend">The backend under test.</param>
    [Theory]
    [InlineData("Vulkan")]
    [InlineData("Wgpu")]
    public void MetadataOwnsExactPhysicalSlotsAndSharesEmpty(string backend)
    {
        Type type = Assembly.Load("Lumyte.Graphics." + backend).GetType($"Lumyte.Graphics.{backend}.{backend}ReferenceMetadata", true)!;
        MethodInfo take = type.GetMethod("TakeOwnership", BindingFlags.Static | BindingFlags.NonPublic)!;
        IShaderReference reference = new Reference();
        IShaderReference[] slots = [reference, reference];
        var metadata = (IShaderReferenceMetadata)take.Invoke(null, [slots])!;
        Assert.Same(metadata, metadata.References);
        Assert.Equal(2, metadata.References.Count);
        Assert.Same(reference, metadata.References[0]);
        Assert.Same(reference, metadata.References[1]);
        Assert.Equal(slots, metadata.References.ToArray());
        Assert.False(metadata.References is IShaderReference[]);
        Assert.False(metadata.References is IList<IShaderReference>);
        Assert.False(metadata.References is IList);
        FieldInfo field = Assert.Single(type.GetFields(Hidden));
        Assert.Equal(typeof(IShaderReference[]), field.FieldType);
        Assert.Same(slots, field.GetValue(metadata));
        Assert.True(field.IsInitOnly);
        Assert.False(field.IsPublic);
        var first = (IShaderReferenceMetadata)take.Invoke(null, [Array.Empty<IShaderReference>()])!;
        var second = (IShaderReferenceMetadata)take.Invoke(null, [new IShaderReference[0]])!;
        Assert.Same(first, second);
        Assert.Same(first, first.References);
        Assert.Empty(first.References);
    }

    /// <summary>Impossible declared counts fail in the static guard, before any writer allocation.</summary>
    /// <param name="backend">The backend under test.</param>
    [Theory]
    [InlineData("Wgpu")]
    [InlineData("Vulkan")]
    public void InvalidCountsRejectBeforeAllocation(string backend)
    {
        Type writer = Assembly.Load("Lumyte.Graphics." + backend).GetType($"Lumyte.Graphics.{backend}.{backend}ShaderValueWriter", true)!;
        Action<int, int> validate = writer.GetMethod("ValidateExpectedCount", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Action<int, int>>();
        foreach (int invalid in new[] { -1, 4, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => validate(48, invalid));
        }

        validate(48, 0);
        validate(48, 3);
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

    /// <summary>All four backend sources borrow once outside the loop and conditionally lease.</summary>
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
        Assert.Contains("Validation?.BeginHostWrite(", copy);
        string writer = ReadSource($"src/Graphics/Lumyte.Graphics.{backend}/{backend}ShaderValueWriter.cs");
        Assert.DoesNotContain("CopyBytesFrom(", writer);
        Assert.Contains(backend == "Browser" ? "BrowserInterop.CopyMappedUploadRange(" : backend == "Wgpu" ? "value.CopyTo(_upload.Slice(" : "value.CopyTo(_destination.Slice(", writer);
        Assert.Contains("_referenceCount >= _expectedCount", writer);
        Assert.Contains("_referenceCount != _expectedCount", writer);
        string metadata = ReadSource($"src/Graphics/Lumyte.Graphics.{backend}/{backend}ReferenceMetadata.cs");
        Assert.Contains("References => this", metadata);
        Assert.Contains("private readonly IShaderReference[] _references", metadata);
        Assert.DoesNotContain("AsReadOnly", metadata);
        int guard = writer.IndexOf("ValidateExpectedCount(size, expectedCount)", StringComparison.Ordinal);
        int allocate = writer.IndexOf("new IShaderReference[expectedCount]", StringComparison.Ordinal);
        Assert.True(guard >= 0 && allocate > guard);
        string unmanaged = transfer[transfer.IndexOf("CreateUnmanaged", StringComparison.Ordinal)..];
        unmanaged = unmanaged[..unmanaged.IndexOf("internal static void CopyFrom", StringComparison.Ordinal)];
        Assert.DoesNotContain("T.ReferenceCount", unmanaged);
    }

    /// <summary>Native compiled transfer entrypoints perform exactly one borrow and one optional lease.</summary>
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
        Assert.Single(instructions, item => item.Member?.Name == "BeginHostWrite");
        Assert.Single(instructions, item => item.Member?.Name == "get_ReferenceCount");
        Type writer = assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ShaderValueWriter", true)!;
        MethodInfo write = writer.GetMethod("WriteValue")!;
        Assert.DoesNotContain(ImmediateRootCommandTests.Instructions(write), item => item.Member?.Name == "CopyBytesFrom");
        Assert.DoesNotContain(ImmediateRootCommandTests.Instructions(writer.GetMethod("WriteReference")!), item => item.Member?.Name == "GetBufferDeviceAddress");
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
            using var device = WgpuDevice.Create();
            if (enabled)
            {
                device.EnableValidation();
            }

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
            using var device = VulkanDevice.Create();
            if (enabled)
            {
                device.EnableValidation();
            }

            await CheckNativeAsync(device, artifact, enabled);
        }
    }

    private static Task<ShaderArtifact> CompileAsync(ShaderTarget target) => new SlangShaderCompiler().CompileAsync(new() { Source = Source, Target = target, Stage = ShaderStage.Compute });

    private static async Task CheckNativeAsync(IGraphicDevice device, ShaderArtifact artifact, bool enabled)
    {
        Assert.Equal(enabled, device.GetType().GetProperty("Validation", BindingFlags.Instance | BindingFlags.Public)!.GetValue(device) != null);
        using IGraphicsBuffer<uint> numbers = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.ShaderRead });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 4 });
        IGpuRef<uint> reference = table.WriteBuffer(0, numbers.SliceElements(1, 4));
        IGpuRef<uint> replacement = table.WriteBuffer(1, numbers.SliceElements(6, 1));
        using IGraphicsShaderDataBuffer<HotPathValue> upload = device.CreateShaderDataBuffer<HotPathValue>(artifact, 5, MemoryPreference.Upload);
        IShaderDataSource data = Assert.IsAssignableFrom<IShaderDataSource>(upload);
        IGraphicsBuffer<byte> raw = Assert.IsAssignableFrom<IGraphicsBuffer<byte>>(upload.GetType().GetProperty("Storage", Hidden)!.GetValue(upload));
        CheckRoots(device, reference, enabled);
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
                Assert.Same(first, first!.References);
                Assert.Same(reference, Assert.Single(first.References));
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
            AssertUnknownDependencies(data, enabled);
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
                    () => raw.CopyBytesFrom(new byte[4], 48, 4),
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
                Assert.Null(device.GetType().GetProperty("Validation", BindingFlags.Instance | BindingFlags.Public)!.GetValue(device));
            }
        }
        finally
        {
            upload.Unmap();
        }

        await CheckMappedBoundsAsync(device);
        await CheckEmptyAsync(device, artifact, enabled);
        await CheckCountMismatchAsync<DeclaredOne>(device, artifact, reference, enabled);
        await CheckCountMismatchAsync<DeclaredThree>(device, artifact, reference, enabled);
        await CheckCountMismatchAsync<DeclaredNegative>(device, artifact, reference, enabled);
        await CheckCountMismatchAsync<DeclaredHuge>(device, artifact, reference, enabled);
    }

    private static void CheckRoots(IGraphicDevice device, IGpuRef<uint> reference, bool enabled)
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
        using (CreateRoot(device, in unpreparedValue, null, out ShaderRootArguments unprepared))
        {
            Assert.Equal(enabled, unprepared.References != null);
        }

        Assert.Equal(1, unpreparedValue.Calls[0]);
        CheckBadRoot(device, new CountedValue<DeclaredOne>(reference));
        CheckBadRoot(device, new CountedValue<DeclaredThree>(reference));
        CheckBadRoot(device, new CountedValue<DeclaredNegative>(reference));
        CheckBadRoot(device, new CountedValue<DeclaredHuge>(reference));
    }

    private static void CheckRoot<T>(IGraphicDevice device, T value, int expected)
        where T : struct, IShaderArguments
    {
        int prepared = 0;
        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata references)
        {
            prepared++;
            Assert.Equal(expected, references.References.Count);
            return null;
        }

        ShaderRootArguments arguments;
        using IDisposable? storage = CreateRoot(device, in value, Prepare, out arguments);
        Assert.Equal(1, prepared);
        Assert.Equal(expected, arguments.References!.References.Count);
        Assert.Same(arguments.References, arguments.References);
        Assert.Equal(T.TransferSize, arguments.WireSize);
        Assert.Equal(1, prepared);
    }

    private static void CheckBadRoot<TCount>(IGraphicDevice device, CountedValue<TCount> value)
        where TCount : struct, ICount
    {
        int prepared = 0;
        Action<IReadOnlyCollection<IShaderReference>>? Prepare(IShaderReferenceMetadata references)
        {
            prepared++;
            return null;
        }

        Exception? failure = Record.Exception(() =>
        {
            using IDisposable? root = CreateRoot(device, in value, Prepare, out _);
        });
        if (TCount.Count < 0 || TCount.Count > 3)
        {
            Assert.IsType<ArgumentOutOfRangeException>(failure);
            Assert.Equal(0, value.Calls[0]);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(1, value.Calls[0]);
        }

        Assert.Equal(0, prepared);
    }

    private static IDisposable? CreateRoot<T>(IGraphicDevice device, in T value, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare, out ShaderRootArguments arguments)
        where T : struct, IShaderArguments
    {
        string backend = device is WgpuDevice ? "Wgpu" : "Vulkan";
        Type transfer = device.GetType().Assembly.GetType($"Lumyte.Graphics.{backend}.{backend}ShaderTransfer", true)!;
        MethodInfo create = transfer.GetMethod("CreateRoot", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T));
        if (device is WgpuDevice wgpu)
        {
            WgpuRoot<T> write = create.CreateDelegate<WgpuRoot<T>>();
            IDisposable storage = write(wgpu, in value, null, prepare);
            arguments = (ShaderRootArguments)storage.GetType().GetProperty("Arguments", Hidden)!.GetValue(storage)!;
            return storage;
        }

        var adapter = new DynamicMethod(
            "CreateVulkanRootArguments",
            typeof(ShaderRootArguments),
            [typeof(VulkanDevice), typeof(T).MakeByRefType(), typeof(Span<byte>), typeof(ShaderDataLayout), typeof(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>)],
            typeof(ShaderCopyHotPathTests).Module,
            skipVisibility: true);
        ILGenerator il = adapter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Call, create);
        il.Emit(OpCodes.Ldfld, create.ReturnType.GetField("Item1")!);
        il.Emit(OpCodes.Ret);
        VulkanRoot<T> vulkanWrite = adapter.CreateDelegate<VulkanRoot<T>>();
        arguments = vulkanWrite((VulkanDevice)device, in value, new byte[T.TransferSize], null, prepare);
        return null;
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
            Assert.Same(empty, QueryDependencies(source, enabled));
        }
        finally
        {
            upload.Unmap();
        }
    }

    private static async Task CheckCountMismatchAsync<TCount>(IGraphicDevice device, ShaderArtifact artifact, IGpuRef<uint> reference, bool enabled)
        where TCount : struct, ICount
    {
        using IGraphicsShaderDataBuffer<CountedValue<TCount>> upload = device.CreateShaderDataBuffer<CountedValue<TCount>>(artifact, 1, MemoryPreference.Upload);
        var value = new CountedValue<TCount>(reference);
        var raw = (IGraphicsBuffer<byte>)upload.GetType().GetProperty("Storage", Hidden)!.GetValue(upload)!;
        MappedUpload borrow = raw.GetType().GetMethod("GetMappedUploadSpan", Hidden)!.CreateDelegate<MappedUpload>(raw);
        await upload.MapAsync();
        raw.CopyFrom(Enumerable.Repeat((byte)0xCD, 48).ToArray());
        try
        {
            Exception? error = Record.Exception(() => upload.CopyFrom([value]));
            if (TCount.Count < 0 || TCount.Count > 3)
            {
                Assert.IsType<ArgumentOutOfRangeException>(error);
                Assert.Equal(0, value.Calls[0]);
            }
            else
            {
                Assert.IsType<InvalidOperationException>(error);
                Assert.Equal(1, value.Calls[0]);
            }

            AssertUnknownDependencies((IShaderDataSource)upload, enabled);
            byte[] bytes = borrow(0, 48).ToArray();
            if (TCount.Count < 0 || TCount.Count > 3)
            {
                Assert.All(bytes, item => Assert.Equal((byte)0xCD, item));
            }
            else
            {
                Assert.All(bytes[..16], item => Assert.Equal((byte)0, item));
                if (TCount.Count == 1)
                {
                    // The undeclared second reference must fail before touching its physical slot.
                    Assert.All(bytes[32..], item => Assert.Equal((byte)0xCD, item));
                }
            }
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

    private readonly struct DeclaredHuge : ICount
    {
        public static int Count => int.MaxValue;
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
