using System;
using System.IO;
using System.Windows;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;

namespace QuickLook.Plugin.ComfyWebpViewer
{
    /// <summary>
    /// Previews WebP images written by ComfyUI.
    ///
    /// Why this plugin exists: QuickLook's built-in ImageViewer asks exiv2 for the EXIF
    /// block and feeds the result straight into XmlDocument.LoadXml(). ComfyUI stores the
    /// whole workflow JSON inside EXIF tags (0x0110 "prompt:{...}", 0x010F/0x010E/... for
    /// extra_pnginfo), and that JSON routinely contains bare '&amp;' and '&lt;' characters,
    /// so LoadXml() throws and the preview dies before a single pixel is drawn.
    ///
    /// This plugin never touches exiv2: the container is parsed by hand and the pixels come
    /// from the Windows Imaging Component WebP decoder.
    /// </summary>
    public class Plugin : IViewer
    {
        internal const string Domain = "QuickLook.Plugin.ComfyWebpViewer";

        private ImagePanel _panel;

        /// <summary>
        /// ImageViewer sits at 0, so anything positive wins the match for .webp files.
        /// </summary>
        public int Priority => 100;

        public void Init()
        {
        }

        public bool CanHandle(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || Directory.Exists(path) || !File.Exists(path))
                    return false;

                var ext = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext) && !ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!WebpFile.HasWebpSignature(path))
                    return false;

                return HandleAllWebP() || WebpFile.LooksLikeComfyUi(path);
            }
            catch
            {
                return false;
            }
        }

        public void Prepare(string path, ContextObject context)
        {
            // Prepare() must be fast and must never throw - that is exactly how the
            // stock ImageViewer fails on ComfyUI files.
            try
            {
                var info = WebpFile.ReadHeader(path);
                if (info != null && info.Width > 0 && info.Height > 0)
                    context.SetPreferredSizeFit(new Size(info.Width, info.Height), 0.83);
                else
                    context.PreferredSize = new Size(1000, 700);
            }
            catch
            {
                context.PreferredSize = new Size(1000, 700);
            }

            context.Theme = (Themes)SettingHelper.Get("Theme", 1, Domain);
            context.CanResize = true;
            context.TitlebarOverlap = false;
            context.TitlebarColourVisibility = true;
            context.FullWindowDragging = false;
        }

        public void View(string path, ContextObject context)
        {
            _panel = new ImagePanel();
            context.ViewerContent = _panel;

            try
            {
                var info = WebpFile.ReadMetadataOnly(path) ?? WebpFile.ReadHeader(path);

                if (info == null)
                {
                    _panel.ShowError(
                        TranslationHelper.Get("Error_BadFile", null, null,
                            "This file is not a valid WebP image.", Domain),
                        path);
                    context.Title = Path.GetFileName(path);
                    return;
                }

                _panel.Load(path, info);

                context.Title = info.Width + "×" + info.Height + ": " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                context.Title = Path.GetFileName(path);
                ProcessHelper.WriteLog("ComfyWebpViewer failed on " + path + "\n" + ex);
            }

            context.IsBusy = false;
        }

        public void Cleanup()
        {
            GC.SuppressFinalize(this);
            _panel?.Dispose();
            _panel = null;
        }

        /// <summary>
        /// When set, the plugin takes over every .webp file instead of only those that
        /// carry ComfyUI metadata (UserData\QuickLook.Plugin.ComfyWebpViewer.config).
        /// </summary>
        private static bool HandleAllWebP()
        {
            return SettingHelper.Get("HandleAllWebP", false, Domain);
        }
    }
}
