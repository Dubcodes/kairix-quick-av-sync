using System.Runtime.InteropServices;

namespace Kairix.QuickAVSync.Windows.Capture;

internal static class MfGuids
{
    public static readonly Guid DevSourceType = new("C60AC5FE-252A-478F-A0EF-BC8FA5F7CAD3");
    public static readonly Guid VideoCaptureSource = new("8AC3587A-4AE7-42D8-99E0-0A6013EEF90F");
    public static readonly Guid FriendlyName = new("60D0E559-52F8-4FA2-BBCE-ACDB34A8EC01");
    public static readonly Guid SymbolicLink = new("58F0AAD8-22BF-4F8A-BB3D-D2C4978C6E2F");
    public static readonly Guid MediaSource = new("279A808D-AEC7-40C8-9C6B-A6B492C78A66");
    public static readonly Guid FrameSize = new("1652C33D-D6B2-4012-B834-72030849A37D");
    public static readonly Guid FrameRate = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    public static readonly Guid Subtype = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    public static readonly Guid InterlaceMode = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    public static readonly Guid DefaultStride = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    public static readonly Guid DeviceTimestamp = new("8F3E35E7-2DCD-4887-8622-2A58BAA652B0");
    public static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    public static readonly Guid Yuy2 = new("32595559-0000-0010-8000-00AA00389B71");
    public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");
}

internal static class MediaFoundationNative
{
    internal const int SourceReaderFirstVideoStream = unchecked((int)0xFFFFFFFC);
    internal const int EndOfStream = 0x00000001;
    internal const int StreamTick = 0x00000100;
    internal const int MediaTypeChanged = 0x00000020;
    internal const int NoMoreTypes = unchecked((int)0xC00D36B9);
    [DllImport("ole32.dll")] internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] internal static extern void CoUninitialize();
    [DllImport("mfplat.dll")] internal static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] internal static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attributes, int initialSize);
    [DllImport("mf.dll")] internal static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr devices, out int count);
    [DllImport("mfreadwrite.dll")] internal static extern int MFCreateSourceReaderFromMediaSource([MarshalAs(UnmanagedType.IUnknown)] object source, IMFAttributes? attributes, out IMFSourceReader reader);
    internal static ComPtr<IMFAttributes> CreateAttributes(int count) { MFCreateAttributes(out var ptr, count).ThrowIfFailed(); return ComPtr<IMFAttributes>.FromOwned(ptr); }
}

internal sealed class ComPtr<T> : IDisposable where T : class
{
    private IntPtr _owned; public T Value { get; }
    private ComPtr(T value, IntPtr owned) { Value = value; _owned = owned; }
    public static ComPtr<T> FromOwned(IntPtr ptr) => new((T)Marshal.GetObjectForIUnknown(ptr), ptr);
    public void Dispose() { if (_owned == IntPtr.Zero) return; Marshal.ReleaseComObject(Value); Marshal.Release(_owned); _owned = IntPtr.Zero; }
}

[ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    int GetItem(ref Guid key, IntPtr value); int GetItemType(ref Guid key, out int type); int CompareItem(ref Guid key, IntPtr value, out int result); int Compare(IMFAttributes theirs, int matchType, out int result);
    int GetUINT32(ref Guid key, out int value); int GetUINT64(ref Guid key, out long value); int GetDouble(ref Guid key, out double value); int GetGUID(ref Guid key, out Guid value);
    int GetStringLength(ref Guid key, out int length); int GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int size, out int length); int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
    int GetBlobSize(ref Guid key, out int size); int GetBlob(ref Guid key, IntPtr buffer, int size, out int blobSize); int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size); int GetUnknown(ref Guid key, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    int SetItem(ref Guid key, IntPtr value); int DeleteItem(ref Guid key); int DeleteAllItems(); int SetUINT32(ref Guid key, int value); int SetUINT64(ref Guid key, long value); int SetDouble(ref Guid key, double value); int SetGUID(ref Guid key, ref Guid value); int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value); int SetBlob(ref Guid key, IntPtr buffer, int size); int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    int LockStore(); int UnlockStore(); int GetCount(out int count); int GetItemByIndex(int index, out Guid key, IntPtr value); int CopyAllItems(IMFAttributes destination);
}

