using Lumyte.Mathematics.Colors;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Expands common arrangements into ordinary declarations without adding control state.</summary>
public static class UiRecipes
{
    /// <summary>Creates an ordinary button with an explicit semantic name and caller-supplied icon.</summary>
    /// <param name="icon">The icon declaration.</param>
    /// <param name="accessibleName">A nonempty explicit name.</param>
    /// <param name="onClick">The ordinary activation callback.</param>
    /// <returns>A new button root.</returns>
    public static Widget IconButton(Widget icon, string accessibleName, Action? onClick = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibleName);
        Validate([icon]);
        return Button(automationName: accessibleName, onClick: onClick)[icon];
    }

    /// <summary>Creates a neutral card with explicit transparent border and eight logical units of padding.</summary>
    /// <param name="content">The ordinary content.</param>
    /// <returns>A new group root.</returns>
    public static Widget Card(Widget content)
    {
        Validate([content]);
        return Stack(with: [Border.Stroke(new(0, 0, 0, 0), 0), Padding.All(8)])[content];
    }

    /// <summary>Creates a card with explicit existing border and padding operations.</summary>
    /// <param name="content">The ordinary content.</param>
    /// <param name="borderColor">The explicit unit border color.</param>
    /// <param name="borderWidth">The finite nonnegative inside stroke width.</param>
    /// <param name="padding">The finite nonnegative content inset.</param>
    /// <returns>A new ordinary group root.</returns>
    public static Widget Card(Widget content, SRgba<float> borderColor, float borderWidth, float padding)
    {
        Validate([content]);
        return Stack(with: [Border.Stroke(borderColor, borderWidth), Padding.All(padding)])[content];
    }

    /// <summary>Places a label beside an editor without changing either declaration or inferring its name.</summary>
    /// <param name="label">The caller's label.</param>
    /// <param name="editor">The explicitly named editor.</param>
    /// <param name="accessibleName">An optional explicit name in passive label metadata; the editor is not rewritten.</param>
    /// <returns>A new row root.</returns>
    public static Widget FormRow(Widget label, Widget editor, string? accessibleName = null)
    {
        Validate([label, editor]);
        return Stack(orientation: UiOrientation.Horizontal, labelAssociation: new UiLabelAssociation(0, 1, accessibleName))[label, editor];
    }

    /// <summary>Wraps optional validation content; null creates an empty, noninteractive group.</summary>
    /// <param name="message">The caller's message.</param>
    /// <returns>A new group root.</returns>
    public static Widget ValidationMessage(Widget? message) => Arrange(message is null ? [] : [message]);

    /// <summary>Arranges actions using normal button Tab navigation, not a separate roving focus policy.</summary>
    /// <param name="actions">Distinct ordinary actions.</param>
    /// <returns>A new row root.</returns>
    public static Widget Toolbar(IReadOnlyList<Widget> actions) => Arrange(actions, UiOrientation.Horizontal);

    /// <summary>Arranges ordinary status content without creating a new live-region role.</summary>
    /// <param name="items">Distinct status declarations.</param>
    /// <returns>A new row root.</returns>
    public static Widget StatusBar(IReadOnlyList<Widget> items) => Arrange(items, UiOrientation.Horizontal);

    /// <summary>Arranges an empty-state message and optional ordinary action.</summary>
    /// <param name="message">The message.</param>
    /// <param name="action">An optional action.</param>
    /// <returns>A new column root.</returns>
    public static Widget EmptyState(Widget message, Widget? action = null) => Arrange(action is null ? [message] : [message, action]);

    /// <summary>Arranges an app-selected editor and optional validation; never reflects over application types.</summary>
    /// <param name="label">The label.</param>
    /// <param name="editor">The explicitly named editor.</param>
    /// <param name="validationMessage">Optional validation content.</param>
    /// <param name="accessibleName">An optional explicit name retained in the row association.</param>
    /// <returns>A new column root.</returns>
    public static Widget PropertyField(Widget label, Widget editor, Widget? validationMessage = null, string? accessibleName = null)
    {
        Widget[] items = validationMessage is null ? [label, editor] : [label, editor, validationMessage];
        Validate(items);
        return Arrange(validationMessage is null ? [FormRow(label, editor, accessibleName)] : [FormRow(label, editor, accessibleName), validationMessage]);
    }

    private static Widget Arrange(IReadOnlyList<Widget> content, UiOrientation orientation = UiOrientation.Vertical)
    {
        ArgumentNullException.ThrowIfNull(content);
        Widget[] snapshot = content.ToArray();
        Validate(snapshot);
        return Stack(orientation: orientation)[snapshot];
    }

    private static void Validate(IReadOnlyList<Widget> content)
    {
        var seen = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        foreach (Widget item in content)
        {
            if (item is null || !seen.Add(item))
            {
                throw new ArgumentException("Recipe content must be non-null and distinct.", nameof(content));
            }
        }
    }
}
