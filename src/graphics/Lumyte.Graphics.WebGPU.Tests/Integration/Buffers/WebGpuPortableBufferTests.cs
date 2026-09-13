using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableBufferTests
{
    [Fact]
    public async Task WriteMappingPreservesTheSelectedNonzeroRangeAcrossUnmap()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(64, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        byte[] expected = Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray();
        try
        {
            using (P.GpuMappedBufferRange first = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 8, 16))
            {
                Assert.Equal(16, first.Memory.Length);
                expected.CopyTo(first.Memory.Span);
            }

            using P.GpuMappedBufferRange second = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);

            Assert.Equal(new byte[8], second.ReadOnlyMemory.Span[..8].ToArray());
            Assert.Equal(expected, second.ReadOnlyMemory.Span.Slice(8, 16).ToArray());
            Assert.Equal(new byte[8], second.ReadOnlyMemory.Span.Slice(24, 8).ToArray());
        }
        finally { backend.DestroyBuffer(buffer); }
    }

    [Fact]
    public async Task ReadMappingExposesZeroInitializedBytesWithoutWritableAccess()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination));
        try
        {
            using P.GpuMappedBufferRange mapped = await backend.MapBufferAsync(buffer, P.GpuMapMode.Read, 8, 16);

            Assert.Equal(new byte[16], mapped.ReadOnlyMemory.ToArray());
            Assert.Throws<InvalidOperationException>(() => mapped.Memory);
        }
        finally { backend.DestroyBuffer(buffer); }
    }

    [Fact]
    public async Task UnmapInvalidatesSavedManagedMemoryAndAllowsAnotherMap()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        try
        {
            P.GpuMappedBufferRange mapped = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
            Memory<byte> writable = mapped.Memory;
            ReadOnlyMemory<byte> readable = mapped.ReadOnlyMemory;

            mapped.Dispose();

            Assert.Throws<ObjectDisposedException>(() => { _ = writable.Span.Length; });
            Assert.Throws<ObjectDisposedException>(() => { _ = readable.Span.Length; });
            Assert.Throws<ObjectDisposedException>(() => writable.Pin());
            Assert.Throws<ObjectDisposedException>(() => readable.Pin());
            mapped.Dispose();
            using P.GpuMappedBufferRange next = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
            Assert.Equal(32, next.Memory.Length);
        }
        finally { backend.DestroyBuffer(buffer); }
    }

    [Fact]
    public async Task InvalidNativeUsageRetainsItsOwnCreationDiagnostic()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle invalid = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.Storage));
        P.GpuBufferHandle valid = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination));
        try
        {
            IReadOnlyList<P.GpuDiagnostic> invalidDiagnostics = await backend.GetCreationDiagnostics(invalid);
            IReadOnlyList<P.GpuDiagnostic> validDiagnostics = await backend.GetCreationDiagnostics(valid);

            Assert.Contains(invalidDiagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation && !string.IsNullOrWhiteSpace(item.Message));
            Assert.Empty(validDiagnostics);
        }
        finally
        {
            backend.DestroyBuffer(valid);
            backend.DestroyBuffer(invalid);
        }
    }

    [Fact]
    public async Task InvalidMapReportsRuntimeFailureAndLeavesBufferAvailable()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        try
        {
            P.GpuOperationException failure = await Assert.ThrowsAsync<P.GpuOperationException>(async () =>
            {
                using P.GpuMappedBufferRange unused = await backend.MapBufferAsync(buffer, P.GpuMapMode.Read, 0, 32);
            });

            Assert.NotEmpty(failure.Diagnostics);
            using P.GpuMappedBufferRange mapped = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
            Assert.Equal(32, mapped.Memory.Length);
        }
        finally { backend.DestroyBuffer(buffer); }
    }

    [Fact]
    public async Task ParallelOperationsKeepCreationDiagnosticsOnTheirOwnResources()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            bool invalid = (index & 1) == 0;
            P.GpuBufferHandle buffer = backend.CreateBuffer(new(32,
                P.GpuBufferUsage.MapRead | (invalid ? P.GpuBufferUsage.Storage : P.GpuBufferUsage.CopyDestination)));
            try
            {
                IReadOnlyList<P.GpuDiagnostic> diagnostics = await backend.GetCreationDiagnostics(buffer);
                if (invalid)
                {
                    Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation);
                }
                else
                {
                    Assert.Empty(diagnostics);
                    using P.GpuMappedBufferRange mapped = await backend.MapBufferAsync(buffer, P.GpuMapMode.Read, 0, 32);
                    Assert.Equal(new byte[32], mapped.ReadOnlyMemory.ToArray());
                }
            }
            finally { backend.DestroyBuffer(buffer); }
        })));
    }

    [Fact]
    public async Task RejectedSecondMappingDoesNotUnmapTheFirstLease()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        try
        {
            using (P.GpuMappedBufferRange first = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32))
            {
                first.Memory.Span[8] = 17;

                P.GpuOperationException failure = await Assert.ThrowsAsync<P.GpuOperationException>(async () =>
                {
                    using P.GpuMappedBufferRange second = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
                });

                Assert.NotEmpty(failure.Diagnostics);
                Assert.Equal((byte)17, first.ReadOnlyMemory.Span[8]);
                first.Memory.Span[8] = 42;
            }

            using P.GpuMappedBufferRange next = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
            Assert.Equal((byte)42, next.ReadOnlyMemory.Span[8]);
        }
        finally { backend.DestroyBuffer(buffer); }
    }

    [Fact]
    public async Task MappingRejectsAnExternalBackendHandle()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            using P.GpuMappedBufferRange unused = await backend.MapBufferAsync(new OtherBuffer(), P.GpuMapMode.Read, 0, 32);
        });
    }

    [Fact]
    public async Task AForeignDeviceCannotDestroyTheOwningDevicesBuffer()
    {
        using WebGpuBackend owner = await WebGpuBackend.CreateAsync();
        using WebGpuBackend other = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = owner.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        try
        {
            Assert.Throws<ArgumentException>(() => other.DestroyBuffer(buffer));

            using P.GpuMappedBufferRange mapped = await owner.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
            Assert.Equal(32, mapped.Memory.Length);
        }
        finally { owner.DestroyBuffer(buffer); }
    }

    [Fact]
    public async Task DestroyedBufferCannotBeMappedAgain()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        backend.DestroyBuffer(buffer);

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            using P.GpuMappedBufferRange unused = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 32);
        });
    }

    [Fact]
    public async Task MappingRejectsLengthThatCannotBeRepresentedByManagedMemory()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource));
        try
        {
            ArgumentOutOfRangeException failure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                using P.GpuMappedBufferRange unused = await backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, (ulong)int.MaxValue + 1);
            });

            Assert.Equal("length", failure.ParamName);
        }
        finally { backend.DestroyBuffer(buffer); }
    }

    private sealed class OtherBuffer : P.GpuBufferHandle;
}
