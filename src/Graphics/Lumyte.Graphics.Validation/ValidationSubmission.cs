using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationSubmission : ValidationResource<IGraphicsSubmission>, IGraphicsSubmission
{
    private readonly ValidationCommandBuffer[] _commands;
    private bool _completed;
    private Task? _completion;

    internal ValidationSubmission(ValidationDevice device, IGraphicsSubmission raw, ValidationCommandBuffer[] commands)
        : base(device, raw, device)
    {
        _commands = commands;
    }

    public SubmissionStatus Status
    {
        get
        {
            if (IsDisposed)
            {
                return SubmissionStatus.Disposed;
            }

            ValidateAlive();
            SubmissionStatus status = Raw.Status;
            Observe(status);
            return status;
        }
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        cancellationToken.ThrowIfCancellationRequested();
        _completion ??= WaitNativeAsync();
        await _completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override void DisposeNative()
    {
        if (Status is not (SubmissionStatus.Completed or SubmissionStatus.Failed))
        {
            throw new InvalidOperationException("Validation: observe actual GPU completion before disposal.");
        }

        Raw.Dispose();
    }

    private void Observe(SubmissionStatus status)
    {
        if (_completed || status is not (SubmissionStatus.Completed or SubmissionStatus.Failed))
        {
            return;
        }

        _completed = true;
        foreach (ValidationCommandBuffer command in _commands)
        {
            command.OnCompleted(status == SubmissionStatus.Completed);
        }

        Device.Tracker.CompleteSemaphores(this);
    }

    private async Task WaitNativeAsync()
    {
        try
        {
            await Raw.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Observe(Raw.Status);
        }
    }
}
