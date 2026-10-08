using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises buffer allocation and explicit CPU access through the common API.</summary>
public static class BufferExercise
{
    /// <summary>Checks typed layouts, mapping, CPU copies, range failures and resource lifetime.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>A report after all checks pass.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        BufferLayout<uint> words = device.GetBufferLayout<uint>();
        Require(words.ElementSizeInBytes == 4 && words.GetSizeInBytes(3) == 12, "UInt32 layout is incorrect.");
        BufferLayout<Triple> triples = device.GetBufferLayout<Triple>();
        Require(triples.ElementStrideInBytes == 12 && triples.GetSizeInBytes(3) == 36, "Struct layout was padded.");
        BufferLayout<byte> bytes = device.GetBufferLayout<byte>();
        Require(bytes.CopyCountAlignment == bytes.CopySizeAlignmentInBytes, "Byte copy alignment is incorrect.");
        Require((words.CopyCountAlignment * words.ElementStrideInBytes) % words.CopySizeAlignmentInBytes == 0, "Element copy alignment is incorrect.");
        Expect<OverflowException>(() => words.GetSizeInBytes(ulong.MaxValue));
        Expect<ArgumentException>(() => device.CreateBuffer(new BufferDesc<uint> { Count = 0, Usage = BufferUsage.CopySource }));

        using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint> { Count = 16, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        Require(upload.Count == 16 && upload.SizeInBytes == 64, "Allocation size was changed.");
        uint[] source = [11, 22, 33, 44];
        Expect<InvalidOperationException>(() => upload.CopyFrom(source));
        Expect<ArgumentOutOfRangeException>(() => upload.Slice(16, 1));
        Expect<ArgumentOutOfRangeException>(() => upload.Slice(ulong.MaxValue, 1));
        Expect<ArgumentException>(() => default(BufferSlice<uint>).CopyFrom(source));
        if (device is IDisposable owner)
        {
            Expect<InvalidOperationException>(owner.Dispose);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await ExpectAsync<OperationCanceledException>(() => upload.MapAsync(cancellation.Token));
        Require(!upload.IsMapped, "A canceled request retained a mapping.");
        await upload.MapAsync();
        Require(upload.IsMapped, "Mapping did not complete.");
        upload.CopyFrom(source);
        BufferSlice<uint> slice = upload.Slice(3, 2);
        Require(slice.OffsetInBytes == 12 && slice.SizeInBytes == 8, "Slice byte range is incorrect.");
        slice.CopyFrom(source.AsSpan(0, 2));
        Expect<ArgumentException>(() => slice.CopyFrom(source));
        Expect<InvalidOperationException>(() => upload.CopyTo(source));
        await ExpectAsync<InvalidOperationException>(() => upload.MapAsync());
        upload.Unmap();
        Expect<InvalidOperationException>(() => upload.CopyFrom(source));
        await upload.MapAsync();
        upload.CopyFrom([]);
        upload.Unmap();

        using IGraphicsBuffer<byte> byteUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await byteUpload.MapAsync();
        byteUpload.Slice(1, 3).CopyFrom([1, 2, 3]);
        byteUpload.Unmap();

        using IGraphicsBuffer<Triple> storage = device.CreateBuffer(new BufferDesc<Triple> { Count = 3, Usage = BufferUsage.ShaderRead });
        Require(storage.SizeInBytes == 36 && storage.Count == 3, "Raw struct allocation was rounded.");
        await ExpectAsync<InvalidOperationException>(() => storage.MapAsync());

        using IGraphicsBuffer<uint> readback = device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        uint[] destination = Enumerable.Repeat(0xAABBCCDDU, 10).ToArray();
        Expect<InvalidOperationException>(() => readback.CopyTo(destination));
        await readback.MapAsync();
        readback.Slice(1, 3).CopyTo(destination);
        Require(destination.Skip(3).All(value => value == 0xAABBCCDDU), "Readback changed bytes outside its destination range.");
        Expect<ArgumentException>(() => readback.CopyTo(destination.AsSpan(0, 1)));
        Expect<InvalidOperationException>(() => readback.CopyFrom(source));
        readback.Unmap();

        BufferSlice<uint> stale = upload.Slice(0, 1);
        upload.Dispose();
        upload.Dispose();
        Expect<ObjectDisposedException>(() => stale.CopyFrom([1]));
        Expect<ObjectDisposedException>(() => upload.Slice(0, 1));
        return "Buffer checks passed: typed sizes, explicit mapping, CPU copies, ranges and lifetime.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static async Task ExpectAsync<TException>(Func<ValueTask> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private readonly record struct Triple(uint X, uint Y, uint Z);
}
