using System.Collections.Concurrent;
using System.Diagnostics;
using Win2Pad.Core.Edge;

namespace Win2Pad.Core;

/// <summary>
/// Runs a capture session: local hooks in, paced HID reports out. Shared by the interactive
/// `capture` command, background mode, and the desktop UI.
/// </summary>
public static class CaptureSession
{
    internal enum Queued { Report, MouseReport, SwitchHost, GoLocal, EdgeEnter }

    internal readonly record struct InputReport(Queued Kind,
        KeyModifiers Modifiers = KeyModifiers.None, byte[]? Usages = null,
        MouseButtons Buttons = MouseButtons.None, int Dx = 0, int Dy = 0, int Wheel = 0,
        IReadOnlyList<PinMove>? Moves = null);

    internal sealed class ReportQueue
    {
        private readonly ConcurrentQueue<InputReport> _ordered = new();
        private readonly object _mouseLock = new();
        private InputReport? _pendingMouse;

        public bool HasMouseReport
        {
            get { lock (_mouseLock) return _pendingMouse.HasValue; }
        }

        public void EnqueueKeyboard(KeyModifiers modifiers, byte[] usages) =>
            _ordered.Enqueue(new(Queued.Report, modifiers, usages));

        public void EnqueueMouse(MouseButtons buttons, int dx, int dy, int wheel)
        {
            lock (_mouseLock)
            {
                var pending = _pendingMouse.GetValueOrDefault();
                _pendingMouse = new(Queued.MouseReport, Buttons: buttons,
                    Dx: pending.Dx + dx, Dy: pending.Dy + dy, Wheel: pending.Wheel + wheel);
            }
        }

        public void EnqueueTargetChange(Queued kind, IReadOnlyList<PinMove>? moves = null)
        {
            lock (_mouseLock)
            {
                if (_pendingMouse is { } mouse)
                {
                    _ordered.Enqueue(mouse);
                    _pendingMouse = null;
                }
                _ordered.Enqueue(new(kind, Moves: moves));
            }
        }

        public bool TryDequeue(out InputReport report) => _ordered.TryDequeue(out report);

        public bool TryTakeMouseReport(out InputReport report)
        {
            lock (_mouseLock)
            {
                if (!_ordered.IsEmpty)
                {
                    report = default;
                    return false;
                }

                report = _pendingMouse.GetValueOrDefault();
                var available = _pendingMouse.HasValue;
                _pendingMouse = null;
                return available;
            }
        }
    }

