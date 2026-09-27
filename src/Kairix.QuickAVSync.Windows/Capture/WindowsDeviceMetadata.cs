using System.Runtime.InteropServices;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed class WindowsAudioEndpointService
{
    public IReadOnlyList<AudioEndpointDescriptor> Enumerate()
    {
        var results = new List<AudioEndpointDescriptor>(); IMMDevice? defaultDevice = null; IMMDeviceEnumerator? enumerator = null; IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
            _ = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Multimedia, out defaultDevice);
            var defaultId = defaultDevice is null ? null : GetId(defaultDevice);
            enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged, out collection).ThrowIfFailed();
            collection.GetCount(out var count).ThrowIfFailed();
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device).ThrowIfFailed();
                try
                {
                    var id = GetId(device); device.GetState(out var state).ThrowIfFailed(); device.OpenPropertyStore(0, out var store).ThrowIfFailed();
                    try
                    {
                        var name = store.GetString(PropertyKeys.DeviceFriendlyName) ?? id;
                        var container = store.GetGuid(PropertyKeys.DeviceContainerId)?.ToString("D");
                        results.Add(new(id, name, container, IsDefaultMicrophone: id == defaultId, IsActive: (state & DeviceState.Active) != 0));
                    }
                    finally { Marshal.ReleaseComObject(store); }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            if (defaultDevice is not null) Marshal.ReleaseComObject(defaultDevice);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
        return results;
    }
    private static string GetId(IMMDevice device) { device.GetId(out var ptr).ThrowIfFailed(); try { return Marshal.PtrToStringUni(ptr) ?? ""; } finally { Marshal.FreeCoTaskMem(ptr); } }
}

internal static class PropertyKeys
{
    public static readonly PropertyKey DeviceFriendlyName = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    public static readonly PropertyKey DeviceContainerId = new(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
}

[StructLayout(LayoutKind.Sequential)] internal readonly record struct PropertyKey(Guid FormatId, uint PropertyId);
[StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant
{
    [FieldOffset(0)] public ushort VariantType; [FieldOffset(8)] public IntPtr Pointer;
}
internal static class PropertyStoreExtensions
{
    public static string? GetString(this IPropertyStore store, PropertyKey key)
    {
        var hr = store.GetValue(ref key, out var value); if (hr < 0) return null;
        try { return value.VariantType == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; } finally { NativeMethods.PropVariantClear(ref value); }
    }
    public static Guid? GetGuid(this IPropertyStore store, PropertyKey key)
    {
        var hr = store.GetValue(ref key, out var value); if (hr < 0) return null;
        try { return value.VariantType == 72 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.Pointer) : null; } finally { NativeMethods.PropVariantClear(ref value); }
    }
}

[Flags] internal enum DeviceState : uint { Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8, All = 15 }
internal enum EDataFlow { Render, Capture, All }
internal enum ERole { Console, Multimedia, Communications }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, DeviceState mask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client); [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    [PreserveSig] int GetId(out IntPtr id); [PreserveSig] int GetState(out DeviceState state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore { [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key); [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value); [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value); [PreserveSig] int Commit(); }

internal static partial class NativeMethods
{
    [DllImport("ole32.dll")] internal static extern int PropVariantClear(ref PropVariant variant);
}

internal static class HResultExtensions { public static void ThrowIfFailed(this int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); } }
