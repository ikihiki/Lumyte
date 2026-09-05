using Lumyte.Core.Time;

namespace Lumyte.Animation;

public readonly record struct Keyframe<T>(Duration Time, T Value);
