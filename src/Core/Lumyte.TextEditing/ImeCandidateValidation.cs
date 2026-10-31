namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate validation.</summary>
public static class ImeCandidateValidation
{
    /// <summary>Copy.</summary>
    /// <param name = "snapshot">The snapshot.</param>
    /// <returns>The operation result.</returns>
    public static ImeCandidateSnapshot Copy(ImeCandidateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Id.SessionId == Guid.Empty || snapshot.Id.SessionGeneration == 0 || snapshot.Id.ElementGeneration == 0 || snapshot.Revision == 0 || snapshot.Items.Count > 1024)
        {
            throw new ArgumentException("Invalid candidate identity or count.", nameof(snapshot));
        }

        int total = 0;
        for (int i = 0; i < snapshot.Items.Count; i++)
        {
            ImeCandidateItem item = snapshot.Items[i];
            TextDocument.ValidateUnicode(item.Text);
            total = checked(total + item.Text.Length);
            if (item.Index != i || item.Text.Length > 4096 || total > 262144)
            {
                throw new ArgumentException("Candidate text or index limit exceeded.", nameof(snapshot));
            }
        }

        if (snapshot.SelectedIndex is { } selected && (selected < 0 || selected >= snapshot.Items.Count))
        {
            throw new ArgumentException("Invalid selection.", nameof(snapshot));
        }

        int previous = -1;
        foreach (int start in snapshot.PageStarts)
        {
            if (start <= previous || start < 0 || start >= snapshot.Items.Count || (previous == -1 && start != 0))
            {
                throw new ArgumentException("Invalid page boundaries.", nameof(snapshot));
            }

            previous = start;
        }

        if (snapshot.CurrentPage is { } page && (page < 0 || page >= snapshot.PageStarts.Count))
        {
            throw new ArgumentException("Invalid current page.", nameof(snapshot));
        }

        if (snapshot.Items.Count == 0 && (snapshot.SelectedIndex is not null || snapshot.CurrentPage is not null || snapshot.PageStarts.Count != 0))
        {
            throw new ArgumentException("An empty candidate list has no selection or page.", nameof(snapshot));
        }

        return snapshot with
        {
            Items = Array.AsReadOnly(snapshot.Items.ToArray()),
            PageStarts = Array.AsReadOnly(snapshot.PageStarts.ToArray()),
        };
    }
}
