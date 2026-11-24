using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.ViewModels;

internal readonly record struct LipSyncAnalysisTicket(long RequestId, int CaptureGeneration, string DeviceId, AnalysisModeKind Mode);

internal sealed class LipSyncAnalysisOwnership
{
    private long _requestId;
    public LipSyncAnalysisTicket Begin(int captureGeneration, string deviceId, AnalysisModeKind mode) =>
        new(Interlocked.Increment(ref _requestId), captureGeneration, deviceId, mode);
    public void Supersede() => Interlocked.Increment(ref _requestId);
    public bool CanCommit(LipSyncAnalysisTicket ticket, int captureGeneration, string? deviceId, AnalysisModeKind mode, bool disposing) =>
        !disposing && ticket.RequestId == Volatile.Read(ref _requestId) && ticket.CaptureGeneration == captureGeneration
        && string.Equals(ticket.DeviceId, deviceId, StringComparison.Ordinal) && ticket.Mode == AnalysisModeKind.SpeechLipSync
        && mode == AnalysisModeKind.SpeechLipSync;
}
