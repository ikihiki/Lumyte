namespace Lumyte.UI.Automation;

/// <summary>Provides locator operations for one borrowed host; simultaneous operations on one page are rejected.</summary>
public sealed class UiAutomationPage
{
    private readonly IUiAutomationHost _host;
    private readonly TimeSpan _timeout;
    private int _active;
    private Guid? _surfaceId;

    /// <summary>Initializes a new instance of the <see cref="UiAutomationPage"/> class.</summary>
    /// <param name="host">A borrowed host tied to one surface lifetime.</param>
    /// <param name="options">Optional finite positive operation timeout.</param>
    public UiAutomationPage(IUiAutomationHost host, UiAutomationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _timeout = options?.Timeout ?? TimeSpan.FromSeconds(5);
        ValidateTimeout(_timeout);
    }

    /// <summary>Creates an exact identity query, including hidden elements.</summary>
    /// <param name="testId">The nonempty identity.</param>
    /// <returns>A lazy locator.</returns>
    public UiLocator GetByTestId(string testId) => new(this, [UiQuery.ByTestId(testId)]);

    /// <summary>Creates an exact role and optional ordinal name query.</summary>
    /// <param name="role">A non-None semantic role.</param>
    /// <param name="name">An exact name or null.</param>
    /// <param name="includeHidden">Whether hidden elements are included.</param>
    /// <returns>A lazy locator.</returns>
    public UiLocator GetByRole(UiRole role, string? name = null, bool includeHidden = false) => new(this, [UiQuery.ByRole(role, name, includeHidden)]);

    /// <summary>Creates retrying assertions for a locator owned by this page.</summary>
    /// <param name="locator">A locator from this page.</param>
    /// <returns>The assertion entry point.</returns>
    public UiExpect Expect(UiLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (!ReferenceEquals(locator.Page, this))
        {
            throw new ArgumentException("The locator belongs to another page.", nameof(locator));
        }

        return new(locator);
    }

    /// <summary>Captures a detached snapshot without starting input.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The snapshot.</returns>
    public Task<UiAutomationSnapshot> SnapshotAsync(CancellationToken cancellationToken = default) => ExecuteAsync(run => run.SnapshotAsync(), null, cancellationToken);

    /// <summary>Captures a real PNG from a screenshot-capable host.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Caller-owned PNG bytes.</returns>
    public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not IUiAutomationScreenshotHost screenshot)
        {
            throw new NotSupportedException("This host does not provide GPU PNG readback.");
        }

        return ExecuteAsync(run => screenshot.CapturePngAsync(run.Token), null, cancellationToken);
    }

    internal async Task<T> ExecuteAsync<T>(Func<UiAutomationRun, ValueTask<T>> operation, UiWaitOptions? options, CancellationToken cancellationToken)
    {
        TimeSpan timeout = options?.Timeout ?? _timeout;
        ValidateTimeout(timeout);
        if (Interlocked.Exchange(ref _active, 1) != 0)
        {
            throw new InvalidOperationException("One page cannot run simultaneous operations.");
        }

        try
        {
            using var run = new UiAutomationRun(_host, timeout, cancellationToken, RememberSurface);
            try
            {
                run.Check();
                T result = await operation(run);
                run.Check();
                return result;
            }
            catch (OperationCanceledException failure) when (cancellationToken.IsCancellationRequested)
            {
                failure.Data["Lumyte.UI.Automation.Trace"] = run.CaptureTrace();
                throw;
            }
            catch (OperationCanceledException failure) when (run.Expired)
            {
                throw run.Failure(UiAutomationFailureKind.Timeout, "The automation deadline expired.", failure);
            }
            catch (UiAutomationException failure)
            {
                throw run.Failure(failure.Kind, failure.Message, failure.InnerException);
            }
            catch (Exception failure)
            {
                throw run.Failure(failure is ObjectDisposedException ? UiAutomationFailureKind.HostUnavailable : UiAutomationFailureKind.ActionFailed, "The host operation failed.", failure);
            }
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentException("Timeout must be finite, positive and supported by TimeProvider timers.", nameof(timeout));
        }
    }

    private void RememberSurface(UiAutomationSnapshot snapshot)
    {
        if (_surfaceId is { } id && id != snapshot.SurfaceId)
        {
            throw new ObjectDisposedException(nameof(IUiAutomationHost), "The host replaced its surface; create a new page.");
        }

        _surfaceId = snapshot.SurfaceId;
    }
}
