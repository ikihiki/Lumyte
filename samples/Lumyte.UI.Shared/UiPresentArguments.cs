using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.UI.SampleSupport;

internal readonly partial record struct UiPresentArguments(IGpuRef<IGraphicsTextureView> Image, IGpuRef<IGraphicsSampler> Sampler, Vector2 Size) : IShaderArguments;
