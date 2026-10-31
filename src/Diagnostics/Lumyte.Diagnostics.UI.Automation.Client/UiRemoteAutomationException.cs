namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>Reports remote failure or an uncertain outcome without automatically repeating input.</summary>
public sealed class UiRemoteAutomationException : Exception
{
    internal UiRemoteAutomationException(string code, Guid? jobId = null, bool inputMayHaveBeenSent = false, Exception? inner = null)
        : base("UI automation: " + code, inner)
    {
        Code = code;
        JobId = jobId;
        InputMayHaveBeenSent = inputMayHaveBeenSent;
    }

    /// <summary>Gets the remote failure or outcome-unknown code.</summary>
    public string Code { get; }

    /// <summary>Gets the known job identity, if admission was received.</summary>
    public Guid? JobId { get; }

    /// <summary>Gets a value indicating whether input may have been sent; false on a nonterminal observation is not an execution guarantee.</summary>
    public bool InputMayHaveBeenSent { get; }
}
