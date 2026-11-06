# Roadmap

What comes next for Win2Pad, roughly in order. Plans change; the
[issues](https://github.com/MalvioApps/Win2Pad/issues) are the place to suggest or discuss.

## 0.3 — Two-way clipboard, several iPads

### Clipboard in both directions, text and images
Bluetooth input only goes from the PC to the iPad, so the way back needs another path: a
ready-made **iOS Shortcut** that talks to Win2Pad over your Wi-Fi.
- *Send to PC* copies the iPad clipboard to Windows; *Get from PC* does the opposite, images
  included.
- Win2Pad can run it for you: when you copy on the iPad with Ctrl+C or Ctrl+X, it also presses
  the key combination assigned to the Shortcut (iPadOS can bind one through Accessibility →
  Keyboards), so the PC receives the copy without an extra step.
- Only on your local network, protected by a key, and off unless you turn it on.

### Several iPads at once
Place two or more iPads in Screens, each against its own edge, each with its own calibration,
orientation and speed. Win2Pad already keeps a profile per iPad; this adds them to the
arrangement and switches between them by edge.

## Later

- **Android tablets.** They pair as a Bluetooth keyboard and mouse already; what is missing is
  calibration (Android accelerates differently) and testing.
- **More iPad keys**: screen brightness, the app switcher, and iPad actions on spare keys of
  your choice.
- **Signed installer**, so Windows stops showing the unknown-publisher warning. SignPath
  Foundation signs open-source projects for free; the alternative is a paid certificate.
- **Remember where the pointer was** when you left the iPad, and come back to it without the
  trip from the corner. Possible now that the acceleration is measured.
- **Leave the iPad by its side edges** onto the monitors next to the one it is placed against.
- **Windows on ARM** builds.
- **Spanish** interface (the documentation already has a Spanish version).
- **winget** package: `winget install Win2Pad`.

## Done

- **0.2** — Ctrl+V on the iPad types the PC clipboard; media keys (volume, playback, Home,
  Spotlight) work on the iPad.
- **0.1** — Edge switching across any side of any monitor with a visual arrangement; automatic
  calibration from the iPad model and a Safari fine-tune that measures the pointer
  acceleration; remembered devices; any keyboard layout; tray and floating indicator;
  installer and automatic updates.
