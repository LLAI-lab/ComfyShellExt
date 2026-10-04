using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ComfyShellExt.Core;
using ComfyShellExt.Core.Obfuscator;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Shared thumbnail handler logic: work out which AI tool produced the file, obtain the base
    /// thumbnail from the handler that owned the file type before us, then paint the tool badge.
    /// Without AI metadata the original bitmap is passed straight through untouched.
    /// </summary>
    public abstract class ThumbnailProviderBase : IThumbnailProvider, IInitializeWithItem, IInitializeWithFile
    {
        protected ComTypes.IStream SourceStream;
        protected IShellItem SourceItem;
        protected string SourcePath;
        /// <summary>True when the base image is a file type icon rather than a real thumbnail.</summary>
        private bool _baseIsIcon;

        protected abstract MediaKind Kind { get; }

        public void Initialize(IShellItem psi, uint grfMode)
        {
            SourceItem = psi;
            SourcePath = TryGetPath(psi);
            Log.Write("init item {0}", SourcePath ?? "(no path)");
        }

        public void Initialize(string pszFilePath, uint grfMode)
        {
            SourcePath = pszFilePath;
            Log.Write("init file {0}", pszFilePath);
        }

        public void GetThumbnail(uint cx, out IntPtr phbmp, out WtsAlphaType pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WtsAlphaType.Unknown;
            var settings = Settings.Current;
            int size = (int)Math.Max(1, Math.Min(cx, 4096));
            if (settings.ObfuscatePreview && Kind == MediaKind.Image &&
                TryRenderObfuscatedPreview(size, settings, out phbmp, out pdwAlpha))
                return;
            string generator = DetectGenerator();
            Log.Write("thumbnail cx={0} kind={1} badge={2} path={3}", cx, Kind,
                generator ?? "no", SourcePath);

            IntPtr baseBitmap;
            WtsAlphaType baseAlpha;
            if (!TryGetBase(size, settings, out baseBitmap, out baseAlpha))
            {
                if (generator == null || !settings.BadgePlaceholder)
                    throw new COMException("no base thumbnail", ShellConstants.EFail);
                using (var tile = PlaceholderRenderer.Render(size, Label()))
                {
                    BadgeRenderer.Draw(tile, settings, generator);
                    phbmp = BitmapUtil.ToHBitmap(tile);
                }
                if (phbmp == IntPtr.Zero) throw new COMException("placeholder failed", ShellConstants.EFail);
                pdwAlpha = WtsAlphaType.Argb;
                return;
            }
            if (generator == null)
            {
                phbmp = baseBitmap;
                pdwAlpha = baseAlpha;
                return;
            }
            IntPtr composed = IntPtr.Zero;
            try
            {
                using (var bitmap = BitmapUtil.FromHBitmap(baseBitmap, baseAlpha))
                {
                    if (bitmap != null)
                    {
                        BadgeRenderer.Draw(bitmap, settings, generator,
                            _baseIsIcon ? settings.BadgeIconPosition : settings.BadgePosition);
                        composed = BitmapUtil.ToHBitmap(bitmap);
                    }
                }
            }
            catch (Exception ex) { Log.Error("compose badge", ex); }

            if (composed != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(baseBitmap);
                phbmp = composed;
                pdwAlpha = WtsAlphaType.Argb;
            }
            else
            {
                phbmp = baseBitmap;
                pdwAlpha = baseAlpha;
            }
        }

        /// <summary>
        /// Obfuscated files are noise; when obfuscate.preview is on, render the deobfuscated
        /// pixels as the thumbnail — the preview pane reuses the same bitmap — and badge it so it
        /// is not mistaken for the original. Any failure falls back to the normal flow.
        /// </summary>
        private bool TryRenderObfuscatedPreview(int size, Settings settings, out IntPtr phbmp, out WtsAlphaType alpha)
        {
            phbmp = IntPtr.Zero;
            alpha = WtsAlphaType.Unknown;
            try
            {
                if (string.IsNullOrEmpty(SourcePath) || !IsObfuscatedImage()) return false;
                using (var stream = OpenStream())
                {
                    if (stream == null) return false;
                    using (var raw = new Bitmap(stream))
                    using (var restored = new Bitmap(raw.Width, raw.Height,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var graphics = System.Drawing.Graphics.FromImage(restored))
                            graphics.DrawImageUnscaled(raw, 0, 0);
                        PixelShuffle.Apply(restored, false);
                        using (var fitted = BitmapUtil.Fit(restored, size))
                        {
                            if (fitted == null) return false;
                            BadgeRenderer.Draw(fitted, settings, Settings.ObfuscationTool,
                                settings.BadgePosition);
                            phbmp = BitmapUtil.ToHBitmap(fitted);
                        }
                    }
                }
                alpha = WtsAlphaType.Argb;
                Log.Write("obfuscated preview {0}", SourcePath);
                return phbmp != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log.Error("obfuscated preview", ex);
                phbmp = IntPtr.Zero;
                return false;
            }
        }

        /// <summary>
        /// Obfuscation marker: the output file name suffix, or — for renamed files — our sealed
        /// metadata payload in the PNG. The pixel shuffle itself is dimension keyed, so both
        /// kinds restore without any key material.
        /// </summary>
        private bool IsObfuscatedImage()
        {
            try
            {
                if (Path.GetFileNameWithoutExtension(SourcePath)
                        .EndsWith(MetaVault.ObfuscatedSuffix, StringComparison.Ordinal)) return true;
                if (Extension() == ".png") return MetaVault.HasPayload(SourcePath);
            }
            catch (Exception ex) { Log.Error("obfuscation detect", ex); }
            return false;
        }

        private bool TryGetBase(int cx, Settings settings, out IntPtr hbmp, out WtsAlphaType alpha)
        {
            var clsid = OriginalProviders.Resolve(Extension(), Kind);
            if (ThumbnailDelegator.TryGet(clsid, (uint)cx, SourceStream, SourceItem, SourcePath,
                    out hbmp, out alpha)) return true;
            hbmp = IntPtr.Zero;
            alpha = WtsAlphaType.Unknown;
            Bitmap fallback = null;
            try
            {
                if (Kind == MediaKind.Image) fallback = DecodeImage(cx);
                else if (settings.EnableFfmpegFallback)
                {
                    using (var frame = FfmpegFrameGrabber.Grab(SourcePath, cx, settings))
                        fallback = frame == null ? null : BitmapUtil.Fit(frame, cx);
                }
                // Windows cannot decode every video, and then Explorer shows the default player's
                // icon. Badging that icon is what the file type looks like anyway, only marked.
                if (fallback == null && settings.BadgeOnIconFallback && !string.IsNullOrEmpty(SourcePath))
                {
                    string note;
                    using (var icon = ShellThumbnail.GetIcon(SourcePath, cx, out note))
                    {
                        if (icon != null)
                        {
                            fallback = new Bitmap(icon);
                            _baseIsIcon = true;
                        }
                        else Log.Write("icon fallback: {0}", note);
                    }
                }
                if (fallback == null) return false;
                hbmp = BitmapUtil.ToHBitmap(fallback);
                alpha = WtsAlphaType.Argb;
                return hbmp != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log.Error("fallback decode", ex);
                return false;
            }
            finally { if (fallback != null) fallback.Dispose(); }
        }

        private Bitmap DecodeImage(int cx)
        {
            using (var stream = OpenStream())
            {
                if (stream == null) return null;
                using (var image = Image.FromStream(stream, false, false))
                    return BitmapUtil.Fit(image, cx);
            }
        }

        /// <summary>Returns which AI tool the file came from, or null when it has no badge.</summary>
        protected string DetectGenerator()
        {
            try
            {
                using (var stream = OpenStream())
                {
                    if (stream == null) return null;
                    return WorkflowDetector.DetectGenerator(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Error("detect", ex);
                return null;
            }
            finally
            {
                if (SourceStream != null) ThumbnailDelegator.Rewind(SourceStream);
            }
        }

        private Stream OpenStream()
        {
            if (SourceStream != null)
            {
                ThumbnailDelegator.Rewind(SourceStream);
                return new ComStream(SourceStream);
            }
            if (string.IsNullOrEmpty(SourcePath)) return null;
            return new FileStream(SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.SequentialScan);
        }

        private string Extension()
        {
            try
            {
                return string.IsNullOrEmpty(SourcePath) ? null : Path.GetExtension(SourcePath).ToLowerInvariant();
            }
            catch { return null; }
        }

        private string Label()
        {
            var extension = Extension();
            return string.IsNullOrEmpty(extension) ? (Kind == MediaKind.Video ? "video" : "image")
                : extension.TrimStart('.');
        }

        private static string TryGetPath(IShellItem item)
        {
            if (item == null) return null;
            try
            {
                string path;
                item.GetDisplayName(ShellConstants.SigdnFileSysPath, out path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("GetDisplayName", ex);
                return null;
            }
        }
    }
}
