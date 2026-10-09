using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Lumyte.Settings;

internal static class SettingsDictionary
{
    private static readonly ConcurrentDictionary<Type, Func<IDictionary, IDictionary>> _factories = new();

    internal static IDictionary CreateEmptyLike(IDictionary source) => _factories.GetOrAdd(source.GetType(), CreateFactory)(source);

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, typeof(Dictionary<,>))]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "This path only accepts existing Dictionary<string, T> types; their public constructors and properties are preserved by DynamicDependency.")]
    private static Func<IDictionary, IDictionary> CreateFactory(Type type)
    {
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Dictionary<,>) || type.GetGenericArguments()[0] != typeof(string))
        {
            throw new NotSupportedException($"Preserving dictionary comparers requires Dictionary<string, T>, but received {type}.");
        }

        // The non-generic IDictionary contract does not expose the comparer. Reflect only on
        // the existing closed dictionary type; no dynamic generic instantiation is required.
        PropertyInfo comparer = type.GetProperty(nameof(Dictionary<string, object>.Comparer))!;
        ConstructorInfo constructor = type.GetConstructor([typeof(IEqualityComparer<string>)])!;
        return source => (IDictionary)constructor.Invoke([comparer.GetValue(source)]);
    }
}
