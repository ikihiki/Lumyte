using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Process-wide EventSources, capture sessions and Console redirection are exclusive.</summary>
[CollectionDefinition("Application profiling", DisableParallelization = true)]
public sealed class ProfilingCollection
{
}
