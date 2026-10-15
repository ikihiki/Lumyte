namespace Lumyte.Memory;

/// <summary>Provides scope-controlled release after admitted operations finish.</summary>
internal interface IMemoryAllocation
{
    /// <summary>Releases this allocation and removes it from its scope.</summary>
    void Release();
}
