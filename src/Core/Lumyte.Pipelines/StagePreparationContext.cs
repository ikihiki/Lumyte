using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Pipelines;

/// <summary>Bounds publication and access to one stage invocation.</summary>
public sealed class StagePreparationContext
{
    private readonly object _run;
    private readonly object _producer;
    private readonly object[] _inputList;
    private readonly long _maxBytes;
    private readonly RunValueOwners? _owners;
    private long _reserved;
    private bool _closed;
    private bool _outputValidated;
    private bool _published;

    internal StagePreparationContext(IStageCapabilities capabilities, object run, object producer, object[] inputs, long maxBytes, RunValueOwners? owners = null)
    {
        Capabilities = capabilities;
        _run = run;
        _producer = producer;
        _inputList = inputs;
        _maxBytes = maxBytes;
        _owners = owners;
        if (capabilities is IStageScopeBinding binding)
        {
            binding.Bind(this);
        }
    }

    /// <summary>Gets the explicitly supplied capabilities.</summary>
    public IStageCapabilities Capabilities { get; }

    /// <summary>Reads a CPU-readable lease admitted to this invocation.</summary>
    /// <typeparam name="TV">The value type.</typeparam>
    /// <param name="value">The borrowed input.</param>
    /// <returns>The immutable value.</returns>
    public TV Read<TV>(ValueLease<TV> value)
    {
        Validate(value);
        if (Capabilities is IStageCpuPolicy { CanReadCpu: false })
        {
            throw new InvalidOperationException("This execution domain cannot read CPU payloads.");
        }

        if (value.Value is INonCpuReadablePlacement)
        {
            throw new InvalidOperationException("Device placement is not CPU-readable.");
        }

        return value.Value;
    }

    /// <summary>Publishes an immutable CPU value in this invocation.</summary>
    /// <typeparam name="TV">The value type.</typeparam>
    /// <param name="immutableValue">The immutable payload.</param>
    /// <returns>The scoped output lease.</returns>
    public ValueLease<TV> Publish<TV>(TV immutableValue)
    {
        CheckOpen();
        if (Capabilities is IStageCpuPolicy { CanPublishCpu: false })
        {
            throw new InvalidOperationException("This execution domain cannot publish CPU payloads.");
        }

        if (immutableValue is INonCpuReadablePlacement)
        {
            throw new InvalidOperationException("CPU publication cannot manufacture a device placement.");
        }

        if (immutableValue is IHostPlacement host && (host.Extent is null || !ReferenceEquals(host.Invocation, _producer)))
        {
            throw new InvalidOperationException("Canonical host publication requires data copied by this invocation; use Forward for admitted inputs.");
        }

        EnsurePublication();
        Reserve(checked(Requirements.ValueLeaseBytes + Unsafe.SizeOf<TV>()));
        var output = new ValueLease<TV>(immutableValue, _run, _producer);
        _published = true;
        return output;
    }

    /// <summary>Publishes an immutable CPU output with a new independently owned cleanup lease.</summary>
    /// <typeparam name="TValue">The immutable output value type.</typeparam>
    /// <param name="immutableValue">The completed immutable output.</param>
    /// <param name="ownerLease">A new independent owner transferred only on successful publication.</param>
    /// <returns>The one invocation-owned output lease.</returns>
    public ValueLease<TValue> PublishOwned<TValue>(TValue immutableValue, IDisposable ownerLease)
    {
        ArgumentNullException.ThrowIfNull(ownerLease);
        RunValueOwners owners = _owners ?? throw new InvalidOperationException("The invocation has no runtime-owned cleanup table.");
        ValueLease<TValue> output = Publish(immutableValue);
        owners.Add(ownerLease);
        return output;
    }

    /// <summary>Creates an independent output use of an admitted input.</summary>
    /// <typeparam name="TV">The value type.</typeparam>
    /// <param name="value">The input lease.</param>
    /// <returns>The independent output lease.</returns>
    public ValueLease<TV> Forward<TV>(ValueLease<TV> value)
    {
        Validate(value);
        if (Capabilities is IStageCpuPolicy { CanPublishCpu: false })
        {
            throw new InvalidOperationException("This execution domain cannot forward CPU payloads.");
        }

        if (value.Value is INonCpuReadablePlacement || (value.Value is IHostPlacement host && host.Extent is null))
        {
            throw new InvalidOperationException("Forward requires an initialized CPU-readable input.");
        }

        EnsurePublication();
        Reserve(checked(Requirements.ValueLeaseBytes + Unsafe.SizeOf<TV>()));
        var output = new ValueLease<TV>(value.Value, _run, _producer);
        _published = true;
        return output;
    }

