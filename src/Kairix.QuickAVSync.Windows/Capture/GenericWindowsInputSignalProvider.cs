using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

// Standard Media Foundation/DirectShow formats describe the stream delivered to
// the application. Windows exposes no universal connector-input status contract.
// This provider is intentionally optional/unavailable until a device publishes
// documented standard source metadata beyond its capture-output media types.
public sealed class GenericWindowsInputSignalProvider : IInputSignalProvider
{
    public string Name => "Windows standard metadata";
    public bool CanHandle(CaptureDeviceDescriptor device) => device.BackendId == "windows-mf";
    public Task<InputSignalInfo?> GetSignalAsync(CaptureDeviceDescriptor device, CancellationToken cancellationToken) => Task.FromResult<InputSignalInfo?>(null);
}
