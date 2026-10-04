using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Core.Obfuscator
{
    /// <summary>
    /// Carries the source image's text metadata (ComfyUI workflow / prompt / EXIF) through
    /// obfuscation when keepmeta is on. Capture collects the original entries, Embed stores
    /// them in a private PNG chunk of the obfuscated file — deflated and XOR-scrambled with an
    /// image-derived keystream, so no workflow text is readable in the file — and Restore
    /// writes them back into the deobfuscated output. Keyless by design: the payload is exactly
    /// as recoverable as the pixels themselves, and casual scanners find no plaintext markers.
    /// PNG sources keep their text chunks byte for byte; other containers are captured as
    /// parsed text entries and re-injected as tEXt on the PNG output (best effort).
    /// </summary>
    public static class MetaVault
    {
        private const string ChunkType = "prVt"; // ancillary private chunk, safe per PNG spec
        private const int ChunkDataHeader = 8;   // "CSE1" magic + payload length

        /// <summary>
        /// Minimal marker chunk for retrofitting files obfuscated before this existed, or when
        /// no metadata is kept. CSE1 = sealed payload, CSE0 = bare marker; not embedded
        /// automatically — keepmeta = 0 files stay byte-clean.
        /// </summary>
        public static void EmbedMarker(string pngPath)
        {
            try
            {
                InsertChunks(pngPath, new[] { BuildChunk(ChunkType,
                    new byte[] { (byte)'C', (byte)'S', (byte)'E', (byte)'0' }) });
            }
            catch (Exception ex) { Log.Error("marker embed " + pngPath, ex); }
        }

        /// <summary>
        /// Suffix the obfuscation command appends to output file names; also the cheap filename
        /// marker the thumbnail provider looks for.
        /// </summary>
        public const string ObfuscatedSuffix = "_混淆";

        /// <summary>
        /// Light check for our sealed payload: walks chunk headers and seeks over the data instead
        /// of reading it, so thumbnails can detect obfuscated files without touching the megabytes
        /// of image data.
        /// </summary>
        public static bool HasPayload(string pngPath)
        {
            try
            {
                using (var stream = File.OpenRead(pngPath))
                    return HasPayload(stream);
            }
            catch { return false; }
        }

        /// <summary>Stream variant, for thumbnail hosts that only hand us an IStream.</summary>
        public static bool HasPayload(Stream stream)
        {
            if (stream == null || !stream.CanSeek) return false;
            long saved = stream.Position;
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                var signature = new byte[8];
                if (stream.Read(signature, 0, signature.Length) != signature.Length ||
                    signature[0] != 0x89 || signature[1] != (byte)'P' || signature[2] != (byte)'N' ||
                    signature[3] != (byte)'G')
                    return false;
                var header = new byte[8];
                while (stream.Read(header, 0, header.Length) == header.Length)
                {
                    int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    var type = Encoding.ASCII.GetString(header, 4, 4);
                    if (type == "IEND") return false;
                    if (type == ChunkType && length >= 4)
                    {
                        var magic = new byte[4];
                        if (stream.Read(magic, 0, magic.Length) != magic.Length) return false;
                        if (IsOwnMagic(magic)) return true;
                        stream.Seek(length - magic.Length, SeekOrigin.Current);
                    }
                    stream.Seek(length + 4, SeekOrigin.Current); // skip data remainder and CRC
                }
            }
            catch { }
            finally
            {
                try { stream.Seek(saved, SeekOrigin.Begin); } catch { }
            }
            return false;
        }

        public static byte[] Capture(string sourcePath)
        {
            try
            {
                var records = new List<byte[]>();
                var extension = Path.GetExtension(sourcePath);
                if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    CapturePngChunks(sourcePath, records);
                else
                    CaptureTextEntries(sourcePath, records);
                if (records.Count == 0) return null;
                // Raw TLV; Seal does the single deflate pass before scrambling.
                return Concat(records);
            }
            catch (Exception ex)
            {
                Log.Error("meta capture " + sourcePath, ex);
                return null;
            }
        }

        public static void Embed(string pngPath, byte[] payload)
        {
            if (payload == null) return;
            try
            {
                InsertChunks(pngPath, new[] { BuildChunk(ChunkType, Seal(payload, ReadDimensions(pngPath))) });
            }
            catch (Exception ex) { Log.Error("meta embed " + pngPath, ex); }
        }

        /// <summary>Returns the captured records of an obfuscated file, or null when it carries none.</summary>
        public static List<Record> Extract(string pngPath)
        {
            try
            {
                foreach (var chunk in EnumerateChunks(pngPath))
                {
                    if (chunk.Type != ChunkType) continue;
                    if (chunk.Data.Length < ChunkDataHeader) continue;
                    if (chunk.Data[0] != (byte)'C' || chunk.Data[1] != (byte)'S' ||
                        chunk.Data[2] != (byte)'E' || chunk.Data[3] != (byte)'1') continue;
                    int length = BitConverter.ToInt32(chunk.Data, 4);
                    if (length < 0 || length > chunk.Data.Length - ChunkDataHeader) continue;
                    var sealedBytes = new byte[length];
                    Buffer.BlockCopy(chunk.Data, ChunkDataHeader, sealedBytes, 0, length);
                    return Parse(Unseal(sealedBytes, pngPath));
                }
            }
            catch (Exception ex) { Log.Error("meta extract " + pngPath, ex); }
            return null;
        }

        public static void Restore(string pngPath, List<Record> records)
        {
            if (records == null || records.Count == 0) return;
            try
            {
                var chunks = new List<byte[]>();
                foreach (var record in records)
                {
                    if (record.ChunkType != null) chunks.Add(BuildChunk(record.ChunkType, record.Data));
                    else chunks.Add(BuildChunk("tEXt", BuildTextChunk(record.Key, record.Value)));
                }
                InsertChunks(pngPath, chunks);
            }
            catch (Exception ex) { Log.Error("meta restore " + pngPath, ex); }
        }

        // ------------------------------------------------------------------ records

        public sealed class Record
        {
            /// <summary>Raw PNG chunk to recreate byte for byte ("tEXt", "zTXt", "iTXt", "eXIf").</summary>
            public string ChunkType;
            public byte[] Data;
            /// <summary>Generic text entry, re-injected as a tEXt chunk.</summary>
            public string Key;
            public string Value;
        }

        private static void CapturePngChunks(string path, List<byte[]> records)
        {
            foreach (var chunk in EnumerateChunks(path))
            {
                bool text = chunk.Type == "tEXt" || chunk.Type == "zTXt" ||
                            chunk.Type == "iTXt" || chunk.Type == "eXIf";
                if (!text) continue;
                if (chunk.Type == ChunkType && IsOwnChunk(chunk.Data)) continue;
                records.Add(EncodeRecord(chunk.Type, chunk.Data));
            }
        }

        private static void CaptureTextEntries(string path, List<byte[]> records)
        {
            var info = WorkflowDetector.InspectFile(path, DetectOptions.Full());
            if (info.Metadata == null) return;
            foreach (var entry in info.Metadata)
            {
                if (string.IsNullOrEmpty(entry.Value)) continue;
                var key = entry.Origin;
                if (string.IsNullOrEmpty(key)) key = "Comment";
                records.Add(EncodeText(key, entry.Value));
            }
        }

        // ------------------------------------------------------------------ payload framing
        //
        // TLV stream: [1][4-byte chunk type][int32 len][data] for raw chunks,
        //             [2][int32 klen][keyword utf8][int32 vlen][value utf8] for text entries.

        private static byte[] EncodeRecord(string chunkType, byte[] data)
        {
            using (var ms = new MemoryStream(9 + data.Length))
            {
                ms.WriteByte(1);
                var type = Encoding.ASCII.GetBytes(chunkType);
                if (type.Length != 4) throw new InvalidDataException("bad chunk type " + chunkType);
                ms.Write(type, 0, 4);
                var len = BitConverter.GetBytes(data.Length);
                ms.Write(len, 0, 4);
                ms.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        private static byte[] EncodeText(string key, string value)
        {
            var keyword = Encoding.UTF8.GetBytes(key);
            var text = Encoding.UTF8.GetBytes(value);
            using (var ms = new MemoryStream(9 + keyword.Length + text.Length))
            {
                ms.WriteByte(2);
                ms.Write(BitConverter.GetBytes(keyword.Length), 0, 4);
                ms.Write(keyword, 0, keyword.Length);
                ms.Write(BitConverter.GetBytes(text.Length), 0, 4);
                ms.Write(text, 0, text.Length);
                return ms.ToArray();
            }
        }

        private static List<Record> Parse(byte[] tlv)
        {
            var records = new List<Record>();
            int pos = 0;
            // Skip the "CSEV" stream header Concat prepends.
            if (tlv.Length >= 4 && tlv[0] == (byte)'C' && tlv[1] == (byte)'S' &&
                tlv[2] == (byte)'E' && tlv[3] == (byte)'V')
                pos = 4;
            while (pos + 5 <= tlv.Length)
            {
                var kind = tlv[pos++];
                if (kind == 1)
                {
                    if (pos + 8 > tlv.Length) break;
                    var type = Encoding.ASCII.GetString(tlv, pos, 4);
                    int length = BitConverter.ToInt32(tlv, pos + 4);
                    pos += 8;
                    if (length < 0 || pos + length > tlv.Length) break;
                    var data = new byte[length];
                    Buffer.BlockCopy(tlv, pos, data, 0, length);
                    pos += length;
                    records.Add(new Record { ChunkType = type, Data = data });
                }
                else if (kind == 2)
                {
                    if (pos + 4 > tlv.Length) break;
                    int klen = BitConverter.ToInt32(tlv, pos); pos += 4;
                    if (klen < 0 || pos + klen > tlv.Length) break;
                    var key = Encoding.UTF8.GetString(tlv, pos, klen); pos += klen;
                    if (pos + 4 > tlv.Length) break;
                    int vlen = BitConverter.ToInt32(tlv, pos); pos += 4;
                    if (vlen < 0 || pos + vlen > tlv.Length) break;
                    var value = Encoding.UTF8.GetString(tlv, pos, vlen); pos += vlen;
                    records.Add(new Record { Key = key, Value = value });
                }
                else break;
            }
            return records;
        }

        private static byte[] Concat(List<byte[]> records)
        {
            long total = 4;
            foreach (var r in records) total += r.Length;
            if (total > 64L * 1024 * 1024) throw new NotSupportedException("元数据载荷超过 64MB，跳过保留。");
            using (var ms = new MemoryStream((int)total))
            {
                ms.WriteByte((byte)'C'); ms.WriteByte((byte)'S'); ms.WriteByte((byte)'E'); ms.WriteByte((byte)'V');
                foreach (var r in records) ms.Write(r, 0, r.Length);
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------ scrambling

        private static byte[] Deflate(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                    deflate.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        private static byte[] Inflate(byte[] packed)
        {
            using (var input = new MemoryStream(packed))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                return output.ToArray();
            }
        }

        /// <summary>"CSE1" + length + deflated payload XORed with a keystream derived from the
        /// image dimensions, so each image scrambles differently and no plaintext survives.</summary>
        private static byte[] Seal(byte[] payload, Tuple<int, int> dimensions)
        {
            var packed = Deflate(payload);
            var data = new byte[ChunkDataHeader + packed.Length];
            data[0] = (byte)'C'; data[1] = (byte)'S'; data[2] = (byte)'E'; data[3] = (byte)'1';
            BitConverter.GetBytes(packed.Length).CopyTo(data, 4);
            uint state = KeyStream(dimensions.Item1, dimensions.Item2);
            for (int i = 0; i < packed.Length; i++)
            {
                state = Xorshift(state);
                data[ChunkDataHeader + i] = (byte)(packed[i] ^ (state >> 24));
            }
            return data;
        }

        /// <summary>Unscrambles the sealed payload (header already stripped by the caller).</summary>
        private static byte[] Unseal(byte[] sealedBytes, string pngPath)
        {
            var dims = ReadDimensions(pngPath);
            uint state = KeyStream(dims.Item1, dims.Item2);
            var packed = new byte[sealedBytes.Length];
            for (int i = 0; i < sealedBytes.Length; i++)
            {
                state = Xorshift(state);
                packed[i] = (byte)(sealedBytes[i] ^ (state >> 24));
            }
            return Inflate(packed);
        }

        private static uint KeyStream(int width, int height)
        {
            return (uint)width * 73856093u ^ (uint)height * 19349663u ^ 0xA5A5A5Au;
        }

        private static uint Xorshift(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static bool IsOwnChunk(byte[] data)
        {
            return data.Length >= 4 && IsOwnMagic(data);
        }

        private static bool IsOwnMagic(byte[] magic)
        {
            return magic[0] == (byte)'C' && magic[1] == (byte)'S' && magic[2] == (byte)'E' &&
                   (magic[3] == (byte)'0' || magic[3] == (byte)'1');
        }

        // ------------------------------------------------------------------ png surgery

        private static Tuple<int, int> ReadDimensions(string pngPath)
        {
            using (var stream = File.OpenRead(pngPath))
            {
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("PNG 头不完整：" + pngPath);
                int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return Tuple.Create(width, height);
            }
        }

        private struct PngChunk
        {
            public string Type;
            public byte[] Data;
        }

        private static IEnumerable<PngChunk> EnumerateChunks(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var signature = new byte[8];
                if (stream.Read(signature, 0, signature.Length) != signature.Length ||
                    signature[0] != 0x89 || signature[1] != (byte)'P' || signature[2] != (byte)'N' ||
                    signature[3] != (byte)'G')
                    throw new InvalidDataException("不是 PNG 文件：" + path);
                var header = new byte[8];
                while (stream.Read(header, 0, header.Length) == header.Length)
                {
                    int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    var type = Encoding.ASCII.GetString(header, 4, 4);
                    if (length < 0 || length > stream.Length) throw new InvalidDataException("PNG 块长度异常");
                    var data = new byte[length];
                    if (length > 0 && stream.Read(data, 0, length) != length)
                        throw new InvalidDataException("PNG 块数据不完整");
                    stream.Seek(4, SeekOrigin.Current); // CRC
                    yield return new PngChunk { Type = type, Data = data };
                    if (type == "IEND") yield break;
                }
            }
        }

        private static byte[] BuildChunk(string type, byte[] data)
        {
            var chunk = new byte[12 + data.Length];
            var typeBytes = Encoding.ASCII.GetBytes(type);
            if (typeBytes.Length != 4) throw new InvalidDataException("bad chunk type " + type);
            chunk[0] = (byte)((data.Length >> 24) & 0xFF);
            chunk[1] = (byte)((data.Length >> 16) & 0xFF);
            chunk[2] = (byte)((data.Length >> 8) & 0xFF);
            chunk[3] = (byte)(data.Length & 0xFF);
            typeBytes.CopyTo(chunk, 4);
            data.CopyTo(chunk, 8);
            uint crc = Crc32(chunk, 4, 4 + data.Length);
            chunk[8 + data.Length] = (byte)((crc >> 24) & 0xFF);
            chunk[9 + data.Length] = (byte)((crc >> 16) & 0xFF);
            chunk[10 + data.Length] = (byte)((crc >> 8) & 0xFF);
            chunk[11 + data.Length] = (byte)(crc & 0xFF);
            return chunk;
        }

        /// <summary>tEXt payload: keyword \0 text, keyword sanitized to Latin-1 printable.</summary>
        private static byte[] BuildTextChunk(string key, string value)
        {
            var keyword = new StringBuilder(key.Length);
            foreach (var c in (key ?? "Comment"))
            {
                if (c >= 0x20 && c <= 0x7E && c != '\0') keyword.Append(c);
                if (keyword.Length >= 79) break;
            }
            if (keyword.Length == 0) keyword.Append("Comment");
            var text = Encoding.UTF8.GetBytes(value ?? "");
            var data = new byte[keyword.Length + 1 + text.Length];
            Encoding.ASCII.GetBytes(keyword.ToString()).CopyTo(data, 0);
            data[keyword.Length] = 0;
            text.CopyTo(data, keyword.Length + 1);
            return data;
        }

        /// <summary>Inserts chunks right before IEND and rewrites the file atomically.</summary>
        private static void InsertChunks(string pngPath, IEnumerable<byte[]> chunks)
        {
            byte[] file = File.ReadAllBytes(pngPath);
            int iend = FindIend(file);
            var head = new byte[iend];
            Buffer.BlockCopy(file, 0, head, 0, iend);
            long extra = 0;
            foreach (var chunk in chunks) extra += chunk.Length;
            using (var temp = new FileStream(pngPath + ".cse.tmp", FileMode.Create, FileAccess.Write))
            {
                temp.Write(head, 0, head.Length);
                foreach (var chunk in chunks) temp.Write(chunk, 0, chunk.Length);
                temp.Write(file, iend, file.Length - iend);
                temp.Flush();
            }
            File.Delete(pngPath);
            File.Move(pngPath + ".cse.tmp", pngPath);
        }

        private static int FindIend(byte[] file)
        {
            int pos = 8;
            while (pos + 8 <= file.Length)
            {
                int length = (file[pos] << 24) | (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
                var type = Encoding.ASCII.GetString(file, pos + 4, 4);
                if (type == "IEND") return pos;
                pos += 12 + length;
            }
            throw new InvalidDataException("PNG 缺少 IEND 块");
        }

        private static uint[] _crcTable;

        private static uint Crc32(byte[] data, int offset, int count)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    _crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
                crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
