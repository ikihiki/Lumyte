using System.Collections.Immutable;
using Definitions = Lumyte.Input.Actions.Compose.Definitions;

namespace Lumyte.Input.Actions;

internal static class CompositionProfileBuilder
{
    public static ActionProfile Build(IReadOnlyList<Definitions.Action> actions, IReadOnlyList<Definitions.Binding> bindings, IEnumerable<Definitions.Context> roots, IReadOnlyList<Definitions.Recognition> recognitions)
    {
        var entries = new List<Entry>();
        var byNode = new Dictionary<Definitions.Context, Entry>();
        var pending = new Stack<(Definitions.Context Context, Definitions.Context? Parent, string Path)>();
        Definitions.Context[] rootArray = roots.ToArray();
        for (int i = rootArray.Length - 1; i >= 0; i--)
        {
            pending.Push((rootArray[i], null, $"@context/{i}"));
        }

        while (pending.TryPop(out (Definitions.Context Context, Definitions.Context? Parent, string Path) next))
        {
            ArgumentNullException.ThrowIfNull(next.Context);
            var entry = new Entry(next.Context, next.Parent, next.Context.Id ?? next.Path);
            if (!byNode.TryAdd(next.Context, entry))
            {
                throw new ArgumentException("A context cannot occur twice or contain itself in a composition tree.");
            }

            entries.Add(entry);
            for (int i = next.Context.Children.Count - 1; i >= 0; i--)
            {
                pending.Push((next.Context.Children[i], next.Context, $"{next.Path}/{i}"));
            }
        }

        foreach (Entry entry in entries)
        {
            string? structuralParent = entry.StructuralParent is null ? null : byNode[entry.StructuralParent].Id;
            string? referenceParent = entry.Node.Parent is null ? null : ResolveContext(entry.Node.Parent, byNode);
            if (entry.StructuralParent is not null && ((entry.Node.Parent is not null && !ReferenceEquals(entry.Node.Parent, entry.StructuralParent)) || (entry.Node.ParentId is not null && entry.Node.ParentId != structuralParent)))
            {
                throw new ArgumentException("A nested context must inherit its containing context.");
            }

            if (referenceParent is not null && entry.Node.ParentId is not null && referenceParent != entry.Node.ParentId)
            {
                throw new ArgumentException("The parent definition and parent identifier disagree.");
            }

            entry.ParentId = structuralParent ?? referenceParent ?? entry.Node.ParentId;
        }

        var bindingNodes = new List<(Definitions.Binding Node, string Context)>();
        var recognitionNodes = new List<(Definitions.Recognition Node, string Context)>();
        foreach (Definitions.Binding binding in bindings)
        {
            bindingNodes.Add((binding, ResolveOwner(binding.Context, binding.ContextId, null, byNode)));
        }

        foreach (Definitions.Recognition recognition in recognitions)
        {
            recognitionNodes.Add((recognition, ResolveOwner(recognition.Context, recognition.ContextId, null, byNode)));
        }

        foreach (Entry entry in entries)
        {
            bindingNodes.AddRange(entry.Node.LocalBindings.Select(binding => (binding, ResolveOwner(binding.Context, binding.ContextId, entry, byNode))));
            recognitionNodes.AddRange(entry.Node.LocalRecognitions.Select(recognition => (recognition, ResolveOwner(recognition.Context, recognition.ContextId, entry, byNode))));
        }

        var profile = new ActionProfile(
            actions.Select(action => action.Build()).ToImmutableArray(),
            bindingNodes.Select(item => item.Node.Build(item.Context)).ToImmutableArray(),
            entries.Select(entry => entry.Node.Build(entry.Id, entry.ParentId)).ToImmutableArray(),
            recognitionNodes.Select(item => item.Node.Build(item.Context)).ToImmutableArray());
        profile.Validate();
        var byId = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var scopes = new Dictionary<string, HashSet<Definitions.Action>>(StringComparer.Ordinal);
        foreach (Entry entry in entries)
        {
            var available = actions.ToHashSet();
            Entry current = entry;
            while (true)
            {
                available.UnionWith(current.Node.LocalActions);
                if (current.ParentId is null)
                {
                    break;
                }

                current = byId[current.ParentId];
            }

            scopes.Add(entry.Id, available);
        }

        foreach ((Definitions.Binding node, string context) in bindingNodes)
        {
            ValidateReference(node.ActionId, scopes[context]);
        }

        foreach ((Definitions.Recognition node, string context) in recognitionNodes)
        {
            foreach (ActionReference reference in node.Actions)
            {
                ValidateReference(reference, scopes[context]);
            }
        }

        // A failed build does not change previously committed anonymous identifiers.
        foreach (Entry entry in entries)
        {
            entry.Node.CommitIdentifier(entry.Id, entry.ParentId);
        }

        foreach (Definitions.Action action in actions.Concat(entries.SelectMany(entry => entry.Node.LocalActions)).Distinct())
        {
            action.CommitIdentifier(action.Id);
        }

        foreach ((Definitions.Binding node, string _) in bindingNodes)
        {
            node.CommitIdentifier(node.Id);
        }

        foreach ((Definitions.Recognition node, string _) in recognitionNodes)
        {
            node.CommitIdentifier(node.Id);
        }

        return profile;
    }

    private static string ResolveContext(Definitions.Context node, Dictionary<Definitions.Context, Entry> entries)
        => entries.TryGetValue(node, out Entry? entry) ? entry.Id : throw new ArgumentException("The referenced context is not part of this profile.");

    private static string ResolveOwner(Definitions.Context? reference, string? id, Entry? containing, Dictionary<Definitions.Context, Entry> entries)
    {
        if (containing is not null && reference is not null && !ReferenceEquals(reference, containing.Node))
        {
            throw new ArgumentException("A nested definition must belong to its containing context.");
        }

        string? referenceId = reference is null ? null : ResolveContext(reference, entries);
        string result = containing?.Id ?? referenceId ?? id ?? throw new ArgumentException("A standalone definition requires a context reference.");
        if ((referenceId is not null && referenceId != result) || (id is not null && id != result))
        {
            throw new ArgumentException("The context definition and context identifier disagree.");
        }

        return result;
    }

    private static void ValidateReference(ActionReference reference, HashSet<Definitions.Action> available)
    {
        if (reference.Definition is not null && !available.Contains(reference.Definition))
        {
            throw new ArgumentException("The referenced action definition is not in this context's scope.");
        }
    }

    private sealed class Entry(Definitions.Context node, Definitions.Context? structuralParent, string id)
    {
        public Definitions.Context Node { get; } = node;

        public Definitions.Context? StructuralParent { get; } = structuralParent;

        public string Id { get; } = id;

        public string? ParentId { get; set; }
    }
}
