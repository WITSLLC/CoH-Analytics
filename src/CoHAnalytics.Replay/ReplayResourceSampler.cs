using System.Diagnostics;

namespace CoHAnalytics.Replay;

public interface IReplayResourceMetricSource
{
    ReplayResourceSample Capture(TimeSpan intervalElapsed);
}

public sealed class ReplayResourceSampler : IReplayResourceMetricSource, IDisposable
{
    private readonly Process? _process;
    private readonly bool _handleCountSupported;
    private TimeSpan _lastProcessorTime;
    private bool _hasPriorSample;

    public ReplayResourceSampler()
    {
        try
        {
            _process = Process.GetCurrentProcess();
            _process.Refresh();
            _lastProcessorTime = _process.TotalProcessorTime;
            _hasPriorSample = true;
            _handleCountSupported = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
        }
        catch
        {
            _process = null;
            _handleCountSupported = false;
        }
    }

    public ReplayResourceSample Capture(TimeSpan intervalElapsed)
    {
        if (_process is null)
        {
            return UnsupportedSample();
        }

        try
        {
            _process.Refresh();
            var cpuPercent = CalculateCpuUtilization(intervalElapsed, _process.TotalProcessorTime);
            return new ReplayResourceSample(
                CpuUtilizationPercent: cpuPercent,
                WorkingSetBytes: _process.WorkingSet64,
                PrivateMemoryBytes: _process.PrivateMemorySize64,
                ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                TotalAllocatedBytes: GC.GetTotalAllocatedBytes(precise: false),
                Gen0CollectionCount: GC.CollectionCount(0),
                Gen1CollectionCount: GC.CollectionCount(1),
                Gen2CollectionCount: GC.CollectionCount(2),
                ThreadCount: _process.Threads.Count,
                HandleCount: TryReadHandleCount(),
                HandleCountSupported: _handleCountSupported);
        }
        catch
        {
            return UnsupportedSample();
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
    }

    private double? CalculateCpuUtilization(TimeSpan intervalElapsed, TimeSpan processorTime)
    {
        if (!_hasPriorSample || intervalElapsed <= TimeSpan.Zero)
        {
            _lastProcessorTime = processorTime;
            _hasPriorSample = true;
            return null;
        }

        var processorDelta = processorTime - _lastProcessorTime;
        _lastProcessorTime = processorTime;
        if (processorDelta < TimeSpan.Zero)
        {
            return null;
        }

        var utilization = processorDelta.TotalMilliseconds
            / (intervalElapsed.TotalMilliseconds * Environment.ProcessorCount)
            * 100.0;
        if (double.IsNaN(utilization) || double.IsInfinity(utilization))
        {
            return null;
        }

        return Math.Clamp(utilization, 0.0, 100.0 * Environment.ProcessorCount);
    }

    private int? TryReadHandleCount()
    {
        if (!_handleCountSupported || _process is null)
        {
            return null;
        }

        try
        {
            return _process.HandleCount;
        }
        catch
        {
            return null;
        }
    }

    private static ReplayResourceSample UnsupportedSample() =>
        new(
            CpuUtilizationPercent: null,
            WorkingSetBytes: null,
            PrivateMemoryBytes: null,
            ManagedHeapBytes: null,
            TotalAllocatedBytes: null,
            Gen0CollectionCount: null,
            Gen1CollectionCount: null,
            Gen2CollectionCount: null,
            ThreadCount: null,
            HandleCount: null,
            HandleCountSupported: false);
}

public sealed class FakeReplayResourceSampler : IReplayResourceMetricSource
{
    private readonly Queue<ReplayResourceSample> _samples = new();

    public void Enqueue(ReplayResourceSample sample) => _samples.Enqueue(sample);

    public ReplayResourceSample Capture(TimeSpan intervalElapsed) =>
        _samples.Count > 0
            ? _samples.Dequeue()
            : new ReplayResourceSample(
                12.5,
                1_024_000,
                900_000,
                512_000,
                2_048_000,
                1,
                0,
                0,
                4,
                120,
                true);
}
