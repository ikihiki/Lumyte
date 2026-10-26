namespace Lumyte.Audio.Execution;

/// <summary>Describes the lifecycle of an audio device service.</summary>
public enum AudioDeviceServiceStatus
{
    /// <summary>No creation has started.</summary>
    Uninitialized,

    /// <summary>Initial device creation is pending.</summary>
    Initializing,

    /// <summary>The current device supports resource construction, including suspended output.</summary>
    Ready,

    /// <summary>The current device is irreversibly unavailable.</summary>
    Lost,

    /// <summary>A replacement device is being created.</summary>
    Recovering,

    /// <summary>Creation failed and may be retried.</summary>
    Failed,

    /// <summary>Admission is closed while owned devices are being released.</summary>
    Stopping,

    /// <summary>All device generations have been released.</summary>
    Stopped,
}
