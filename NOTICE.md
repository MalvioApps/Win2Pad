# Third-party notices

Win2Pad builds on two open-source projects. Thank you to their authors.

## windows-ble-hid (BleHid)

<https://github.com/abhishek-raj/windows-ble-hid>, commit `b33a004`.

The Bluetooth HID peripheral, input capture, HID reports and descriptors, key mapping, lock-key
tracking, pointer pacing and the desktop app shell in `src/Win2Pad.Core`, `src/Win2Pad.App` and
`tests/Win2Pad.Core.Tests` started as a copy of BleHid, renamed and then modified. Changes made
here include key mapping by physical position (scan code) for non-US layouts, dropping the Left
Control that Windows synthesizes with AltGr, and the link-state tray badge.

```
MIT License

Copyright (c) 2026 Abhishek Raj

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## BlueShare

<https://github.com/giagar5/BlueShare>, commit `0a26a12`, licensed under the
[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

The edge-switching design (pinning the tablet pointer to a corner, mirroring its edge clamping,
calibrating the screen size in mouse counts and returning on overshoot) comes from BlueShare.
When that logic is ported, the ported files will say so in their headers and list their changes,
as section 4 of the Apache License requires.
