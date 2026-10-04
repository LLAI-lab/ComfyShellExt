"""Adds AI generator metadata fixtures (A1111, NovelAI, SwarmUI, Fooocus, InvokeAI) and updates
expected.json in place. Reuses the existing plain.* fixtures as carrier images, so ffmpeg is not
needed. Run after make_fixtures.py."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import fixtures_img as img

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "tests" / "fixtures"


def main():
    png = (OUT / "plain.png").read_bytes()
    jpg = (OUT / "plain.jpg").read_bytes()
    gif = (OUT / "plain.gif").read_bytes()

    a1111_text = ("a photo of a cozy cafe, warm light, 1girl\n"
                  "Negative prompt: lowres, bad anatomy, watermark\n"
                  'Steps: 28, Sampler: DPM++ 2M Karras, CFG scale: 7, Seed: 3534562570, '
                  "Size: 832x1216, Model hash: e334dc2ce6, Model: zImageTurbo_turbo, "
                  'Denoising strength: 0.5, Clip skip: 2, Lora hashes: "lora_a: e334dc2ce682, '
                  'lora_b: a1abbb21b31c", Version: neo')

    novelai_json = json.dumps({
        "prompt": "1girl, silver hair, city at night",
        "uc": "lowres, bad hands",
        "steps": 23, "scale": 5.0, "sampler": "k_euler", "seed": 1234567,
        "width": 832, "height": 1216, "noise_schedule": "karras", "sm": False, "dyn": False,
    })
    swarmui_json = json.dumps({
        "sui_image_params": {
            "prompt": "a red sports car on a mountain road",
            "negativeprompt": "blurry, jpeg artifacts",
            "model": "sd_xl_base_1.0", "steps": 30, "cfgscale": 6.5,
            "width": 1024, "height": 1024, "seed": 777,
        }
    })
    fooocus_json = json.dumps({
        "Prompt": "an astronaut riding a horse, film grain",
        "Negative Prompt": "worst quality, low quality",
        "Settings": {
            "base_model": "juggernautXL_v8", "sampler_name": "dpmpp_2m_sde_gpu",
            "steps": 25, "cfg_scale": 4.0, "seed": 424242,
            "width": 1024, "height": 768, "performance_selection": "Speed",
        },
    })
    invokeai_json = json.dumps({
        "prompt": "a lighthouse in a storm, dramatic lighting",
        "negative_prompt": "blurry",
        "steps": 30, "cfg_scale": 7.5, "seed": 9,
        "width": 512, "height": 768,
        "sampler": {"name": "ddim"}, "model": {"name": "stable-diffusion-xl"},
    })

    def write(name, data, generator, container, note):
        (OUT / name).write_bytes(data)
        return {"file": name, "has": False, "container": container,
                "generator": generator, "note": note}

    new = [
        # plain.png gains the A1111 tEXt chunk; has stays false: it is not a ComfyUI workflow.
        write("a1111_full.png", img.png_insert(png, [img.png_text_chunk("parameters", a1111_text)]),
              "A1111", "png", "A1111 parameters with quoted Lora hashes"),
        write("a1111.jpg", img.jpeg_insert_exif(
            jpg, img.build_tiff({0x9286: "ASCII\x00\x00\x00" + a1111_text})),
            "A1111", "jpeg", "A1111 parameters in EXIF UserComment"),
        write("novelai.png", img.png_insert(png, [
            img.png_text_chunk("Software", "NovelAI 4.6"),
            img.png_text_chunk("Source", "Stable Diffusion XL 793C2B75"),
            img.png_text_chunk("Description", "1girl, silver hair, city at night"),
            img.png_text_chunk("Comment", novelai_json)]),
            "NovelAI", "png", "Software + Comment JSON"),
        write("novelai.gif", img.gif_insert_comment(
            gif, "Software: NovelAI 3.24\nSource: 832x1216\nComment: " + novelai_json),
            "NovelAI", "gif", "GIF comment block with embedded JSON"),
        write("swarmui.png", img.png_insert(png, [
            img.png_text_chunk("parameters", swarmui_json)]),
            "SwarmUI", "png", "sui_image_params JSON in parameters"),
        write("fooocus.png", img.png_insert(png, [
            img.png_text_chunk("parameters", fooocus_json)]),
            "Fooocus", "png", "Fooocus settings JSON in parameters"),
        write("invokeai.png", img.png_insert(png, [
            img.png_text_chunk("invokeai_metadata", invokeai_json)]),
            "InvokeAI", "png", "invokeai_metadata JSON"),
    ]

    manifest_path = OUT / "expected.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for item in manifest:
        if item["file"] == "a1111.png":
            item["generator"] = "A1111"
            item["note"] = "A1111 metadata is AI generation, but never flagged as ComfyUI"
    names = {item["file"] for item in manifest}
    for item in new:
        if item["file"] not in names:
            manifest.append(item)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("updated %s, %d cases" % (manifest_path, len(manifest)))


if __name__ == "__main__":
    main()
