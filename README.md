# Win2Pad

**Use your Windows keyboard and mouse on your iPad.** Push the pointer past the edge of your
screen and it carries on onto the tablet, like Universal Control does between a Mac and an iPad.

Win2Pad is software only. Your PC's own Bluetooth presents itself to the iPad as a standard
Bluetooth keyboard and mouse, so there is nothing to install on the iPad and no extra hardware.

> Status: early development. The Bluetooth keyboard and mouse work; edge switching and automatic
> calibration are in progress.

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

## Roadmap

| Phase | Result |
|---|---|
| 1. Bluetooth and keyboard | Control the iPad with a hotkey; tray icon shows the link state |
| 2. Edge switching | Move across any edge of any monitor and back |
| 3. Automatic calibration | One-click calibration through a page in Safari, per device and orientation |
| 4. Interface | Device list, visual screen arrangement editor, floating status indicator |
| 5. Distribution | Installer, automatic updates, start with Windows |

## Building

```
dotnet test Win2Pad.sln
dotnet run --project src/Win2Pad.App
```

Needs the .NET 9 SDK (see `global.json`) and the .NET 8 runtime.

## Credits

Win2Pad started from [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid) by
Abhishek Raj (MIT) and takes its edge-switching design from
[BlueShare](https://github.com/giagar5/BlueShare) (Apache-2.0). See [NOTICE.md](NOTICE.md).

iPad and iPadOS are trademarks of Apple Inc. Win2Pad is not affiliated with Apple or Microsoft.

## License

[MIT](LICENSE)
