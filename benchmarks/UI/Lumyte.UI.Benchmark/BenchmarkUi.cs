using Lumyte.Composition;
using Lumyte.Signals;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Benchmark;

/// <summary>Generated application functions with observable builds.</summary>
public static partial class BenchmarkUi
{
    /// <summary>Application composition definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Builds one reactive leaf.</summary>
        /// <param name="width">Reactive width.</param>
        /// <param name="built">Build observation.</param>
        /// <returns>A rectangle declaration.</returns>
        [Composable(Factory = "BenchmarkUi")]
        public static Widget Leaf(Signal<float?> width, Action built)
        {
            built();
            return Rectangle(width: width.Value, height: 8);
        }
    }
}
