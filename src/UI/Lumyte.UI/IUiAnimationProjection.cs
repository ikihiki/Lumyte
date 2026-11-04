namespace Lumyte.UI;

/// <summary>Stages and publishes one typed projection without exposing its mutable display signal.</summary>
internal interface IUiAnimationProjection
{
    /// <summary>Gets the shared lifetime and evaluation producer.</summary>
    UiAnimationProducer Producer { get; }

    /// <summary>Evaluates a pending value before any projection is published.</summary>
    void Stage();

    /// <summary>Publishes an already staged display value.</summary>
    void Publish();
}
