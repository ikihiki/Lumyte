using System.Diagnostics.CodeAnalysis;
using Lumyte.UI.Navigation;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Combines canonical route prefixes without creating service providers or scopes.</summary>
public sealed class UiPageGroupBuilder
{
    private readonly UiApplicationBuilder _builder;

    internal UiPageGroupBuilder(UiApplicationBuilder builder, string prefix)
    {
        _builder = builder;
        Prefix = prefix;
    }

    /// <summary>Gets the complete canonical group prefix.</summary>
    public string Prefix { get; }

    /// <summary>Combines a local prefix with this group's prefix.</summary>
    /// <param name="prefix">The local canonical prefix.</param>
    /// <returns>A nested group.</returns>
    public UiPageGroupBuilder MapGroup(string prefix) => _builder.MapGroup(Combine(prefix));

    /// <summary>Registers a no-argument scoped page within this group.</summary>
    /// <typeparam name="TPage">The page type.</typeparam>
    /// <param name="localKey">The local canonical route.</param>
    /// <returns>The complete destination key.</returns>
    public UiPageKey<UiNoArgs> MapPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage>(string localKey)
        where TPage : class, IUiPage => _builder.MapPage<TPage>(Combine(localKey));

    /// <summary>Registers a typed scoped page within this group.</summary>
    /// <typeparam name="TPage">The page type.</typeparam>
    /// <typeparam name="TArgs">The exact argument type.</typeparam>
    /// <param name="localKey">The local canonical route.</param>
    /// <param name="captureArgs">The pure argument copier.</param>
    /// <returns>The complete destination key.</returns>
    public UiPageKey<TArgs> MapPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage, TArgs>(string localKey, Func<TArgs, TArgs> captureArgs)
        where TPage : class, IUiPage => _builder.MapPage<TPage, TArgs>(new(Combine(localKey)), captureArgs);

    /// <summary>Registers a scoped synchronous handler within this group.</summary>
    /// <param name="localKey">The local canonical route.</param>
    /// <param name="handler">The synchronous UI builder.</param>
    /// <returns>The complete destination key.</returns>
    public UiPageKey<UiNoArgs> MapPage(string localKey, Func<UiPageContext, Widget> handler)
        => _builder.MapPage(Combine(localKey), handler);

    /// <summary>Registers a scoped synchronous handler within this group.</summary>
    /// <typeparam name="T1">The first page-scoped service.</typeparam>
    /// <param name="localKey">The local canonical route.</param>
    /// <param name="handler">The synchronous UI builder.</param>
    /// <returns>The complete destination key.</returns>
    public UiPageKey<UiNoArgs> MapPage<T1>(string localKey, Func<UiPageContext, T1, Widget> handler)
        where T1 : notnull
        => _builder.MapPage<T1>(Combine(localKey), handler);

    /// <summary>Registers a scoped synchronous handler within this group.</summary>
    /// <typeparam name="T1">The first page-scoped service.</typeparam>
    /// <typeparam name="T2">The second page-scoped service.</typeparam>
    /// <param name="localKey">The local canonical route.</param>
    /// <param name="handler">The synchronous UI builder.</param>
    /// <returns>The complete destination key.</returns>
    public UiPageKey<UiNoArgs> MapPage<T1, T2>(string localKey, Func<UiPageContext, T1, T2, Widget> handler)
        where T1 : notnull
        where T2 : notnull
        => _builder.MapPage<T1, T2>(Combine(localKey), handler);

    /// <summary>Registers a scoped synchronous handler within this group.</summary>
    /// <typeparam name="T1">The first page-scoped service.</typeparam>
    /// <typeparam name="T2">The second page-scoped service.</typeparam>
    /// <typeparam name="T3">The third page-scoped service.</typeparam>
    /// <param name="localKey">The local canonical route.</param>
    /// <param name="handler">The synchronous UI builder.</param>
    /// <returns>The complete destination key.</returns>
    public UiPageKey<UiNoArgs> MapPage<T1, T2, T3>(string localKey, Func<UiPageContext, T1, T2, T3, Widget> handler)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
        => _builder.MapPage<T1, T2, T3>(Combine(localKey), handler);

    private string Combine(string local)
    {
        _builder.CheckMutable();
        string key = new UiPageKey<UiNoArgs>(local).Value;
        return Prefix == "/" ? key : key == "/" ? Prefix : new UiPageKey<UiNoArgs>(Prefix + key).Value;
    }
}
