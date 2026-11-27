using System.Diagnostics.CodeAnalysis;

using Lumyte.Graphics.Abstractions;

using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuShaderData<T> : CpuResource, IGraphicsShaderDataBuffer<T>, IShaderDataSource

    where T : struct, IShaderArguments
{
    internal CpuShaderData(CpuGraphicsDevice owner, ShaderArtifact artifact, ulong count, MemoryPreference memory)
            : base(owner)
    {
        Count = count;
        Memory = memory;
        Layout = ShaderDataLayout.Data<T>(artifact, ShaderTarget.SpirV);
        Bytes = new byte[checked((int)SizeInBytes)];
    }

    public ulong Count
    {
        get;
    }

    public MemoryPreference Memory
    {
        get;
    }

    public bool IsMapped
    {
        get;
        private set;
    }

    public IShaderDataLayout Layout
    {
        get;
    }

    public object ShaderHandle => this;

    public ulong SizeInBytes => checked(Count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => (ulong)T.TransferSize;

    internal byte[] Bytes
    {
        get;
    }

    public void ValidateAlive() => Check();

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        Check();
        dependencies = null;
        return false;
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        Check();
        cancellationToken.ThrowIfCancellationRequested();
        if (Memory != MemoryPreference.Upload || IsMapped)
        {
            throw new InvalidOperationException("Requires unmapped upload storage.");
        }

        IsMapped = true;
        return ValueTask.CompletedTask;
    }

    public void Unmap()
    {
        Check();
        if (!IsMapped)
        {
            throw new InvalidOperationException("Not mapped.");
        }

        IsMapped = false;
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        Check();
        return new(this, offset, count);
    }

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
    {
        Check();
        if (!IsMapped || Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Requires mapped upload storage.");
        }

        if (elementOffset > Count || (ulong)source.Length > Count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        for (int i = 0;
        i < source.Length;
        i++)
        {
            var writer = new CpuWriter(Owner, Bytes.AsSpan(checked((int)((elementOffset + (ulong)i) * ShaderElementStrideInBytes)), T.TransferSize));
            source[i].Write(ref writer);
            writer.Complete();
        }

        Owner.Uploads += checked(source.Length * T.TransferSize);
    }

    public void CopyFrom<TSource>(ReadOnlySpan<TSource> source, ulong elementOffset = 0)
        where TSource : struct, IShaderArguments
    {
        Check();
        ShaderTransfer.ValidateCompatibility<T, TSource>();
        if (!IsMapped || Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Requires mapped upload storage.");
        }

        if (elementOffset > Count || (ulong)source.Length > Count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        for (int i = 0;
        i < source.Length;
        i++)
        {
            var writer = new CpuWriter(Owner, Bytes.AsSpan(checked((int)((elementOffset + (ulong)i) * ShaderElementStrideInBytes)), T.TransferSize));
            source[i].Write(ref writer);
            writer.Complete();
        }

        Owner.Uploads += checked(source.Length * T.TransferSize);
    }
}