    /// <param name="mouseIntervalMs">
    /// Read per report rather than captured, so the UI can retune pacing without restarting.
    /// </param>
    /// <param name="stopEndsSession">
    /// Console mode ends on Ctrl+Alt+Q. Background mode has no console to return to, so the
    /// same hotkey drops to the local target instead and capture keeps running.
    /// </param>
    public static async Task<int> RunAsync(
        HidPeripheral peripheral,
        Action<string> log,
        bool verbose,
        Func<int> mouseIntervalMs,
        bool stopEndsSession,
        CancellationToken cancellationToken)
    {
        using var capture = new InputCapture { Verbose = verbose };
        var stopped = new TaskCompletionSource();
        using var registration = cancellationToken.Register(() => stopped.TrySetResult());

        // The pointer switch gesture is opt-in via a JSON file, and that file is watched so a
        // toggle from the UI or another CLI instance reaches this session without a restart.
        using var gestureWatcher = new SwitchGestureWatcher(AppPaths.SwitchGestureConfig);
        ApplyGesture(gestureWatcher.Current);
        gestureWatcher.Changed += ApplyGesture;

        void ApplyGesture(SwitchGestureOptions options)
        {
            capture.SetSwitchGesture(options);
            if (options.Warning is { } warning) log($"  [gesture] {warning}");
            log(options.Enabled
                ? $"  [gesture] shake to switch enabled ({options.RequiredReversals} reversals of {options.MinStrokePx}px within {options.WindowMs}ms)"
                : "  [gesture] shake to switch disabled");
        }

        // Edge switching: settings saved from the UI replace the switcher in place.
        var edgeClock = Stopwatch.StartNew();
        double Seconds() => edgeClock.Elapsed.TotalSeconds;
        var switcher = CreateSwitcher(EdgeOptions.Load());
        void OnEdgeSaved(EdgeOptions options)
        {
            var replacement = CreateSwitcher(options);
            replacement.OnTargetChanged(peripheral.IsLocalTarget);
            Volatile.Write(ref switcher, replacement);
        }
        EdgeOptions.Saved += OnEdgeSaved;

        EdgeSwitcher CreateSwitcher(EdgeOptions options)
        {
            log(!options.Enabled ? "  [edge] edge switching disabled"
                : !options.Calibration.IsValid ? "  [edge] edge switching needs a calibration"
                : $"  [edge] tablet on the {options.EffectiveSide.ToString().ToLowerInvariant()}{(options.Placement is null ? "" : " of the arranged monitor")}, calibration {options.CalibrationWidth}x{options.CalibrationHeight}");
            return new EdgeSwitcher(options, new Win32Screens(), Seconds);
        }

        // Keystrokes must all be delivered, but pointer motion is coalesced: the hook produces
        // far more events than the BLE link can carry. Target changes travel through the same
        // queue so they take effect only after the key-release report has gone to the old host.
        var reports = new ReportQueue();
        var sent = 0;

        // The radio interleaves connection events across every subscribed link, so a second host
        // starves the one we are notifying even when it receives nothing. Broadcast pays again on
        // top of that: measured, 2 hosts needed 40 ms rather than 20 ms.
        int PointerIntervalMs()
        {
            var hostIntervalMs = peripheral.MouseReportIntervalMs(mouseIntervalMs());
            var links = Math.Max(1, peripheral.SubscribedMouseClients);
            var broadcasting = peripheral.SelectedHostId is null && !peripheral.IsLocalTarget;
            return broadcasting ? hostIntervalMs * 2 * links : hostIntervalMs;
        }

        // Signal-driven rather than polled: Task.Delay has ~15 ms granularity on Windows,
        // which alone made pointer motion feel sluggish.
        var signal = new SemaphoreSlim(0, 1);
        void Wake()
        {
            try { signal.Release(); }
            catch (SemaphoreFullException) { }
        }

        // The tablet accelerates each report by its size, so the pointer model is fed the reports
        // exactly as sent, not the hook's individual moves. When one takes the pointer back out,
        // input returns to Windows at the matching point; nothing may stay pressed on the tablet.
        async Task TrackSentAsync(InputReport report)
        {
            if (peripheral.IsLocalTarget) return;
            if (Volatile.Read(ref switcher).OnRemoteMotion(report.Dx, report.Dy) is not { } back) return;

            capture.ReturnLocal(back.X, back.Y);
            if (report.Buttons != MouseButtons.None) await peripheral.SendMouseAsync(MouseButtons.None, 0, 0, 0);
            await peripheral.ReleaseKeysAsync();
            reports.EnqueueTargetChange(Queued.GoLocal);
            Wake();
        }

        using var pumpCancellation = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            var iterations = 0;
            long lastMouseSend = -1000;
            if (verbose) log("  [pump] started");
            while (!pumpCancellation.IsCancellationRequested)
            {
                iterations++;
                try
                {
                    await signal.WaitAsync(pumpCancellation.Token);
                }
                catch (OperationCanceledException) { break; }

                try
                {
                    while (reports.TryDequeue(out var key))
                    {
                        if (key.Kind == Queued.MouseReport)
                        {
                            await peripheral.SendMouseAsync(key.Buttons, key.Dx, key.Dy, key.Wheel);
                            lastMouseSend = clock.ElapsedMilliseconds;
                            Interlocked.Increment(ref sent);
                            await TrackSentAsync(key);
                            continue;
                        }

                        if (key.Kind == Queued.EdgeEnter)
                        {
                            await peripheral.RefreshHostNamesAsync();
                            if (!SelectEdgeHost(peripheral))
                            {
                                Volatile.Read(ref switcher).OnTargetChanged(local: true);
                                capture.SetPassThrough(true);
                                log("  [edge] no tablet connected - staying on this PC");
                                continue;
                            }

                            // Pin into a corner, then move along the edge, each followed by a pause
                            // so the tablet does not accelerate them. Motion the user makes meanwhile
                            // waits in the coalescing slot and follows these moves.
                            foreach (var move in key.Moves ?? [])
                            {
                                await peripheral.SendMouseAsync(MouseButtons.None, move.Dx, move.Dy, 0);
                                await Task.Delay(move.SettleMs, pumpCancellation.Token);
                            }
                            lastMouseSend = clock.ElapsedMilliseconds;
                            log($"  [edge] -> {peripheral.SelectedHostDisplay}");
                            continue;
                        }

                        if (key.Kind is Queued.SwitchHost or Queued.GoLocal)
                        {
                            // Read before the target moves: this is the host we are leaving, and
                            // a lock left on there is invisible once we stop sending to it.
                            var leftHost = peripheral.IsLocalTarget ? null : peripheral.SelectedHostDisplay;
                            var leftLocks = peripheral.TargetLockWarning;

                            await peripheral.RefreshHostNamesAsync();
                            var target = key.Kind == Queued.GoLocal
                                ? peripheral.SelectLocal()
                                : peripheral.SelectNextHost();
                            capture.SetPassThrough(peripheral.IsLocalTarget);

                            var locks = peripheral.TargetLockWarning;
                            var arriving = locks is null ? string.Empty : $" - {locks}";
                            log($"  [host] -> {target} (pointer interval {PointerIntervalMs()} ms){arriving}");
                            if (leftLocks is not null && leftHost is not null)
                                log($"  [host] left {leftHost} with {leftLocks}");
                            continue;
                        }

                        var started = clock.ElapsedMilliseconds;
                        await peripheral.SendKeyboardAsync(key.Modifiers, key.Usages!);
                        var elapsed = clock.ElapsedMilliseconds - started;
                        if (verbose && sent < 40) log($"  [pump] key notify #{sent} took {elapsed} ms");
                        Interlocked.Increment(ref sent);
                    }

                    if (!reports.HasMouseReport) continue;

                    // NotifyValueAsync returns on queueing, so overshoot is invisible here and
                    // shows up as pointer drift after the user stops moving.
                    var interval = PointerIntervalMs();

                    var sinceLast = clock.ElapsedMilliseconds - lastMouseSend;
                    if (sinceLast < interval)
                        await Task.Delay((int)(interval - sinceLast), pumpCancellation.Token);

                    if (!reports.TryTakeMouseReport(out var mouse)) continue;

                    {
                        var started = clock.ElapsedMilliseconds;
                        await peripheral.SendMouseAsync(mouse.Buttons, mouse.Dx, mouse.Dy, mouse.Wheel);
                        lastMouseSend = clock.ElapsedMilliseconds;
                        if (verbose && sent < 40) log($"  [pump] mouse notify #{sent} ({mouse.Dx},{mouse.Dy}) took {lastMouseSend - started} ms");
                        Interlocked.Increment(ref sent);
                        await TrackSentAsync(mouse);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    log($"  [pump] send error: {ex}");
                }
            }

            if (verbose) log($"  [pump] exited after {iterations} iterations, {clock.ElapsedMilliseconds} ms");
        });

