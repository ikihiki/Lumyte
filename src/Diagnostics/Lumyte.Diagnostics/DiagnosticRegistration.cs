namespace Lumyte.Diagnostics;

internal sealed record DiagnosticRegistration(Type Point, Type Contributor, SubsystemDescriptor Descriptor, Func<IServiceProvider, IDiagnosticContributor> Resolve);
