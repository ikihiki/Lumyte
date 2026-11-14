namespace Lumyte.Composition.Benchmark;

/// <summary>Checks snapshot ownership and generated consumer semantics.</summary>
public static class BenchmarkChecks
{
    /// <summary>Verifies every finite content boundary and passive recipe behavior.</summary>
    public static void Verify()
    {
        foreach (int count in new[] { 0, 1, 8, 256 })
        {
            var benchmark = new CompositionBenchmarks { Count = count };
            benchmark.Setup();
            int[] first = benchmark.Apply();
            if (first.Length > 0)
            {
                first[0] = -1;
            }

            int[] second = benchmark.Apply();
            if (ReferenceEquals(first, second) || (count > 0 && second[0] != 1) || benchmark.Adapter() != 2 || benchmark.Factory().Value != 19 || benchmark.Factory().Children.Length != count)
            {
                throw new InvalidOperationException("Composition snapshot or construction contract failed.");
            }

            var wrapper = (Compose.Definitions.FunctionHost)benchmark.FunctionWrapper();
            var rebuilt = (Compose.Definitions.FunctionHost)benchmark.Rebuild();
            if (ReferenceEquals(wrapper, rebuilt) || ((Compose.Definitions.Item)rebuilt.Evaluate()).Value != 17 || Compose.Item().Value != 7 || Compose.Item(value: 0).Value != 0)
            {
                throw new InvalidOperationException("Recipe or omitted-value contract failed.");
            }
        }
    }
}
