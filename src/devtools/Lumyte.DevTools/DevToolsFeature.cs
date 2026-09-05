namespace Lumyte.DevTools;

public enum DevToolsFeatureKind
{
    Query,
    Command,
    Event,
}

public sealed record DevToolsFeature(
    string Name,
    DevToolsFeatureKind Kind,
    Type RequestType,
    Type? ResponseType);

public class DevToolsException : InvalidOperationException
{
    public DevToolsException(string message) : base(message) { }
}

public sealed class DevToolsFeatureAlreadyRegisteredException : DevToolsException
{
    internal DevToolsFeatureAlreadyRegisteredException(DevToolsDomain domain, DevToolsFeatureKind kind, string name)
        : base($"The {kind.ToString().ToLowerInvariant()} '{domain.Name}/{name}' is already registered.") { }
}

public sealed class DevToolsFeatureNotRegisteredException : DevToolsException
{
    internal DevToolsFeatureNotRegisteredException(DevToolsDomain domain, DevToolsFeatureKind kind, string name)
        : base($"The {kind.ToString().ToLowerInvariant()} '{domain.Name}/{name}' is not registered.") { }
}

public sealed class DevToolsContractMismatchException : DevToolsException
{
    internal DevToolsContractMismatchException(
        DevToolsDomain domain,
        DevToolsFeatureKind kind,
        string name,
        Type expectedRequest,
        Type? expectedResponse,
        Type actualRequest,
        Type? actualResponse)
        : base($"The {kind.ToString().ToLowerInvariant()} '{domain.Name}/{name}' uses contract " +
            $"'{Format(expectedRequest, expectedResponse)}', not '{Format(actualRequest, actualResponse)}'.") { }

    private static string Format(Type request, Type? response) => response is null
        ? request.FullName ?? request.Name
        : $"{request.FullName ?? request.Name} -> {response.FullName ?? response.Name}";
}
