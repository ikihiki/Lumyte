using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics;

internal sealed class DiagnosticLoggerProvider(TelemetryRouter router) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, router, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class SinkLogger(DiagnosticLoggerProvider provider, TelemetryRouter router, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && !category.StartsWith("Lumyte.Diagnostics", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var scopes = new List<KeyValuePair<string, object?>>();
            provider._scopes.ForEachScope(
                (scope, list) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> fields)
                {
                    list.AddRange(fields.Take(32));
                }
            },
                scopes);
            string[] ids = scopes.Where(pair => pair.Key == "lumyte.instance.id").Select(pair => pair.Value as string ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length != 1 || !router.TryGet(ids[0], out DiagnosticTelemetry? sink) || sink == null || !sink.LogEnabled(category, logLevel))
            {
                return;
            }

            try
            {
                sink.WriteLog(category, logLevel, eventId, formatter(state, exception), state as IEnumerable<KeyValuePair<string, object?>> ?? [], scopes, exception);
            }
            catch (Exception)
            {
                // Providers never throw into game code or recursively log transfer errors.
                sink.Drop();
            }
        }
    }
}
