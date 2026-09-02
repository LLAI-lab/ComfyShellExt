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
            failures += CheckClass("image", new ImageThumbnailProvider(), true);
            failures += CheckClass("video", new VideoThumbnailProvider(), false);
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
        /// data object parsing, workflow detection and item insertion.
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
                int items = CountItems(probe);
                bool ok = expected ? items > 0 : items == 0;
                if (!ok) failures++;
                Console.WriteLine("menu   {0,-24} items={1} workflow={2} {3}",
                    System.IO.Path.GetFileName(probe), items, expected, ok ? "as designed" : "UNEXPECTED");
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

        private static int CountItems(string path)
        {
            var handler = new WorkflowContextMenu();
            var data = new System.Windows.Forms.DataObject();
            var list = new System.Collections.Specialized.StringCollection { System.IO.Path.GetFullPath(path) };
            data.SetFileDropList(list);
            if (((IShellExtInit)handler).Initialize(IntPtr.Zero,
                    (ComTypes.IDataObject)data, IntPtr.Zero) != 0) return -1;
            var menu = CreatePopupMenu();
            try
            {
                ((IContextMenu)handler).QueryContextMenu(menu, 0, 1000, 1999, 0);
                return GetMenuItemCount(menu);
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
