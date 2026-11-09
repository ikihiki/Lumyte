using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Isolates process-wide no-GC allocation measurements from other test collections.</summary>
[CollectionDefinition(nameof(AnimationAllocationCollection), DisableParallelization = true)]
public sealed class AnimationAllocationCollection;
