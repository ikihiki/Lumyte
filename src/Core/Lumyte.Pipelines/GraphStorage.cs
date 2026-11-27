namespace Lumyte.Pipelines;

internal sealed class GraphStorage
{
    private readonly List<Node> _nodes = [];
    private bool _frozen;

    internal Port<TO> Add<TO>(Node node, object[] ports)
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(node.Factory);
        foreach (object port in ports)
        {
            ArgumentNullException.ThrowIfNull(port);
        }

        foreach (int index in node.Inputs)
        {
            if (index < 0 || index > _nodes.Count)
            {
                throw new ArgumentException("Missing input or cyclic connection.", nameof(node));
            }
        }

        _nodes.Add(node);
        return new Port<TO>(this, _nodes.Count);
    }

    internal void Validate<TV>(Port<TV> port)
    {
        CheckOpen();
        ArgumentNullException.ThrowIfNull(port);
        if (!ReferenceEquals(port.Graph, this) || port.Index < 0 || port.Index > _nodes.Count)
        {
            throw new ArgumentException("The port belongs to another graph.", nameof(port));
        }
    }

    internal Port<TO> Append<TA, TO>(Node[] nodes, int output, Port<TA> input)
    {
        Validate(input);
        foreach (Node existing in _nodes)
        {
            existing.ValidateSchema();
        }

        int offset = _nodes.Count;
        Node[] rebound = nodes.Select(node => node.Rebind(node.Inputs.Select(index => index == 0 ? input.Index : offset + index).ToArray())).ToArray();
        foreach (Node node in rebound)
        {
            node.ValidateSchema();
        }

        _nodes.AddRange(rebound);
        return new Port<TO>(this, output == 0 ? input.Index : offset + output);
    }

    internal Node[] Freeze()
    {
        CheckOpen();
        Node[] snapshot = _nodes.ToArray();
        _frozen = true;
        return snapshot;
    }

    private void CheckOpen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("The declaration graph is frozen.");
        }
    }
}
