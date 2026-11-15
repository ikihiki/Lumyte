using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationGpuRef<T, TNative> : IGpuRef<T>, IValidationReference
{
    private readonly IGpuRef<TNative> _raw;
    private readonly IShaderReference _metadata;
    private readonly ValidationArgumentTable _table;
    private readonly object _resource;
    private readonly object _revision;
    private readonly int _kind;

    internal ValidationGpuRef(ValidationArgumentTable table, IGpuRef<TNative> raw, object resource, object revision, int kind)
    {
        _table = table;
        _raw = raw;
        _resource = resource;
        _revision = revision;
        _kind = kind;
        _metadata = raw as IShaderReference ?? throw new InvalidOperationException("Backend reference lacks metadata.");
    }

    public ValidationDevice Device => _table.Device;

    public object Table => _table;

    public object Resource => _resource;

    public ShaderBufferAccess BufferAccess => _metadata.BufferAccess;

    public uint Slot => _metadata.Slot;

    public ulong OffsetInBytes => _metadata.OffsetInBytes;

    public ulong SizeInBytes => _metadata.SizeInBytes;

    public ulong RegistrationOffsetInBytes => _metadata.RegistrationOffsetInBytes;

    public ulong RegistrationSizeInBytes => _metadata.RegistrationSizeInBytes;

    public ulong Count => _raw.Count;

    public void ValidateRegistration()
    {
        _table.ValidateAlive();
        Device.UnwrapObject(_resource);
        _table.ValidateRevision(_kind, Slot, _revision);
    }

    public IGpuRef<T> GetElement(ulong index)
    {
        ValidateRegistration();
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (Count == 1)
        {
            return this;
        }

        return new ValidationGpuRef<T, TNative>(_table, _raw.GetElement(index), _resource, _revision, _kind);
    }

    public void WriteNative<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        ValidateRegistration();
        writer.WriteReference(_raw);
    }
}
