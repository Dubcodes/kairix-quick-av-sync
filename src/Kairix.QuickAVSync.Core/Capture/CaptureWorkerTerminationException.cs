namespace Kairix.QuickAVSync.Capture;

/// <summary>
/// A capture worker did not acknowledge cancellation. The session must retain
/// its native ownership and be quarantined rather than disposed again.
/// </summary>
public sealed class CaptureWorkerTerminationException(string message) : TimeoutException(message);
