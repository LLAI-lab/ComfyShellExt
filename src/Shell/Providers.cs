using System;
using System.Runtime.InteropServices;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Thumbnail handler for still images. Deliberately does not implement IInitializeWithStream,
    /// mirroring the video handler: the sandboxed thumbnail host initialises through a stream and
    /// never reveals the file name, which the obfuscation preview keywords need. Opting out of
    /// process isolation (image.disableprocessisolation) makes Explorer initialise with the
    /// shell item / file path instead. Items without a file system path (images inside a zip)
    /// lose their thumbnail handler and fall back to whatever the shell can do.
    /// </summary>
    [ComVisible(true)]
    [Guid("B8D82C63-93A4-451F-B5D6-0EB66DA46857")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ComfyShellExt.ImageThumbnailProvider")]
    public sealed class ImageThumbnailProvider : ThumbnailProviderBase
    {
        public const string Clsid = "{B8D82C63-93A4-451F-B5D6-0EB66DA46857}";

        protected override MediaKind Kind { get { return MediaKind.Image; } }

        [ComRegisterFunction]
        public static void RegisterServer(Type type) { Registrar.Register(type, MediaKind.Image); }

        [ComUnregisterFunction]
        public static void UnregisterServer(Type type) { Registrar.Unregister(type); }
    }

    /// <summary>
    /// Thumbnail handler for video. Deliberately does not implement IInitializeWithStream: the
    /// Windows video handler and ffmpeg both need a real path, and the shell only hands us one when
    /// stream initialisation is unavailable.
    /// </summary>
    [ComVisible(true)]
    [Guid("54F5C660-9A8D-4095-A32B-B4A72B6DD1C3")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("ComfyShellExt.VideoThumbnailProvider")]
    public sealed class VideoThumbnailProvider : ThumbnailProviderBase
    {
        public const string Clsid = "{54F5C660-9A8D-4095-A32B-B4A72B6DD1C3}";

        protected override MediaKind Kind { get { return MediaKind.Video; } }

        [ComRegisterFunction]
        public static void RegisterServer(Type type) { Registrar.Register(type, MediaKind.Video); }

        [ComUnregisterFunction]
        public static void UnregisterServer(Type type) { Registrar.Unregister(type); }
    }
}
