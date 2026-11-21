using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Provides validated native bindings for one admitted device value.</summary>
/// <typeparam name="TSchema">The canonical schema.</typeparam>
public sealed class DeviceBindings<TSchema>
    where TSchema : ISchema
{
    private readonly DeviceValue _value;
    private readonly Action _validate;
    private readonly BarrierScope _scope;

    internal DeviceBindings(DeviceValue value, Action validate, BarrierScope scope)
    {
        _value = value;
        _validate = validate;
        _scope = scope;
    }

    /// <summary>Gets the immutable canonical extent and source identity.</summary>
    public SchemaExtent Extent
    {
        get
        {
            _validate();
            return _value.Extent;
        }
    }

    /// <summary>Gets a source view at its captured resource index.</summary>
    /// <param name="bindingIndex">The exact captured resource index.</param>
    /// <returns>The borrowed actual native texture view.</returns>
    public IGraphicsTextureView GetTexture(int bindingIndex)
    {
        _validate();
        return _value.GetTexture(bindingIndex, _scope);
    }

    /// <summary>Gets the canonical typed buffer at binding zero.</summary>
    /// <typeparam name="TElement">The exact canonical element type.</typeparam>
    /// <param name="bindingIndex">The declared binding index.</param>
    /// <returns>The borrowed native storage.</returns>
    public BufferSlice<TElement> GetBuffer<TElement>(int bindingIndex)
        where TElement : unmanaged
    {
        _validate();
        if (bindingIndex != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex));
        }

        return _value.GetBuffer<TElement>();
    }
}
