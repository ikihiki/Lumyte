using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics plan builder.</summary>
public sealed partial class GraphicsPlanBuilder
{
    private readonly Dictionary<object, ResourceIdentity> _imports = new(ReferenceEqualityComparer.Instance);
    private readonly List<PlanPass> _passes = [];
    private readonly List<PlanResource> _exports = [];
    private readonly List<PlanTextureExport> _textureExports = [];
    private readonly List<PlanResourceExport> _resourceExports = [];
    private bool _frozen;
    private int _resourceCount;
    private int _versionCount;
    private int _commandsReserved;
    private ulong _argumentBytesReserved;
    private int _referencesReserved;
    private int _usesReserved;

    internal GraphicsPlanBuilder(GraphicsFrameScope frame)
    {
        Frame = frame;
    }

    internal GraphicsFrameScope Frame { get; }

    internal int PassCount => _passes.Count;

    internal int ResourceCount => _resourceCount;

    /// <summary>Declares create buffer.</summary>
    /// <param name = "description">The description.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public PlanBuffer<T> CreateBuffer<T>(BufferDesc<T> description)
        where T : unmanaged
    {
        Mutable();
        var id = new ResourceIdentity(d => d.CreateBuffer(description))
        {
            Measure = d => d.GetBufferLayout<T>().GetSizeInBytes(description.Count),
        };
        return Add(new PlanBuffer<T>(this, id, 0, new(0, description.Count), []));
    }

    /// <summary>Declares create texture.</summary>
    /// <param name = "description">The description.</param>
    /// <returns>The declared result.</returns>
    public PlanTexture CreateTexture(TextureDesc description)
    {
        Mutable();
        if (description.Width == 0 || description.Height == 0 || description.ArrayLayers == 0 || description.MipLevels == 0 || description.MipLevels > System.Numerics.BitOperations.Log2(Math.Max(description.Width, description.Height)) + 1 || checked((ulong)description.MipLevels * description.ArrayLayers) > (ulong)Frame.Session.Budget.MaximumResourceUses)
        {
            throw new ArgumentOutOfRangeException(nameof(description), "Texture dimensions and subresource reservations must be finite and valid.");
        }

        var id = new ResourceIdentity(d => d.CreateTexture(description))
        {
            MipLevels = description.MipLevels,
            TextureFormat = description.Format,
            Measure = d => TextureBytes(d, description.Width, description.Height, description.MipLevels, description.ArrayLayers, description.Format),
        };
        return Add(new PlanTexture(this, id, 0, new(0, checked((ulong)description.MipLevels * description.ArrayLayers)), []));
    }

    /// <summary>Declares import buffer.</summary>
    /// <param name = "buffer">The buffer.</param>
    /// <param name = "useLease">The use lease.</param>
    /// <param name = "initialized">The initialized.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public PlanBuffer<T> ImportBuffer<T>(IGraphicsBuffer<T> buffer, IDisposable useLease, bool initialized = true)
        where T : unmanaged
    {
        Mutable();
        Frame.Own(useLease);
        if (!_imports.TryGetValue(buffer, out ResourceIdentity? id))
        {
            _imports.Add(buffer, id = new(_ => buffer, buffer) { Measure = _ => buffer.SizeInBytes });
        }

        if (id.Versions.Count != 0)
        {
            if (initialized != (id.Versions[0].Initialized.Count != 0))
            {
                throw new ArgumentException("Conflicting import initialization metadata.");
            }

            return id.Versions[0] as PlanBuffer<T> ?? throw new ArgumentException("Incompatible imported type.");
        }

        PlanRange range = new(0, buffer.Count);
        return Add(new PlanBuffer<T>(this, id, 0, range, initialized ? [range] : []));
    }

