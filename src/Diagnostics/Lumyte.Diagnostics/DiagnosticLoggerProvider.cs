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

            DiagnosticTelemetry? sink = null;
            try
            {
                var fields = new LogFields(state as IEnumerable<KeyValuePair<string, object?>>);
                provider._scopes.ForEachScope(static (scope, target) => target.AddScope(scope), fields);
                if (fields.Ambiguous || fields.InstanceId == null || !router.TryGet(fields.InstanceId, out sink)
                    || sink == null || !sink.LogEnabled(category, logLevel))
                {
                    return;
                }

                sink.WriteLog(category, logLevel, eventId, formatter(state, exception), fields, exception);
            }
            catch (Exception)
            {
                // Providers never throw into game code or recursively log transfer errors.
                sink?.Drop();
            }
        }
    }

    private sealed class LogFields : Dictionary<string, DiagnosticValue>
    {
        private int _visited;

        public LogFields(IEnumerable<KeyValuePair<string, object?>>? state)
            : base(StringComparer.Ordinal)
        {
            if (state != null)
            {
                foreach (KeyValuePair<string, object?> pair in state)
                {
                    DiagnosticTelemetry.CopyField(this, pair);
                    if (++_visited == 32)
                    {
                        break;
                    }
                }
            }
        }

        public string? InstanceId { get; private set; }

        public bool Ambiguous { get; private set; }

        public void AddScope(object? scope)
        {
            if (Ambiguous || scope is not IEnumerable<KeyValuePair<string, object?>> values)
            {
                return;
            }

            int visited = 0;
            foreach (KeyValuePair<string, object?> pair in values)
            {
                if (pair.Key == "lumyte.instance.id")
                {
                    string id = pair.Value as string ?? string.Empty;
                    if (InstanceId != null && InstanceId != id)
                    {
                        Ambiguous = true;
                        return;
                    }

                    InstanceId = id;
                }

                if (_visited < 32)
                {
                    DiagnosticTelemetry.CopyField(this, pair);
                    _visited++;
                }

                if (++visited == 32)
                {
                    break;
                }
            }
        }
    }
}
