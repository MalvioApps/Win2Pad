# Roadmap

What comes next for Win2Pad, roughly in order. Plans change; the
[issues](https://github.com/MalvioApps/Win2Pad/issues) are the place to suggest or discuss.

## 0.3 — Several iPads

### Several iPads at once
Place two or more iPads in Screens, each against its own edge, each with its own calibration,
orientation and speed. Win2Pad already keeps a profile per iPad; this adds them to the
arrangement and switches between them by edge.

## Later

- **Shared clipboard.** A Bluetooth keyboard cannot carry the clipboard, and iPadOS only lets an
  app touch it while the app is in front. Typing the text out was too slow for long texts, and an
  iOS Shortcut over Wi-Fi worked but took minutes to set up and needed Full Keyboard Access. Both
  attempts are kept on the `experiment/clipboard` branch; a small iPad companion app is the
  next idea to try.
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

- **0.2** — Media keys (volume, playback, Home, Spotlight) work on the iPad.
- **0.1** — Edge switching across any side of any monitor with a visual arrangement; automatic
  calibration from the iPad model and a Safari fine-tune that measures the pointer
  acceleration; remembered devices; any keyboard layout; tray and floating indicator;
  installer and automatic updates.
