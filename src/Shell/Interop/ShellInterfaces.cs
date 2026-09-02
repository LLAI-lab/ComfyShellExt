using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell.Interop
{
    public enum WtsAlphaType
    {
        Unknown = 0,
        Rgb = 1,
        Argb = 2
    }

    [ComImport, Guid("e357fccd-a995-4576-b01f-234630154e96")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        void GetThumbnail(uint cx, out IntPtr phbmp, out WtsAlphaType pdwAlpha);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithItem
    {
        void Initialize(IShellItem psi, uint grfMode);
    }

    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SizeI
    {
        public int cx;
        public int cy;
    }

    /// <summary>Flags for IShellItemImageFactory.GetImage.</summary>
    public enum Siigbf
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
        ScaleUp = 0x100
    }

    /// <summary>
    /// The shell's own thumbnail entry point. Asking through this goes down the exact path Explorer
    /// uses, which is how we can tell whether Explorer is really calling our handler.
    /// </summary>
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        void GetImage(SizeI size, Siigbf flags, out IntPtr phbm);
    }

    public static class ShellConstants
    {
        public const uint StgmRead = 0x00000000;
        public const uint SigdnFileSysPath = 0x80058000;
        public const int EFail = unchecked((int)0x80004005);
        public const int ENotImpl = unchecked((int)0x80004001);

        /// <summary>Thumbnail provider interface id, used as the ShellEx registry key name.</summary>
        public const string ThumbnailProviderIid = "{e357fccd-a995-4576-b01f-234630154e96}";

        /// <summary>Windows' own image handler: PhotoMetadataHandler.dll, Photo Thumbnail Provider.</summary>
        public static readonly Guid PhotoThumbnailProvider =
            new Guid("{C7657C4A-9F68-40fa-A4DF-96BC08EB3551}");

        /// <summary>Windows' own shell32 Property Thumbnail Handler, used for video and audio.</summary>
        public static readonly Guid PropertyThumbnailHandler =
            new Guid("{9DBD2C50-62AD-11D0-B806-00C04FD706EC}");
    }
}
