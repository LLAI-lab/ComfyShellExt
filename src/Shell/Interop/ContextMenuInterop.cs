using System;
using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell.Interop
{
    [ComImport, Guid("000214e8-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellExtInit
    {
        [PreserveSig]
        int Initialize(IntPtr pidlFolder, ComTypes.IDataObject pdtobj, IntPtr hkeyProgId);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu
    {
        /// <summary>Returns the number of menu items added, as a success HRESULT.</summary>
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(IntPtr pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    internal static class MenuNative
    {
        public const uint MfByPosition = 0x0400;
        public const uint MfString = 0x0000;
        public const uint MfSeparator = 0x0800;

        public const uint CmfDefaultOnly = 0x00000001;
        public const uint CmfVerbsOnly = 0x00000002;
        public const uint CmfNoVerbs = 0x00000008;

        public const uint GcsVerbA = 0x00000000;
        public const uint GcsHelpTextA = 0x00000001;
        public const uint GcsVerbW = 0x00000004;
        public const uint GcsHelpTextW = 0x00000005;

        public const int CfHdrop = 15;
        public const int DvaspectContent = 1;
        public const int TymedHglobal = 1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool InsertMenuW(IntPtr hMenu, uint position, uint flags,
            UIntPtr newItemId, string newItem);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint DragQueryFileW(IntPtr hDrop, uint file, StringBuilder buffer, uint bufferSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("ole32.dll")]
        public static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);

        /// <summary>
        /// Reads the verb out of a CMINVOKECOMMANDINFO. Layout is identical for the EX variant up to
        /// this point, and lpVerb sits one pointer past hwnd on both 32 and 64 bit.
        /// Explorer passes MAKEINTRESOURCE(offset), so the high word is zero and the low word is the
        /// command offset. Offset zero is a real command and must not be read as a null pointer.
        /// </summary>
        public static bool TryReadVerbId(IntPtr pici, out int id, out string verb)
        {
            id = -1;
            verb = null;
            if (pici == IntPtr.Zero) return false;
            var lpVerb = Marshal.ReadIntPtr(pici, 8 + IntPtr.Size);
            long value = lpVerb.ToInt64();
            if ((value >> 16) == 0)
            {
                id = (int)(value & 0xFFFF);
                return true;
            }
            try { verb = Marshal.PtrToStringAnsi(lpVerb); }
            catch { verb = null; }
            return verb != null;
        }

        public static IntPtr ReadOwnerWindow(IntPtr pici)
        {
            try { return pici == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(pici, 8); }
            catch { return IntPtr.Zero; }
        }

        public static void WriteString(IntPtr buffer, uint charCapacity, string value, bool unicode)
        {
            if (buffer == IntPtr.Zero || charCapacity == 0) return;
            var text = value ?? "";
            if (text.Length > charCapacity - 1) text = text.Substring(0, (int)charCapacity - 1);
            if (unicode)
            {
                var bytes = Encoding.Unicode.GetBytes(text + "\0");
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
            }
            else
            {
                var bytes = Encoding.Default.GetBytes(text + "\0");
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
            }
        }
    }
}
