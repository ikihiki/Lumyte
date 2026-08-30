namespace Lumyte.DevTools;

public sealed class DevToolsHub
{
    private readonly Lock sync = new();
    private readonly Dictionary<FeatureKey, Registration> registrations = [];
    private long nextId;

    public IReadOnlyList<DevToolsDomain> Domains
    {
        get
        {
            lock (sync)
            {
                return registrations.Keys.Select(static key => new DevToolsDomain(key.Domain))
                    .Distinct().OrderBy(static domain => domain.Name, StringComparer.Ordinal).ToArray();
            }
        }
    }

    public IReadOnlyList<DevToolsFeature> GetFeatures(DevToolsDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        lock (sync)
        {
            return registrations.Where(pair => StringComparer.Ordinal.Equals(pair.Key.Domain, domain.Name))
                .Select(static pair => pair.Value.Feature).OrderBy(static feature => feature.Kind)
                .ThenBy(static feature => feature.Name, StringComparer.Ordinal).ToArray();
        }
    }

    public IDisposable RegisterQuery<TRequest, TResponse>(DevToolsDomain domain, DevToolsQuery<TRequest, TResponse> query, Func<TRequest, CancellationToken, ValueTask<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(handler);
        return Register(domain, DevToolsFeatureKind.Query, query.Name, typeof(TRequest), typeof(TResponse), handler, CreateInvoker(handler));
    }

