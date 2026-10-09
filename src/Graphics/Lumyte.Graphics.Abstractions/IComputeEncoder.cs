namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides a non-owning recording scope.</summary>
public interface IComputeEncoder
{
    /// <summary>Selects the device's logical registration table for subsequent execution.</summary>
    /// <param name="table">The live table from this device.</param>
    void SetArgumentTable(IArgumentTable table);

    /// <summary>Snapshots application root values using their generated codec.</summary>
    /// <typeparam name="T">The generated argument structure.</typeparam>
    /// <param name="arguments">The root values for the selected program.</param>
    void SetArguments<T>(in T arguments)
        where T : struct;

    /// <summary>Selects a same-device compute program.</summary>
    /// <param name="pipeline">The compute program.</param>
    void SetPipeline(IGraphicsComputePipeline pipeline);

    /// <summary>Records workgroups without choosing shader workgroup size.</summary>
    /// <param name="groupCountX">The positive X group count.</param>
    /// <param name="groupCountY">The positive Y group count.</param>
    /// <param name="groupCountZ">The positive Z group count.</param>
    void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1);

    /// <summary>Ends this pass once without submitting or waiting.</summary>
    void End();
}
