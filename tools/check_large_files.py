"""One-off checks for the large-file paths. Builds temporary fixtures, then deletes them."""
import json
import struct
import subprocess
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import fixtures_img as img
import fixtures_json as wf

ROOT = Path(__file__).resolve().parent.parent
FIX = ROOT / "tests" / "fixtures"
TMP = ROOT / "out" / "bigtest"
CLI = ROOT / "dist" / "ComfyWorkflowDb.exe"


def run(*args):
    result = subprocess.run([str(CLI), *args], capture_output=True, text=True, encoding="utf-8")
    return result.stdout + result.stderr


def big_chunk_png(png, payload_mb, text):
    """A private chunk larger than the reader's buffer limit, placed before the workflow."""
    body = b"\x00" * (payload_mb * 1024 * 1024)
    ctype = b"prIv"
    big = (struct.pack(">I", len(body)) + ctype + body +
           struct.pack(">I", zlib.crc32(ctype + body) & 0xFFFFFFFF))
    return img.png_insert(png, [big, img.png_text_chunk("workflow", text)])


def midfile_moov_mp4(mp4, filler_mb, text):
    """moov parked in the middle of the file, workflow inside a box we do not parse."""
    body = text.encode("utf-8")
    custom = struct.pack(">I", 8 + len(body)) + b"wfjs" + body
    pos = 0
    parts = {}
    order = []
    while pos + 8 <= len(mp4):
        size = struct.unpack(">I", mp4[pos:pos + 4])[0]
        btype = mp4[pos + 4:pos + 8].decode("latin-1")
        if size < 8:
            break
        parts[btype] = mp4[pos:pos + size]
        order.append(btype)
        pos += size
    assert "moov" in parts and "ftyp" in parts, order
    moov = parts["moov"]
    moov = struct.pack(">I", len(moov) + len(custom)) + b"moov" + moov[8:] + custom
    filler_body = b"\x00" * (filler_mb * 1024 * 1024)
    filler = struct.pack(">I", 8 + len(filler_body)) + b"free" + filler_body
    rest = b"".join(parts[t] for t in order if t not in ("ftyp", "moov"))
    return parts["ftyp"] + filler + moov + rest


def main():
    TMP.mkdir(parents=True, exist_ok=True)
    graph = wf.dumps(wf.graph_json())
    wrapper = wf.wrapper_json()
    png = (FIX / "plain.png").read_bytes()
    mp4 = (FIX / "plain.mp4").read_bytes()

    cases = []
    a = TMP / "bigchunk.png"
    a.write_bytes(big_chunk_png(png, 70, graph))
    cases.append((a, "70 MB private chunk before the workflow tEXt"))

    b = TMP / "midmoov.mp4"
    b.write_bytes(midfile_moov_mp4(mp4, 12, wrapper))
    cases.append((b, "moov 12 MB into the file, workflow in an unparsed box"))

    failures = 0
    for path, note in cases:
        out = run("check", str(path))
        ok = "workflow  : YES" in out
        if not ok:
            failures += 1
        print("%s  %-16s %8.1f MB  %s" % ("PASS" if ok else "FAIL", path.name,
                                          path.stat().st_size / 1048576, note))
        for line in out.splitlines():
            if line.strip().startswith(("container", "workflow", "graph", "prompt", "note")):
                print("      " + line.strip())

    for path, _ in cases:
        path.unlink(missing_ok=True)
    TMP.rmdir()
    print("\n%d failure(s)" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
