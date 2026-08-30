namespace Lumyte.DevTools;

public sealed record DevToolsDomain
{
    public DevToolsDomain(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}

public sealed record DevToolsQuery<TRequest, TResponse>
{
    public DevToolsQuery(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}

public sealed record DevToolsCommand<TRequest, TResponse>
{
    public DevToolsCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}

public sealed record DevToolsEvent<T>
{
    public DevToolsEvent(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}
