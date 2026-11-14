using System.Runtime.CompilerServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Shares explicitly requested reflection and layouts for the lifetime of their shader owners.</summary>
public sealed partial class ShaderDataLayout
{
    // Required binding discovery (HasRoot/RootTarget) may read reflection independently
    // of optional layout checks. Raw packing must never enter this cache.
    private static ReflectionCache GetReflection(ShaderTargetData target)
        => BindingTargets.Values.GetValue(target, static value => new(value));

    private static class BindingTargets
    {
        internal static readonly ConditionalWeakTable<ShaderTargetData, ReflectionCache> Values = new();

        static BindingTargets()
        {
        }
    }

    private static class ArtifactTargets
    {
        internal static readonly ConditionalWeakTable<ShaderArtifact, ArtifactCache> Values = new();

        static ArtifactTargets()
        {
        }
    }

    private sealed class ArtifactCache
    {
        private readonly Dictionary<ShaderTarget, ReflectionCache> _reflections = [];

        internal ShaderDataLayout Data(ShaderArtifact artifact, ShaderTarget target, string name)
        {
            lock (_reflections)
            {
                if (!_reflections.TryGetValue(target, out ReflectionCache? reflection))
                {
                    reflection = GetReflection(artifact.GetTarget(target));
                    _reflections.Add(target, reflection);
                }

                return reflection.Data(name);
            }
        }
    }

    private sealed class ReflectionCache
    {
        private readonly JsonElement _metadata;
        private readonly Lazy<HashSet<string>> _rootNames;
        private readonly object _gate = new();
        private Dictionary<string, ShaderDataLayout>? _data;
        private Dictionary<string, ShaderDataLayout>? _roots;

        internal ReflectionCache(ShaderTargetData target)
        {
            using var document = JsonDocument.Parse(target.ReflectionJson);
            _metadata = document.RootElement.Clone();
            _rootNames = new(() =>
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement parameter in _metadata.GetProperty("parameters").EnumerateArray())
                {
                    string name = parameter.GetProperty("name").GetString()!;
                    if (parameter.GetProperty("type").GetProperty("kind").GetString() == "constantBuffer" && name != "lumyteDrawInfo")
                    {
                        names.Add(name);
                    }
                }

                return names;
            });
        }

        internal bool HasRoot(string? name) => name is null ? _rootNames.Value.Count != 0 : _rootNames.Value.Contains(name);

        internal ShaderDataLayout Data(string name)
        {
            lock (_gate)
            {
                Dictionary<string, ShaderDataLayout> cache = _data ??= new(StringComparer.Ordinal);
                if (cache.TryGetValue(name, out ShaderDataLayout? layout))
                {
                    return layout;
                }

                if (!_metadata.TryGetProperty("lumyteAbi", out JsonElement abi) || abi.GetInt32() != 1)
                {
                    throw new NotSupportedException("The artifact has no supported shader data ABI.");
                }

                JsonElement type = DataType(_metadata, name);
                if (type.ValueKind == JsonValueKind.Undefined)
                {
                    throw new NotSupportedException("The artifact is missing the shader data type schema: " + name);
                }

                layout = new(type, _metadata);
                cache.Add(name, layout);
                return layout;
            }
        }

        internal ShaderDataLayout Root(string name)
        {
            lock (_gate)
            {
                Dictionary<string, ShaderDataLayout> cache = _roots ??= new(StringComparer.Ordinal);
                if (cache.TryGetValue(name, out ShaderDataLayout? layout))
                {
                    return layout;
                }

                JsonElement parameter = _metadata.GetProperty("parameters").EnumerateArray().SingleOrDefault(p => p.GetProperty("name").GetString() == name);
                if (parameter.ValueKind == JsonValueKind.Undefined)
                {
                    throw new ArgumentException("The program does not declare this root parameter.", nameof(name));
                }

                layout = new(parameter.GetProperty("type").GetProperty("elementType"), _metadata);
                cache.Add(name, layout);
                return layout;
            }
        }
    }
}
