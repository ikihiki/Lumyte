namespace Lumyte.Graphics.Abstractions;

/// <summary>Retains one exact program revision until its GPU users have finished.</summary>
/// <param name="resources">The borrowed program bundle retained by the callback.</param>
/// <param name="release">Returns the provider's lease once; must not wait for GPU use.</param>
/// <param name="validate">Optional runtime validation before returning the resources.</param>
public sealed class GraphicsProgramLease(GraphicsProgramResources resources, Action release, Action? validate = null) : IDisposable
{
    private readonly GraphicsProgramResources _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    private Action? _release = release ?? throw new ArgumentNullException(nameof(release));

    /// <summary>Gets the retained program; a returned lease rejects access.</summary>
    public GraphicsProgramResources Resources
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            validate?.Invoke();
            return _resources;
        }
    }

    /// <summary>Creates a lease owning a new shader pair and pipeline from externally loaded artifacts.</summary>
    /// <param name="device">The borrowed physical device.</param>
    /// <param name="vertexArtifact">The compatible vertex artifact.</param>
    /// <param name="fragmentArtifact">The compatible fragment artifact.</param>
    /// <returns>The native owner, released in pipeline, fragment, vertex order.</returns>
    public static GraphicsProgramLease Create(IGraphicDevice device, ShaderArtifact vertexArtifact, ShaderArtifact fragmentArtifact)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(vertexArtifact);
        ArgumentNullException.ThrowIfNull(fragmentArtifact);
        List<IDisposable> owned = [];
        try
        {
            IGraphicsShader vertex = device.CreateShader(vertexArtifact);
            owned.Add(vertex);
            IGraphicsShader fragment = device.CreateShader(fragmentArtifact);
            owned.Add(fragment);
            IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
            owned.Add(pipeline);
            return new(new(device, pipeline, fragmentArtifact), () => Release(owned));
        }
        catch (Exception error)
        {
            try
            {
                Release(owned);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(error, cleanup);
            }

            throw;
        }
    }

    /// <summary>Returns the provider's lease once, without submitting or waiting for GPU work.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();

    private static void Release(List<IDisposable> owned)
    {
        List<Exception>? errors = null;
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            try
            {
                owned[i].Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more program resources failed to dispose.", errors);
        }
    }
}
