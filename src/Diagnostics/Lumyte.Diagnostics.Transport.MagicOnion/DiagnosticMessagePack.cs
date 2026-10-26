using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Shared generated serializer settings for hub server and clients.</summary>
public static class DiagnosticMessagePack
{
    /// <summary>Gets the shared immutable direct-write serializer options.</summary>
    public static MessagePackSerializerOptions Options { get; } = MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create([new DiagnosticMessageFormatter()], [DiagnosticMessagePackResolver.Instance, StandardResolver.Instance]))
        .WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(32));

    /// <summary>Gets direct publication encoding and decoding with bounded depth.</summary>
    public static MessagePackMagicOnionSerializerProvider Provider { get; } = MessagePackMagicOnionSerializerProvider.Default.WithOptions(Options);
}
