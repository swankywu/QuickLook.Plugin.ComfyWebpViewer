using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.ComfyWebpViewer
{
    internal sealed class WebpDecodeResult
    {
        public List<BitmapSource> Frames = new List<BitmapSource>();
        public List<int> Delays = new List<int>();
        public bool Truncated;
        public string Error;
    }

    /// <summary>
    /// Decodes WebP through the Windows Imaging Component.
    /// The Windows WebP WIC decoder (built into Windows 10 1803 and later) handles both
    /// the lossy/lossless bitstreams and the animated VP8X container, so the plugin
    /// ships without any native dependency.
    /// </summary>
    internal static class WebpDecoder
    {
        // Rough ceiling: an animated 4K WebP can otherwise eat gigabytes.
        private const long MaxTotalPixels = 160L * 1000 * 1000;

        public static WebpDecodeResult Decode(string path, WebpFile info)
        {
            var result = new WebpDecodeResult();

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    var frameCount = decoder.Frames.Count;
                    if (frameCount <= 0)
                    {
                        result.Error = "No decodable frame";
                        return result;
                    }

                    var w = decoder.Frames[0].PixelWidth;
                    var h = decoder.Frames[0].PixelHeight;
                    var budget = (int)Math.Max(1, Math.Min(frameCount, MaxTotalPixels / Math.Max(1, (long)w * h)));

                    result.Truncated = budget < frameCount;

                    for (var i = 0; i < budget; i++)
                    {
                        var source = decoder.Frames[i];
                        if (source == null)
                            break;

                        // WebP files often store 72 DPI. WPF uses that to compute the natural
                        // DIP size (PixelWidth/DpiX*96), which breaks Stretch=None and our
                        // pixel-based fit/zoom math. Normalize every frame to 96 DPI so DIP
                        // size equals pixel size.
                        var normalized = NormalizeDpi(source);
                        var frozen = (BitmapSource)normalized.GetAsFrozen();
                        frozen.Freeze();
                        result.Frames.Add(frozen);
                    }

                    BuildDelays(result, info);
                }
            }
            catch (Exception ex)
            {
                result.Frames.Clear();
                result.Delays.Clear();
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// WIC reports WebP frames with the DPI stored in the file (often 72). WPF treats
        /// the natural size as PixelWidth/DpiX*96, so a 1504px@72dpi image has a natural
        /// DIP size of 2005x1109. With Stretch=None that causes the image to be clipped
        /// when we constrain it to PixelWidth x PixelHeight. Re-create the bitmap at 96
        /// DPI so DIP size equals pixel size and all zoom/fit math is consistent.
        /// </summary>
        private static BitmapSource NormalizeDpi(BitmapSource source)
        {
            if (Math.Abs(source.DpiX - 96.0) < 0.01 && Math.Abs(source.DpiY - 96.0) < 0.01)
                return source;

            var wb = new WriteableBitmap(
                source.PixelWidth,
                source.PixelHeight,
                96.0,
                96.0,
                source.Format,
                source.Palette);

            wb.Lock();
            try
            {
                source.CopyPixels(
                    Int32Rect.Empty,
                    wb.BackBuffer,
                    wb.BackBufferStride * wb.PixelHeight,
                    wb.BackBufferStride);
                wb.AddDirtyRect(new Int32Rect(0, 0, wb.PixelWidth, wb.PixelHeight));
            }
            finally
            {
                wb.Unlock();
            }

            return wb;
        }

        private static void BuildDelays(WebpDecodeResult result, WebpFile info)
        {
            var count = result.Frames.Count;
            if (count == 0)
                return;

            if (count == 1)
            {
                result.Delays.Add(0);
                return;
            }

            if (info != null && info.FrameDelays.Count >= count)
            {
                for (var i = 0; i < count; i++)
                    result.Delays.Add(Math.Max(10, info.FrameDelays[i]));
                return;
            }

            // Fall back to a comfortable 12 fps.
            for (var i = 0; i < count; i++)
                result.Delays.Add(80);
        }
    }
}
