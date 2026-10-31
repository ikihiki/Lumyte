using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>A remote lazy query chain evaluated on the game, never by polling client snapshots.</summary>
public sealed class UiRemoteLocator
{
    private readonly UiRemoteLocatorStep[] _steps;

    internal UiRemoteLocator(UiRemotePage page, UiRemoteLocatorStep[] steps)
    {
        if (steps.Length > 8)
        {
            throw new ArgumentException("At most eight query steps are supported.", nameof(steps));
        }

        Page = page;
        _steps = steps;
    }

    internal UiRemotePage Page { get; }

    /// <summary>Restricts an exact identity query to descendants.</summary>
    /// <param name="testId">The identity.</param>
    /// <returns>A new locator.</returns>
    public UiRemoteLocator GetByTestId(string testId) => new(Page, [.. _steps, UiRemotePage.Step(testId)]);

    /// <summary>Restricts a role/name query to descendants.</summary>
    /// <param name="role">button or group.</param>
    /// <param name="name">Optional exact name.</param>
    /// <param name="includeHidden">Whether hidden elements match.</param>
    /// <returns>A new locator.</returns>
    public UiRemoteLocator GetByRole(string role, string? name = null, bool includeHidden = false) => new(Page, [.. _steps, UiRemotePage.Step(role, name, includeHidden)]);

    /// <summary>Returns one game-side count and frees its result.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The exact count.</returns>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default) => JsonSerializer.Deserialize(await Page.ExecuteAsync(new(1, "count", _steps), cancellationToken), UiAutomationJsonContext.Default.Int32);

    /// <summary>Submits exactly one logical click with game-side auto-waiting.</summary>
    /// <param name="position">Optional finite logical offset.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed job.</returns>
    public async Task ClickAsync(UiRemotePoint? position = null, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(1, "click", _steps, Position: position), cancellationToken);

    /// <summary>Focuses with ordinary game-side Tab input.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed job.</returns>
    public async Task FocusAsync(CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(1, "focus", _steps), cancellationToken);

    /// <summary>Focuses and sends physical key and modifier input on the game.</summary>
    /// <param name="key">The canonical Input Key name.</param>
    /// <param name="modifiers">Up to four distinct canonical UI modifier names.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed job.</returns>
    public async Task PressAsync(string key, IReadOnlyList<string>? modifiers = null, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(1, "press", _steps, Key: key, Modifiers: modifiers?.ToArray() ?? []), cancellationToken);

    internal Task<byte[]> AssertAsync(string predicate, int? count, CancellationToken token) => Page.ExecuteAsync(new(1, "expect", _steps, Predicate: predicate, Count: count), token);
}
