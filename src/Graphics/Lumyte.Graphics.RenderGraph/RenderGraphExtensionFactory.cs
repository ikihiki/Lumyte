namespace Lumyte.Graphics.RenderGraph;

/// <summary>A prepared factory and optional shared resources transferred to the extension configuration.</summary>
/// <typeparam name="T">The exact graph-local extension state type.</typeparam>
/// <param name="Factory">Creates one cached state per graph without reflection or implicit service resolution.</param>
/// <param name="Owner">Optional shared owner, released when the configuration is disposed.</param>
/// <remarks>The returned graph-local state is borrowed. Retain its GPU or resource ownership with graph.Retain.</remarks>
public readonly record struct RenderGraphExtensionFactory<T>(Func<RenderGraph, T> Factory, IDisposable? Owner = null)
    where T : class;
