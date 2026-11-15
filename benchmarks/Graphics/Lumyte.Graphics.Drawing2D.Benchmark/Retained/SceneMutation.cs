namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>Bounded alternating scene edits, never repeated no-op assignments.</summary>
public enum SceneMutation
{
    /// <summary>Replace one immutable content placement.</summary>
    Content,

    /// <summary>Change one placement transform.</summary>
    Transform,

    /// <summary>Toggle one placement clip.</summary>
    Clip,

    /// <summary>Alternate insertion and removal of one extra placement.</summary>
    InsertRemove,

    /// <summary>Move the first placement between the front and back.</summary>
    Reorder,
}
