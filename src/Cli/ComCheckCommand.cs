using System;
using System.Runtime.InteropServices;
using ComfyShellExt.Core;
using ComfyShellExt.Shell;
using ComfyShellExt.Shell.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Cli
{
    /// <summary>
    /// Verifies the COM surface Explorer will use: that each provider answers QueryInterface for the
    /// interfaces it is supposed to expose, and that a thumbnail can be produced through a real COM
    /// boundary rather than a direct managed call.
    /// </summary>
    internal static class ComCheckCommand
    {
        private static readonly Type[] Interfaces =
        {
            typeof(IThumbnailProvider), typeof(IInitializeWithStream),
            typeof(IInitializeWithItem), typeof(IInitializeWithFile)
        };

        public static int Run(Args args)
        {
            int failures = 0;
            failures += CheckClass("image", new ImageThumbnailProvider(), false);
            failures += CheckClass("video", new VideoThumbnailProvider(), false);
            failures += CheckIsolation();
            failures += CheckMenu(args.Values.Count > 0 ? args.Values[0] : null);
            failures += CheckVerbDecoding();
            if (args.Values.Count > 0) failures += RoundTrip(args.Values[0], args.Int("size", 256));
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "COM surface OK" : failures + " problem(s) found");
            return failures == 0 ? 0 : 1;
        }

        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);

        /// <summary>
        /// Builds a real menu for a real selection, which exercises the whole conditional path:
        /// data object parsing, workflow detection, item insertion, and the offset to verb mapping
        /// Explorer relies on to route the click back to the right command.
        /// </summary>
        private static int CheckMenu(string sample)
        {
            int failures = 0;
            var handler = new WorkflowContextMenu();
            failures += CheckClass("menu", handler, false, typeof(IShellExtInit), typeof(IContextMenu));
            if (sample == null) return failures;
            foreach (var probe in new[] { sample, FindPlain(sample) })
            {
                if (probe == null) continue;
                var expected = WorkflowDetector.InspectFile(probe, DetectOptions.Fast()).HasWorkflow;
                var verbs = MenuVerbs(probe);
                var flat = new System.Collections.Generic.List<string>(verbs);
                flat.RemoveAll(v => v == "-");
                // Expected layout: viewer entry only for AI metadata, obfuscation pair only for
                // images. The verb sequence per offset is the contract Explorer relies on.
                System.Collections.Generic.List<string> want;
                bool image = IsImageExtension(System.IO.Path.GetExtension(probe));
                if (expected && image)
                    want = new System.Collections.Generic.List<string> {
                        "ComfyShellExtView", "ComfyShellExtObfuscate", "ComfyShellExtDeobfuscate" };
                else if (expected)
                    want = new System.Collections.Generic.List<string> { "ComfyShellExtView" };
                else if (image)
                    want = new System.Collections.Generic.List<string> {
                        "ComfyShellExtObfuscate", "ComfyShellExtDeobfuscate" };
                else
                    want = new System.Collections.Generic.List<string>();
                bool ok = flat.Count == want.Count;
                if (ok) for (int i = 0; i < want.Count; i++)
                    if (flat[i] != want[i]) { ok = false; break; }
                if (!ok) failures++;
                Console.WriteLine("menu   {0,-24} items={1} [{2}] {3}",
                    System.IO.Path.GetFileName(probe), verbs.Count, string.Join(", ", verbs),
                    ok ? "as designed" : "UNEXPECTED: clicks would launch the wrong command");
            }
            return failures;
        }

        /// <summary>
        /// Explorer identifies the clicked item by its offset from idCmdFirst, passed in place of a
        /// verb pointer. The first item's offset is zero, which is easy to mistake for "no verb" and
        /// then the item silently does nothing, so both offsets are checked explicitly.
        /// </summary>
        private static int CheckVerbDecoding()
        {
            int failures = 0;
            int size = 16 + IntPtr.Size * 4;
            var block = Marshal.AllocHGlobal(size);
            try
            {
                for (int expected = 0; expected <= 1; expected++)
                {
                    for (int i = 0; i < size; i++) Marshal.WriteByte(block, i, 0);
                    Marshal.WriteInt32(block, 0, size);
                    Marshal.WriteIntPtr(block, 8 + IntPtr.Size, new IntPtr(expected));
                    int id;
                    bool ok = WorkflowContextMenu.TryDecodeCommand(block, out id) && id == expected;
                    if (!ok) failures++;
                    Console.WriteLine("menu   invoke offset {0}         {1,-10} {2}", expected,
                        ok ? "decoded" : "not decoded",
                        ok ? "as designed" : "UNEXPECTED: this item would do nothing");
                }
            }
            finally { Marshal.FreeHGlobal(block); }
            return failures;
        }

        /// <summary>Extensions the obfuscate entries cover; mirrors the menu handler's rule.</summary>
        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".jpe", ".webp", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".avif" };

        private static bool IsImageExtension(string extension)
        {
            foreach (var image in ImageExtensions)
                if (extension.Equals(image, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Builds the menu like Explorer does, then asks the handler for each position's verb via
        /// GetCommandString. Separators report no verb and show as "-".
        /// </summary>
        private static System.Collections.Generic.List<string> MenuVerbs(string path)
        {
            var verbs = new System.Collections.Generic.List<string>();
            var handler = new WorkflowContextMenu();
            var data = new System.Windows.Forms.DataObject();
            var list = new System.Collections.Specialized.StringCollection { System.IO.Path.GetFullPath(path) };
            data.SetFileDropList(list);
            if (((IShellExtInit)handler).Initialize(IntPtr.Zero,
                    (ComTypes.IDataObject)data, IntPtr.Zero) != 0)
            {
                verbs.Add("(initialize failed)");
                return verbs;
            }
            var menu = CreatePopupMenu();
            try
            {
                var context = (IContextMenu)handler;
                context.QueryContextMenu(menu, 0, 1000, 1999, 0);
                int count = GetMenuItemCount(menu);
                for (uint offset = 0; offset < count; offset++)
                {
                    // 4 = GcsVerbW; MenuNative is internal to the shell assembly. The verb lands
                    // in an unmanaged buffer because the interop signature takes a raw pointer.
                    IntPtr buffer = Marshal.AllocHGlobal(128);
                    try
                    {
                        int hr = context.GetCommandString((IntPtr)offset, 4, IntPtr.Zero, buffer, 128);
                        verbs.Add(hr == 0 ? Marshal.PtrToStringUni(buffer) : "-");
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                return verbs;
            }
            finally { DestroyMenu(menu); }
        }

        /// <summary>Finds a sibling file without a workflow, to prove the entries stay hidden.</summary>
        private static string FindPlain(string sample)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sample));
                foreach (var file in System.IO.Directory.GetFiles(dir, "plain.*"))
                    if (!WorkflowDetector.InspectFile(file, DetectOptions.Fast()).HasWorkflow) return file;
            }
            catch { }
            return null;
        }

        private static int CheckClass(string label, object instance, bool expectStream,
            params Type[] contracts)
        {
            int failures = 0;
            var list = contracts == null || contracts.Length == 0 ? Interfaces : contracts;
            var unknown = Marshal.GetIUnknownForObject(instance);
            try
            {
                foreach (var contract in list)
                {
                    var iid = contract.GUID;
                    IntPtr ppv;
                    int hr = Marshal.QueryInterface(unknown, ref iid, out ppv);
                    if (hr == 0) Marshal.Release(ppv);
                    bool wanted = contract != typeof(IInitializeWithStream) || expectStream;
                    bool ok = (hr == 0) == wanted;
                    if (!ok) failures++;
                    Console.WriteLine("{0,-6} {1,-24} {2,-10} {3}", label, contract.Name,
                        hr == 0 ? "available" : "absent", ok ? "as designed" : "UNEXPECTED");
                }
            }
            finally { Marshal.Release(unknown); }
            return failures;
        }

        /// <summary>
        /// Both providers initialise from the file path, which the sandboxed thumbnail host
        /// refuses unless the CLSID opts out of process isolation; without the value Explorer
        /// silently falls back to the original provider and no thumbnail of ours is drawn at
        /// all. Read from the live registry rather than the ini, since that is what Explorer obeys.
        /// </summary>
        private static int CheckIsolation()
        {
            int failures = 0;
            failures += CheckIsolation("image", typeof(ImageThumbnailProvider));
            failures += CheckIsolation("video", typeof(VideoThumbnailProvider));
            return failures;
        }

        private static int CheckIsolation(string label, Type type)
        {
            int failures = 0;
            object value = null;
            try
            {
                using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(
                    "CLSID\\{" + type.GUID + "}"))
                    value = key == null ? null : key.GetValue("DisableProcessIsolation");
            }
            catch { }
            bool set;
            try { set = Convert.ToInt32(value) == 1; }
            catch { set = false; }
            bool ok = set;
            if (!ok) failures++;
            Console.WriteLine("{0,-6} {1,-24} {2,-10} {3}", label, "DisableProcessIsolation",
                set ? "=1" : "missing",
                ok ? "as designed" : "UNEXPECTED: path based handler, Explorer will not initialise " +
                                     "it - re-run install.bat as admin");
            return failures;
        }

        /// <summary>Calls GetThumbnail through a COM callable wrapper, the way the shell host does.</summary>
        private static int RoundTrip(string path, int size)
        {
            Console.WriteLine();
            var full = System.IO.Path.GetFullPath(path);
            var kind = ThumbnailPreview.ClassifyByExtension(System.IO.Path.GetExtension(full));
            object instance = kind == MediaKind.Video
                ? (object)new VideoThumbnailProvider()
                : new ImageThumbnailProvider();
            var unknown = Marshal.GetIUnknownForObject(instance);
            try
            {
                var initialiser = (IInitializeWithFile)Marshal.GetTypedObjectForIUnknown(
                    unknown, typeof(IInitializeWithFile));
                initialiser.Initialize(full, ShellConstants.StgmRead);
                var provider = (IThumbnailProvider)Marshal.GetTypedObjectForIUnknown(
                    unknown, typeof(IThumbnailProvider));
                IntPtr hbmp;
                WtsAlphaType alpha;
                provider.GetThumbnail((uint)size, out hbmp, out alpha);
                if (hbmp == IntPtr.Zero)
                {
                    Console.WriteLine("round trip {0}: no bitmap returned", System.IO.Path.GetFileName(full));
                    return 1;
                }
                int depth;
                var description = ThumbnailPreview.DescribeBitmap(hbmp, out depth);
                Console.WriteLine("round trip {0}: {1} alpha={2} via {3}",
                    System.IO.Path.GetFileName(full), description, alpha, kind);
                ThumbnailPreview.ReleaseBitmap(hbmp);
                return depth == 32 ? 0 : 1;            }
            catch (Exception ex)
            {
                Console.WriteLine("round trip failed: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
            finally { Marshal.Release(unknown); }
        }
    }
}
