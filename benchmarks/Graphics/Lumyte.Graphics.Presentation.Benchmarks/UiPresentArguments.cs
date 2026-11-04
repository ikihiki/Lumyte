using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Presentation.Benchmarks;

// Exact product shader argument shape/name; generated locally, without production friendship.
internal readonly partial record struct UiPresentArguments(IGpuRef<IGraphicsTextureView> Image, IGpuRef<IGraphicsSampler> Sampler, Vector2 Size, uint EncodeSrgb) : IShaderArguments;
