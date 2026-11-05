namespace Win2Pad.Core;

/// <summary>What the user needs to know at a glance: is the tablet there, and which screen has the input.</summary>
public enum LinkState
{
    /// <summary>The Bluetooth peripheral is not running.</summary>
    Off,

    /// <summary>Advertising, but no host has subscribed to the keyboard yet (not paired, or out of range).</summary>
    Waiting,

    /// <summary>A host is connected and input stays on this PC.</summary>
    Local,

    /// <summary>Input is being sent to the host.</summary>
    Remote
}

public static class LinkStates
{
    /// <summary>
    /// Derives the state from the peripheral's own facts. A host counts as connected only once it
    /// subscribes to the keyboard report: an LE link without a subscription cannot receive input.
    /// </summary>
    public static LinkState From(bool isRunning, int keyboardSubscribers, bool isRedirecting) =>
        !isRunning ? LinkState.Off
        : keyboardSubscribers <= 0 ? LinkState.Waiting
        : isRedirecting ? LinkState.Remote
        : LinkState.Local;

    public static string Describe(LinkState state, string target) => state switch
    {
        LinkState.Off => "Not running",
        LinkState.Waiting => "Waiting for the tablet",
        LinkState.Local => "Connected, using this PC",
        LinkState.Remote => $"Controlling {target}",
        _ => state.ToString()
    };
}
