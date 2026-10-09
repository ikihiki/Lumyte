using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using MagicOnion.Client;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

internal sealed class MagicOnionDiagnosticTransportFactory(Uri address, string token) : IDiagnosticTransportFactory
{
    public async ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello, CancellationToken cancellationToken)
    {
        var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { MaxReceiveMessageSize = 4 * 1024 * 1024, MaxSendMessageSize = 4 * 1024 * 1024 });
        var receiver = new Receiver();
        IDiagnosticHub? hub = null;
        try
        {
            hub = await StreamingHubClient.ConnectAsync<IDiagnosticHub, IDiagnosticReceiver>(
                channel,
                receiver,
                option: new CallOptions(headers: new Metadata { { "authorization", "Bearer " + token } }),
                serializerProvider: DiagnosticMessagePack.Provider,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            WireWelcome welcome = await hub.JoinAsync(WireMapper.ToWire(hello)).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Connection(channel, hub, receiver, WireMapper.FromWire(welcome));
        }
        catch (Exception exception)
        {
            if (hub != null)
            {
                await hub.DisposeAsync().ConfigureAwait(false);
            }

            channel.Dispose();
            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new DiagnosticTransportException("MagicOnion session establishment failed.", exception);
        }
    }

    private sealed class Receiver : IDiagnosticReceiver
    {
        public Channel<DiagnosticCommand> Commands { get; } = Channel.CreateBounded<DiagnosticCommand>(256);

        public void OnCommand(WireCommand command)
        {
            try
            {
                if (!Commands.Writer.TryWrite(WireMapper.FromWire(command)))
                {
                    Commands.Writer.TryComplete(new DiagnosticTransportException("Command receive queue is full."));
                }
            }
            catch (Exception exception)
            {
                Commands.Writer.TryComplete(new DiagnosticTransportException("Malformed server command.", exception));
            }
        }
    }

    private sealed class Connection(GrpcChannel channel, IDiagnosticHub hub, Receiver receiver, SessionWelcome welcome) : IDiagnosticConnection
    {
        private int _reader;
        private int _disposed;

        public SessionWelcome Welcome => welcome;

        public async IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _reader, 1) != 0)
            {
                throw new InvalidOperationException("Only one command reader is supported.");
            }

            _ = ObserveDisconnectAsync();
            await foreach (DiagnosticCommand command in receiver.Commands.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return command;
            }
        }

        public async ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message, CancellationToken cancellationToken)
        {
            try
            {
                return WireMapper.FromWire(await hub.PublishAsync(WireMapper.ToWire(message)).WaitAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (RpcException exception)
            {
                throw new DiagnosticTransportException("MagicOnion publication failed.", exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await hub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                receiver.Commands.Writer.TryComplete();
                channel.Dispose();
            }
        }

        private async Task ObserveDisconnectAsync()
        {
            try
            {
                await hub.WaitForDisconnect().ConfigureAwait(false);
                receiver.Commands.Writer.TryComplete(new DiagnosticTransportException("MagicOnion connection closed."));
            }
            catch (Exception exception)
            {
                receiver.Commands.Writer.TryComplete(new DiagnosticTransportException("MagicOnion connection failed.", exception));
            }
        }
    }
}
