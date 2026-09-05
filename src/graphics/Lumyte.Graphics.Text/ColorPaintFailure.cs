using System.Runtime.ExceptionServices;

namespace Lumyte.Graphics.Text;

/// <summary>Classifies failures raised while copying an untrusted COLRv1 paint graph.</summary>
internal static class ColorPaintFailure
{
    internal static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException
            or ObjectDisposedException
            or StackOverflowException
            or AccessViolationException;

    internal static bool IsRecoverable(Exception exception)
        => !IsFatal(exception)
            && exception is ArgumentException
                or ArithmeticException
                or InvalidOperationException
                or NotSupportedException;

    internal static void RethrowIfFatal(Exception exception)
    {
        if (IsFatal(exception))
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
