using Lumyte.Windowing;

namespace Lumyte.Diagnostics.Windowing;

/// <summary>Generated diagnostic operations over registered WindowSystem instances.</summary>
public sealed partial class WindowDiagnostics(WindowDiagnosticRegistry registry)
{
    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "list-systems", DisplayName = "List window systems")]
    private DiagnosticResult<WindowDiagnosticSystemList> ListSystems()
        => DiagnosticResult<WindowDiagnosticSystemList>.Success(new(registry.List()));

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "list-windows", DisplayName = "List managed windows")]
    private DiagnosticResult<WindowDiagnosticWindowList> ListWindows(string system)
        => FindSystem<WindowDiagnosticWindowList>(system, out WindowDiagnosticRegistry.Target? target)
            ?? DiagnosticResult<WindowDiagnosticWindowList>.Success(new(target!.List()));

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "read", DisplayName = "Read managed window")]
    private DiagnosticResult<WindowDiagnosticSnapshot> Read(string system, string window)
        => Find<WindowDiagnosticSnapshot>(system, window, out WindowDiagnosticRegistry.Target? target, out WindowNode? node)
            ?? DiagnosticResult<WindowDiagnosticSnapshot>.Success(target!.Snapshot(node!));

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "show", DisplayName = "Show managed window")]
    private DiagnosticResult<WindowDiagnosticRequest> Show(string system, string window)
        => Mutate(system, window, static node => node.Show());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "hide", DisplayName = "Hide managed window")]
    private DiagnosticResult<WindowDiagnosticRequest> Hide(string system, string window)
        => Mutate(system, window, static node => node.Hide());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "activate", DisplayName = "Activate managed window")]
    private DiagnosticResult<WindowDiagnosticRequest> Activate(string system, string window)
        => Mutate(system, window, static node => node.Activate());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "bring-to-front", DisplayName = "Bring managed window to front")]
    private DiagnosticResult<WindowDiagnosticRequest> BringToFront(string system, string window)
        => Mutate(system, window, static node => node.BringToFront());

    [DiagnosticOperation(DiagnosticPermission.Edit, Id = "close", DisplayName = "Close managed window")]
    private DiagnosticResult<WindowDiagnosticRequest> Close(string system, string window)
        => Mutate(system, window, static node => node.Close());

    private DiagnosticResult<WindowDiagnosticRequest> Mutate(string system, string window, Func<WindowNode, WindowNodeRequestId> action)
    {
        DiagnosticResult<WindowDiagnosticRequest>? missing = Find<WindowDiagnosticRequest>(system, window, out _, out WindowNode? node);
        if (missing is not null)
        {
            return missing;
        }

        try
        {
            WindowNodeRequestId request = action(node!);
            return DiagnosticResult<WindowDiagnosticRequest>.Success(new(system, window, checked((long)request.Value)));
        }
        catch (ArgumentException)
        {
            return DiagnosticResult<WindowDiagnosticRequest>.Reject("invalid-value", "The value is outside the window contract.");
        }
        catch (InvalidOperationException)
        {
            return DiagnosticResult<WindowDiagnosticRequest>.Reject("invalid-state", "The window rejected this operation in its current state.");
        }
        catch (KeyNotFoundException)
        {
            return DiagnosticResult<WindowDiagnosticRequest>.Reject("not-found", "The managed window no longer exists.");
        }
    }

    private DiagnosticResult<T>? FindSystem<T>(string id, out WindowDiagnosticRegistry.Target? target)
        where T : notnull
    {
        if (!registry.TryGet(id, out target))
        {
            return DiagnosticResult<T>.Reject("not-found", "The WindowSystem is not registered.");
        }

        return null;
    }

    private DiagnosticResult<T>? Find<T>(string system, string window, out WindowDiagnosticRegistry.Target? target, out WindowNode? node)
        where T : notnull
    {
        DiagnosticResult<T>? missing = FindSystem<T>(system, out target);
        if (missing is not null)
        {
            node = null;
            return missing;
        }

        if (!target!.TryGet(window, out node))
        {
            return DiagnosticResult<T>.Reject("not-found", "The managed window is not registered.");
        }

        return null;
    }
}
