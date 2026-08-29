using Lumyte.Core.Time;

namespace Lumyte.Animation;

public static partial class AnimationKit
{
    public static Keyframe<T> Keyframe<T>(Duration time, T value) => new(time, value);
}
