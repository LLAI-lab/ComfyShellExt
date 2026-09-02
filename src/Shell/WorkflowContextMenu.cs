using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ComfyShellExt.Core;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Right click entries for files that carry a ComfyUI workflow. The file is inspected while the
    /// menu is being built, so nothing is added for ordinary pictures and videos. Invoking a command
    /// only starts ComfyWorkflowMenu.exe, keeping work out of the Explorer process.
    /// </summary>
    [ComVisible(true)]
    [Guid("AAA3323D-F184-4E10-A8A4-21402817A5A9")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ComfyShellExt.WorkflowContextMenu")]
    public sealed class WorkflowContextMenu : IShellExtInit, IContextMenu
    {
        public const string Clsid = "{AAA3323D-F184-4E10-A8A4-21402817A5A9}";
        private const int MaxFiles = 64;
        private const int MaxProbed = 8;
        private const int CmdView = 0;
        private const int CmdExport = 1;

        private readonly List<string> _files = new List<string>();

        public int Initialize(IntPtr pidlFolder, ComTypes.IDataObject pdtobj, IntPtr hkeyProgId)
        {
            _files.Clear();
            try { ReadSelection(pdtobj); }
            catch (Exception ex) { Log.Error("menu initialize", ex); }
            return _files.Count > 0 ? 0 : ShellConstants.EFail;
        }

        public int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags)
        {
            try
            {
                if ((uFlags & MenuNative.CmfDefaultOnly) != 0) return 0;
                var settings = Settings.Current;
                if (!settings.MenuDynamic || _files.Count == 0) return 0;
                if (!AnyHasWorkflow(settings)) return 0;

                uint position = indexMenu;
                int added = 0;
                if (_files.Count == 1 && idCmdFirst + CmdView <= idCmdLast)
                {
                    if (MenuNative.InsertMenuW(hmenu, position++, MenuNative.MfByPosition |
                            MenuNative.MfString, (UIntPtr)(idCmdFirst + CmdView), settings.MenuViewLabel))
                        added++;
                }
                if (idCmdFirst + CmdExport <= idCmdLast)
                {
                    var label = _files.Count == 1
                        ? settings.MenuExportLabel
                        : settings.MenuExportLabel + " (" + _files.Count + ")";
                    if (MenuNative.InsertMenuW(hmenu, position, MenuNative.MfByPosition |
                            MenuNative.MfString, (UIntPtr)(idCmdFirst + CmdExport), label))
                        added++;
                }
                return added;
            }
            catch (Exception ex)
            {
                Log.Error("QueryContextMenu", ex);
                return 0;
            }
        }

        public int InvokeCommand(IntPtr pici)
        {
            try
            {
                int id;
                string verb;
                if (!MenuNative.TryReadVerbId(pici, out id, out verb)) return ShellConstants.EFail;
                if (verb != null)
                    id = verb.Equals("ComfyShellExtView", StringComparison.OrdinalIgnoreCase) ? CmdView
                        : verb.Equals("ComfyShellExtExport", StringComparison.OrdinalIgnoreCase) ? CmdExport
                        : -1;
                if (id != CmdView && id != CmdExport) return ShellConstants.EFail;
                Launch(id == CmdView ? "view" : "export", MenuNative.ReadOwnerWindow(pici));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("InvokeCommand", ex);
                return ShellConstants.EFail;
            }
        }

        /// <summary>Exposed so the self test can verify verb decoding without launching anything.</summary>
        public static bool TryDecodeCommand(IntPtr pici, out int id)
        {
            string verb;
            return MenuNative.TryReadVerbId(pici, out id, out verb);
        }

        public int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax)
        {
            try
            {
                bool view = idCmd.ToInt64() == CmdView;
                string text;
                switch (uType)
                {
                    case MenuNative.GcsVerbA:
                    case MenuNative.GcsVerbW:
                        text = view ? "ComfyShellExtView" : "ComfyShellExtExport";
                        break;
                    case MenuNative.GcsHelpTextA:
                    case MenuNative.GcsHelpTextW:
                        text = view
                            ? "查看该文件内嵌的 ComfyUI 工作流"
                            : "把内嵌的 ComfyUI 工作流导出为 .json";
                        break;
                    default:
                        return ShellConstants.ENotImpl;
                }
                bool unicode = uType == MenuNative.GcsVerbW || uType == MenuNative.GcsHelpTextW;
                MenuNative.WriteString(pszName, cchMax, text, unicode);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("GetCommandString", ex);
                return ShellConstants.ENotImpl;
            }
        }

        private bool AnyHasWorkflow(Settings settings)
        {
            var options = DetectOptions.Fast();
            options.EnableRawScan = settings.EnableRawScan;
            // A right click must stay snappy, so the brute force window is much smaller here.
            options.MaxRawScanBytes = Math.Min(settings.MaxRawScanMB * 1024L * 1024L, 1024L * 1024L);
            int probed = 0;
            foreach (var file in _files)
            {
                if (probed++ >= MaxProbed) break;
                if (WorkflowDetector.InspectFile(file, options).HasWorkflow) return true;
            }
            return false;
        }

        private void Launch(string verb, IntPtr owner)
        {
            var exe = Path.Combine(Paths.AppDir, "ComfyWorkflowMenu.exe");
            if (!File.Exists(exe))
            {
                // A silent no-op here is the worst outcome, so say what is missing.
                Log.Write("ComfyWorkflowMenu.exe missing in {0}", Paths.AppDir);
                MenuNative.MessageBoxW(owner,
                    "找不到 ComfyWorkflowMenu.exe：\n\n" + exe +
                    "\n\n请确认整个 ComfyShellExt 目录完整，然后以管理员身份重新运行 install.bat。",
                    "ComfyShellExt", 0x10);
                return;
            }
            var arguments = new StringBuilder(verb);
            int count = 0;
            foreach (var file in _files)
            {
                if (verb == "view" && count >= 1) break;
                arguments.Append(" \"").Append(file).Append('"');
                count++;
            }
            var info = new ProcessStartInfo(exe, arguments.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Paths.AppDir
            };
            try
            {
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Log.Error("launch " + verb, ex);
                MenuNative.MessageBoxW(owner,
                    "启动 ComfyWorkflowMenu.exe 失败：\n\n" + ex.Message,
                    "ComfyShellExt", 0x10);
            }
        }

        private void ReadSelection(ComTypes.IDataObject data)
        {
            if (data == null) return;
            var format = new ComTypes.FORMATETC
            {
                cfFormat = MenuNative.CfHdrop,
                ptd = IntPtr.Zero,
                dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = ComTypes.TYMED.TYMED_HGLOBAL
            };
            ComTypes.STGMEDIUM medium;
            data.GetData(ref format, out medium);
            try
            {
                var drop = MenuNative.GlobalLock(medium.unionmember);
                if (drop == IntPtr.Zero) return;
                try
                {
                    uint count = MenuNative.DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
                    var buffer = new StringBuilder(1024);
                    for (uint i = 0; i < count && _files.Count < MaxFiles; i++)
                    {
                        buffer.Length = 0;
                        if (MenuNative.DragQueryFileW(drop, i, buffer, (uint)buffer.Capacity) == 0) continue;
                        var path = buffer.ToString();
                        if (File.Exists(path)) _files.Add(path);
                    }
                }
                finally { MenuNative.GlobalUnlock(medium.unionmember); }
            }
            finally { MenuNative.ReleaseStgMedium(ref medium); }
        }

        [ComRegisterFunction]
        public static void RegisterServer(Type type) { MenuRegistrar.Register(type); }

        [ComUnregisterFunction]
        public static void UnregisterServer(Type type) { MenuRegistrar.Unregister(type); }
    }
}
