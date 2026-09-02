"""Binary surgery helpers for building metadata carrying image fixtures."""
import struct
import zlib

PNG_SIG = b"\x89PNG\r\n\x1a\n"


def _chunk(ctype, payload):
    return (struct.pack(">I", len(payload)) + ctype + payload +
            struct.pack(">I", zlib.crc32(ctype + payload) & 0xFFFFFFFF))


def png_text_chunk(keyword, text, kind="tEXt"):
    key = keyword.encode("latin-1")
    if kind == "tEXt":
        return _chunk(b"tEXt", key + b"\x00" + text.encode("latin-1", "replace"))
    if kind == "zTXt":
        body = key + b"\x00" + b"\x00" + zlib.compress(text.encode("latin-1", "replace"))
        return _chunk(b"zTXt", body)
    body = key + b"\x00" + b"\x01\x00" + b"\x00" + b"\x00" + zlib.compress(text.encode("utf-8"))
    return _chunk(b"iTXt", body)


def png_insert(data, chunks):
    """Inserts chunks right after IHDR."""
    assert data[:8] == PNG_SIG, "not a png"
    pos = 8
    length = struct.unpack(">I", data[pos:pos + 4])[0]
    end = pos + 8 + length + 4          # past IHDR
    return data[:end] + b"".join(chunks) + data[end:]


def build_tiff(tags):
    """Little endian TIFF with a single IFD. tags maps tag id -> ascii string."""
    entries = []
    blobs = []
    data_offset = 8 + 2 + 12 * len(tags) + 4
    for tag in sorted(tags):
        raw = tags[tag].encode("utf-8") + b"\x00"
        if len(raw) <= 4:
            value = raw + b"\x00" * (4 - len(raw))
        else:
            value = struct.pack("<I", data_offset + sum(len(b) for b in blobs))
            blobs.append(raw)
        entries.append(struct.pack("<HHI", tag, 2, len(raw)) + value)
    return (b"II\x2a\x00" + struct.pack("<I", 8) + struct.pack("<H", len(entries)) +
            b"".join(entries) + struct.pack("<I", 0) + b"".join(blobs))


def jpeg_insert_exif(data, tiff):
    """Inserts an APP1 Exif segment directly after SOI."""
    assert data[:2] == b"\xff\xd8", "not a jpeg"
    payload = b"Exif\x00\x00" + tiff
    return data[:2] + b"\xff\xe1" + struct.pack(">H", len(payload) + 2) + payload + data[2:]


def _riff_chunks(data):
    pos = 12
    while pos + 8 <= len(data):
        cid = data[pos:pos + 4]
        size = struct.unpack("<I", data[pos + 4:pos + 8])[0]
        yield cid, data[pos + 8:pos + 8 + size]
        pos += 8 + size + (size & 1)


def _pad(chunk_id, payload):
    out = chunk_id + struct.pack("<I", len(payload)) + payload
    return out + (b"\x00" if len(payload) & 1 else b"")


def webp_with_exif(data, tiff, width, height):
    """Rebuilds a simple WebP as an extended (VP8X) file carrying an EXIF chunk."""
    assert data[:4] == b"RIFF" and data[8:12] == b"WEBP", "not a webp"
    body = b""
    for cid, payload in _riff_chunks(data):
        if cid in (b"VP8 ", b"VP8L", b"ALPH", b"ANIM", b"ANMF"):
            body += _pad(cid, payload)
    vp8x = struct.pack("<B3x", 0x08) + struct.pack("<I", width - 1)[:3] + \
        struct.pack("<I", height - 1)[:3]
    out = _pad(b"VP8X", vp8x) + body + _pad(b"EXIF", tiff)
    return b"RIFF" + struct.pack("<I", len(out) + 4) + b"WEBP" + out


def gif_insert_comment(data, text):
    """Inserts a comment extension right after the header and global colour table."""
    assert data[:3] == b"GIF", "not a gif"
    packed = data[10]
    pos = 13
    if packed & 0x80:
        pos += 3 * (1 << ((packed & 7) + 1))
    raw = text.encode("utf-8")
    blocks = b""
    for i in range(0, len(raw), 255):
        piece = raw[i:i + 255]
        blocks += bytes([len(piece)]) + piece
    return data[:pos] + b"\x21\xfe" + blocks + b"\x00" + data[pos:]