        capture.Log += message => log(message);
        capture.KeyboardReport += (modifiers, usages) =>
        {
            reports.EnqueueKeyboard(modifiers, usages);
            Wake();
        };
        // The tablet's pointer speed setting: movement is scaled, keeping the fractions so slow
        // moves are not rounded away.
        double carryX = 0, carryY = 0;
        capture.MouseReport += (buttons, dx, dy, wheel) =>
        {
            var speed = Volatile.Read(ref switcher).Options.PointerSpeed;
            if (speed is > 0 and not 1.0 && (dx != 0 || dy != 0))
            {
                carryX += dx * speed;
                carryY += dy * speed;
                dx = (int)carryX;
                dy = (int)carryY;
                carryX -= dx;
                carryY -= dy;
            }
            reports.EnqueueMouse(buttons, dx, dy, wheel);
            Wake();
        };
        capture.LocalPointerMoved += (dx, dy, buttonsHeld) =>
        {
            var current = Volatile.Read(ref switcher);
            if (current.OnLocalMotion(dx, dy, buttonsHeld) is not { } entry) return;

            // Stop Windows from moving the pointer at once; the reports wait until the pump has
            // selected the tablet and sent the entry moves.
            capture.SetPassThrough(false);
            reports.EnqueueTargetChange(Queued.EdgeEnter, entry.Moves);
            Wake();
        };
        capture.SwitchHostRequested += () =>
        {
            reports.EnqueueTargetChange(Queued.SwitchHost);
            Wake();
        };
        capture.StopRequested += () =>
        {
            if (stopEndsSession)
            {
                stopped.TrySetResult();
                return;
            }

            // Local recovery must not wait behind a notification to a host that just vanished.
            // Keep GoLocal queued as well so a healthy host still receives key releases first.
            capture.SetPassThrough(true);
            reports.EnqueueTargetChange(Queued.GoLocal);
            Wake();
        };

        await peripheral.RefreshHostNamesAsync();
        capture.SetPassThrough(peripheral.IsLocalTarget);
        switcher.OnTargetChanged(peripheral.IsLocalTarget);

        // The UI can retarget mid-session, and the hook has to stop swallowing input when it does.
        void OnTargetChanged()
        {
            capture.SetPassThrough(peripheral.IsLocalTarget);
            Volatile.Read(ref switcher).OnTargetChanged(peripheral.IsLocalTarget);
        }
        peripheral.TargetChanged += OnTargetChanged;

        try
        {
            log($"  pointer report interval: {PointerIntervalMs()} ms");
            log($"  sending to: {peripheral.SelectedHostDisplay}");
            log("  Ctrl+Alt+CapsLock or Ctrl+D+C switches target.");
            log(stopEndsSession
                ? "  capturing - Ctrl+Alt+Q stops."
                : "  capturing - Ctrl+Alt+Q returns input to this PC.");

            capture.Start();
            await stopped.Task;
        }
        finally
        {
            capture.Stop();
            peripheral.TargetChanged -= OnTargetChanged;
            EdgeOptions.Saved -= OnEdgeSaved;
        }

        pumpCancellation.Cancel();
        try { await pump; } catch (OperationCanceledException) { }
        await peripheral.ReleaseKeysAsync();
        log($"  capture stopped. keyboard events={capture.KeyboardEvents}, mouse events={capture.MouseEvents}, reports sent={sent}");
        return sent;
    }

    /// <summary>Keeps the selected tablet if it is still connected, otherwise takes the first one.</summary>
    private static bool SelectEdgeHost(HidPeripheral peripheral)
    {
        var hosts = peripheral.Hosts();
        if (hosts.Count == 0) return false;
        if (!peripheral.IsLocalTarget && peripheral.SelectedHostId is { } id
            && hosts.Any(h => string.Equals(h.DeviceId, id, StringComparison.OrdinalIgnoreCase)))
            return true;
        return peripheral.SelectHost(0);
    }
}
