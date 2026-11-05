using System.Runtime.InteropServices;
using System.Windows;
using Win2Pad.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Win2Pad.App.Services;

/// <summary>
/// Notification area presence. WPF-UI 4.3 ships no tray control, so this wraps the WinForms one.
/// The icon carries a coloured badge for the link state, so the user can tell from the taskbar
/// whether the tablet is connected and which screen has the input.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Dictionary<LinkState, Drawing.Icon> _icons;
    private LinkState _state = LinkState.Off;
    private string _target = "nothing yet";
    private string? _lockWarning;

    public TrayIconService(Action open, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Win2Pad", null, (_, _) => open());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        using var baseIcon = LoadBaseIcon();
        _icons = Enum.GetValues<LinkState>().ToDictionary(s => s, s => WithBadge(baseIcon, BadgeColor(s)));

        _icon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _icons[_state],
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
        RefreshText();
    }

    public LinkState State => _state;

    public void ShowMessage(string text) =>
        _icon.ShowBalloonTip(3000, "Win2Pad", text, Forms.ToolTipIcon.Info);

    public void SetState(LinkState state)
    {
        if (_state == state) return;
        _state = state;
        _icon.Icon = _icons[state];
        RefreshText();
    }

    /// <summary>
    /// Keeps the notification area in step with where input is going and the state of the lock
    /// keys there. The physical keyboard lamp belongs to Windows and describes Windows, so the
    /// tray is the honest place to show a different machine's lock state.
    /// </summary>
    public void UpdateStatus(string target, string? lockWarning)
    {
        if (_target == target && _lockWarning == lockWarning) return;

        var previous = _lockWarning;
        _target = target;
        _lockWarning = lockWarning;
        RefreshText();

        // Only on a rising edge: re-announcing an unchanged warning on every switch would nag.
        if (lockWarning is not null && lockWarning != previous)
            _icon.ShowBalloonTip(3000, "Lock keys", $"{target}: {lockWarning}", Forms.ToolTipIcon.Warning);
    }

    private void RefreshText()
    {
        var text = $"Win2Pad - {LinkStates.Describe(_state, _target)}"
                   + (_lockWarning is null ? string.Empty : $"\n{_lockWarning}");
        // NotifyIcon.Text throws above 63 characters, and host names are user-controlled.
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    private static Drawing.Color BadgeColor(LinkState state) => state switch
    {
        LinkState.Off => Drawing.Color.FromArgb(0x8A, 0x8A, 0x8A),
        LinkState.Waiting => Drawing.Color.FromArgb(0xF2, 0xA9, 0x00),
        LinkState.Local => Drawing.Color.FromArgb(0x2F, 0x80, 0xED),
        LinkState.Remote => Drawing.Color.FromArgb(0x27, 0xAE, 0x60),
        _ => Drawing.Color.Gray
    };

    // SmallIconSize is the DPI-scaled tray metric, so this selects the frame Windows is about to
    // ask for instead of letting it rescale a mismatched one.
    private static Drawing.Icon LoadBaseIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/win2pad.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>Draws a status dot in the bottom-right corner, outlined so it reads on light and dark taskbars.</summary>
    private static Drawing.Icon WithBadge(Drawing.Icon source, Drawing.Color color)
    {
        using var bitmap = source.ToBitmap();
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var size = bitmap.Width * 0.5f;
            var rect = new Drawing.RectangleF(bitmap.Width - size, bitmap.Height - size, size - 0.5f, size - 0.5f);
            using var fill = new Drawing.SolidBrush(color);
            using var outline = new Drawing.Pen(Drawing.Color.White, Math.Max(1f, bitmap.Width / 16f));
            g.FillEllipse(fill, rect);
            g.DrawEllipse(outline, rect);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // FromHandle does not own the handle, so clone it into an icon that does.
            using var borrowed = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values) icon.Dispose();
    }
}