    /// <summary>Declares import texture.</summary>
    /// <param name = "texture">The texture.</param>
    /// <param name = "useLease">The use lease.</param>
    /// <param name = "initialized">The initialized.</param>
    /// <param name = "initialState">The initial state.</param>
    /// <returns>The declared result.</returns>
    public PlanTexture ImportTexture(IGraphicsTexture texture, IDisposable useLease, bool initialized = true, TextureState initialState = TextureState.Undefined)
    {
        if (initialized && initialState == TextureState.Undefined)
        {
            throw new ArgumentException("Initialized imported textures require an explicit preserved incoming state.");
        }

        Mutable();
        Frame.Own(useLease);
        if (!_imports.TryGetValue(texture, out ResourceIdentity? id))
        {
            _imports.Add(texture, id = new(_ => texture, texture) { MipLevels = texture.MipLevels, TextureFormat = texture.Format, InitialTextureState = initialState, Measure = d => TextureBytes(d, texture.Width, texture.Height, texture.MipLevels, texture.ArrayLayers, texture.Format) });
        }

        if (id.Versions.Count != 0)
        {
            if (initialized != (id.Versions[0].Initialized.Count != 0))
            {
                throw new ArgumentException("Conflicting import initialization metadata.");
            }

            if (initialState != id.InitialTextureState)
            {
                throw new ArgumentException("Conflicting imported texture state.");
            }

            return (PlanTexture)id.Versions[0];
        }

        PlanRange range = new(0, checked((ulong)texture.MipLevels * texture.ArrayLayers));
        return Add(new PlanTexture(this, id, 0, range, initialized ? [range] : []));
    }

    /// <summary>Creates a normalized view alias without changing allocation identity or content version.</summary>
    /// <param name = "source">The immutable texture version.</param>
    /// <param name = "description">The exact mip/layer selection.</param>
    /// <returns>An alias with the same producer and independently bounded range.</returns>
    public PlanTexture View(PlanTexture source, TextureViewDesc description)
    {
        Mutable();
        Validate(source, source.Range);
        uint mips = description.MipLevelCount ?? source.Identity.MipLevels - description.BaseMipLevel;
        uint totalLayers = checked((uint)(source.Identity.Versions[0].Range.Count / source.Identity.MipLevels));
        if (description.BaseArrayLayer >= totalLayers || !Enum.IsDefined(description.Dimension))
        {
            throw new ArgumentOutOfRangeException(nameof(description));
        }

        uint layers = description.ArrayLayerCount ?? (description.Dimension == TextureViewDimension.D2 ? 1u : description.Dimension == TextureViewDimension.Cube ? 6u : totalLayers - description.BaseArrayLayer);
        if (mips == 0 || layers == 0 || description.BaseMipLevel >= source.Identity.MipLevels || mips > source.Identity.MipLevels - description.BaseMipLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(description));
        }

        if (layers > 1 && (description.BaseMipLevel != 0 || mips != source.Identity.MipLevels))
        {
            throw new NotSupportedException("Non-contiguous mip/layer views require an explicit resource-range set; this backend only admits contiguous views.");
        }

