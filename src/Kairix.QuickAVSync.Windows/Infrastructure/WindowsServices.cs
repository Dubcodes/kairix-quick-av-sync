using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Infrastructure;

public sealed class SettingsService(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kairix", "QuickAVSync", "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public AppSettings Load() { try { return File.Exists(Path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options) ?? new() : new(); } catch { return new(); } }
    public void Save(AppSettings value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var clean = new AppSettings { LastDeviceId = value.LastDeviceId, LastDeviceName = value.LastDeviceName, AutoDetect = value.AutoDetect, AutoSpike = value.AutoSpike, AutoVisual = value.AutoVisual, VisualSensitivity = Math.Clamp(value.VisualSensitivity, 0, 100), RollingBufferSeconds = Math.Clamp(value.RollingBufferSeconds, 1, 30), WorkWindowMilliseconds = Math.Clamp(value.WorkWindowMilliseconds, 50, 2000), NativeFormatByDevice = new(value.NativeFormatByDevice ?? new(), StringComparer.Ordinal), ReconstructFieldsByDevice = new(value.ReconstructFieldsByDevice ?? new(), StringComparer.Ordinal), ReconstructionFieldOrderByDevice = new(value.ReconstructionFieldOrderByDevice ?? new(), StringComparer.Ordinal) };
        File.WriteAllText(Path, JsonSerializer.Serialize(clean, Options));
    }
}

public sealed record MemoryStatus(ulong TotalBytes, ulong AvailableBytes, long ProcessBytes)
{
    public ulong SystemUsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public ulong ProcessUsedBytes => (ulong)Math.Clamp(ProcessBytes, 0, (long)Math.Min(SystemUsedBytes, long.MaxValue));
    public ulong OtherSystemUsedBytes => SystemUsedBytes >= ProcessUsedBytes ? SystemUsedBytes - ProcessUsedBytes : 0;
}

public sealed class MemoryStatusService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx { public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>(); public uint Load; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx status);
    public MemoryStatus Get()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return new(0, 0, Process.GetCurrentProcess().WorkingSet64);
        return new(status.TotalPhys, status.AvailPhys, Process.GetCurrentProcess().WorkingSet64);
    }
}

public sealed class AppLogger : IDiagnosticSink
{
    private readonly string _path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kairix", "QuickAVSync", "logs", "current.log");
    private readonly object _gate = new();
    public string Path => _path;
    public void Write(string message) => Write("app", message);
    public void Write(string category, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > 1_000_000) File.Move(_path, System.IO.Path.ChangeExtension(_path, ".previous.log"), true);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
