namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides a non-owning recording scope.</summary>
public interface IComputeEncoder
{
    /// <summary>Selects the same-device table against which draw or dispatch references are validated.</summary>
    /// <param name="table">The live argument table containing all directly and transitively referenced resources.</param>
    void SetArgumentTable(IArgumentTable table);

    /// <summary>Configures reference preparation and detached execution admission for subsequently selected arguments.</summary>
    /// <param name="prepare">The metadata preparation callback, or null to disable argument validation.</param>
    void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare);

    /// <summary>Copies application root values directly through their value writer.</summary>
    /// <typeparam name="T">The generated argument structure.</typeparam>
    /// <param name="arguments">The root values for the selected program.</param>
    void SetArguments<T>(in T arguments)
        where T : IShaderArguments;

    /// <summary>Copies an ordinary unmanaged value directly without an interface or generated code.</summary>
    /// <typeparam name="T">The unmanaged argument type.</typeparam>
    /// <param name="arguments">The raw C# value, including padding.</param>
    /// <param name="rootParameter">The shader root parameter name.</param>
    /// <param name="shaderTypeName">The shader structure name, or the C# type name.</param>
    void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged;

    /// <summary>Selects a same-device compute program.</summary>
    /// <param name="pipeline">The compute program.</param>
    void SetPipeline(IGraphicsComputePipeline pipeline);

    /// <summary>Records workgroups without choosing shader workgroup size.</summary>
    /// <param name="groupCountX">The positive X group count.</param>
    /// <param name="groupCountY">The positive Y group count.</param>
    /// <param name="groupCountZ">The positive Z group count.</param>
    void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1);

    /// <summary>Records one GPU-sourced dispatch without CPU command-data inspection.</summary>
    /// <param name="arguments">Exactly one unmapped record with Indirect usage; counts must respect device limits.</param>
    void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments);

    /// <summary>Ends this pass once without submitting or waiting.</summary>
    void End();
}
