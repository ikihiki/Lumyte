namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class RenderingWorkloadCatalog
{
    internal static IEnumerable<(string Suite, string Name)> Cases => Content2DWorkloads.Cases.Select(n => ("2d", n))
        .Concat(GameUiWorkloads.Cases.Select(n => ("game-ui", n)))
        .Concat(EditorUiWorkloads.Cases.Select(n => ("editor-ui", n)));

    internal static IRenderingWorkload Create(string name, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (Content2DWorkloads.Cases.Contains(name))
        {
            return Content2DWorkloads.Create(name, count);
        }

        if (GameUiWorkloads.Cases.Contains(name))
        {
            return GameUiWorkloads.Create(name, count);
        }

        if (EditorUiWorkloads.Cases.Contains(name))
        {
            return EditorUiWorkloads.Create(name, count);
        }

        throw new ArgumentException($"Unknown workload: {name}");
    }

    internal static string[] Select(string suite, string? name)
    {
        if (suite is not ("all" or "2d" or "game-ui" or "editor-ui"))
        {
            throw new ArgumentException($"Unknown suite: {suite}");
        }

        string[] selected = Cases.Where(c => suite == "all" || c.Suite == suite).Where(c => name is null || c.Name == name).Select(c => c.Name).ToArray();
        if (selected.Length == 0)
        {
            throw new ArgumentException("No workload matches suite/case.");
        }

        return selected;
    }
}
