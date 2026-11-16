using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Prevents global EventSource subscriptions from overlapping other UI tests.</summary>
[CollectionDefinition("UI profiling", DisableParallelization = true)]
public sealed class ProfilingCollection
{
}