        PlanRange range = new(checked(((ulong)description.BaseArrayLayer * source.Identity.MipLevels) + description.BaseMipLevel), checked((ulong)mips * layers));
        Validate(source, range);
        TextureViewDesc normalized = description with { MipLevelCount = mips, ArrayLayerCount = layers };
        PlanTexture? existing = source.Identity.Versions.OfType<PlanTexture>().FirstOrDefault(v => v.Version == source.Version && v.Range == range && v.NativeViewDescription == normalized);
        return existing ?? Add(new PlanTexture(this, source.Identity, source.Version, range, source.Initialized) { Producer = source.Producer, ViewDescription = normalized });
    }

    /// <summary>Normalizes a borrowed native view to its physical texture identity and exact view range.</summary>
    /// <param name = "view">The borrowed native view.</param>
    /// <param name = "useLease">The independent use authority.</param>
    /// <param name = "initialized">Whether imported contents are initialized.</param>
    /// <param name = "initialState">The explicit incoming layout.</param>
    /// <returns>The initial immutable imported content view.</returns>
    public PlanTexture ImportTextureView(IGraphicsTextureView view, IDisposable useLease, bool initialized, TextureState initialState)
    {
        PlanTexture source = ImportTexture(view.Texture, useLease, initialized, initialState);
        return View(source, new() { BaseMipLevel = view.Info.BaseMipLevel, MipLevelCount = view.Info.MipLevelCount, BaseArrayLayer = view.Info.BaseArrayLayer, ArrayLayerCount = view.Info.ArrayLayerCount, Dimension = view.Info.Dimension });
    }

    /// <summary>Declares add pass.</summary>
    /// <param name = "name">The name.</param>
    /// <returns>The declared result.</returns>
    public PlanPass AddPass(string name)
    {
        Mutable();
        if (_passes.Count >= Frame.Session.Budget.MaximumPasses)
        {
            throw new InvalidOperationException("Pass declaration budget exceeded.");
        }

        var pass = new PlanPass(this, name);
        _passes.Add(pass);
        return pass;
    }

    /// <summary>Declares export.</summary>
    /// <param name = "resource">The resource.</param>
    public void Export(PlanResource resource)
    {
        Mutable();
        Validate(resource, resource.Range);
        if (!_exports.Contains(resource))
        {
            _exports.Add(resource);
        }
    }

    /// <summary>Exports a buffer or shader-data version with an explicit final external access dependency.</summary>
    /// <param name="resource">The immutable buffer declaration.</param>
    /// <param name="finalScope">The receiving scope, including host read.</param>
    public void Export(PlanResource resource, BarrierScope finalScope)
    {
        Mutable();
        if (resource is PlanTexture)
        {
            throw new ArgumentException("Texture exports require an explicit final layout overload.");
        }

        Export(resource);
        int existing = _resourceExports.FindIndex(e => e.Resource == resource);
        if (existing >= 0)
        {
            BarrierScope previous = _resourceExports[existing].FinalScope;
            _resourceExports[existing] = new(resource, new(previous.Stages | finalScope.Stages, previous.Access | finalScope.Access));
        }
        else
        {
            if (_resourceExports.Count >= Frame.Session.Budget.MaximumResources)
            {
                throw new InvalidOperationException("Buffer export reservation exceeded.");
            }

            _resourceExports.Add(new(resource, finalScope));
        }
    }

    /// <summary>Exports an initialized image with an explicit final native layout.</summary>
    /// <param name="texture">The exact immutable output version and view.</param>
    /// <param name="finalState">The final interop state; undefined is forbidden.</param>
    /// <param name="finalScope">The explicit receiving access scope.</param>
    public void Export(PlanTexture texture, TextureState finalState, BarrierScope finalScope)
    {
        Mutable();
        Validate(texture, texture.Range);
        if (!Enum.IsDefined(finalState) || finalState == TextureState.Undefined)
        {
            throw new ArgumentOutOfRangeException(nameof(finalState));
        }

        if (finalState == TextureState.Present && !texture.Identity.Presentable)
        {
            throw new ArgumentException("Present requires an explicitly imported acquired surface image.");
        }

        if (_textureExports.Any(e => e.Texture.Identity == texture.Identity && e.Texture.Range.Overlaps(texture.Range) && e.FinalState != finalState))
        {
            throw new ArgumentException("Overlapping exports have conflicting final states.");
        }

        Export(texture);
        var observation = new PlanTextureExport(texture, finalState, finalScope);
        if (!_textureExports.Contains(observation))
        {
            if (_textureExports.Count >= Frame.Session.Budget.MaximumResources)
            {
                throw new InvalidOperationException("Texture export reservation exceeded.");
            }

            _textureExports.Add(observation);
        }
    }

    /// <summary>Imports an explicitly acquired surface image without owning its allocation.</summary>
    /// <param name="surfaceFrame">The caller-owned acquired presentation frame.</param>
    /// <param name="useLease">An independent use lease retained through GPU completion.</param>
    /// <param name="initialized">Whether the incoming contents are defined.</param>
    /// <param name="initialState">The explicit acquired layout.</param>
    /// <returns>The initial immutable presentation image declaration.</returns>
    public PlanTexture ImportSurfaceTexture(IGraphicsSurfaceFrame surfaceFrame, IDisposable useLease, bool initialized, TextureState initialState)
    {
        ArgumentNullException.ThrowIfNull(surfaceFrame);
        if (surfaceFrame.Status != SurfaceFrameStatus.Acquired)
        {
            throw new ArgumentException("The presentation image is not acquired.", nameof(surfaceFrame));
        }

        PlanTexture texture = ImportTexture(surfaceFrame.Texture, useLease, initialized, initialState);
        if (texture.Identity.SurfaceFrame is { } existing && !ReferenceEquals(existing, surfaceFrame))
        {
            throw new ArgumentException("One allocation cannot represent multiple acquired-image generations.");
        }

        texture.Identity.SurfaceFrame = surfaceFrame;
        texture.Identity.Presentable = true;
        return texture;
    }

    internal void ReserveResourceUse()
    {
        Mutable();
        if (_usesReserved >= Frame.Session.Budget.MaximumResourceUses)
        {
            throw new InvalidOperationException("Semantic resource-use reservation exceeded before declaration.");
        }

        _usesReserved++;
    }

    internal void ReserveCommand(ulong argumentBytes = 0, int references = 0)
    {
        Mutable();
        ulong nextBytes = checked(_argumentBytesReserved + argumentBytes);
        int nextReferences = checked(_referencesReserved + references);
        if (_commandsReserved >= Frame.Session.Budget.MaximumCommands || nextBytes > Frame.Session.Budget.MaximumArgumentBytes || nextReferences > Frame.Session.Budget.MaximumTableReferences || references < 0)
        {
            throw new InvalidOperationException("Command declaration reservation exceeded before argument capture.");
        }

        _commandsReserved++;
        _argumentBytesReserved = nextBytes;
        _referencesReserved = nextReferences;
    }

    internal bool IsInitialized(PlanResource resource, PlanRange range)
        => Covered(resource.Initialized, range) && (resource.Producer is null || !resource.Producer.DiscardedContents.Any(d => d.Resource.SameVersion(resource) && d.Range.Overlaps(range)));

    internal void Mutable(bool compiling = false)
    {
        Frame.ValidateDeclarations();
        Frame.Session.Validate();
        ObjectDisposedException.ThrowIf(Frame.IsDisposed, Frame);
        if (_frozen || (Frame.IsConsumed && !compiling))
        {
            throw new InvalidOperationException("The plan is frozen.");
        }
    }

    internal void Validate(PlanResource resource, PlanRange range)
    {
        if (resource.Owner != this || range.Count == 0 || !resource.Range.Contains(range))
        {
            throw new ArgumentException("Foreign resource or invalid range.");
        }
    }

    internal T Write<T>(PlanPass pass, T input, PlanRange range, PlanWriteMode mode, BarrierScope scope)
        where T : PlanResource
    {
        Mutable();
        Validate(input, range);
        if (input.Version != input.Identity.LatestVersion)
        {
            throw new InvalidOperationException("Writes must consume the latest immutable version; merge is not supported.");
        }

        if (mode is not (PlanWriteMode.Preserve or PlanWriteMode.Discard))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (_versionCount >= Frame.Session.Budget.MaximumResourceVersions)
        {
            throw new InvalidOperationException("Content version reservation exceeded before declaration.");
        }

        ReserveResourceUse();
        _versionCount++;
        if (mode == PlanWriteMode.Preserve)
        {
            pass.Read(input, input.Range, scope);
        }

        int version = ++input.Identity.LatestVersion;
        IReadOnlyList<PlanRange> valid = mode == PlanWriteMode.Preserve ? input.Initialized.Concat([range]).ToArray() : [range];
        PlanResource output = input.NextVersion(version, valid, mode == PlanWriteMode.Preserve);
        output.Producer = pass;
        input.Identity.Versions.Add(output);
        if (input.Identity.Imported is not null)
        {
            pass.IsRoot = true;
        }

        pass.UseList.Add(new(output, range, mode == PlanWriteMode.Preserve ? PlanAccess.ReadWrite : PlanAccess.Write, scope));
        return (T)output;
    }

    internal CompiledGraphicsPlan Compile()
    {
        Mutable(compiling: true);
        _frozen = true;
        var live = new HashSet<PlanPass>();
        void Visit(PlanPass p)
        {
            if (live.Add(p))
            {
                foreach (PlanPass d in p.Dependencies)
                {
                    Visit(d);
                }

                foreach (PlanResourceUse? u in p.UseList.Where(u => u.Access != PlanAccess.Write))
                {
                    if (u.Resource.Producer is { } producer && producer != p)
                    {
                        Visit(producer);
                    }
                }
            }
        }

        foreach (PlanPass? p in _passes.Where(p => p.IsRoot))
        {
            Visit(p);
        }

        foreach (PlanResource r in _exports)
        {
            if (r.Producer is { } p)
            {
                Visit(p);
            }

            if (!IsInitialized(r, r.Range))
            {
                throw new InvalidOperationException("Export reads undefined contents.");
            }
        }

        var edges = live.ToDictionary(p => p, p => new HashSet<PlanPass>(p.Dependencies));
        var barriers = new List<PlanBarrier>();
        foreach (PlanPass p in live)
        {
            p.ValidateInternalSynchronization();
            foreach (PlanResourceUse u in p.UseList)
            {
                if (u.Resource is PlanTexture && (u.TextureState is null || u.TextureState == TextureState.Undefined))
                {
                    throw new InvalidOperationException("Texture use requires an explicit non-undefined layout.");
                }

                if (u.Access != PlanAccess.Write && !IsInitialized(u.Resource, u.Range))
                {
                    throw new InvalidOperationException("Read of undefined contents.");
                }

                if (u.Access != PlanAccess.Write && u.Resource.Producer is { } producer && producer != p)
                {
                    edges[p].Add(producer);
                    foreach (PlanResourceUse? produced in producer.UseList.Where(v => v.Resource.Identity == u.Resource.Identity && v.Resource.Version == u.Resource.Version && v.Access != PlanAccess.Read && v.Range.Overlaps(u.Range)))
                    {
                        barriers.Add(new(producer, p, produced, u));
                    }
                }

                foreach (PlanPass? q in live.Where(q => q != p))
                {
                    foreach (PlanResourceUse v in q.UseList)
                    {
                        if (u.Resource.Identity != v.Resource.Identity || !u.Range.Overlaps(v.Range) || (u.Access == PlanAccess.Read && v.Access == PlanAccess.Read))
                        {
                            continue;
                        }

                        if (u.Resource.Version < v.Resource.Version)
                        {
                            edges[q].Add(p);
                            barriers.Add(new(p, q, u, v));
                        }
                    }
                }
            }

            if (p.Commands.Count == 0)
            {
                throw new InvalidOperationException("A live pass must contain structured commands.");
            }
        }

        foreach (PlanResource exported in _exports)
        {
            exported.ValidateExport(live);
            if (live.SelectMany(p => p.UseList).Any(u => u.Resource.Identity == exported.Identity && u.Resource.Version > exported.Version && u.Access != PlanAccess.Read))
            {
                throw new NotSupportedException("Exporting overwritten old contents requires an explicit preservation copy.");
            }
        }

        var ordered = new List<PlanPass>();
        while (ordered.Count != live.Count)
        {
            PlanPass? next = _passes.FirstOrDefault(p => live.Contains(p) && !ordered.Contains(p) && edges[p].All(ordered.Contains));
            if (next is null)
            {
                throw new InvalidOperationException("Dependency/version ordering cycle; old contents cannot be preserved in-place.");
            }

            ordered.Add(next);
        }

        return new(Frame, ordered.ToArray(), barriers.Distinct().ToArray(), _exports.ToArray(), _textureExports.ToArray(), _resourceExports.ToArray());
    }

    private static ulong TextureBytes(IGraphicDevice device, uint width, uint height, uint mips, uint layers, TextureFormat format)
    {
        ulong bytes = 0;
        uint texelBytes = format switch
        {
            TextureFormat.R8Unorm => 1,
            TextureFormat.Rg8Unorm or TextureFormat.R16Float => 2,
            TextureFormat.Rgba16Float or TextureFormat.Depth24Stencil8 => 8,
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb or TextureFormat.Rg16Float or TextureFormat.Rgb10A2Unorm or TextureFormat.Depth32Float => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        for (uint mip = 0; mip < mips; mip++)
        {
            bytes = checked(bytes + checked((ulong)Math.Max(1u, width >> (int)mip) * Math.Max(1u, height >> (int)mip) * layers * texelBytes));
        }

        return bytes;
    }

    private static bool Covered(IReadOnlyList<PlanRange> ranges, PlanRange range)
    {
        ulong cursor = range.Offset;
        foreach (PlanRange r in ranges.OrderBy(r => r.Offset))
        {
            if (r.Offset > cursor)
            {
                break;
            }

            cursor = Math.Max(cursor, r.End);
            if (cursor >= range.End)
            {
                return true;
            }
        }

        return false;
    }

    private T Add<T>(T resource)
        where T : PlanResource
    {
        if (_resourceCount >= Frame.Session.Budget.MaximumResources)
        {
            throw new InvalidOperationException("Resource declaration budget exceeded.");
        }

        _resourceCount = checked(_resourceCount + 1);
        resource.Identity.Versions.Add(resource);
        return resource;
    }
}
