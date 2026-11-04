using Lumyte.Input.Abstractions;

namespace Lumyte.Input.Processing;

/// <summary>Preserves cross-device acquisition order and metadata while applying independent processor scopes.</summary>
public sealed class OrderedCorrectingInputSource : IOrderedInputSource, IInputDeviceRegistry
{
    private readonly OrderedPipeline _pipeline;

    /// <summary>Initializes a new instance of the <see cref="OrderedCorrectingInputSource"/> class.</summary>
    /// <param name="inner">The borrowed ordered source.</param>
    /// <param name="factory">Creates independent steps per device/window.</param>
    /// <param name="getTime">The monotonic processing clock.</param>
    public OrderedCorrectingInputSource(IOrderedInputSource inner, Func<InputDeviceDescriptor, InputWindowId?, IEnumerable<IDeviceDataProcessor>> factory, Func<TimeSpan> getTime)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _pipeline = new(inner, (descriptor, window) => factory(descriptor, window)?.Cast<object>().ToArray() ?? throw new ArgumentException("A processor factory returned null."), getTime, virtualizing: false);
    }

    /// <inheritdoc/>
    public Guid StreamId => _pipeline.StreamId;

    /// <summary>Prepares all live scope replacements before scheduling the boundary change.</summary>
    /// <param name="factory">The replacement factory.</param>
    public void SetProcessorFactory(Func<InputDeviceDescriptor, InputWindowId?, IEnumerable<IDeviceDataProcessor>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _pipeline.SetFactory((descriptor, window) => factory(descriptor, window)?.Cast<object>().ToArray() ?? throw new ArgumentException("A processor factory returned null."));
    }

    /// <inheritdoc/>
    public void Initialize(IInputDeviceRegistry registry) => _pipeline.Initialize(registry);

    /// <inheritdoc/>
    public void Update() => _pipeline.Update();

    /// <inheritdoc/>
    public InputEventBatch? PeekEvents() => _pipeline.PeekEvents();

    /// <inheritdoc/>
    public void CommitEvents(ulong batchId) => _pipeline.CommitEvents(batchId);

    /// <inheritdoc/>
    public void AbortEvents() => _pipeline.AbortEvents();

    /// <inheritdoc/>
    public void Shutdown() => _pipeline.Shutdown();

    /// <summary>Detaches this wrapper without disposing its borrowed source or transferred devices.</summary>
    public void Dispose() => _pipeline.Dispose();

    /// <inheritdoc/>
    public InputDeviceId RegisterDevice(IInputDevice device) => _pipeline.RegisterDevice(device);

    /// <inheritdoc/>
    public void UnregisterDevice(InputDeviceId device) => _pipeline.UnregisterDevice(device);
}