    public IDisposable RegisterCommand<TRequest, TResponse>(DevToolsDomain domain, DevToolsCommand<TRequest, TResponse> command, Func<TRequest, CancellationToken, ValueTask<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handler);
        return Register(domain, DevToolsFeatureKind.Command, command.Name, typeof(TRequest), typeof(TResponse), handler, CreateInvoker(handler));
    }

    public DevToolsEventPublisher<T> RegisterEvent<T>(DevToolsDomain domain, DevToolsEvent<T> @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        long id = RegisterCore(domain, DevToolsFeatureKind.Event, @event.Name, typeof(T), null, null);
        return new DevToolsEventPublisher<T>(this, domain, @event, id);
    }

    public ValueTask<TResponse> QueryAsync<TRequest, TResponse>(DevToolsDomain domain, DevToolsQuery<TRequest, TResponse> query, TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InvokeAsync<TRequest, TResponse>(domain, DevToolsFeatureKind.Query, query.Name, request, cancellationToken);
    }

    public ValueTask<TResponse> CommandAsync<TRequest, TResponse>(DevToolsDomain domain, DevToolsCommand<TRequest, TResponse> command, TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InvokeAsync<TRequest, TResponse>(domain, DevToolsFeatureKind.Command, command.Name, request, cancellationToken);
    }

    public IDisposable Subscribe<T>(DevToolsDomain domain, DevToolsEvent<T> @event, Func<T, CancellationToken, ValueTask> listener)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(listener);
        FeatureKey key = CreateKey(domain, DevToolsFeatureKind.Event, @event.Name);
        lock (sync)
        {
            Registration registration = GetRegistration(key, typeof(T), null);
            long id = ++nextId;
            registration.Listeners.Add(id, (value, token) => listener((T)value!, token));
            return new Lifetime(() => RemoveListener(key, registration.Id, id));
        }
    }

    public ValueTask<object?> InvokeAsync(
        DevToolsDomain domain,
        DevToolsFeatureKind kind,
        string name,
        object? request,
        CancellationToken cancellationToken = default)
    {
        if (kind is not DevToolsFeatureKind.Query and not DevToolsFeatureKind.Command)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only queries and commands can be invoked.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        FeatureKey key = CreateKey(domain, kind, name);
        Func<object?, CancellationToken, ValueTask<object?>> invoker;
        lock (sync)
        {
            Registration registration = GetRegistration(key);
            if (request is not null && !registration.Feature.RequestType.IsInstanceOfType(request))
            {
                throw new DevToolsContractMismatchException(
                    domain,
                    kind,
                    name,
                    registration.Feature.RequestType,
                    registration.Feature.ResponseType,
                    request.GetType(),
                    registration.Feature.ResponseType);
            }

            invoker = registration.Invoker!;
        }

        return invoker(request, cancellationToken);
    }

    public IDisposable Subscribe(
        DevToolsDomain domain,
        string eventName,
        Func<object?, CancellationToken, ValueTask> listener)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(listener);
        FeatureKey key = CreateKey(domain, DevToolsFeatureKind.Event, eventName);
        lock (sync)
        {
            Registration registration = GetRegistration(key);
            long id = ++nextId;
            registration.Listeners.Add(id, listener);
            return new Lifetime(() => RemoveListener(key, registration.Id, id));
        }
    }
    internal async ValueTask PublishAsync<T>(DevToolsDomain domain, DevToolsEvent<T> @event, long registrationId, T value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<object?, CancellationToken, ValueTask>[] listeners;
        FeatureKey key = CreateKey(domain, DevToolsFeatureKind.Event, @event.Name);
        lock (sync)
        {
            Registration registration = GetRegistration(key, typeof(T), null);
            if (registration.Id != registrationId)
            {
                throw new DevToolsFeatureNotRegisteredException(domain, DevToolsFeatureKind.Event, @event.Name);
            }

            listeners = registration.Listeners.Values.ToArray();
        }

        foreach (Func<object?, CancellationToken, ValueTask> listener in listeners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await listener(value, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void Unregister(DevToolsDomain domain, DevToolsFeatureKind kind, string name, long registrationId)
    {
        FeatureKey key = CreateKey(domain, kind, name);
        lock (sync)
        {
            if (registrations.TryGetValue(key, out Registration? registration) && registration.Id == registrationId)
            {
                registrations.Remove(key);
            }
        }
    }

    private IDisposable Register(DevToolsDomain domain, DevToolsFeatureKind kind, string name, Type requestType, Type responseType, Delegate handler, Func<object?, CancellationToken, ValueTask<object?>> invoker)
    {
        long id = RegisterCore(domain, kind, name, requestType, responseType, handler, invoker);
        return new Lifetime(() => Unregister(domain, kind, name, id));
    }

    private long RegisterCore(DevToolsDomain domain, DevToolsFeatureKind kind, string name, Type requestType, Type? responseType, Delegate? handler, Func<object?, CancellationToken, ValueTask<object?>>? invoker = null)
    {
        FeatureKey key = CreateKey(domain, kind, name);
        lock (sync)
        {
            if (registrations.ContainsKey(key))
            {
                throw new DevToolsFeatureAlreadyRegisteredException(domain, kind, name);
            }

            long id = ++nextId;
            registrations.Add(key, new Registration(id, new DevToolsFeature(name, kind, requestType, responseType), handler, invoker));
            return id;
        }
    }

    private ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(DevToolsDomain domain, DevToolsFeatureKind kind, string name, TRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FeatureKey key = CreateKey(domain, kind, name);
        Func<TRequest, CancellationToken, ValueTask<TResponse>> handler;
        lock (sync)
        {
            Registration registration = GetRegistration(key, typeof(TRequest), typeof(TResponse));
            handler = (Func<TRequest, CancellationToken, ValueTask<TResponse>>)registration.Handler!;
        }

        return handler(request, cancellationToken);
    }

    private static Func<object?, CancellationToken, ValueTask<object?>> CreateInvoker<TRequest, TResponse>(
        Func<TRequest, CancellationToken, ValueTask<TResponse>> handler) =>
        async (request, token) => await handler((TRequest)request!, token).ConfigureAwait(false);

    private Registration GetRegistration(FeatureKey key)
    {
        if (!registrations.TryGetValue(key, out Registration? registration))
        {
            throw new DevToolsFeatureNotRegisteredException(new DevToolsDomain(key.Domain), key.Kind, key.Name);
        }

        return registration;
    }
    private Registration GetRegistration(FeatureKey key, Type requestType, Type? responseType)
    {
        DevToolsDomain domain = new(key.Domain);
        if (!registrations.TryGetValue(key, out Registration? registration))
        {
            throw new DevToolsFeatureNotRegisteredException(domain, key.Kind, key.Name);
        }

        if (registration.Feature.RequestType != requestType || registration.Feature.ResponseType != responseType)
        {
            throw new DevToolsContractMismatchException(domain, key.Kind, key.Name, registration.Feature.RequestType, registration.Feature.ResponseType, requestType, responseType);
        }

        return registration;
    }

    private static FeatureKey CreateKey(DevToolsDomain domain, DevToolsFeatureKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new FeatureKey(domain.Name, kind, name);
    }

    private void RemoveListener(FeatureKey key, long registrationId, long listenerId)
    {
        lock (sync)
        {
            if (registrations.TryGetValue(key, out Registration? registration) && registration.Id == registrationId)
            {
                registration.Listeners.Remove(listenerId);
            }
        }
    }

    private readonly record struct FeatureKey(string Domain, DevToolsFeatureKind Kind, string Name);

    private sealed class Registration(long id, DevToolsFeature feature, Delegate? handler, Func<object?, CancellationToken, ValueTask<object?>>? invoker)
    {
        public long Id { get; } = id;
        public DevToolsFeature Feature { get; } = feature;
        public Delegate? Handler { get; } = handler;
        public Func<object?, CancellationToken, ValueTask<object?>>? Invoker { get; } = invoker;
        public Dictionary<long, Func<object?, CancellationToken, ValueTask>> Listeners { get; } = [];
    }

    private sealed class Lifetime(Action dispose) : IDisposable
    {
        private Action? dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }
}