    /// <summary>Reads canonical host data.</summary>
    /// <typeparam name="TS">The canonical schema.</typeparam>
    /// <param name="value">The admitted host lease.</param>
    /// <returns>The immutable canonical data.</returns>
    public SchemaData<TS> ReadHost<TS>(ValueLease<Host<TS>> value)
        where TS : ISchema
    {
        SchemaData<TS> data = Read(value).Data ?? throw new InvalidOperationException("Uninitialized host placement.");
        if (!data.Descriptor.Matches(TS.Descriptor))
        {
            throw new InvalidOperationException("Canonical host schema changed after publication.");
        }

        return data;
    }

    /// <summary>Reserves the canonical bytes before making an immutable copy.</summary>
    /// <typeparam name="TS">The canonical schema.</typeparam>
    /// <typeparam name="T">The canonical element type.</typeparam>
    /// <param name="values">The source elements.</param>
    /// <param name="extent">The matching immutable extent.</param>
    /// <returns>Invocation-owned immutable data.</returns>
    public SchemaData<TS> CopyHostData<TS, T>(ReadOnlySpan<T> values, SchemaExtent extent)
        where TS : ISchema
        where T : unmanaged
    {
        CheckOpen();
        if (Capabilities is IStageCpuPolicy { CanPublishCpu: false })
        {
            throw new InvalidOperationException("This execution domain cannot allocate canonical CPU data.");
        }

        ArgumentNullException.ThrowIfNull(extent);
        SchemaDescriptor descriptor = TS.Descriptor;
        if (descriptor.ElementType != typeof(T) || descriptor.ElementStride != Unsafe.SizeOf<T>() || extent.ElementCount != values.Length)
        {
            throw new ArgumentException("Canonical element layout or extent mismatch.", nameof(values));
        }

        long bytes = checked(extent.ElementCount * descriptor.ElementStride);
        Reserve(checked(bytes + Requirements.SchemaDataBytes));
        byte[] copy = MemoryMarshal.AsBytes(values).ToArray();
        return new SchemaData<TS>(copy, extent, _producer, new CanonicalStorage<T>(copy), descriptor);
    }

    /// <summary>Publishes canonical data created in this invocation.</summary>
    /// <typeparam name="TS">The canonical schema.</typeparam>
    /// <param name="value">The immutable invocation-owned data.</param>
    /// <returns>The host output lease.</returns>
    public ValueLease<Host<TS>> PublishHost<TS>(SchemaData<TS> value)
        where TS : ISchema
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Invocation, _producer))
        {
            throw new ArgumentException("Data belongs to another invocation.", nameof(value));
        }

        return Publish(new Host<TS>(value));
    }

    internal void Close()
    {
        _closed = true;
        if (Capabilities is IStageScopeBinding binding)
        {
            binding.Complete(_outputValidated);
        }
    }

    internal void ValidateOutput<TV>(ValueLease<TV> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Run, _run) || !ReferenceEquals(value.Producer, _producer))
        {
            throw new InvalidOperationException("Output belongs to another invocation.");
        }

        _outputValidated = true;
    }

    private void Validate<TV>(ValueLease<TV> value)
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(value);
        if (!ReferenceEquals(value.Run, _run) || !ContainsInput(value))
        {
            throw new InvalidOperationException("Lease is not an input of this invocation.");
        }
    }

    private bool ContainsInput(object value)
    {
        foreach (object input in _inputList)
        {
            if (ReferenceEquals(input, value))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsurePublication()
    {
        CheckOpen();
        if (_published)
        {
            throw new InvalidOperationException("One stage invocation publishes exactly one typed output lease.");
        }
    }

    private void CheckOpen() => ObjectDisposedException.ThrowIf(_closed, this);

    private void Reserve(long bytes)
    {
        long next = checked(_reserved + bytes);
        if (next > _maxBytes)
        {
            throw new InvalidOperationException("Stage host budget exceeded before allocation.");
        }

        _reserved = next;
    }
}
