# Win2Pad

**Use your Windows keyboard and mouse on your iPad.** Push the pointer past the edge of your
screen and it carries on onto the tablet, like Universal Control does between a Mac and an iPad.

Win2Pad is software only. Your PC's own Bluetooth presents itself to the iPad as a standard
Bluetooth keyboard and mouse, so there is nothing to install on the iPad and no extra hardware.

> Status: early development (0.1). Everything below works; expect rough edges.

## Install

Download `Win2Pad-win-Setup.exe` from the [latest release](https://github.com/MalvioApps/Win2Pad/releases/latest)
and run it. Win2Pad installs for your user only (no administrator rights), adds itself to the
Start menu and updates itself from GitHub releases. Windows may warn that the installer is not
signed; choose *More info → Run anyway*.

Then follow the checklist on the Status page: start Win2Pad, pair the iPad from its
Settings → Bluetooth, and push the pointer against the screen edge.

## Requirements

- Windows 10 2004 or later, or Windows 11.
- A Bluetooth adapter that supports the LE peripheral role. Most built-in adapters from the last
  few years do.
- iPadOS 13.4 or later (pointer support). Android tablets should work too but are not tested yet.

## How it works

1. Win2Pad advertises a Bluetooth LE HID keyboard and mouse (HID over GATT) using the Windows
   in-box stack.
2. You pair the iPad with it once, from the iPad's Bluetooth settings.
3. Win2Pad captures the Windows keyboard and mouse with low-level hooks and, while the tablet is
   active, sends the input as HID reports instead of letting Windows handle it.
4. Keys are sent by physical position, so the iPad applies its own keyboard layout. Set the
   hardware keyboard layout on the iPad to match your keyboard.
5. Push the pointer against the screen edge on the iPad's side to carry on onto it, and past the
   iPad's edge facing the PC to come back at the same height.

## Calibration

A Bluetooth mouse only sends relative movement, so Win2Pad estimates where the iPad pointer is.
For that it needs the iPad's screen size in mouse counts:

- **Automatic.** When the iPad connects, Win2Pad reads its model over Bluetooth (for example
  `iPad7,12`), looks up its screen size and calibrates from it. Nothing to do.
- **Fine-tune with Safari (optional).** Settings → Fine-tune with Safari shows a QR code. Open
  the page on the iPad and Win2Pad moves the pointer by itself, measuring the screen and the
  pointer acceleration. Use it if you changed the iPad's pointer tracking speed, or for the most
  precise switching.

Each iPad is remembered with its calibration for both orientations.

## Features

- **Edge switching** across any side of any monitor, arranged visually like Windows' display
  settings, with the shared stretch of edge adjustable up to every screen on that side.
- **Automatic calibration** from the iPad model read over Bluetooth, with an optional Safari
  fine-tune that also measures the pointer acceleration.
- **Remembered devices** with their own calibration, orientation and pointer speed.
- **Status at a glance**: the tray icon and an optional floating label change colour with where
  the input is.
- **Keyboard layouts**: keys go by physical position, AltGr works.
- **Automatic updates** from GitHub releases; starts with Windows if you want.

## Releasing

Push a version tag; the `release` workflow tests, builds the Velopack installer and publishes
the GitHub release that installed copies update from:

```
git tag v0.1.0
git push origin v0.1.0
```

## Building

```
dotnet test Win2Pad.sln
dotnet run --project src/Win2Pad.App
```

Needs the .NET 9 SDK (see `global.json`) and the .NET 8 runtime. To build the installer
locally, install `vpk` (`dotnet tool install -g vpk --version 1.2.161`) and follow the steps in
`.github/workflows/release.yml`.

## Credits

Win2Pad started from [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid) by
Abhishek Raj (MIT) and takes its edge-switching design from
[BlueShare](https://github.com/giagar5/BlueShare) (Apache-2.0). See [NOTICE.md](NOTICE.md).

iPad and iPadOS are trademarks of Apple Inc. Win2Pad is not affiliated with Apple or Microsoft.

## License

[MIT](LICENSE)
