using System.Runtime.CompilerServices;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Planning;

/// <summary>Creates opaque typed values from declarations owned by the same common frame.</summary>
public static class PlanDeviceValues
{
    /// <summary>Checks canonical element storage before constructing an opaque buffer value.</summary>
    /// <typeparam name = "TSchema">The canonical schema.</typeparam>
    /// <typeparam name = "T">The unmanaged buffer element.</typeparam>
    /// <param name = "plan">The common declaration builder.</param>
    /// <param name = "buffer">The immutable buffer version.</param>
    /// <param name = "extent">The immutable logical extent.</param>
    /// <returns>A declaration that CPU stage callbacks cannot read.</returns>
    public static Device<TSchema> BufferValue<TSchema, T>(this GraphicsPlanBuilder plan, PlanBuffer<T> buffer, SchemaExtent extent)
        where TSchema : ISchema
        where T : unmanaged
    {
        plan.Mutable();
        plan.Validate(buffer, buffer.Range);
        ArgumentNullException.ThrowIfNull(extent);
        if (TSchema.Descriptor.ElementType != typeof(T) || TSchema.Descriptor.ElementStride != Unsafe.SizeOf<T>() || checked((ulong)extent.ElementCount) != buffer.Range.Count)
        {
            throw new ArgumentException("Canonical buffer schema or extent mismatch.");
        }

        return new(buffer, extent);
    }

    /// <summary>Returns the declaration carried by an externally retained typed result; this never maps native contents.</summary>
    /// <typeparam name = "TSchema">The canonical schema.</typeparam>
    /// <param name = "plan">The same declaration owner.</param>
    /// <param name = "value">The opaque declaration.</param>
    /// <returns>The resource version, usable by further declarations in this frame.</returns>
    public static PlanResource Resource<TSchema>(this GraphicsPlanBuilder plan, Device<TSchema> value)
        where TSchema : ISchema
    {
        plan.Mutable();
        plan.Validate(value.Resource, value.Resource.Range);
        return value.Resource;
    }

    /// <summary>Constructs an opaque image declaration; schema semantics remain explicit author data, not inferred from native bytes.</summary>
    /// <typeparam name = "TSchema">The canonical image schema.</typeparam>
    /// <param name = "plan">The common frame owner.</param>
    /// <param name = "texture">The immutable texture version.</param>
    /// <param name = "extent">The finite semantic extent.</param>
    /// <returns>The typed non-CPU-readable image declaration.</returns>
    public static Device<TSchema> TextureValue<TSchema>(this GraphicsPlanBuilder plan, PlanTexture texture, SchemaExtent extent)
        where TSchema : ISchema
    {
        plan.Mutable();
        plan.Validate(texture, texture.Range);
        ArgumentNullException.ThrowIfNull(extent);
        return new(texture, extent);
    }

    /// <summary>Creates an opaque shader-data declaration using its static wire stride, not managed reference layout.</summary>
    /// <typeparam name="TSchema">The declared device schema.</typeparam>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="plan">The common frame owner.</param>
    /// <param name="data">The immutable shader-data version.</param>
    /// <param name="extent">The finite element extent.</param>
    /// <returns>The non-CPU-readable typed declaration.</returns>
    public static Device<TSchema> ShaderDataValue<TSchema, T>(this GraphicsPlanBuilder plan, PlanShaderData<T> data, SchemaExtent extent)
        where TSchema : Lumyte.Pipelines.ISchema
        where T : struct, Lumyte.Graphics.Abstractions.IShaderArguments
    {
        plan.Mutable();
        plan.Validate(data, data.Range);
        ArgumentNullException.ThrowIfNull(extent);
        if (TSchema.Descriptor.ElementType != typeof(T) || TSchema.Descriptor.ElementStride != T.TransferSize || checked((ulong)extent.ElementCount) != data.Range.Count)
        {
            throw new ArgumentException("Shader-data schema or finite extent differs from its static wire ABI.");
        }

        return new(data, extent);
    }
}
