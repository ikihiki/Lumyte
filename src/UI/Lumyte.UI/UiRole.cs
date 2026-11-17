namespace Lumyte.UI;

/// <summary>Describes the semantic role exposed to UI tools.</summary>
public enum UiRole
{
    /// <summary>No semantic role.</summary>
    None,

    /// <summary>A semantic group.</summary>
    Group,

    /// <summary>A button.</summary>
    Button,

    /// <summary>An editable text field.</summary>
    TextBox,

    /// <summary>A redacted IME candidate presenter.</summary>
    ImeCandidates,

    /// <summary>Read-only text.</summary>
    Text,

    /// <summary>An image.</summary>
    Image,

    /// <summary>An icon.</summary>
    Icon,

    /// <summary>A progress indicator.</summary>
    ProgressBar,

    /// <summary>An indeterminate busy indicator.</summary>
    BusyIndicator,

    /// <summary>A checked command button.</summary>
    ToggleButton,

    /// <summary>A nullable check box.</summary>
    CheckBox,

    /// <summary>An on/off switch.</summary>
    Switch,

    /// <summary>A single selection group.</summary>
    RadioGroup,

    /// <summary>A single selectable option.</summary>
    RadioButton,

    /// <summary>A scroll viewport.</summary>
    ScrollView,

    /// <summary>A scroll position control.</summary>
    ScrollBar,

    /// <summary>A numeric single-thumb track.</summary>
    Slider,

    /// <summary>An ordered two-thumb numeric track.</summary>
    RangeSlider,

    /// <summary>A single-choice item collection.</summary>
    ListBox,

    /// <summary>A selectable collection item.</summary>
    ListItem,

    /// <summary>A non-editable popup selector.</summary>
    ComboBox,

    /// <summary>A keyed tab selection group.</summary>
    TabView,

    /// <summary>A selectable tab header and retained panel.</summary>
    TabItem,

    /// <summary>A collapsible content panel.</summary>
    Expander,

    /// <summary>A command menu.</summary>
    Menu,

    /// <summary>A command, checked command or submenu anchor.</summary>
    MenuItem,

    /// <summary>A passive contextual tooltip with its anchor.</summary>
    Tooltip,
}
