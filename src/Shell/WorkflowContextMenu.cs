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
    /// The right click entries for files with AI generation metadata (ComfyUI, SD WebUI and
    /// friends, NovelAI, SwarmUI, Fooocus, InvokeAI), plus the image obfuscation entries which
    /// show for any configured image regardless of content. Inspection happens while the menu is
    /// being built, so the viewer entry adds nothing for ordinary pictures; invoking only starts
    /// ComfyWorkflowMenu.exe, keeping work out of the Explorer process.
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
        private const int MaxViewFiles = 16;
        private const int CmdView = 0;
        private const int CmdObfuscate = 1;
        private const int CmdDeobfuscate = 2;
        private const string VerbView = "ComfyShellExtView";
        private const string VerbObfuscate = "ComfyShellExtObfuscate";
        private const string VerbDeobfuscate = "ComfyShellExtDeobfuscate";

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
                bool hasImage = settings.MenuObfuscate && AnyHasImageExtension(settings);
                bool hasAiMeta = AnyHasAiMeta(settings);
                if (!hasImage && !hasAiMeta) return 0;
                int wanted = (hasAiMeta ? 1 : 0) + (hasImage ? 2 : 0);
                if (idCmdFirst + (uint)wanted > idCmdLast) return 0;

                uint position = indexMenu;
                int added = 0;
                if (hasAiMeta)
                {
                    var label = settings.MenuViewLabel;
                    if (_files.Count > 1) label += " (" + _files.Count + ")";
                    if (!MenuNative.InsertMenuW(hmenu, position++, MenuNative.MfByPosition |
                            MenuNative.MfString, (UIntPtr)(idCmdFirst + CmdView), label))
                        return added;
                    added++;
                }
                if (hasImage)
                {
                    if (added > 0)
                        MenuNative.InsertMenuW(hmenu, position++, MenuNative.MfByPosition |
                            MenuNative.MfSeparator, (UIntPtr)(idCmdFirst + added), null);
                    added += InsertObfuscate(hmenu, position, idCmdFirst + (uint)added, settings);
                }
                return added;
            }
            catch (Exception ex)
            {
                Log.Error("QueryContextMenu", ex);
                return 0;
            }
        }

        /// <summary>Obfuscate and deobfuscate entries for the selection. Both are always offered:
        /// the selection decides what makes sense, and the executor reports files it cannot handle.</summary>
        private int InsertObfuscate(IntPtr hmenu, uint position, uint idCmd, Settings settings)
        {
            if (!MenuNative.InsertMenuW(hmenu, position, MenuNative.MfByPosition |
                    MenuNative.MfString, (UIntPtr)idCmd, settings.MenuObfuscateLabel))
                return 0;
            if (!MenuNative.InsertMenuW(hmenu, position + 1, MenuNative.MfByPosition |
                    MenuNative.MfString, (UIntPtr)(idCmd + 1u), settings.MenuDeobfuscateLabel))
                return 1;
            return 2;
        }

        public int InvokeCommand(IntPtr pici)
        {
            try
            {
                int id;
                string verb;
                if (!MenuNative.TryReadVerbId(pici, out id, out verb)) return ShellConstants.EFail;
                if (verb != null)
                {
                    if (verb.Equals(VerbView, StringComparison.OrdinalIgnoreCase)) id = CmdView;
                    else if (verb.Equals(VerbObfuscate, StringComparison.OrdinalIgnoreCase)) id = CmdObfuscate;
                    else if (verb.Equals(VerbDeobfuscate, StringComparison.OrdinalIgnoreCase)) id = CmdDeobfuscate;
                    else id = -1;
                }
                switch (id)
                {
                    case CmdView: Launch("view", pici, MaxViewFiles); return 0;
                    case CmdObfuscate: Launch("obfuscate", pici, MaxFiles); return 0;
                    case CmdDeobfuscate: Launch("deobfuscate", pici, MaxFiles); return 0;
                    default: return ShellConstants.EFail;
                }
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
                long id = idCmd.ToInt64();
                string text;
                switch (uType)
                {
                    case MenuNative.GcsVerbA:
                    case MenuNative.GcsVerbW:
                        text = id == CmdView ? VerbView
                             : id == CmdObfuscate ? VerbObfuscate
                             : id == CmdDeobfuscate ? VerbDeobfuscate
                             : null;
                        break;
                    case MenuNative.GcsHelpTextA:
                    case MenuNative.GcsHelpTextW:
                        text = id == CmdView
                            ? "查看 AI 生图的提示词与参数（ComfyUI / SD WebUI / NovelAI 等）"
                            : id == CmdObfuscate
                            ? "Gilbert 曲线像素重排：图片变成纯噪点，隐藏画面与工作流，输出 PNG，可随时解混淆还原"
                            : id == CmdDeobfuscate
                            ? "还原像素重排过的图片（对未混淆的图片无意义）"
                            : null;
                        break;
                    default:
                        return ShellConstants.ENotImpl;
                }
                if (text == null) return ShellConstants.ENotImpl;
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

        private bool AnyHasImageExtension(Settings settings)
        {
            foreach (var file in _files)
            {
                var extension = System.IO.Path.GetExtension(file);
                foreach (var image in settings.ImageExtensions)
                    if (string.Equals(extension, image, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool AnyHasAiMeta(Settings settings)
        {
            var options = DetectOptions.Fast();
            options.EnableRawScan = settings.EnableRawScan;
            // A right click must stay snappy, so the brute force window is much smaller here.
            options.MaxRawScanBytes = Math.Min(settings.MaxRawScanMB * 1024L * 1024L, 1024L * 1024L);
            int probed = 0;
            foreach (var file in _files)
            {
                if (probed++ >= MaxProbed) break;
                if (WorkflowDetector.InspectFile(file, options).HasAiMeta) return true;
            }
            return false;
        }

        private void Launch(string verb, IntPtr pici, int maxFiles)
        {
            var owner = MenuNative.ReadOwnerWindow(pici);
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
                if (count >= maxFiles) break;
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
