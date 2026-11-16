using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Prevents process-wide graph listeners from observing unrelated parallel tests.</summary>
[CollectionDefinition("Graph profiling events", DisableParallelization = true)]
public sealed class RenderGraphProfilingCollection
{
}
