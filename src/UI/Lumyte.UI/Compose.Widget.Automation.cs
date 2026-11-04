using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration with construction-time child ownership.</summary>
        public abstract partial class Widget
        {
            private string? _testId;

            /// <summary>Gets or sets an optional explicit identity; non-null values must contain non-whitespace characters.</summary>
            [ComposeParameter]
            public string? TestId
            {
                get => _testId;
                set
                {
                    if (value is not null && string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException("A test identity cannot be empty or whitespace.", nameof(value));
                    }

                    _testId = value;
                }
            }

            /// <summary>Gets or sets an explicit semantic name; no text or ARIA inference is performed.</summary>
            [ComposeParameter]
            public string? AutomationName { get; set; }

            /// <summary>Gets or sets the semantic role, independently of supported actions.</summary>
            [ComposeParameter]
            public UiRole Role { get; set; }
        }
    }
}