[ComImport, Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate : IMFAttributes
{
    new int GetItem(ref Guid key, IntPtr value); new int GetItemType(ref Guid key, out int type); new int CompareItem(ref Guid key, IntPtr value, out int result); new int Compare(IMFAttributes theirs, int matchType, out int result);
    new int GetUINT32(ref Guid key, out int value); new int GetUINT64(ref Guid key, out long value); new int GetDouble(ref Guid key, out double value); new int GetGUID(ref Guid key, out Guid value); new int GetStringLength(ref Guid key, out int length); new int GetString(ref Guid key, System.Text.StringBuilder value, int size, out int length); new int GetAllocatedString(ref Guid key, out IntPtr value, out int length); new int GetBlobSize(ref Guid key, out int size); new int GetBlob(ref Guid key, IntPtr buffer, int size, out int blobSize); new int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size); new int GetUnknown(ref Guid key, ref Guid iid, out object value); new int SetItem(ref Guid key, IntPtr value); new int DeleteItem(ref Guid key); new int DeleteAllItems(); new int SetUINT32(ref Guid key, int value); new int SetUINT64(ref Guid key, long value); new int SetDouble(ref Guid key, double value); new int SetGUID(ref Guid key, ref Guid value); new int SetString(ref Guid key, string value); new int SetBlob(ref Guid key, IntPtr buffer, int size); new int SetUnknown(ref Guid key, object value); new int LockStore(); new int UnlockStore(); new int GetCount(out int count); new int GetItemByIndex(int index, out Guid key, IntPtr value); new int CopyAllItems(IMFAttributes destination);
    [PreserveSig] int ActivateObject(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value); [PreserveSig] int ShutdownObject(); [PreserveSig] int DetachObject();
}

[ComImport, Guid("45BC8A7B-AC88-46D8-9A1C-125B799B2A38"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType : IMFAttributes
{
    new int GetItem(ref Guid key, IntPtr value); new int GetItemType(ref Guid key, out int type); new int CompareItem(ref Guid key, IntPtr value, out int result); new int Compare(IMFAttributes theirs, int matchType, out int result); new int GetUINT32(ref Guid key, out int value); new int GetUINT64(ref Guid key, out long value); new int GetDouble(ref Guid key, out double value); new int GetGUID(ref Guid key, out Guid value); new int GetStringLength(ref Guid key, out int length); new int GetString(ref Guid key, System.Text.StringBuilder value, int size, out int length); new int GetAllocatedString(ref Guid key, out IntPtr value, out int length); new int GetBlobSize(ref Guid key, out int size); new int GetBlob(ref Guid key, IntPtr buffer, int size, out int blobSize); new int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size); new int GetUnknown(ref Guid key, ref Guid iid, out object value); new int SetItem(ref Guid key, IntPtr value); new int DeleteItem(ref Guid key); new int DeleteAllItems(); new int SetUINT32(ref Guid key, int value); new int SetUINT64(ref Guid key, long value); new int SetDouble(ref Guid key, double value); new int SetGUID(ref Guid key, ref Guid value); new int SetString(ref Guid key, string value); new int SetBlob(ref Guid key, IntPtr buffer, int size); new int SetUnknown(ref Guid key, object value); new int LockStore(); new int UnlockStore(); new int GetCount(out int count); new int GetItemByIndex(int index, out Guid key, IntPtr value); new int CopyAllItems(IMFAttributes destination);
    int GetMajorType(out Guid guid); int IsCompressedFormat(out int compressed); int IsEqual(IMFMediaType type, out int flags); int GetRepresentation(Guid representation, out IntPtr value); int FreeRepresentation(Guid representation, IntPtr value);
}

[ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig] int GetStreamSelection(int streamIndex, out int selected); [PreserveSig] int SetStreamSelection(int streamIndex, int selected); [PreserveSig] int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType); [PreserveSig] int GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType); [PreserveSig] int SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType); [PreserveSig] int SetCurrentPosition(ref Guid positionFormat, IntPtr position); [PreserveSig] int ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags, out long timestamp, out IMFSample? sample); [PreserveSig] int Flush(int streamIndex); [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid service, ref Guid iid, out IntPtr value); [PreserveSig] int GetPresentationAttribute(int streamIndex, ref Guid attribute, IntPtr value);
}

[ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample : IMFAttributes
{
    new int GetItem(ref Guid key, IntPtr value); new int GetItemType(ref Guid key, out int type); new int CompareItem(ref Guid key, IntPtr value, out int result); new int Compare(IMFAttributes theirs, int matchType, out int result); new int GetUINT32(ref Guid key, out int value); new int GetUINT64(ref Guid key, out long value); new int GetDouble(ref Guid key, out double value); new int GetGUID(ref Guid key, out Guid value); new int GetStringLength(ref Guid key, out int length); new int GetString(ref Guid key, System.Text.StringBuilder value, int size, out int length); new int GetAllocatedString(ref Guid key, out IntPtr value, out int length); new int GetBlobSize(ref Guid key, out int size); new int GetBlob(ref Guid key, IntPtr buffer, int size, out int blobSize); new int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size); new int GetUnknown(ref Guid key, ref Guid iid, out object value); new int SetItem(ref Guid key, IntPtr value); new int DeleteItem(ref Guid key); new int DeleteAllItems(); new int SetUINT32(ref Guid key, int value); new int SetUINT64(ref Guid key, long value); new int SetDouble(ref Guid key, double value); new int SetGUID(ref Guid key, ref Guid value); new int SetString(ref Guid key, string value); new int SetBlob(ref Guid key, IntPtr buffer, int size); new int SetUnknown(ref Guid key, object value); new int LockStore(); new int UnlockStore(); new int GetCount(out int count); new int GetItemByIndex(int index, out Guid key, IntPtr value); new int CopyAllItems(IMFAttributes destination);
    [PreserveSig] int GetSampleFlags(out int flags); [PreserveSig] int SetSampleTime(long time); [PreserveSig] int GetSampleTime(out long time); [PreserveSig] int SetSampleDuration(long duration); [PreserveSig] int GetSampleDuration(out long duration); [PreserveSig] int GetBufferCount(out int count); [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer); [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer); [PreserveSig] int AddBuffer(IMFMediaBuffer buffer); [PreserveSig] int RemoveBufferByIndex(int index); [PreserveSig] int RemoveAllBuffers(); [PreserveSig] int GetTotalLength(out int length); [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer { [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength); [PreserveSig] int Unlock(); [PreserveSig] int GetCurrentLength(out int currentLength); [PreserveSig] int SetCurrentLength(int currentLength); [PreserveSig] int GetMaxLength(out int maxLength); }

internal static class MfAttributeExtensions
{
    internal static void SetGuid(this IMFAttributes attributes, Guid key, Guid value) => attributes.SetGUID(ref key, ref value).ThrowIfFailed();
    internal static string GetAllocatedString(this IMFAttributes attributes, Guid key) { attributes.GetAllocatedString(ref key, out var ptr, out _).ThrowIfFailed(); try { return Marshal.PtrToStringUni(ptr) ?? ""; } finally { Marshal.FreeCoTaskMem(ptr); } }
    internal static string? TryGetAllocatedString(this IMFAttributes attributes, Guid key) { try { var hr = attributes.GetAllocatedString(ref key, out var ptr, out _); if (hr < 0) return null; try { return Marshal.PtrToStringUni(ptr); } finally { Marshal.FreeCoTaskMem(ptr); } } catch (COMException) { return null; } }
    internal static long? TryGetUInt64(this IMFAttributes attributes, Guid key) { try { return attributes.GetUINT64(ref key, out var value) >= 0 ? value : null; } catch (COMException) { return null; } }
    internal static int? TryGetUInt32(this IMFAttributes attributes, Guid key) { try { return attributes.GetUINT32(ref key, out var value) >= 0 ? value : null; } catch (COMException) { return null; } }
    internal static Guid? TryGetGuid(this IMFAttributes attributes, Guid key) { try { return attributes.GetGUID(ref key, out var value) >= 0 ? value : null; } catch (COMException) { return null; } }
}
