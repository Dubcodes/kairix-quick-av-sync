using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Kairix.QuickAVSync.Capture;

public enum CaptureRetirementDisposition { Disposed, Quarantined }

public sealed record CaptureRetirementResult(
    CaptureRetirementDisposition Disposition,
    string DeviceKey,
    string DeviceName,
    string Reason,
    TaskStatus DisposalTaskStatus);

public sealed record CaptureQuarantineSnapshot(
    string DeviceKey,
    string DeviceName,
    string Reason,
    TaskStatus DisposalTaskStatus);

/// <summary>
/// Gives each capture session exactly one retirement operation. A session whose
/// disposal cannot complete safely is retained here so later reconnects never
/// repeat Flush/Stop or release native objects still owned by a live worker.
/// </summary>
public sealed class CaptureSessionRetirementCoordinator
{
    private readonly ConditionalWeakTable<ICaptureSession, RetirementEntry> _retirements = new();
    private readonly ConcurrentDictionary<ICaptureSession, CaptureRetirementResult> _quarantined
        = new(ReferenceEqualityComparer.Instance);
    private int _stopAttempts;

    public int StopAttempts => Volatile.Read(ref _stopAttempts);

    public Task<CaptureRetirementResult> RetireAsync(
        ICaptureSession session,
        string deviceKey,
        string deviceName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var retirement = _retirements.GetValue(session, key => new(() => RetireCoreAsync(key, deviceKey, deviceName, timeout)));
        return retirement.Operation.Value.WaitAsync(cancellationToken);
    }

    public bool IsDeviceQuarantined(string deviceKey) => Snapshots.Any(snapshot => string.Equals(snapshot.DeviceKey, deviceKey, StringComparison.Ordinal));

    public IReadOnlyList<CaptureQuarantineSnapshot> Snapshots => _quarantined.Values
        .Select(result => new CaptureQuarantineSnapshot(result.DeviceKey, result.DeviceName, result.Reason, result.DisposalTaskStatus))
        .ToArray();

    private async Task<CaptureRetirementResult> RetireCoreAsync(ICaptureSession session, string deviceKey, string deviceName, TimeSpan timeout)
    {
        Interlocked.Increment(ref _stopAttempts);
        Task disposal;
        try { disposal = session.DisposeAsync().AsTask(); }
        catch (Exception ex) { return Quarantined(session, deviceKey, deviceName, ex.Message, TaskStatus.Faulted); }

        var completed = await Task.WhenAny(disposal, Task.Delay(timeout)).ConfigureAwait(false);
        if (!ReferenceEquals(completed, disposal))
        {
            _ = disposal.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return Quarantined(session, deviceKey, deviceName, $"Capture disposal exceeded the bounded {timeout.TotalSeconds:0.###} second retirement window.", disposal.Status);
        }

        try
        {
            await disposal.ConfigureAwait(false);
            return new(CaptureRetirementDisposition.Disposed, deviceKey, deviceName, "Capture session stopped cleanly.", disposal.Status);
        }
        catch (CaptureWorkerTerminationException ex) { return Quarantined(session, deviceKey, deviceName, ex.Message, disposal.Status); }
        catch (Exception ex) { return Quarantined(session, deviceKey, deviceName, $"Capture disposal failed and the session was retained: {ex.Message}", disposal.Status); }
    }

    private CaptureRetirementResult Quarantined(ICaptureSession session, string deviceKey, string deviceName, string reason, TaskStatus status)
    {
        var result = new CaptureRetirementResult(CaptureRetirementDisposition.Quarantined, deviceKey, deviceName, reason, status);
        _quarantined.TryAdd(session, result);
        return result;
    }

    private sealed class RetirementEntry(Func<Task<CaptureRetirementResult>> operation)
    {
        public Lazy<Task<CaptureRetirementResult>> Operation { get; } = new(operation, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
