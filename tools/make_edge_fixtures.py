"""Extra fixtures for the failure modes that real ComfyUI output runs into."""
import json
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import fixtures_img as img
import fixtures_json as wf


def truncate_text_chunk(png, keyword, text, keep):
    """A tEXt chunk holding a JSON payload cut off part way through."""
    return img.png_insert(png, [img.png_text_chunk(keyword, text[:keep])])


def broken_ztxt(png, keyword, text, keep_ratio=0.55):
    """A zTXt chunk whose deflate stream stops early, as a damaged writer would leave it."""
    body = zlib.compress(text.encode("latin-1", "replace"))
    body = body[: int(len(body) * keep_ratio)]
    payload = keyword.encode("latin-1") + b"\x00\x00" + body
    chunk = (struct.pack(">I", len(payload)) + b"zTXt" + payload +
             struct.pack(">I", zlib.crc32(b"zTXt" + payload) & 0xFFFFFFFF))
    return img.png_insert(png, [chunk])


def png_with_exif(png, tiff):
    payload = b"Exif\x00\x00" + tiff
    chunk = (struct.pack(">I", len(payload)) + b"eXIf" + payload +
             struct.pack(">I", zlib.crc32(b"eXIf" + payload) & 0xFFFFFFFF))
    return img.png_insert(png, [chunk])


def png_after_idat(png, keyword, text):
    """Metadata appended between the last IDAT and IEND, which some tools do."""
    pos = 8
    last = None
    while pos + 8 <= len(png):
        length = struct.unpack(">I", png[pos:pos + 4])[0]
        ctype = png[pos + 4:pos + 8]
        if ctype == b"IEND":
            last = pos
            break
        pos += 12 + length
    assert last is not None, "no IEND"
    return png[:last] + img.png_text_chunk(keyword, text) + png[last:]


def xmp_escaped(jpg, text):
    """XMP keeps JSON as element text, so every quote arrives XML escaped."""
    escaped = (text.replace("&", "&amp;").replace("<", "&lt;")
               .replace(">", "&gt;").replace('"', "&quot;"))
    packet = (
        '<?xpacket begin="\ufeff" id="W5M0MpCehiHzreSzNTczkc9d"?>'
        '<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF '
        'xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
        '<rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">'
        '<dc:description><rdf:Alt><rdf:li xml:lang="x-default">'
        + escaped +
        '</rdf:li></rdf:Alt></dc:description></rdf:Description></rdf:RDF>'
        '</x:xmpmeta><?xpacket end="w"?>'
    ).encode("utf-8")
    header = b"http://ns.adobe.com/xap/1.0/\x00"
    payload = header + packet
    assert len(payload) + 2 < 65536, "xmp packet too large for one APP1"
    return jpg[:2] + b"\xff\xe1" + struct.pack(">H", len(payload) + 2) + payload + jpg[2:]


def _boxes(data, start=0, end=None):
    end = len(data) if end is None else end
    pos = start
    while pos + 8 <= end:
        size = struct.unpack(">I", data[pos:pos + 4])[0]
        btype = data[pos + 4:pos + 8]
        if size == 1:
            size = struct.unpack(">Q", data[pos + 8:pos + 16])[0]
        elif size == 0:
            size = end - pos
        if size < 8:
            return
        yield pos, size, btype
        pos += size


def mp4_quicktime_comment(data, text):
    """Adds moov/udta/(c)cmt as a bare string payload, the QuickTime style layout."""
    body = text.encode("utf-8")
    cmt = struct.pack(">I", 8 + len(body)) + b"\xa9cmt" + body
    udta = struct.pack(">I", 8 + len(cmt)) + b"udta" + cmt
    for pos, size, btype in _boxes(data):
        if btype != b"moov":
            continue
        moov = data[pos:pos + size]
        grown = struct.pack(">I", size + len(udta)) + b"moov" + moov[8:] + udta
        return data[:pos] + grown + data[pos + size:]
    raise AssertionError("no moov box")


def main():
    root = Path(__file__).resolve().parent.parent
    out = root / "tests" / "fixtures"
    graph = wf.dumps(wf.graph_json())
    prompt = wf.dumps(wf.prompt_json())
    wrapper = wf.wrapper_json()
    png = (out / "plain.png").read_bytes()
    jpg = (out / "plain.jpg").read_bytes()
    mp4 = (out / "plain.mp4").read_bytes()
    added = []

    def write(name, data, container, note):
        (out / name).write_bytes(data)
        added.append({"file": name, "has": True, "container": container, "note": note})

    write("truncated.png", truncate_text_chunk(png, "workflow", graph, 900),
          "png", "tEXt cut off mid JSON")
    write("ztxt_broken.png", broken_ztxt(png, "workflow", graph),
          "png", "zTXt with a deflate stream that ends early")
    write("exif.png", png_with_exif(png, img.build_tiff({0x010E: "workflow:" + graph})),
          "png", "workflow in a PNG eXIf chunk")
    write("after_idat.png", png_after_idat(png, "prompt", prompt),
          "png", "tEXt placed after IDAT")
    write("xmp_escaped.jpg", xmp_escaped(jpg, prompt),
          "jpeg", "XMP packet with XML escaped quotes")
    write("qt_comment.mp4", mp4_quicktime_comment(mp4, wrapper),
          "mp4", "moov/udta/(c)cmt as a bare string")

    manifest = out / "expected.json"
    cases = json.loads(manifest.read_text(encoding="utf-8")) if manifest.exists() else []
    known = {case["file"] for case in cases}
    cases.extend(case for case in added if case["file"] not in known)
    manifest.write_text(json.dumps(cases, indent=2, ensure_ascii=False), encoding="utf-8")

    print("added %d fixtures, manifest now has %d cases" % (len(added), len(cases)))
    for case in added:
        print("  %-20s %8d bytes  %s" % (case["file"], (out / case["file"]).stat().st_size, case["note"]))


if __name__ == "__main__":
    main()
