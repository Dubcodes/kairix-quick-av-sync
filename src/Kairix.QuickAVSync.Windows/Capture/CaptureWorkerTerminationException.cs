namespace Kairix.QuickAVSync.Windows.Capture;

/// <summary>
/// A native worker did not acknowledge cancellation. Its COM owner deliberately
/// remains alive; callers must not reopen or release the session underneath it.
/// </summary>
public sealed class CaptureWorkerTerminationException(string message) : TimeoutException(message);
