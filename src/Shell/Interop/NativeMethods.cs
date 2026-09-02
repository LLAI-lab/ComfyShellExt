using System;
using System.Runtime.InteropServices;

namespace ComfyShellExt.Shell.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapStruct
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public short bmPlanes;
        public short bmBitsPixel;
        public IntPtr bmBits;
    }

    internal static class NativeMethods
    {
        public const int BiRgb = 0;
        public const int DibRgbColors = 0;

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, int usage,
            out IntPtr ppvBits, IntPtr hSection, int offset);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, int start, int lines,
            [Out] byte[] bits, ref BitmapInfoHeader bmi, int usage);

        [DllImport("gdi32.dll")]
        public static extern int GetObject(IntPtr handle, int count, out BitmapStruct bitmap);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr handle);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("ole32.dll")]
        public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context,
            ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int AssocQueryString(int flags, int str, string assoc, string extra,
            [Out] System.Text.StringBuilder outBuffer, ref int outBufferSize);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes,
            bool create, IntPtr template, out System.Runtime.InteropServices.ComTypes.IStream stream);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
            ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

        public const int ClsctxInprocServer = 1;
        public const int ClsctxLocalServer = 4;
        public const int AssocfNoTruncate = 0x00000020;
        public const int AssocstrShellExtension = 16;
    }
}
