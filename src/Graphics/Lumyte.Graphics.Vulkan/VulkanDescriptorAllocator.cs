using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// Native pool access is externally synchronized here, independently of table mutation.
internal sealed unsafe class VulkanDescriptorAllocator(VulkanDevice owner) : IDisposable
{
    private const uint SetsPerPage = 32;
    private readonly object _gate = new();
    private readonly List<Page> _pages = [];
    private bool _disposed;

    internal long AllocatedSets { get; private set; }

    internal long FreedSets { get; private set; }

    internal long CreatedPools { get; private set; }

    internal int LiveSets { get; private set; }

    internal int PoolCount => _pages.Count;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (Page page in _pages)
            {
                owner.Api.DestroyDescriptorPool(owner.NativeDevice, page.Native, null);
            }

            _pages.Clear();
            LiveSets = 0;
        }
    }

    internal Lease Allocate(DescriptorSetLayout layout, uint textures, uint samplers)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (Page page in _pages)
            {
                if (!page.Exhausted && page.Live < SetsPerPage && page.Textures == textures && page.Samplers == samplers)
                {
                    Lease? lease = TryAllocate(page, layout);
                    if (lease != null)
                    {
                        return lease;
                    }
                }
            }

            DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
            sizes[0] = new() { Type = DescriptorType.SampledImage, DescriptorCount = checked(textures * SetsPerPage) };
            sizes[1] = new() { Type = DescriptorType.Sampler, DescriptorCount = checked(samplers * SetsPerPage) };
            var info = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit, MaxSets = SetsPerPage, PoolSizeCount = 2, PPoolSizes = sizes };
            owner.CheckResult(owner.Api.CreateDescriptorPool(owner.NativeDevice, &info, null, out DescriptorPool pool), "CreateDescriptorPool");
            var created = new Page(pool, textures, samplers);
            _pages.Add(created);
            CreatedPools++;
            try
            {
                return TryAllocate(created, layout) ?? throw new InvalidOperationException("A fresh descriptor pool cannot satisfy its declared capacity.");
            }
            catch
            {
                owner.Api.DestroyDescriptorPool(owner.NativeDevice, pool, null);
                _pages.Remove(created);
                throw;
            }
        }
    }

    private Lease? TryAllocate(Page page, DescriptorSetLayout layout)
    {
        var info = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = page.Native, DescriptorSetCount = 1, PSetLayouts = &layout };
        Result result = owner.Api.AllocateDescriptorSets(owner.NativeDevice, &info, out DescriptorSet set);
        if (result is Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool)
        {
            page.Exhausted = true;
            return null;
        }

        owner.CheckResult(result, "AllocateDescriptorSets");
        page.Live++;
        LiveSets++;
        AllocatedSets++;
        return new Lease(this, page, set);
    }

    private void Free(Page page, DescriptorSet set)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            owner.CheckResult(owner.Api.FreeDescriptorSets(owner.NativeDevice, page.Native, 1, &set), "FreeDescriptorSets");
            page.Live--;
            LiveSets--;
            FreedSets++;
            if (page.Live == 0)
            {
                // Do not retain idle pages of arbitrary descriptor shapes.
                owner.Api.DestroyDescriptorPool(owner.NativeDevice, page.Native, null);
                _pages.Remove(page);
            }
        }
    }

    internal sealed class Page(DescriptorPool native, uint textures, uint samplers)
    {
        internal DescriptorPool Native { get; } = native;

        internal uint Textures { get; } = textures;

        internal uint Samplers { get; } = samplers;

        internal uint Live { get; set; }

        internal bool Exhausted { get; set; }
    }

    internal sealed class Lease(VulkanDescriptorAllocator allocator, Page page, DescriptorSet native) : IDisposable
    {
        private bool _disposed;

        internal DescriptorSet Native { get; } = native;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                allocator.Free(page, Native);
            }
        }
    }
}
