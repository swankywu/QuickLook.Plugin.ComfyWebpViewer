using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuickLook.Plugin.ComfyWebpViewer
{
    /// <summary>
    /// Everything we need to know about a WebP file without decoding any pixels:
    /// canvas size, colour model, animation layout, plus the raw metadata chunks.
    /// Only chunk headers are read, so this stays cheap even for very large files.
    /// </summary>
    internal sealed class WebpFile
    {
        private const uint RiffFourCc = 0x46464952; // 'RIFF'
        private const uint WebpFourCc = 0x50424557; // 'WEBP'
        private const uint Vp8FourCc = 0x20385056;  // 'VP8 '
        private const uint Vp8LFourCc = 0x4C385056; // 'VP8L'
        private const uint Vp8XFourCc = 0x58385056; // 'VP8X'
        private const uint AnimFourCc = 0x4D494E41; // 'ANIM'
        private const uint AnmfFourCc = 0x464D4E41; // 'ANMF'
        private const uint ExifFourCc = 0x46495845; // 'EXIF'
        private const uint XmpFourCc = 0x20504D58;  // 'XMP '

        // VP8X feature flags (first payload byte)
        private const byte FlagIcc = 0x20;
        private const byte FlagAlpha = 0x10;
        private const byte FlagExif = 0x08;
        private const byte FlagXmp = 0x04;
        private const byte FlagAnimation = 0x02;

        private string _bitstream;

        public string Path { get; private set; }
        public long FileSize { get; private set; }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool HasAlpha { get; private set; }
        public bool IsAnimated { get; private set; }

        public int LoopCount { get; private set; } = 1;
        public List<int> FrameDelays { get; } = new List<int>();

        /// <summary>IFD0 entries, decoded as text where possible.</summary>
        public Dictionary<int, string> ExifTags { get; } = new Dictionary<int, string>();

        /// <summary>The raw ComfyUI payloads, e.g. "prompt" / "workflow".</summary>
        public Dictionary<string, string> ComfyPayloads { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasComfyMetadata => ComfyPayloads.Count > 0;

        public string Codec
        {
            get
            {
                if (IsAnimated)
                    return "WebP (animated)";

                if (_bitstream == "lossless")
                    return "WebP (lossless)";
                if (_bitstream == "lossy")
                    return "WebP (lossy)";

                return "WebP";
            }
        }

        // ------------------------------------------------------------------ probing

        public static bool HasWebpSignature(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 16)
                        return false;

                    var head = new byte[16];
                    if (fs.Read(head, 0, head.Length) != head.Length)
                        return false;

                    return ReadU32Le(head, 0) == RiffFourCc && ReadU32Le(head, 8) == WebpFourCc;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ComfyUI stuffs the API prompt into EXIF tag 0x0110 as "prompt:{...}" and every
        /// extra_pnginfo entry (workflow, ...) into 0x010F, 0x010E, ... as "name:{...}".
        /// </summary>
        public static bool LooksLikeComfyUi(string path)
        {
            try
            {
                var info = Read(path, true);
                return info != null && info.HasComfyMetadata;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads only enough of the container to know the canvas size.</summary>
        public static WebpFile ReadHeader(string path)
        {
            return Read(path, false);
        }

        /// <summary>Full walk: size, animation timing and metadata chunks.</summary>
        public static WebpFile ReadMetadataOnly(string path)
        {
            return Read(path, true);
        }

        private static WebpFile Read(string path, bool withMetadata)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var file = new WebpFile { Path = path, FileSize = fs.Length };

                    if (fs.Length < 20)
                        return null;

                    var head = new byte[12];
                    if (fs.Read(head, 0, head.Length) != head.Length)
                        return null;

                    if (ReadU32Le(head, 0) != RiffFourCc || ReadU32Le(head, 8) != WebpFourCc)
                        return null;

                    var riffSize = ReadU32Le(head, 4);
                    var end = Math.Min(fs.Length, (long)riffSize + 8);

                    Walk(fs, 12, end, file, withMetadata);

                    // A size is the bare minimum for a usable preview.
                    return file.Width > 0 && file.Height > 0 ? file : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void Walk(FileStream fs, long start, long end, WebpFile file, bool withMetadata)
        {
            var pos = start;
            var sizeKnown = false;
            var guard = 0;

            while (pos + 8 <= end && guard++ < 20000)
            {
                fs.Position = pos;

                var hdr = new byte[8];
                if (fs.Read(hdr, 0, 8) != 8)
                    break;

                var fourCc = ReadU32Le(hdr, 0);
                var size = ReadU32Le(hdr, 4);
                if (size > int.MaxValue - 8)
                    break;

                var payloadAt = pos + 8;
                var advance = 8L + size + (size & 1);
                if (payloadAt + size > end)
                    break;

                if (fourCc == Vp8XFourCc && size >= 10)
                {
                    var p = ReadAt(fs, payloadAt, 10);
                    var flags = p[0];

                    if (!sizeKnown)
                    {
                        file.Width = 1 + (p[4] | (p[5] << 8) | (p[6] << 16));
                        file.Height = 1 + (p[7] | (p[8] << 8) | (p[9] << 16));
                        file.HasAlpha = (flags & FlagAlpha) != 0;
                        sizeKnown = true;
                    }

                    if ((flags & FlagAnimation) != 0)
                        file.IsAnimated = true;
                }
                else if (fourCc == Vp8FourCc && size >= 10)
                {
                    var p = ReadAt(fs, payloadAt + 6, 4);

                    if (!sizeKnown)
                    {
                        file.Width = (p[0] | (p[1] << 8)) & 0x3FFF;
                        file.Height = (p[2] | (p[3] << 8)) & 0x3FFF;
                        file.HasAlpha = false;
                        sizeKnown = true;
                    }

                    file._bitstream = "lossy";
                }
                else if (fourCc == Vp8LFourCc && size >= 5)
                {
                    var p = ReadAt(fs, payloadAt + 1, 4);
                    var bits = (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));

                    if (!sizeKnown)
                    {
                        file.Width = (int)(bits & 0x3FFF) + 1;
                        file.Height = (int)((bits >> 14) & 0x3FFF) + 1;
                        file.HasAlpha = ((bits >> 28) & 0x1) != 0;
                        sizeKnown = true;
                    }

                    file._bitstream = "lossless";
                }
                else if (fourCc == AnimFourCc)
                {
                    file.IsAnimated = true;

                    if (withMetadata && size >= 6)
                    {
                        // 4 bytes background colour, then a 16-bit loop count.
                        var p = ReadAt(fs, payloadAt + 4, 2);
                        file.LoopCount = p[0] | (p[1] << 8);
                    }
                }
                else if (fourCc == AnmfFourCc)
                {
                    file.IsAnimated = true;

                    if (withMetadata && size >= 16)
                    {
                        var p = ReadAt(fs, payloadAt + 12, 3);
                        var delay = p[0] | (p[1] << 8) | (p[2] << 16);
                        file.FrameDelays.Add(Math.Max(10, delay));
                    }
                }
                else if (withMetadata && fourCc == ExifFourCc && size > 8 && size < 64L * 1024 * 1024)
                {
                    var buffer = ReadAt(fs, payloadAt, (int)size);
                    ParseExif(buffer, file);
                }
                else if (withMetadata && fourCc == XmpFourCc && size > 0 && size < 16L * 1024 * 1024)
                {
                    var buffer = ReadAt(fs, payloadAt, (int)size);
                    var xmp = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                    var idx = xmp.IndexOf("prompt:{", StringComparison.Ordinal);
                    if (idx >= 0)
                        file.AddPayload("prompt", xmp.Substring(idx + "prompt:".Length));
                }

                pos += advance;

                // Header-only probing stops at the first chunk that yields a canvas size.
                if (!withMetadata && sizeKnown)
                    break;
            }
        }

        private static byte[] ReadAt(FileStream fs, long offset, int count)
        {
            var buffer = new byte[count];
            fs.Position = offset;

            var done = 0;
            while (done < count)
            {
                var read = fs.Read(buffer, done, count - done);
                if (read <= 0)
                    break;
                done += read;
            }

            return done == count ? buffer : null;
        }

        // --------------------------------------------------------------------- EXIF

        private static void ParseExif(byte[] data, WebpFile file)
        {
            if (data == null || data.Length < 8)
                return;

            // Pillow's animated WebP writer leaves the "Exif\0\0" prefix in place,
            // the static one strips it. Support both.
            var offset = 0;
            if (data[0] == 0x45 && data[1] == 0x78 && data[2] == 0x69 && data[3] == 0x66 &&
                data[4] == 0x00 && data[5] == 0x00)
            {
                offset = 6;
            }

            if (data.Length < offset + 8)
                return;

            var le = data[offset] == 0x49 && data[offset + 1] == 0x49;     // 'II'
            var be = data[offset] == 0x4D && data[offset + 1] == 0x4D;     // 'MM'
            if (!le && !be)
                return;

            if (ReadU16(data, offset + 2, le) != 0x002A)
                return;

            var ifd0 = (int)ReadU32(data, offset + 4, le);
            WalkIfd(data, offset, ifd0, le, file);
        }

        private static void WalkIfd(byte[] data, int tiffBase, int offset, bool le, WebpFile file)
        {
            var at = tiffBase + offset;
            if (at < 0 || at + 2 > data.Length)
                return;

            var count = ReadU16(data, at, le);
            if (count > 512)
                return;

            for (var i = 0; i < count; i++)
            {
                var entry = at + 2 + i * 12;
                if (entry + 12 > data.Length)
                    return;

                var tag = ReadU16(data, entry, le);
                var type = ReadU16(data, entry + 2, le);
                var num = ReadU32(data, entry + 4, le);

                // 2 = ASCII, 7 = UNDEFINED  (ComfyUI writes plain ASCII payloads)
                if (type != 2 && type != 7)
                    continue;
                if (num == 0 || num > 8 * 1024 * 1024)
                    continue;

                int valueOffset;
                if (num <= 4)
                {
                    valueOffset = entry + 8;
                }
                else
                {
                    valueOffset = tiffBase + (int)ReadU32(data, entry + 8, le);
                    if (valueOffset < 0 || valueOffset + num > data.Length)
                        continue;
                }

                var raw = new byte[num];
                Array.Copy(data, valueOffset, raw, 0, num);
                var text = Encoding.UTF8.GetString(raw).TrimEnd('\0', ' ');
                if (text.Length == 0)
                    continue;

                file.ExifTags[tag] = text;

                var colon = text.IndexOf(':');
                if (colon > 0 && colon < 64 && colon + 1 < text.Length && text[colon + 1] == '{')
                    file.AddPayload(text.Substring(0, colon), text.Substring(colon + 1));
                else if (text.IndexOf("class_type", StringComparison.Ordinal) >= 0 ||
                         text.IndexOf("\"nodes\"", StringComparison.Ordinal) >= 0)
                    file.AddPayload("workflow", text);
            }
        }

        private void AddPayload(string name, string json)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(json))
                return;
            if (json.Length < 8 || json[0] != '{')
                return;
            if (ComfyPayloads.ContainsKey(name))
                return;

            ComfyPayloads[name] = json;
        }

        // ------------------------------------------------------------ bit twiddling

        private static uint ReadU32Le(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        private static uint ReadU32(byte[] b, int o, bool le)
        {
            return le
                ? (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24))
                : (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        }

        private static ushort ReadU16(byte[] b, int o, bool le)
        {
            return le
                ? (ushort)(b[o] | (b[o + 1] << 8))
                : (ushort)((b[o] << 8) | b[o + 1]);
        }
    }
}
