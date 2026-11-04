using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal sealed class UiFunctionBuild(Dictionary<UiFunctionStateKey, object>? previous, UiUseEnvironment? environment)
{
    [ThreadStatic]
    private static UiFunctionBuild? _current;
    private readonly Dictionary<(string File, int Line, string? Key), int> _occurrences = [];
    private readonly Dictionary<UiFunctionStateKey, object>? _previous = previous;

    internal Dictionary<UiFunctionStateKey, object> Values { get; } = [];

    private UiUseEnvironment? Environment { get; } = environment;

    internal static T Use<T>(Func<T> initialize, string? key, string file, int line)
    {
        UiFunctionBuild current = Current();
        (string File, int Line, string? Key) site = (file, line, key);
        int occurrence = current._occurrences.GetValueOrDefault(site);
        current._occurrences[site] = occurrence + 1;
        var identity = new UiFunctionStateKey(file, line, key, occurrence);
        object state = current._previous?.GetValueOrDefault(identity) ?? new State<T>(initialize());
        if (state is not State<T> value)
        {
            throw new InvalidOperationException("A Use call site changed its value type.");
        }

        current.Values.Add(identity, value);
        return value.Value;
    }

    internal static T UseContext<T>(UiContextKey<T> context) => UiUseEnvironment.Read(Current().Environment, context);

    internal Widget Run(Func<Widget> build, out SignalReadSet reads)
    {
        UiFunctionBuild? previous = _current;
        _current = this;
        try
        {
            return UiSurface.CaptureIndependentBuild(build, out reads) ?? throw new InvalidOperationException("A composable function returned no UI tree.");
        }
        finally
        {
            _current = previous;
        }
    }

    private static UiFunctionBuild Current() => _current ?? throw new InvalidOperationException("Use requires a composable function evaluated by the UI.");

    private sealed class State<T>(T value)
    {
        internal T Value { get; } = value;
    }
}
