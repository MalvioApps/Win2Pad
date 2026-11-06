using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Win2Pad.Core.Typing;

/// <summary>One key press as the tablet receives it: a position and the modifiers held for it.</summary>
public readonly record struct KeyStroke(byte Usage, KeyModifiers Modifiers);

/// <summary>Where a character is on the keyboard layout, if it is there at all.</summary>
public interface IKeyLayout
{
    bool TryGetKey(char c, out KeyStroke stroke);
}

/// <summary>
/// Turns text into the key presses that type it. A Bluetooth keyboard sends positions, and the
/// tablet applies its own layout, so the positions must come from the same layout the tablet
/// uses: the one the user has in Windows, which is also the one set on the iPad.
/// </summary>
public static class TextTyper
{
    /// <summary>Spacing accents that dead keys put on the next letter, by their combining mark.</summary>
    private static readonly Dictionary<char, char> DeadKeyAccents = new()
    {
        ['́'] = '´', // acute: á
        ['̀'] = '`', // grave: à
        ['̂'] = '^', // circumflex: â
        ['̈'] = '¨', // diaeresis: ü
        ['̃'] = '~', // tilde: ã
        ['̊'] = '°', // ring: å
        ['̧'] = '¸', // cedilla: ç where it has no key of its own
    };

    /// <summary>
    /// The strokes for <paramref name="text"/>. Line breaks become Enter (a Windows CR LF is one
    /// Enter), tabs Tab. A letter the layout only reaches through a dead key (á on Spanish) is
    /// typed as the accent key and then the letter. Characters the layout cannot type at all,
    /// such as emoji, are left out and counted.
    /// </summary>
    public static (IReadOnlyList<KeyStroke> Strokes, int Skipped) ToStrokes(string text, IKeyLayout layout)
    {
        var strokes = new List<KeyStroke>(text.Length);
        var skipped = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') continue; // CR LF: the LF types it
                c = '\n';
            }
            if (c == '\n')
            {
                strokes.Add(new KeyStroke(0x28, KeyModifiers.None));
                continue;
            }
            if (c == '\t')
            {
                strokes.Add(new KeyStroke(0x2B, KeyModifiers.None));
                continue;
            }

            // Outside the basic plane (emoji and the like): no keyboard types these.
            if (char.IsSurrogate(c))
            {
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                skipped++;
                continue;
            }

            if (layout.TryGetKey(c, out var stroke))
            {
                strokes.Add(stroke);
                continue;
            }

            if (TryDeadKey(c, layout, out var accent, out var letter))
            {
                strokes.Add(accent);
                strokes.Add(letter);
                continue;
            }

            skipped++;
        }
        return (strokes, skipped);
    }

    private static bool TryDeadKey(char c, IKeyLayout layout, out KeyStroke accent, out KeyStroke letter)
    {
        accent = letter = default;
        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        if (decomposed.Length != 2) return false;
        return DeadKeyAccents.TryGetValue(decomposed[1], out var spacing)
               && layout.TryGetKey(spacing, out accent)
               && layout.TryGetKey(decomposed[0], out letter);
    }
}

/// <summary>The keyboard layout Windows uses for the window in front, which the user also set on the tablet.</summary>
public sealed class WindowsKeyLayout : IKeyLayout
{
    private readonly IntPtr _layout;

    public WindowsKeyLayout(IntPtr layout) => _layout = layout;

    /// <summary>The layout of the window in front, or the system's when there is none.</summary>
    public static WindowsKeyLayout Current()
    {
        var window = GetForegroundWindow();
        var thread = window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out _);
        return new WindowsKeyLayout(GetKeyboardLayout(thread));
    }

    public bool TryGetKey(char c, out KeyStroke stroke)
    {
        stroke = default;
        var result = VkKeyScanExW(c, _layout);
        if (result == -1) return false;

        var virtualKey = result & 0xFF;
        var shift = (result >> 8) & 0xFF;
        // Bit 1 Shift, bit 2 Ctrl, bit 4 Alt; Ctrl and Alt together are AltGr.
        if ((shift & 0x02) != 0 && (shift & 0x04) == 0) return false; // Ctrl alone: not a character key
        var modifiers = KeyModifiers.None;
        if ((shift & 0x01) != 0) modifiers |= KeyModifiers.LeftShift;
        if ((shift & 0x06) == 0x06) modifiers |= KeyModifiers.RightAlt;

        var scan = MapVirtualKeyExW((uint)virtualKey, MAPVK_VK_TO_VSC_EX, _layout);
        var extended = (scan & 0xE000) == 0xE000;
        if (!VirtualKeyMap.TryGetUsage(virtualKey, scan & 0xFF, extended, out var usage)) return false;
        stroke = new KeyStroke(usage, modifiers);
        return true;
    }

    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [DllImport("user32.dll")]
    private static extern short VkKeyScanExW(char ch, IntPtr layout);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}

/// <summary>The Windows clipboard, as far as pasting text on the tablet needs it.</summary>
public static class WindowsClipboard
{
    private const uint CF_UNICODETEXT = 13;

    /// <summary>Changes on every copy anywhere on the PC; cheap, so safe to read from the input hook.</summary>
    public static uint SequenceNumber => GetClipboardSequenceNumber();

    /// <summary>Whether there is text to paste; cheap, does not open the clipboard.</summary>
    public static bool HasText => IsClipboardFormatAvailable(CF_UNICODETEXT);

    /// <summary>
    /// The clipboard text, or null. Opening the clipboard can wait on whichever program holds it,
    /// so this must never run on the input hook thread.
    /// </summary>
    public static string? ReadText()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    var handle = GetClipboardData(CF_UNICODETEXT);
                    if (handle == IntPtr.Zero) return null;
                    var pointer = GlobalLock(handle);
                    if (pointer == IntPtr.Zero) return null;
                    try
                    {
                        return Marshal.PtrToStringUni(pointer);
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
        return null;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);
}
