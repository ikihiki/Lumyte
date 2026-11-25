namespace Lumyte.Graphics.Pipelines;

/// <summary>Declares whether a transfer edge carries reusable source data or invocation-derived data.</summary>
public enum UploadRetention
{
    /// <summary>Allows exact immutable source chunks to be retained when the compiled host enables retention.</summary>
    Source,

    /// <summary>Always allocates invocation-owned storage without entering the immutable source cache.</summary>
    Fresh,
}
