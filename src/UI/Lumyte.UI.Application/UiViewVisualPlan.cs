using Lumyte.Animation;
using Lumyte.Core.Time;

namespace Lumyte.UI.Application;

internal sealed record UiViewVisualPlan(UiVisualTransition? Enter, UiVisualTransition? Exit, bool ExitThenEnter, bool Connect, Duration ConnectedDuration, AnimationEasing ConnectedEasing);
