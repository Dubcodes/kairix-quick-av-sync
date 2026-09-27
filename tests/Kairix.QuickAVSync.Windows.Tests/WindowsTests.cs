using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Windows.Infrastructure;
using Kairix.QuickAVSync.Windows.Capture;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class SettingsTests
{
    [Fact] public void RoundTripsOnlyAllowedConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { var service = new SettingsService(path); service.Save(new() { LastDeviceId = "device", AutoDetect = false, RollingBufferSeconds = 12 }); var loaded = service.Load(); Assert.Equal("device", loaded.LastDeviceId); Assert.False(loaded.AutoDetect); Assert.Equal(12, loaded.RollingBufferSeconds); var json = File.ReadAllText(path); Assert.DoesNotContain("waveform", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed class WindowsEnumerationTests
{
    [Fact] public void AudioEndpointEnumerationIsDeterministicAndUnique()
    {
        var endpoints = new WindowsAudioEndpointService().Enumerate();
        Assert.Equal(endpoints.Count, endpoints.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    [Fact] public async Task MediaFoundationEnumerationReturnsUniqueDescriptors()
    {
        var devices = await new WindowsCaptureBackend().EnumerateDevicesAsync(default);
        Assert.Equal(devices.Count, devices.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
