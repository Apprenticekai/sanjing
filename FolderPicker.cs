using System.Runtime.InteropServices;

namespace MultiFrameViewer;

internal static class FolderPicker
{
    public static string? Pick(IntPtr owner)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
            dialog.SetTitle("选择图片总文件夹，子文件夹会自动加入播放");
            var result = dialog.Show(owner);
            if (result != 0)
            {
                return null;
            }
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out var pointer);
                try
                {
                    return Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog;

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem shellItem);
        void SetFolder(IShellItem shellItem);
        void GetFolder(out IShellItem shellItem);
        void GetCurrentSelection(out IShellItem shellItem);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem shellItem);
        void AddPlace(IShellItem shellItem, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);
        void Close(int hresult);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr shellItem);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdnName, out IntPtr name);
        void GetAttributes(uint attributesMask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
