"""Generates the test fixtures. Requires ffmpeg on PATH; no python packages needed."""
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import fixtures_img as img
import fixtures_json as wf

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "tests" / "fixtures"
FFMPEG = shutil.which("ffmpeg") or "ffmpeg"
EXPECTED = []


def ff(*args):
    subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", *args], check=True)


def expect(name, has, container, note=""):
    EXPECTED.append({"file": name, "has": has, "container": container, "note": note})


def write(name, data, has, container, note=""):
    (OUT / name).write_bytes(data)
    expect(name, has, container, note)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    graph = wf.dumps(wf.graph_json())
    prompt = wf.dumps(wf.prompt_json())
    wrapper = wf.wrapper_json()
    graph_cjk = wf.dumps(wf.graph_json(positive=wf.POSITIVE_CJK), ascii_only=False)
    a1111 = ("masterpiece, best quality, 1girl\nNegative prompt: lowres, bad anatomy\n"
             "Steps: 20, Sampler: Euler a, CFG scale: 7, Seed: 42, Size: 512x512")

    base = OUT / "_base.png"
    ff("-f", "lavfi", "-i", "testsrc2=size=512x512:duration=1", "-frames:v", "1", str(base))
    png = base.read_bytes()

    write("plain.png", png, False, "png", "no metadata at all")
    write("a1111.png", img.png_insert(png, [img.png_text_chunk("parameters", a1111)]),
          False, "png", "A1111 metadata must not be flagged as ComfyUI")
    write("comfy_text.png", img.png_insert(png, [
        img.png_text_chunk("prompt", prompt), img.png_text_chunk("workflow", graph)]),
        True, "png", "standard SaveImage output")
    write("comfy_prompt_only.png", img.png_insert(png, [img.png_text_chunk("prompt", prompt)]),
          True, "png", "API prompt only")
    write("comfy_itxt_cjk.png", img.png_insert(png, [
        img.png_text_chunk("workflow", graph_cjk, "iTXt")]), True, "png", "compressed UTF-8 iTXt")
    write("comfy_ztxt.png", img.png_insert(png, [img.png_text_chunk("workflow", graph, "zTXt")]),
          True, "png", "compressed latin-1 zTXt")
    write("appended.png", png + b"\x00\x00" + wrapper.encode("utf-8"), True, "png",
          "payload after IEND, only the raw scan can see it")

    ff("-i", str(base), "-frames:v", "1", str(OUT / "plain.jpg"))
    jpg = (OUT / "plain.jpg").read_bytes()
    expect("plain.jpg", False, "jpeg")
    tiff = img.build_tiff({0x010E: "workflow:" + graph,
                           0x9286: "ASCII\x00\x00\x00prompt:" + prompt})
    write("comfy.jpg", img.jpeg_insert_exif(jpg, tiff), True, "jpeg",
          "EXIF ImageDescription + UserComment")

    ff("-i", str(base), "-c:v", "libwebp", "-lossless", "1", "-frames:v", "1",
       str(OUT / "plain.webp"))
    webp = (OUT / "plain.webp").read_bytes()
    expect("plain.webp", False, "webp")
    tiff = img.build_tiff({0x0110: "prompt:" + prompt, 0x010F: "workflow:" + graph})
    write("comfy.webp", img.webp_with_exif(webp, tiff, 512, 512), True, "webp",
          "SaveAnimatedWEBP style EXIF tags")

    ff("-i", str(base), "-vf", "scale=256:256", "-frames:v", "1", str(OUT / "plain.gif"))
    gif = (OUT / "plain.gif").read_bytes()
    expect("plain.gif", False, "gif")
    write("comfy.gif", img.gif_insert_comment(gif, wrapper), True, "gif", "GIF comment extension")

    video = ["-f", "lavfi", "-i", "testsrc2=size=480x270:rate=12:duration=2",
             "-pix_fmt", "yuv420p"]
    ff(*video, "-c:v", "libx264", "-crf", "34", str(OUT / "plain.mp4"))
    expect("plain.mp4", False, "mp4")
    ff(*video, "-c:v", "libx264", "-crf", "34", "-metadata", "comment=" + wrapper,
       str(OUT / "comfy_comment.mp4"))
    expect("comfy_comment.mp4", True, "mp4", "moov/udta/meta/ilst (c)cmt")
    ff(*video, "-c:v", "libx264", "-crf", "34", "-movflags", "use_metadata_tags",
       "-metadata", "workflow=" + graph, "-metadata", "prompt=" + prompt,
       str(OUT / "comfy_mdta.mp4"))
    expect("comfy_mdta.mp4", True, "mp4", "moov/meta mdta keys table")

    ff(*video, "-c:v", "libvpx", "-deadline", "realtime", "-b:v", "300k",
       str(OUT / "plain.webm"))
    expect("plain.webm", False, "matroska")
    ff(*video, "-c:v", "libvpx", "-deadline", "realtime", "-b:v", "300k",
       "-metadata", "comment=" + wrapper, str(OUT / "comfy_comment.webm"))
    expect("comfy_comment.webm", True, "matroska", "matroska SimpleTag COMMENT")
    ff(*video, "-c:v", "libx264", "-crf", "34", "-metadata", "workflow=" + graph,
       str(OUT / "comfy.mkv"))
    expect("comfy.mkv", True, "matroska", "custom SimpleTag WORKFLOW")

    ff(*video, "-c:v", "mpeg4", "-q:v", "8", "-metadata", "comment=" + wrapper,
       str(OUT / "comfy.avi"))
    expect("comfy.avi", True, "avi", "RIFF LIST INFO ICMT")

    ff("-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "flac",
       "-metadata", "workflow=" + graph, str(OUT / "comfy.flac"))
    expect("comfy.flac", True, "flac", "Vorbis comment")

    base.unlink(missing_ok=True)
    (OUT / "expected.json").write_text(json.dumps(EXPECTED, indent=2), encoding="utf-8")
    print("wrote %d fixtures to %s" % (len(EXPECTED), OUT))
    for item in EXPECTED:
        size = (OUT / item["file"]).stat().st_size
        print("  %-24s %8d bytes  expect=%s" % (item["file"], size, item["has"]))


if __name__ == "__main__":
    main()
