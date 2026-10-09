using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Shared generated serializer settings for hub server and clients.</summary>
public static class DiagnosticMessagePack
{
    /// <summary>Gets the bounded-depth, generated wire serializer.</summary>
    public static MessagePackMagicOnionSerializerProvider Provider { get; } = MessagePackMagicOnionSerializerProvider.Default.WithOptions(
        MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(DiagnosticMessagePackResolver.Instance, StandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(32)));
}
