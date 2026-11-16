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

    private int QueryVersion => _steps.Any(step => step.Role is "slider" or "rangeslider") ? 6 : _steps.Any(step => step.Role is "togglebutton" or "checkbox" or "switch" or "radiogroup" or "radiobutton") ? 4 : 1;

    /// <summary>Sets a finite scalar through the server's normal numeric input lease.</summary>
    /// <param name="value">The desired finite value.</param>
    /// <param name="cancellationToken">Cancels this operation.</param>
    /// <returns>The completed operation.</returns>
    public async Task SetValueAsync(double value, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(6, "set-value", _steps, NumericValue: value), cancellationToken);

    /// <summary>Sets both finite ordered endpoints through one ordinary input.</summary>
    /// <param name="lower">The finite lower value.</param>
    /// <param name="upper">The finite upper value.</param>
    /// <param name="cancellationToken">Cancels this operation.</param>
    /// <returns>The completed operation.</returns>
    public async Task SetRangeAsync(double lower, double upper, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(6, "set-range", _steps, NumericValue: lower, NumericUpper: upper), cancellationToken);

    /// <summary>Restricts an exact identity query to descendants.</summary>
    /// <param name = "testId">The identity.</param>
    /// <returns>A new locator.</returns>
    public UiRemoteLocator GetByTestId(string testId) => new(Page, [.. _steps, UiRemotePage.Step(testId)]);

    /// <summary>Restricts a role/name query to descendants.</summary>
    /// <param name = "role">A closed wire role accepted by UiRemotePage.GetByRole.</param>
    /// <param name = "name">Optional exact name.</param>
    /// <param name = "includeHidden">Whether hidden elements match.</param>
    /// <returns>A new locator.</returns>
    public UiRemoteLocator GetByRole(string role, string? name = null, bool includeHidden = false) => new(Page, [.. _steps, UiRemotePage.Step(role, name, includeHidden)]);

    /// <summary>Returns one game-side count and frees its result.</summary>
    /// <param name = "cancellationToken">Cancels the operation.</param>
    /// <returns>The exact count.</returns>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default) => JsonSerializer.Deserialize(await Page.ExecuteAsync(new(QueryVersion, "count", _steps), cancellationToken), UiAutomationJsonContext.Default.Int32);

    /// <summary>Submits exactly one logical click with game-side auto-waiting.</summary>
    /// <param name = "position">Optional finite logical offset.</param>
    /// <param name = "cancellationToken">Cancels the operation.</param>
    /// <returns>The completed job.</returns>
    public async Task ClickAsync(UiRemotePoint? position = null, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(QueryVersion, "click", _steps, Position: position), cancellationToken);

    /// <summary>Scrolls signed logical pixels through the exclusive normal wheel path.</summary>
    /// <param name="delta">The finite signed logical-pixel delta.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed job.</returns>
    public async Task ScrollAsync(UiRemotePoint delta, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(5, "scroll", _steps, Position: delta), cancellationToken);

    /// <summary>Focuses with ordinary game-side Tab input.</summary>
    /// <param name = "cancellationToken">Cancels the operation.</param>
    /// <returns>The completed job.</returns>
    public async Task FocusAsync(CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(QueryVersion, "focus", _steps), cancellationToken);

    /// <summary>Focuses and sends physical key and modifier input on the game.</summary>
    /// <param name = "key">The canonical Input Key name.</param>
    /// <param name = "modifiers">Up to four distinct canonical UI modifier names.</param>
    /// <param name = "cancellationToken">Cancels the operation.</param>
    /// <returns>The completed job.</returns>
    public async Task PressAsync(string key, IReadOnlyList<string>? modifiers = null, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(QueryVersion, "press", _steps, Key: key, Modifiers: modifiers?.ToArray() ?? []), cancellationToken);

    /// <summary>Fills through the server's exclusive normal editing path.</summary>
    /// <param name="text">The replacement text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed operation result.</returns>
    public async Task FillAsync(string text, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(Math.Max(2, QueryVersion), "fill", _steps, Text: text), cancellationToken);

    /// <summary>Checks committed text through explicit opt-in inspection.</summary>
    /// <param name="text">The expected committed value.</param>
    /// <param name="cancellationToken">Cancels the assertion.</param>
    /// <returns>The completed operation result.</returns>
    public async Task ToHaveValueAsync(string text, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(Math.Max(2, QueryVersion), "expect-value", _steps, Text: text), cancellationToken);

    /// <summary>Checks once through normal server input.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed job.</returns>
    public async Task CheckAsync(CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(4, "check", _steps), cancellationToken);

    /// <summary>Unchecks once through normal server input.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed job.</returns>
    public async Task UncheckAsync(CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(4, "uncheck", _steps), cancellationToken);

    /// <summary>Selects an ordinal group key through normal server pointer input.</summary>
    /// <param name="optionKey">The desired nonempty key.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed job.</returns>
    public async Task SelectOptionAsync(string optionKey, CancellationToken cancellationToken = default) => await Page.ExecuteAsync(new(4, "select-option", _steps, OptionKey: optionKey), cancellationToken);

    internal Task<byte[]> AssertAsync(string predicate, int? count, CancellationToken token) => Page.ExecuteAsync(new(QueryVersion, "expect", _steps, Predicate: predicate, Count: count), token);
}
