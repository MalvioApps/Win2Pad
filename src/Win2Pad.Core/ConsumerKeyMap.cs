namespace Win2Pad.Core;

/// <summary>
/// The keyboard's media and app keys, as Consumer Control usages the tablet understands: volume,
/// playback, and the browser Home and Search keys, which iPadOS takes as Home and Spotlight.
/// </summary>
public static class ConsumerKeyMap
{
    public const ushort Mute = 0xE2;
    public const ushort VolumeUp = 0xE9;
    public const ushort VolumeDown = 0xEA;
    public const ushort PlayPause = 0xCD;
    public const ushort NextTrack = 0xB5;
    public const ushort PreviousTrack = 0xB6;
    public const ushort Stop = 0xB7;
    public const ushort Home = 0x223;   // AC Home: the iPad's Home
    public const ushort Search = 0x221; // AC Search: Spotlight

    public static bool TryGetUsage(int virtualKey, out ushort usage)
    {
        usage = virtualKey switch
        {
            0xAD => Mute,          // VK_VOLUME_MUTE
            0xAE => VolumeDown,    // VK_VOLUME_DOWN
            0xAF => VolumeUp,      // VK_VOLUME_UP
            0xB0 => NextTrack,     // VK_MEDIA_NEXT_TRACK
            0xB1 => PreviousTrack, // VK_MEDIA_PREV_TRACK
            0xB2 => Stop,          // VK_MEDIA_STOP
            0xB3 => PlayPause,     // VK_MEDIA_PLAY_PAUSE
            0xAC => Home,          // VK_BROWSER_HOME
            0xAA => Search,        // VK_BROWSER_SEARCH
            _ => 0
        };
        return usage != 0;
    }
}
