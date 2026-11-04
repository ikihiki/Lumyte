namespace Lumyte.Graphics.Drawing2D;

/// <summary>Immutable commands in canonical pages of at most 64 commands, shared by placements.</summary>
internal sealed class DrawingContentPages2D
{
    /// <summary>The maximum number of commands in a canonical CPU page.</summary>
    internal const int PageCapacity = 64;

    private DrawingContentPages2D(DrawList2D[] pages)
    {
        Pages = Array.AsReadOnly(pages);
        Count = pages.Sum(page => page.Commands.Count);
    }

    /// <summary>Gets the command count, including all pages.</summary>
    internal int Count { get; }

    /// <summary>Gets the page count. Empty content has zero pages.</summary>
    internal int PageCount => Pages.Count;

    internal IReadOnlyList<DrawList2D> Pages { get; }

    /// <summary>Creates immutable content without replaying commands or changing captured state.</summary>
    /// <param name="content">The immutable source recording.</param>
    /// <returns>The paged content.</returns>
    internal static DrawingContentPages2D FromDrawList(DrawList2D content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Commands.Count == 0)
        {
            return new([]);
        }

        if (content.Commands.Count <= PageCapacity)
        {
            return new([content]);
        }

        var pages = new DrawList2D[((content.Commands.Count - 1) / PageCapacity) + 1];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = new(content.Commands.Skip(i * PageCapacity).Take(PageCapacity));
        }

        return new(pages);
    }

    /// <summary>Replaces one page, preserving every other page by reference.</summary>
    /// <param name="index">The existing page index.</param>
    /// <param name="content">A full 64-command page, or 1–64 commands for the final page.</param>
    /// <returns>The changed content, or this instance for an identical page reference.</returns>
    internal DrawingContentPages2D WithPage(int index, DrawList2D content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= Pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (content.Commands.Count < 1 || content.Commands.Count > PageCapacity || (index < Pages.Count - 1 && content.Commands.Count != PageCapacity))
        {
            throw new ArgumentException("Replacement must preserve canonical page boundaries.", nameof(content));
        }

        if (ReferenceEquals(Pages[index], content))
        {
            return this;
        }

        DrawList2D[] pages = Pages.ToArray();
        pages[index] = content;
        return new(pages);
    }
}
