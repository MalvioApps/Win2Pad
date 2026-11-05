namespace Win2Pad.Core;

/// <summary>
/// Names shared by the CLI and the desktop app. Only one process may own the radio, so each
/// front end has to be able to see and stop the other.
/// </summary>
public static class SingleInstance
{
    public const string MutexName = @"Local\Win2Pad.Peripheral";

    /// <summary>Manual reset: a stop request stays signalled until the owner has torn down.</summary>
    public const string StopEventName = @"Local\Win2Pad.Stop";
}
