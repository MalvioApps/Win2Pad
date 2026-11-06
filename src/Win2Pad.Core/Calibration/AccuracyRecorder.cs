using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Win2Pad.Core.Edge;

namespace Win2Pad.Core.Calibration;

/// <summary>
/// Records, side by side, where the pointer model believes the tablet pointer is and where the
/// Safari page sees it really is, while the mouse is used normally. Writes a CSV with two kinds
/// of rows, in time order: R for each report applied to the model, P for each real position.
/// </summary>
public sealed class AccuracyRecorder : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<string> _rows = new();
    private readonly StreamWriter _writer;
    private readonly Timer _flush;
    private readonly Func<PageInfo?> _page;
    private readonly Func<PointerCalibration> _calibration;
    private readonly object _gate = new();
    private (double X, double Y)? _real;
    private bool _disposed;

    public AccuracyRecorder(string path, Func<PageInfo?> page, Func<PointerCalibration> calibration)
    {
        Path = path;
        _page = page;
        _calibration = calibration;
        _writer = new StreamWriter(path) { AutoFlush = false };
        _writer.WriteLine("ms,kind,dx,dy,est_x,est_y,speed,ratio,overshoot,returned,real_x,real_y,error_x,error_y");
        _flush = new Timer(_ => Flush(), null, 500, 500);
    }

    public string Path { get; }

    /// <summary>The last real position, in the model's counts; null before the page saw the pointer.</summary>
    public (double X, double Y)? Real
    {
        get { lock (_gate) return _real; }
    }

    public long Samples { get; private set; }

    public void OnReport(TrackedReport r) =>
        _rows.Enqueue(Row("R", r.Dx, r.Dy, r.X, r.Y, r.Speed, r.Ratio, r.Overshoot, r.Returned ? 1 : 0, null, null, null, null));

    /// <summary>A real position from the page, compared with the estimate of that moment.</summary>
    public void OnSample(PointerSample sample, LiveEstimate? estimate)
    {
        if (_page() is not { } page) return;
        var real = ToCounts(sample, page, _calibration());
        lock (_gate) _real = real;
        Samples++;
        double? ex = null, ey = null;
        if (estimate is { IsTracking: true } e)
        {
            ex = e.X - real.X;
            ey = e.Y - real.Y;
        }
        _rows.Enqueue(Row("P", null, null, estimate?.X, estimate?.Y, null, null, null, null, real.X, real.Y, ex, ey));
    }

    /// <summary>
    /// A page position in the model's units: points on the tablet screen (the viewport sits below
    /// Safari's bars, TopBand points down) scaled to the calibrated size in counts.
    /// </summary>
    public static (double X, double Y) ToCounts(PointerSample sample, PageInfo page, PointerCalibration calibration) =>
        (sample.X / page.Width * calibration.Width,
         (sample.Y + page.TopBand) / page.Height * calibration.Height);

    private string Row(string kind, int? dx, int? dy, double? x, double? y, double? speed, double? ratio,
        double? overshoot, int? returned, double? realX, double? realY, double? errorX, double? errorY)
    {
        static string F(double? v) => v is { } d ? d.ToString("0.#", CultureInfo.InvariantCulture) : "";
        return string.Join(",", _clock.ElapsedMilliseconds, kind, dx?.ToString(CultureInfo.InvariantCulture) ?? "",
            dy?.ToString(CultureInfo.InvariantCulture) ?? "", F(x), F(y), F(speed), ratio is { } r ? r.ToString("0.###", CultureInfo.InvariantCulture) : "",
            F(overshoot), returned?.ToString(CultureInfo.InvariantCulture) ?? "", F(realX), F(realY), F(errorX), F(errorY));
    }

    private void Flush()
    {
        lock (_gate)
        {
            if (_disposed) return;
            while (_rows.TryDequeue(out var row)) _writer.WriteLine(row);
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        _flush.Dispose();
        Flush();
        lock (_gate)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }
}
