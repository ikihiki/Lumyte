namespace Lumyte.Resources;

/// <summary>Provides the opaque selector text and allocation-free segment traversal.</summary>
public readonly record struct ResourceSelector(ReadOnlyMemory<char> Text)
{
    public bool IsEmpty => Text.IsEmpty;

    public Enumerator GetEnumerator() => new(Text);

    public override string ToString() => Text.ToString();

    public struct Enumerator
    {
        private readonly ReadOnlyMemory<char> text;
        private int start;
        private int end;

        internal Enumerator(ReadOnlyMemory<char> text)
        {
            this.text = text;
            start = -1;
            end = -1;
        }

        public readonly ReadOnlyMemory<char> Current => text[start..end];

        public bool MoveNext()
        {
            if (text.IsEmpty || end == text.Length)
            {
                return false;
            }

            start = end < 0 ? 0 : end + 1;
            int separator = text.Span[start..].IndexOf('/');
            end = separator < 0 ? text.Length : start + separator;
            return true;
        }
    }
}
